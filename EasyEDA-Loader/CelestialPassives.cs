using EDP;
using SCH;

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

        public static bool IsSupported(string designatorPrefix)
        {
            string key = NormalizePrefix(designatorPrefix);
            return key == "R" || key == "C";
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

        public static void CreateComponent(ISch_Lib schLib, ISch_Component component, string pcbLibraryPath, string package, SymbolData ee_symbol)
        {
            string prefix = NormalizePrefix(ee_symbol.Head.Parameters.Pre);

            // Keep EasyEDA's pin designators in case a part isn't numbered 1/2
            var pins = new System.Collections.Generic.List<EeSymbolPin>();
            foreach (var shape in ee_symbol.Shapes)
            {
                if (shape is EeSymbolPin pin)
                    pins.Add(pin);
            }

            string pin1Designator = pins.Count > 0 ? pins[0].Settings.SpicePinNumber : "1";
            string pin2Designator = pins.Count > 1 ? pins[1].Settings.SpicePinNumber : "2";

            // Both symbols: pin 1 at (-100,0) rot 180, pin 2 at (100,0) rot 0, 100mil long.
            // Pin numbers and names are both hidden; name matches designator.
            var leftPin = EESCH.CreateLeftPin(schLib, component, -100, 0, pin1Designator, pin1Designator, 100, TPinElectrical.eElectricPassive, false, null);
            var rightPin = EESCH.CreateRightPin(schLib, component, 100, 0, pin2Designator, pin2Designator, 100, TPinElectrical.eElectricPassive, false, null);
            leftPin?.SetState_ShowDesignator(false);
            rightPin?.SetState_ShowDesignator(false);

            if (prefix == "R")
            {
                // SCH - PASSIVES - RESISTOR.SCHLIB
                EESCH.CreatePolyline(schLib, component, Blue, -100, 0, -60, 0);
                EESCH.CreatePolyline(schLib, component, Blue, -10, 30, -30, -30, -50, 30, -60, 0);
                EESCH.CreatePolyline(schLib, component, Blue, -10, 30, 10, -30, 30, 30, 50, -30, 60, 0);
                EESCH.CreatePolyline(schLib, component, Blue, 100, 0, 60, 0);
            }
            else if (prefix == "C")
            {
                // SCH - PASSIVES - CAPACITOR.SCHLIB
                EESCH.CreatePolyline(schLib, component, Blue, -100, 0, -25, 0);
                EESCH.CreatePolyline(schLib, component, Blue, -25, 80, -25, -80);
                EESCH.CreatePolyline(schLib, component, Blue, 25, 80, 25, -80);
                EESCH.CreatePolyline(schLib, component, Blue, 100, 0, 25, 0);
            }

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
