using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace EasyEDA_Loader
{
    public partial class EasyedaApi
    {
        private const string Version = "6.4.19.5";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";
        private HttpClient HttpClient;

        public EasyedaApi()
        {
            HttpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }

        private void LogResponse(string content)
        {
            try
            {
                var jsonObj = JToken.Parse(content);
                string prettyJson = jsonObj.ToString(Formatting.Indented);
                Debug.WriteLine($"[API] Response Content (Pretty JSON):\n{prettyJson}");
                Console.WriteLine($"[API] Response Content (Pretty JSON):\n{prettyJson}");
            }
            catch
            {
                Debug.WriteLine($"[API] Response Content: {content}");
                Console.WriteLine($"[API] Response Content: {content}");
            }
        }

        // How long each kind of response is trusted before asking EasyEDA again. Older copies
        // are still served if EasyEDA is unreachable or blocking us.
        private static readonly TimeSpan PartLifetime = TimeSpan.FromDays(90);      // symbol/footprint JSON
        private static readonly TimeSpan ImageLifetime = TimeSpan.FromDays(365);    // part photos
        private static readonly TimeSpan ModelLifetime = TimeSpan.MaxValue;         // 3D models are addressed by uuid: immutable
        private static readonly TimeSpan SearchLifetime = TimeSpan.FromHours(12);   // stock and price move daily
        private static readonly TimeSpan InfoLifetime = TimeSpan.FromDays(30);

        private static bool IsJsonWithResult(string text)
        {
            try
            {
                var obj = JObject.Parse(text);
                return obj["result"] != null && obj["result"].Type != JTokenType.Null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static HttpRequestMessage ProRequest(HttpMethod method, string url, HttpContent content = null)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            request.Headers.Add("Referer", "https://pro.easyeda.com/editor");
            request.Headers.Add("Origin", "https://pro.easyeda.com");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            return request;
        }

        public async Task<Root> GetComponentJsonAsync(string lcscId, CancellationToken cancellationToken)
        {
            string url = $"https://easyeda.com/api/products/{lcscId}/components?version={Version}";
            try
            {
                string content = await ApiCache.GetTextAsync("parts", lcscId.Trim().ToUpperInvariant(), PartLifetime,
                    HttpClient, () => new HttpRequestMessage(HttpMethod.Get, url), IsJsonWithResult, cancellationToken);
                return JsonConvert.DeserializeObject<Root>(content);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task<BitmapImage> LoadPngAsync(string imageUrl, CancellationToken cancellationToken)
        {
            // Add the host path if it doesnt exist
            if (!imageUrl.Contains("//"))
                imageUrl = "//image.lceda.cn" + imageUrl;
            string fullUrl = $"https:{imageUrl}";

            try
            {
                var imageData = await ApiCache.GetBytesAsync("images", fullUrl, ImageLifetime,
                    HttpClient, () => new HttpRequestMessage(HttpMethod.Get, fullUrl), b => b.Length > 0, cancellationToken);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(imageData);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task<byte[]> LoadModelAsync(string modelUuid, CancellationToken cancellationToken)
        {
            string url = $"https://modules.easyeda.com/qAxj6KHrDKw4blvCG8QJPs7Y/{modelUuid}";
            try
            {
                return await ApiCache.GetBytesAsync("models", url, ModelLifetime,
                    HttpClient, () => new HttpRequestMessage(HttpMethod.Get, url), b => b.Length > 0, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task<byte[]> LoadRawModelAsync(string modelUuid, CancellationToken cancellationToken)
        {
            string url = $"https://modules.easyeda.com/3dmodel/{modelUuid}";
            try
            {
                return await ApiCache.GetBytesAsync("models", url, ModelLifetime,
                    HttpClient, () => new HttpRequestMessage(HttpMethod.Get, url), b => b.Length > 0, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
        public class PartInfo
        {
            public string Name { get; set; }
            public string Part { get; set; }
            public string Description { get; set; }
            public ProductInfo Info { get; set; }
            public bool HasSymbol { get; set; }
            public bool Has3d { get; set; }
            public bool HasFootprint { get; set; }
            public string Manufacturer { get; set; }
            public string Package { get; set; }
            public long Stock { get; set; }
            public string Price { get; set; }       // unit price at the lowest quantity break, USD
            public string PriceBreaks { get; set; } // e.g. "1+: $2.1579  10+: $1.8426"
            public string PartClass { get; set; }   // JLCPCB "Basic" / "Extended"
        }

        public class ProductInfo
        {
            public string Description { get; set; }
            public Vec3 Size { get; set; }
            public Vec3 Rotation { get; set; }
            public Vec3 Offset { get; set; }
            public Dictionary<string, string> Parameters { get; set; }
        }
        public async Task<ProductInfo> GetProductInfoAsync(string search, string uuid)
        {
            string url = "https://pro.easyeda.com/api/v2/devices/search";
            var formData = new Dictionary<string, string>
            {
                { "page", "1" },
                { "pageSize", "1" },
                { "uid", uuid },
                { "path", uuid },
                { "wd", search.ToLower() },
                { "returnListStyle", "classifyarr" }
            };

            try
            {
                string result = await ApiCache.GetTextAsync("info", $"{uuid}|{search.ToLower()}", InfoLifetime, HttpClient,
                    () => ProRequest(HttpMethod.Post, url, new FormUrlEncodedContent(formData)), IsJsonWithResult, CancellationToken.None);
                JObject obj = JObject.Parse(result);
                JObject obj_info = (JObject)obj["result"]["lists"]["lcsc"][0];
                var attributes = obj_info["attributes"].ToObject<Dictionary<string, string>>();
                return ProductFromAttributes(attributes, obj_info["description"].ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API] Error: {ex.Message}");
                return null;
            }
        }

        static public ProductInfo ProductFromAttributes(Dictionary<string, string> attributes, string description)
        {
            Vec3 size = null;
            Vec3 rotation = null;
            Vec3 offset = null;

            if (attributes != null && attributes.ContainsKey("3D Model Transform"))
            {
                try
                {
                    var transformInfo = attributes["3D Model Transform"].Split(',');
                    if (transformInfo.Length >= 9)
                    {
                        size = new Vec3
                        {
                            X = EeShape.ConvertToMM(double.Parse(transformInfo[0], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Y = EeShape.ConvertToMM(double.Parse(transformInfo[1], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Z = EeShape.ConvertToMM(double.Parse(transformInfo[2], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                        };
                        rotation = new Vec3
                        {
                            X = double.Parse(transformInfo[3], System.Globalization.CultureInfo.InvariantCulture),
                            Y = double.Parse(transformInfo[4], System.Globalization.CultureInfo.InvariantCulture),
                            Z = double.Parse(transformInfo[5], System.Globalization.CultureInfo.InvariantCulture),
                        };
                        offset = new Vec3
                        {
                            X = EeShape.ConvertToMM(double.Parse(transformInfo[6], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Y = EeShape.ConvertToMM(double.Parse(transformInfo[7], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Z = EeShape.ConvertToMM(double.Parse(transformInfo[8], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                        };
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[API] Error parsing 3D Model Transform: {ex.Message}");
                    Console.WriteLine($"[API] Error parsing 3D Model Transform: {ex.Message}");
                }
            }

            List<string> keysToRemove = new()
            {
                "Add into BOM",
                "Convert to PCB",
                "Symbol",
                "Designator",
                "Footprint",
                "3D Model",
                "3D Model Title",
                "3D Model Transform",
                "Name"
            };
            
            return new ProductInfo
            {
                Description = description,
                Size = size,
                Rotation = rotation,
                Offset = offset,
                Parameters = attributes?.Where(kvp => !keysToRemove.Contains(kvp.Key) && kvp.Value != "-").ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, string>()
            };
        }

        public async Task<List<PartInfo>> SearchProductInfoAsync(string lcscId)
        {
            // The server reads the keyword from the query string and ignores the JSON body
            string keyword = (lcscId ?? "").Trim();
            string url = $"https://pro.easyeda.com/api/v2/eda/product/search?keyword={Uri.EscapeDataString(keyword)}";
            string payload = JsonConvert.SerializeObject(new { codes = keyword });

            string result = await ApiCache.GetTextAsync("search", keyword.ToUpperInvariant(), SearchLifetime, HttpClient,
                () => ProRequest(HttpMethod.Post, url, new StringContent(payload, Encoding.UTF8, "application/json")),
                IsJsonWithResult, CancellationToken.None);

            JObject obj = JObject.Parse(result);
            var products = obj["result"]?["productList"] as JArray ?? new JArray();
            return products.Select(ParsePart).ToList();
        }
    }
}
