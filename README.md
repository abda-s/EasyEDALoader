# Local changes (FSUK-BMS)

This copy has been modified for Altium Designer 25 and the FSUK-BMS project.

## Celestial-style passive symbols

Resistors (prefix `R`) and capacitors (prefix `C`) no longer come in as a generic box. They are drawn with the same symbols as the Celestial library's `SCH - PASSIVES - RESISTOR.SCHLIB` and `SCH - PASSIVES - CAPACITOR.SCHLIB`, see [CelestialPassives.cs](/EasyEDA-Loader/CelestialPassives.cs). The geometry is copied into the plugin, so the Celestial library is **not** needed at runtime. Every other part is converted from its EasyEDA drawing (see below).

| | Resistor | Capacitor |
|---|---|---|
| Pins | Pin 1 (-100, 0) rot 180°, pin 2 (100, 0) rot 0°, 100 mil long, black. Numbers and names hidden (name = number) | Same |
| Body | Blue (`RGB(0,0,255)`) polylines, Small width: leads to ±60, zigzag through ±30 mil | Leads to ±25, plates at x = ±25 from y = -80 to +80 |
| Value | RKM code: 12.7kΩ → `12K7`, 4.7kΩ → `4K7`, 60Ω → `60R`, 100kΩ → `100K`, 0.1Ω → `0R1` | 100nF → `100n`, 4.7uF → `4u7`, 2.2nF → `2n2`, 10pF → `10p`, 0.1uF → `100n` |
| Text | Designator at (0, +90), `Value` parameter visible at (0, -90) | Same layout at ±140, to clear the plates |

All text is centre-justified with Autoposition off. Autoposition is off because `PlaceComponent` rotates the placed part, and that rotation would otherwise move the text. The comment is set to `=Value` (kept for the BOM) but hidden. Values that can't be parsed are left as they come from LCSC.

To add another passive (e.g. inductor), open the Celestial symbol in Altium and read off each pin's location, rotation and length, plus every line/polyline vertex, from the Properties panel. Then add a block in `CelestialPassives.CreateComponent`, add the prefix to `IsSupported`, and set its text offset in `TextOffset` (and a formatter in `FormatValue` if needed). `EESCH.CreatePolyline` draws a polyline from a list of (x, y) pairs in mils.

> **The cached symbol trap:** if a part with the same name already exists in `Documents/AltiumEE/EasyEDA.SchLib`, the loader reuses it and never runs the drawing code. Delete the old part from the SchLib before re-importing it to see a symbol change.

## Symbols drawn like EasyEDA

Every other part is no longer a generated box: [SymbolConverter.cs](/EasyEDA-Loader/SymbolConverter.cs) rebuilds the EasyEDA drawing itself, and the preview pane draws the same converted geometry. Parts with no readable pins fall back to the old generated box.

| | EasyEDA (std) | Altium |
|---|---|---|
| Units | 1 unit = 10 mil | mil |
| Y axis | down (SVG) | up |
| Origin | `head.x/head.y` | same point, nudged so pin ends land on the 100 mil grid |
| Pin position | electrical end, path runs toward the body | `Location` = body end, `Orientation` points to the electrical end |

So `x_alt = (x - ox) * 10`, `y_alt = -(y - oy) * 10`, and every direction's Y flips. Pin length and side come from the pin path (`h`/`v`/`L`), with the rotation field as a fallback.

| EasyEDA shape | Altium |
|---|---|
| `P` pin | pin with number, name, show/hide flags and electrical type. Inversion dot → `Symbol_OuterEdge = eDot` (pin stretched to the body), clock → `Symbol_InnerEdge = eClock` |
| `R` rect | rectangle, Altium's yellow body fill |
| `PL` / `PG` | polyline / polygon; fills matching the stroke stay solid (arrows, diode triangles) |
| `C` / `E` | ellipse |
| `A` arc | arc / elliptical arc; centre from the SVG endpoint form, start/end swapped because the Y flip reverses the direction |
| `PT` path | M/L/H/V/Z kept, C/Q/A curves flattened into short segments |

## Request caching

EasyEDA's CloudFront blocks an IP for a while (HTTP 403) after a burst of requests, so every request goes through [ApiCache.cs](/EasyEDA-Loader/API/ApiCache.cs):

