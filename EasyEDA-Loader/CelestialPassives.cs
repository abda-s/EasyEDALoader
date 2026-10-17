using EDP;
using SCH;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyEDA_Loader
{
    // Draws standard 2-pin passive symbols copied from the Celestial library
    // (C:\Alituim_libraries\altium-library\symbols\Passives). The geometry is
    // baked in here - no runtime dependency on the library files.
    //
    // Coordinates are in mils, taken from the Properties panel of the
    // Celestial symbols. To add/adjust a symbol, edit its block in
    // CreateComponent: pins (location + rotation + length) and polylines
    // (vertex list). Body is blue, Small line width; pins are black.
    public static class CelestialPassives
    {
        private static readonly int Blue = EESCH.RGB(0, 0, 255);

        // Polylines of each Celestial symbol, (x, y) pairs in mils.
        private static readonly double[][] ResistorBody =
        {
            new double[] { -100, 0, -60, 0 },
            new double[] { -10, 30, -30, -30, -50, 30, -60, 0 },
            new double[] { -10, 30, 10, -30, 30, 30, 50, -30, 60, 0 },
            new double[] { 100, 0, 60, 0 },
        };
        private static readonly double[][] CapacitorBody =
        {
            new double[] { -100, 0, -25, 0 },
            new double[] { -25, 80, -25, -80 },
            new double[] { 25, 80, 25, -80 },
            new double[] { 100, 0, 25, 0 },
        };

        // Decides whether a part gets the Celestial resistor ("R") or capacitor ("C") symbol,
        // or null for everything else. The designator prefix alone isn't enough: LCSC's data
        // labels some resistors "U?" (e.g. C2930002), so the value, package and description
        // are checked too. Only plain 2-pin parts qualify, and polarised capacitors are left
        // to the EasyEDA drawing so their + marking isn't lost.
        public static string Classify(SymbolData symbol, string description)
        {
            if (symbol?.Shapes == null)
                return null;
            if (symbol.Shapes.OfType<EeSymbolPin>().Count() != 2)
                return null;

            var para = symbol.Head?.Parameters;
            string prefix = NormalizePrefix(para?.Pre);
            string value = (para?.Value ?? "").Trim();
            string package = (para?.Package ?? "").Trim().ToUpperInvariant();
            string text = $"{para?.Name} {package} {description}".ToUpperInvariant();

            // A different, specific prefix (D, L, FB, LED, F, Y...) is trusted as-is.
            if (prefix != "" && prefix != "R" && prefix != "C" && prefix != "U")
                return null;
            // 2-pin parts that look like resistors/capacitors but need their own symbol.
            if (Regex.IsMatch(text, @"THERMISTOR|NTC|PTC|VARISTOR|POTENTIOMETER|FUSE|FERRITE|BEAD|INDUCTOR|CRYSTAL|RESONATOR"))
                return null;

            string kind = null;
            if (prefix == "R" || prefix == "C")
                kind = prefix;
            else if (Regex.IsMatch(value, @"^[0-9.]+\s*[kKmMG]?\s*(Ω|ohms?)$", RegexOptions.IgnoreCase))
                kind = "R";
            else if (Regex.IsMatch(value, @"^[0-9.]+\s*[pnuµμm]?F$"))
                kind = "C";
            else if (Regex.IsMatch(package, @"^R\d{4}\b|^RES"))
                kind = "R";
            else if (Regex.IsMatch(package, @"^C\d{4}\b|^CAP"))
                kind = "C";
            else if (text.Contains("RESISTOR"))
                kind = "R";
            else if (text.Contains("CAPACITOR") || text.Contains("MLCC"))
                kind = "C";

            if (kind == "C" && IsPolarised(symbol, text))
                return null;
            return kind;
        }

        private static bool IsPolarised(SymbolData symbol, string text)
        {
            if (Regex.IsMatch(text, @"TANTALUM|ELECTROLYTIC|POLYMER|POLARI[SZ]ED|CASE-[A-Z]"))
                return true;
            // EasyEDA names the pins of polarised capacitors + / -
            if (symbol.Shapes.OfType<EeSymbolPin>().Any(p => p.Name?.Text?.Trim() is "+" or "-"))
                return true;
            // An electrolytic's curved plate is an arc or path; a plain capacitor is only lines.
            if (symbol.Shapes.Any(s => s is EeSymbolArc || s is EeSymbolPath || s is EeSymbolEllipse || s is EeSymbolCircle))
                return true;
            // Tantalum and electrolytic symbols draw a small "+" as two short crossing lines.
            var segments = symbol.Shapes.OfType<EeSymbolPolyline>()
                .Where(p => !(p is EeSymbolPolygon))
                .Select(p => p.Points.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(n => n.Length == 4)
                .Select(n => n.Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
                .Where(n => Math.Abs(n[2] - n[0]) + Math.Abs(n[3] - n[1]) <= 6) // short: the + is ~4 units
                .ToList();
            foreach (var h in segments.Where(n => Math.Abs(n[1] - n[3]) < 0.01))
            {
                foreach (var v in segments.Where(n => Math.Abs(n[0] - n[2]) < 0.01))
                {
                    double vx = v[0], hy = h[1];
                    if (vx >= Math.Min(h[0], h[2]) && vx <= Math.Max(h[0], h[2]) &&
                        hy >= Math.Min(v[1], v[3]) && hy <= Math.Max(v[1], v[3]))
                        return true;
                }
            }
            return false;
        }

        // Pin 1 on the left, pin 2 on the right, whatever order EasyEDA lists them in.
        // The numbers are kept from EasyEDA so they still match the footprint's pads.
        private static (string Left, string Right) PinNumbers(SymbolData symbol)
        {
            var numbers = symbol.Shapes.OfType<EeSymbolPin>()
                .Select(p => string.IsNullOrWhiteSpace(p.Designator?.Text) ? p.Settings.SpicePinNumber : p.Designator.Text.Trim())
                .OrderBy(n => int.TryParse(n, out int v) ? v : int.MaxValue)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();
            return (numbers.Count > 0 ? numbers[0] : "1", numbers.Count > 1 ? numbers[1] : "2");
        }

        // The same symbol as SymbolConverter geometry, for the dialog's preview.
        public static SymbolConverter.Geometry PreviewGeometry(string kind, SymbolData symbol)
        {
            var (left, right) = PinNumbers(symbol);
            var g = new SymbolConverter.Geometry();
            g.Pins.Add(new SymbolConverter.Pin { X = -100, Y = 0, Length = 100, Orientation = 180, Designator = left, Name = left });
            g.Pins.Add(new SymbolConverter.Pin { X = 100, Y = 0, Length = 100, Orientation = 0, Designator = right, Name = right });
            foreach (var line in kind == "C" ? CapacitorBody : ResistorBody)
            {
                var poly = new SymbolConverter.Poly();
                for (int i = 0; i + 1 < line.Length; i += 2)
                    poly.Points.Add((line[i], line[i + 1]));
                g.Polys.Add(poly);
            }
            return g;
        }

        // Distance of the designator (above) and value (below) from the centre line,
        // chosen to clear the body: resistor zigzag is +/-30, capacitor plates +/-80.
        private static double TextOffset(string prefix) => NormalizePrefix(prefix) == "C" ? 140 : 90;

        // Converts the LCSC Value into RKM-style code for the part type.
        // Returns the input unchanged for other parts or if it can't be parsed.
        public static string FormatValue(string designatorPrefix, string value)
        {
            switch (NormalizePrefix(designatorPrefix))
            {
                case "R": return FormatResistance(value);
                case "C": return FormatCapacitance(value);
                default: return value;
            }
        }

        // "12.7kΩ" -> 12K7, 4K7, 60R, 100K, 0R1, 2M2
        public static string FormatResistance(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var m = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"^([0-9]*\.?[0-9]+)\s*([kKmMgG]?)\s*(Ω|ohms?|R)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success || !TryParse(m.Groups[1].Value, out decimal number))
                return value;

            // Case matters for the multiplier: m = milli, M = mega
            switch (m.Groups[2].Value)
            {
                case "k": case "K": number *= 1_000m; break;
                case "M": number *= 1_000_000m; break;
                case "G": case "g": number *= 1_000_000_000m; break;
                case "m": number /= 1_000m; break;
            }

            if (number >= 1_000_000_000m) return Rkm(number / 1_000_000_000m, "G");
            if (number >= 1_000_000m) return Rkm(number / 1_000_000m, "M");
            if (number >= 1_000m) return Rkm(number / 1_000m, "K");
            return Rkm(number, "R");
        }

        // "100nF" -> 100n, 4.7uF -> 4u7, 2.2nF -> 2n2, 10pF -> 10p, 0.1uF -> 100n
        public static string FormatCapacitance(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var m = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"^([0-9]*\.?[0-9]+)\s*([pnuµμm]?)\s*F?$");
            if (!m.Success || !TryParse(m.Groups[1].Value, out decimal pico))
                return value;

            // Work in picofarads to stay exact
            switch (m.Groups[2].Value)
            {
                case "": pico *= 1_000_000_000_000m; break;
                case "m": pico *= 1_000_000_000m; break;
                case "u": case "µ": case "μ": pico *= 1_000_000m; break;
                case "n": pico *= 1_000m; break;
            }

            if (pico >= 1_000_000_000m) return Rkm(pico / 1_000_000_000m, "m");
            if (pico >= 1_000_000m) return Rkm(pico / 1_000_000m, "u");
            if (pico >= 1_000m) return Rkm(pico / 1_000m, "n");
            return Rkm(pico, "p");
        }

        private static bool TryParse(string text, out decimal number) =>
            decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);

        // The unit letter replaces the decimal point: 4.7 + "K" -> 4K7
        private static string Rkm(decimal scaled, string letter)
        {
            string text = scaled.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            int dot = text.IndexOf('.');
            return dot < 0 ? text + letter : text.Substring(0, dot) + letter + text.Substring(dot + 1);
        }

        // Visible Value parameter centred below the body
        public static void AddValueParameter(ISch_Component c, string designatorPrefix, string value)
        {
            ISch_Parameter param = c.AddSchParameter();
            param.SetState_Name("Value");
            param.SetState_Text(FormatValue(designatorPrefix, value));
            param.SetState_ShowName(false);
            param.SetState_IsHidden(false);
            PlaceText(param, 0, -TextOffset(designatorPrefix));
        }

        // Centre-justified, with autoposition off - otherwise Altium re-positions
        // the text as soon as the placed part is rotated (PlaceComponent does that).
        private static void PlaceText(ISch_Parameter text, double x, double y)
        {
            if (text == null)
                return;
            text.SetState_Location(new DXP.Point
            {
                X = AltiumApi.MilsToCoord(x),
                Y = AltiumApi.MilsToCoord(y)
            });
            text.SetState_Justification(TTextJustification.eJustify_Center);
            text.SetState_Autoposition(false);
        }

        private static string NormalizePrefix(string pre)
        {
            if (string.IsNullOrEmpty(pre))
                return string.Empty;
            string trimmed = pre.TrimEnd('?', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0');
            return trimmed.Trim().ToUpperInvariant();
        }

        // kind is "R" or "C", from Classify.
        public static void CreateComponent(ISch_Lib schLib, ISch_Component component, string pcbLibraryPath, string package, SymbolData ee_symbol, string kind)
        {
            string prefix = kind;

            // Some LCSC parts carry the wrong prefix (a resistor labelled U?), so set it here.
            component.GetState_SchDesignator().SetState_Text(kind + "?");

            var (pin1Designator, pin2Designator) = PinNumbers(ee_symbol);

            // Both symbols: pin 1 at (-100,0) rot 180, pin 2 at (100,0) rot 0, 100mil long.
            // Pin numbers and names are both hidden; name matches designator.
            var leftPin = EESCH.CreateLeftPin(schLib, component, -100, 0, pin1Designator, pin1Designator, 100, TPinElectrical.eElectricPassive, false, null);
            var rightPin = EESCH.CreateRightPin(schLib, component, 100, 0, pin2Designator, pin2Designator, 100, TPinElectrical.eElectricPassive, false, null);
            leftPin?.SetState_ShowDesignator(false);
            rightPin?.SetState_ShowDesignator(false);

            // SCH - PASSIVES - RESISTOR.SCHLIB / CAPACITOR.SCHLIB
            foreach (var line in kind == "C" ? CapacitorBody : ResistorBody)
                EESCH.CreatePolyline(schLib, component, Blue, line);

            // Designator above the body; the visible Value parameter (AddValueParameter)
            // goes below it, so the comment is kept for the BOM but hidden.
            PlaceText(component.GetState_SchDesignator(), 0, TextOffset(prefix));
            component.GetState_SchComment().SetState_Text("=Value");
            component.GetState_SchComment().SetState_IsHidden(true);

            EESCH.AssignFootprint(component, pcbLibraryPath, package, "");
            schLib.AddSchComponent(component);
        }
    }
}
