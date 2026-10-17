using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEDA_Loader
{
    // LCSC-style parametric browsing through EasyEDA Pro's own endpoints (the same calls the
    // EasyEDA Pro editor's "LCSC" library panel makes). LCSC's website API is behind a CDN
    // that blocks non-browser clients, so EasyEDA is used instead.
    //
    //   GET /api/eda/catalog/tree                       category tree (catalogId, catalogNameEn, sonList)
    //   GET /api/v2/eda/product/list?catalog=&currPage=&pageSize=&needAggs=true
    //       standard=<pkg>$<pkg>                        Package filter
    //       param=<value>@<name>$<value>@<name>         any other parameter filter
    //       keyword=<text>                              free text inside the category
    //     -> result.total, result.paramList (filter columns for the current filters), result.productList
    public partial class EasyedaApi
    {
        public class CatalogNode
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public List<CatalogNode> Children { get; set; } = new();
        }

        public class FilterFacet
        {
            public string Name { get; set; }
            public List<string> Values { get; set; } = new();
        }

        public class BrowseResult
        {
            public long Total { get; set; }
            public List<FilterFacet> Facets { get; set; } = new();
            public List<PartInfo> Parts { get; set; } = new();
        }

        public const string PackageFacet = "Package";
        public const string ManufacturerFacet = "Manufacturer";

        private HttpRequestMessage EasyedaGet(string pathAndQuery)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://pro.easyeda.com" + pathAndQuery);
            request.Headers.Add("Referer", "https://pro.easyeda.com/editor");
            request.Headers.Add("Origin", "https://pro.easyeda.com");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            return request;
        }

        public async Task<List<CatalogNode>> GetCatalogTreeAsync(CancellationToken cancellationToken)
        {
            // The category tree almost never changes.
            string text = await ApiCache.GetTextAsync("catalog", "tree", TimeSpan.FromDays(7), HttpClient,
                () => EasyedaGet("/api/eda/catalog/tree"), IsJsonWithResult, cancellationToken);
            var obj = JObject.Parse(text);

            List<CatalogNode> Read(JToken list) =>
                (list as JArray)?.Select(n => new CatalogNode
                {
                    Id = (int)n["catalogId"],
                    Name = (string)n["catalogNameEn"] ?? (string)n["catalogName"],
                    Children = Read(n["sonList"]) ?? new List<CatalogNode>(),
                }).ToList();

            return Read(obj["result"]) ?? new List<CatalogNode>();
        }

        // filters: facet name -> selected values. Package goes to "standard", Manufacturer is
        // sent as the keyword (the API filters manufacturers by id, which it doesn't expose),
        // everything else goes to "param" as value@name.
        public async Task<BrowseResult> BrowseAsync(int catalogId, int page, int pageSize,
            IDictionary<string, List<string>> filters, string keyword, CancellationToken cancellationToken)
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new("catalog", catalogId.ToString(CultureInfo.InvariantCulture)),
                new("currPage", page.ToString(CultureInfo.InvariantCulture)),
                new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                new("needAggs", "true"),
            };

            var keywords = new List<string>();
            if (!string.IsNullOrWhiteSpace(keyword))
                keywords.Add(keyword.Trim());

            var paramValues = new List<string>();
            foreach (var kv in filters ?? new Dictionary<string, List<string>>())
            {
                if (kv.Value == null || kv.Value.Count == 0)
                    continue;
                if (kv.Key == PackageFacet)
                    query.Add(new("standard", string.Join("$", kv.Value)));
                else if (kv.Key == ManufacturerFacet)
                    keywords.Add(kv.Value[0]);
                else
                    paramValues.AddRange(kv.Value.Select(v => $"{v}@{kv.Key}"));
            }
            if (paramValues.Count > 0)
                query.Add(new("param", string.Join("$", paramValues)));
            if (keywords.Count > 0)
                query.Add(new("keyword", string.Join(" ", keywords)));

            string qs = string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));
            Debug.WriteLine($"[API] GET /api/v2/eda/product/list?{qs}");

            // Each page/filter combination is cached for half a day (stock and prices move daily),
            // so paging back, re-ticking a filter or reopening the dialog costs nothing.
            string text = await ApiCache.GetTextAsync("browse", qs, SearchLifetime, HttpClient,
                () => EasyedaGet("/api/v2/eda/product/list?" + qs), IsJsonWithResult, cancellationToken);
            var obj = JObject.Parse(text);
            var result = obj["result"] as JObject;
            if (result == null)
                throw new InvalidOperationException((string)obj["msg"] ?? "No result from EasyEDA");

            var browse = new BrowseResult { Total = (long?)result["total"] ?? 0 };
            if (result["paramList"] is JArray facets)
            {
                foreach (var f in facets)
                {
                    browse.Facets.Add(new FilterFacet
                    {
                        Name = (string)f["parameterName"],
                        Values = f["parameterValueList"]?.Select(v => (string)v).Where(v => !string.IsNullOrEmpty(v) && v != "-").ToList() ?? new List<string>(),
                    });
                }
            }
            if (result["productList"] is JArray products)
            {
                foreach (var p in products)
                    browse.Parts.Add(ParsePart(p));
            }
            return browse;
        }

        // Shared by keyword search and category browsing.
        public static PartInfo ParsePart(JToken product)
        {
            var info = new ProductInfo();
            bool hasSymbol = false, hasFootprint = false, has3d = false;
            try
            {
                if (product["device_info"] is JObject device_info)
                {
                    var attributes = device_info["attributes"]?.ToObject<Dictionary<string, string>>();
                    string description = (string)device_info["Description"] ?? (string)device_info["description"] ?? "";
                    info = ProductFromAttributes(attributes, description);
                    hasSymbol = device_info["symbol_info"] != null;
                    hasFootprint = device_info["footprint_info"] != null;
                    has3d = device_info["footprint_info"]?["model_3d"] != null;
                }
            }
            catch (Exception)
            {
            }

            // price: [[qty, unitPrice, unitPrice], ...] ; priceList: [{price}] on keyword search
            string price = null;
            var breaks = new List<string>();
            if (product["price"] is JArray priceArray)
            {
                foreach (var b in priceArray.OfType<JArray>())
                {
                    if (b.Count >= 2)
                        breaks.Add($"{b[0]}+: ${b[1]}");
                    price ??= b.Count >= 2 ? (string)b[1] : null;
                }
            }
            price ??= (string)product["priceList"]?.FirstOrDefault()?["price"];

            string partClass = (string)product["JLCPCB Part Class"] ?? "";
            if (partClass.EndsWith(" Part"))
                partClass = partClass.Substring(0, partClass.Length - " Part".Length);

            return new PartInfo
            {
                Name = (string)product["mpn"],
                Part = (string)product["number"],
                Description = info.Description ?? "",
                Info = info,
                HasSymbol = hasSymbol,
                HasFootprint = hasFootprint,
                Has3d = has3d,
                Manufacturer = (string)product["manufacturer"] ?? "",
                Package = (string)product["package"] ?? "",
                Stock = (long?)product["stockNumber"] ?? (long?)product["stock"] ?? 0,
                Price = price != null ? "$" + price : "",
                PriceBreaks = string.Join("   ", breaks),
                PartClass = partClass,
            };
        }
    }
}
