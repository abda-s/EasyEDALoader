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
            return key == "R"; // TODO: add "C" once the capacitor geometry is filled in below
        }

        public static bool IsResistor(string designatorPrefix) => NormalizePrefix(designatorPrefix) == "R";

        // Converts an LCSC value like "12.7kΩ" into RKM code: 12K7, 4K7, 60R, 100K, 0R1, 2M2.
        // Returns the input unchanged if it can't be parsed.
        public static string FormatResistance(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var m = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"^([0-9]*\.?[0-9]+)\s*([kKmMgG]?)\s*(Ω|ohms?|R)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success)
                return value;

            if (!decimal.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal number))
                return value;

            // Case matters for the multiplier: m = milli, M = mega
            switch (m.Groups[2].Value)
            {
                case "k": case "K": number *= 1_000m; break;
                case "M": number *= 1_000_000m; break;
                case "G": case "g": number *= 1_000_000_000m; break;
                case "m": number /= 1_000m; break;
            }

            string letter;
            decimal scaled;
            if (number >= 1_000_000_000m) { letter = "G"; scaled = number / 1_000_000_000m; }
            else if (number >= 1_000_000m) { letter = "M"; scaled = number / 1_000_000m; }
            else if (number >= 1_000m) { letter = "K"; scaled = number / 1_000m; }
            else { letter = "R"; scaled = number; }

            string text = scaled.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            int dot = text.IndexOf('.');
            return dot < 0 ? text + letter : text.Substring(0, dot) + letter + text.Substring(dot + 1);
        }

        // Visible Value parameter at (0,-90) centred, as in the reference symbol
        public static void AddValueParameter(ISch_Component c, string value)
        {
            ISch_Parameter param = c.AddSchParameter();
            param.SetState_Name("Value");
            param.SetState_Text(value);
            param.SetState_ShowName(false);
            param.SetState_IsHidden(false);
            PlaceText(param, 0, -90);
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

            if (prefix == "R")
            {
                // SCH - PASSIVES - RESISTOR.SCHLIB
                // Pin 1: (-100,0) rot 180, len 100. Pin 2: (100,0) rot 0, len 100.
                EESCH.CreateLeftPin(schLib, component, -100, 0, pin1Designator, "1", 100, TPinElectrical.eElectricPassive, false, null);
                EESCH.CreateRightPin(schLib, component, 100, 0, pin2Designator, "2", 100, TPinElectrical.eElectricPassive, false, null);

                EESCH.CreatePolyline(schLib, component, Blue, -100, 0, -60, 0);
                EESCH.CreatePolyline(schLib, component, Blue, -10, 30, -30, -30, -50, 30, -60, 0);
                EESCH.CreatePolyline(schLib, component, Blue, -10, 30, 10, -30, 30, 30, 50, -30, 60, 0);
                EESCH.CreatePolyline(schLib, component, Blue, 100, 0, 60, 0);

                // Designator above the zigzag; the visible Value parameter (AddValueParameter)
                // goes below it, so the comment is kept for the BOM but hidden.
                PlaceText(component.GetState_SchDesignator(), 0, 90);
                component.GetState_SchComment().SetState_Text("=Value");
                component.GetState_SchComment().SetState_IsHidden(true);
            }
            else if (prefix == "C")
            {
                // SCH - PASSIVES - CAPACITOR.SCHLIB
                // TODO: fill in from the Celestial capacitor symbol (pins + every line/polyline vertex).
            }

            EESCH.AssignFootprint(component, pcbLibraryPath, package, "");
            schLib.AddSchComponent(component);
        }
    }
}
