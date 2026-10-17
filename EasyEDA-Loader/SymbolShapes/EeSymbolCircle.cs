using System;

namespace EasyEDA_Loader
{
    public class EeSymbolCircle : EeSymbolShape
    {
        public static EeSymbolCircle FromString(string data)
        {
            var parts = data.Split(new[] { "~" }, StringSplitOptions.None);
            return new EeSymbolCircle
            {
                // C~cx~cy~r~stroke~width~style~fill~id~locked (parts[0] is the "C" tag)
                CenterX = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                CenterY = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                Radius = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                StrokeColor = parts.Length > 4 ? parts[4] : "",
                StrokeWidth = parts.Length > 5 ? parts[5] : "",
                StrokeStyle = parts.Length > 6 ? parts[6] : "",
                FillColor = parts.Length > 7 ? parts[7] : "",
                Id = parts.Length > 8 ? parts[8] : "",
                IsLocked = parts.Length > 9 && ParseBoolean(parts[9])
            };
        }

        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }
        public string StrokeColor { get; set; }
        public string StrokeWidth { get; set; }
        public string StrokeStyle { get; set; }
        public string FillColor { get; set; }
        public string Id { get; set; }
        public bool IsLocked { get; set; }
    }

}
