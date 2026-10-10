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