- Memory cache for the Altium session, plus a disk cache in `%LOCALAPPDATA%\EasyEDA-Loader\cache\`: 3D models forever, photos 1 year, part JSON 90 days, category tree 7 days, searches and browse pages 12 hours.
- Two callers asking for the same thing share one download; requests to one host are at least 0.4 s apart.
- After a 403/429 the host is left alone for 10 minutes, and any cached copy (however old) is served instead of an error. Errors show inline in the dialog, not in a popup.
- The preview waits 350 ms after the selection changes, so arrowing through the list doesn't fetch every row.

Delete the cache folder to force fresh data.

## LCSC-style browsing

The dialog has a category tree, filter columns (package, manufacturer and the category's own parameters) and paged results with stock, price and Basic/Extended class, using EasyEDA Pro's catalog endpoints ([CatalogApi.cs](/EasyEDA-Loader/API/CatalogApi.cs), [DialogWindow.Browse.cs](/EasyEDA-Loader/DialogWindow.Browse.cs)). The whole dialog is dark, with a dark title bar.

## To do

- [ ] **Offline parts from EasyEDA Pro.** The EasyEDA Pro desktop client ships with ~10,000 common parts. Read them from the user's own local install (not copied into this repo, since the data belongs to EasyEDA/LCSC) and use them before going online. First step: install EasyEDA Pro, run it offline/half-offline once, then inspect the library files read-only for format, part count and whether LCSC numbers are stored. Pro uses its own format, so it needs a second reader feeding `SymbolConverter` and the footprint code.
- [ ] Share the cache between team members (export/import buttons).
- [ ] Background pre-fetch of a BOM's LCSC numbers at a gentle pace.

## Other changes

- **Part follows the cursor when placed.** Instead of dropping the part at the sheet origin, `PlaceComponent` copies it and runs Altium's own `Sch:Paste`, so it drags on the cursor until you click (Esc cancels, Space rotates). This overwrites the clipboard. If that fails, it falls back to a click-to-place crosshair (`ChooseLocationInteractively`). `IntegratedLibraryManager.PlaceLibraryComponent` was not used: it places nothing when called from code.
- **No error popup after import.** Primitive import failures are still written to `Documents/AltiumEE/import-errors.log`, but the warning dialog was removed.
- **`ComponentPolarityLayer`** in EasyEDA footprints is mapped to Mechanical 12, instead of throwing a layer-map error.
- **Altium Designer 25:** the project targets `net8.0-windows` and references DevExpress **v25.2** assemblies, copied from the AD25 install into `EasyEDA-Loader/Assemblies/` (gitignored).

## Build & deploy (no Visual Studio needed)

`Build.ps1` needs MSBuild. With only the .NET 8 SDK installed, build with `dotnet` instead. It **must** be the Release configuration, because `Package.ps1` copies from `bin\Release`:

```powershell
cd C:\dev\EasyEDALoader
dotnet build "EasyEDA-Loader\EasyEDA-Loader.csproj" -c Release
.\Package.ps1
.\Deploy.ps1          # close Altium first, it locks the DLL
```

Then start Altium. To confirm the deploy worked, check that `C:\ProgramData\Altium\Altium Designer {GUID}\Extensions\EasyEDA-Loader\EasyEDA-Loader.dll` has the same timestamp as `dist\EasyEDA-Loader.dll`.

---

# Motivations / Inspirations

Sourcing parts from JLCPCB can be a bit of a pain when you don't have the footprint, the symbol, or the model. 

If you're a user of KiCAD you're in luck as there's a nice project [easyeda2kicad.py](https://github.com/uPesy/easyeda2kicad.py) which will offline convert and pull models for you, and there's another script which integrates this script into KiCAD.

If you're an Altium user, you're stuck either running this script and importing via KiCAD import, or exporting the Altium files from EasyEDA and copying them manually into a footprint.

I used easyeda2kicad as a reference for this project as well as AtliumLibraryLoader script for reference on Altium APIs to manipulate adding parts to libraries

# Usage

Using the extension is pretty straight forward once it is installed, there will be a new Menu option `EasyEDA Loader` while a SchDoc is active, this will open a Modal Dialog which prompts the LCSC Part number e.g. "C2040". Tick which part you want to add after searching and press Add to Library

![Dialog](/Assets/Loader.PNG)

Hitting OK will automatically create `EasyEDA.pcblib` and `EasyEDA.schlib` if they don't already exist in `Documents/AltiumEE`, create the footprint, download the 3d model, create the symbol, add part info, map the footprint to the symbol, then place the component into the active schematic at the bottom left.

# Comparisons

Left EasyEDA, Right Altium after import

## Symbol

![Comparison of EasyEDA Symbol](/Assets/Compare-Symbol.png)

## Footprint

![Comparison of EasyEDA Footprint](/Assets/Compare-Footprint.png)

## 3D Model

![Comparison of EasyEDA 3D](/Assets/Compare-3D.png)

## Part Info

![Comparison of EasyEDA Part Info](/Assets/PartInfo-EEL.PNG)

# Building

You shouldn't need anything special to build, just .NET 4.8, Language v8.0, and probably assembly references to Altium's internal libraries.

The following Assembly references were made and can be found in

```
C:\Program Files\Altium\AD24\System
C:\Program Files\Altium\AD24\System\DotNet\DevExpress.Wpf
```

```
Altium.Controls
Altium.Controls.Skins
Altium.SDK
Altium.SDK.Interfaces
DevExpress.Data.v22.1
DevExpress.Mvvm.v22.1
DevExpress.Printing.v22.1.Core
DevExpress.Utils.v22.1
DevExpress.Xpf.Core.v22.1
DevExpress.Xpf.Grid.v22.1.Core
DevExpress.Xpf.Grid.v22.1
```

# Standalone

The standalone version is a simple WPF app that draws the primitives to a Canvas and was mainly used to validate without having to repeatedly re-launch Altium. Unfortunately doesn't load the step file, but will load the raw obj model, you can also use it to manually save the step or obj.

# Installation

There are two ways to install EasyEDA-Loader.

## Option 1: Install from a Release Archive (Recommended)

Download the latest release archive from the [Releases](https://github.com/your-org/EasyEDALoader/releases) page. The archive contains a ready-to-deploy `dist\` folder alongside `Deploy.ps1`.

1. Extract the archive to any location.
2. Open a PowerShell terminal in that directory.
3. Run the deploy script:

```powershell
.\Deploy.ps1
```

The script will:
- Automatically discover your Altium Designer installation (no hardcoded GUIDs).
- Copy plugin files into the correct `Extensions\EasyEDA-Loader` folder.
- Register the extension in `ExtensionsRegistry.xml`.
- Prompt if Altium Designer is currently running (use `-Force` to skip the prompt).

```powershell
.\Deploy.ps1 -Force      # Deploy even if Altium is running
.\Deploy.ps1 -SkipRegistry  # Copy files only, skip registry update
```

## Option 2: Build & Deploy from Source

If you have the source code, use the build script suite to build, package, and deploy in one step.

### Script Overview

| Script | Description |
|--------|-------------|
| `_Shared.ps1` | Shared helper functions (dot-sourced by other scripts, do not run directly) |
| `Build.ps1` | Builds the plugin (via MSBuild) and optionally the Standalone app (via dotnet) |
| `Package.ps1` | Stages built artifacts into `dist\`, excluding Altium SDK and DevExpress DLLs (provided at runtime) |
| `Deploy.ps1` | Copies `dist\` into the Altium Extensions folder and registers in `ExtensionsRegistry.xml` |
| `All.ps1` | Orchestrates Build, Package, and Deploy in sequence |

### Prerequisites

- Visual Studio 2022 or later with MSBuild (for plugin build)
- .NET SDK 6.0+ (for Standalone build, if using `-IncludeStandalone`)
- Altium Designer 24 installed on the target machine
- PowerShell 5.1+ (run as Administrator when deploying)

### Quick Start

```powershell
cd path\to\EasyEDALoader

# Build, package, and deploy in one command:
.\All.ps1

# Debug build:
.\All.ps1 -Configuration Debug

# Deploy even if Altium Designer is currently running:
.\All.ps1 -Force

# Also build the Standalone viewer app:
.\All.ps1 -IncludeStandalone
```

### Running Steps Individually

```powershell
# Step 1: Build the plugin (and optionally Standalone)
.\Build.ps1
.\Build.ps1 -Configuration Debug
.\Build.ps1 -IncludeStandalone

# Step 2: Stage files into dist\
.\Package.ps1
.\Package.ps1 -IncludeStandalone

# Step 3: Deploy into Altium Designer
.\Deploy.ps1
.\Deploy.ps1 -Force
.\Deploy.ps1 -SkipRegistry
```

### DLL Bundling Strategy

The build scripts follow the same deployment pattern as the AltiumMCP extension. Only third-party and polyfill DLLs that Altium Designer does **not** ship are bundled alongside the plugin:

**Bundled with the plugin:**
| DLL | Purpose |
|-----|---------|
| `EasyEDA-Loader.dll` | The plugin |
| `EasyEDA-Loader.Ins` | Server manifest |
| `EasyEDA-Loader.rcs` | Menu resources |
| `EasyEDA-Loader.dll.config` | Assembly binding redirects |
| `Microsoft.Bcl.AsyncInterfaces.dll` | Async/await polyfill |
| `Newtonsoft.Json.dll` | JSON serialization |
| `System.Buffers.dll` | Span\<T\> / Memory\<T\> |
| `System.IO.Pipelines.dll` | Stream pipe processing |
| `System.Memory.dll` | Memory\<T\> support |
| `System.Numerics.Vectors.dll` | SIMD Vector\<T\> |
| `System.Runtime.CompilerServices.Unsafe.dll` | Unsafe ref operations |
| `System.Threading.Tasks.Extensions.dll` | ValueTask support |
| `System.ValueTuple.dll` | ValueTuple support |

**NOT bundled (provided by Altium Designer at runtime):**
`Altium.SDK.dll`, `Altium.SDK.Interfaces.dll`, `Altium.Controls.dll`, `Altium.Controls.Skins.dll`, and all `DevExpress.*.dll` (v22.1).

## Known Issues
The 3D model is not places *quite* right, something is still different from the reported translation and the actual. See [EeFootprint3dModel](/EasyEDA-Loader/FootprintShapes/EeFootprint3dModel.cs) for more information and how and where it retrieves model info from.
