using DXP;
using PCB;
using SCH;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEDA_Loader
{
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class EasyEDALoaderModule : ServerModule
    {
        private bool noGUIMode;

        public EasyEDALoaderModule(IClient argClient)
          : base(argClient, "EasyEDA-Loader")
        {
            noGUIMode = argClient.ProductInfo().SupportsUIFeature("NoGUI", false);
        }

        protected override IServerDocument NewDocumentInstance(string argKind, string argFileName) => (IServerDocument)null;

        protected override void InitializeCommands()
        {
            RegisterCommand("EasyEDARun", new CommandProc(Run));
            RegisterCommand("EasyEDAImport", new CommandProc(BatchImport));
        }

        private void RegisterCommand(string argCommandId, CommandProc commandProc) => ((DXP.CommandLauncher)CommandLauncher).RegisterCommand(argCommandId, (CommandProc)((IServerDocumentView view, ref string parameters) =>
        {
            try
            {
                commandProc(view, ref parameters);
            }
            catch (Exception ex)
            {
                if (noGUIMode)
                {
                    throw;
                }
                else
                {
                    int num = (int)MessageBox.Show(ex.Message, "EasyEDA Loader Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }
        }));

        private static void PlaceComponent(string schLibraryPath, string partName)
        {
            var currentSheet = AltiumApi.GlobalVars.SCHServer.GetCurrentSchDocument();
            if (currentSheet == null)
                throw new InvalidOperationException("Must be in a schematic document before placing a component.");

            var newComponent = AltiumApi.GlobalVars.SCHServer.LoadComponentFromLibrary(partName, schLibraryPath);
            currentSheet.AddSchObject(newComponent);
            newComponent.MoveToXY(0, 0);
            newComponent.SetState_Orientation(TRotationBy90.eRotate0);

            // Hand the part to Altium's own Paste, which drags it on the cursor as a
            // preview until the user clicks (Esc cancels). Uses the clipboard.
            bool onSheet = true;
            try
            {
                DXP.Utils.RunCommand("Sch:Deselect", "Action=All");
                newComponent.SetState_Selection(true);
                DXP.Utils.RunCommand("Sch:Copy", "");
                currentSheet.RemoveSchObject(newComponent);
                onSheet = false;
                currentSheet.GraphicallyInvalidate();
                DXP.Utils.RunCommand("Sch:Paste", "");
                return;
            }
            catch (Exception ex)
            {
                ImportLog.Error($"Paste-to-cursor placement failed for {partName}, falling back to click-to-place", ex);
            }

            // Fallback: crosshair, click where the part goes. Esc cancels; the part stays
            // in EasyEDA.SchLib either way.
            if (onSheet)
                currentSheet.RemoveSchObject(newComponent);
            var location = new DXP.Point();
            if (!currentSheet.ChooseLocationInteractively(ref location, $"Click to place {partName}"))
            {
                currentSheet.GraphicallyInvalidate();
                return;
            }
            currentSheet.AddSchObject(newComponent);
            newComponent.MoveToXY(location.X, location.Y);
            currentSheet.GraphicallyInvalidate();
        }

        private void Run(
          IServerDocumentView argContext,
          ref string argParameters)
        {
            Dialog dialog = new Dialog();
            DialogResult result = dialog.ShowDialog();
            if (result != DialogResult.OK || dialog.SelectedComponents.Count == 0)
                return;

            var currentDoc = AltiumApi.GlobalVars.Client.GetCurrentView().GetOwnerDocument();
            if (currentDoc == null)
            {
                MessageBox.Show("Must be in a schematic document before running", "EasyEDA Loader Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }

            ImportSelections(dialog.SelectedComponents, currentDoc, dialog.CloseDocuments,
                (schLibraryPath, partNames) =>
                {
                    // Only the last part goes on the cursor
                    string last = partNames.FindLast(n => n != null);
                    if (dialog.PlaceInSchematic && last != null)
                        PlaceComponent(schLibraryPath, last);
                },
                (part, ex) => MessageBox.Show($"Failed to process component {part}: {ex.Message}", "EasyEDA Loader Error", MessageBoxButtons.OK, MessageBoxIcon.Hand));
        }

        public static string BatchResultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltiumEE", "batch-import.txt");

        // EasyEDA-Loader:EasyEDAImport, no dialog. Parameters (Altium style, '|' separated):
        //   Parts=C5219272,C13585,C13585   LCSC numbers, a repeated number places the part again
        //   Place=True                     place on the current schematic in a grid (default True)
        //   X=1000|Y=7000                  grid origin in mils (top-left)
        //   StepX=1000|StepY=1200|PerRow=6 grid pitch in mils and parts per row
        // Writes a per-part result to BatchResultPath; no message boxes.
        private void BatchImport(IServerDocumentView argContext, ref string argParameters)
        {
            var p = ParseParameters(argParameters);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] EasyEDAImport {argParameters}");

            var ids = (p.TryGetValue("PARTS", out var s) ? s : "")
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool place = !p.TryGetValue("PLACE", out var pl) || !pl.Equals("False", StringComparison.OrdinalIgnoreCase);
            int x0 = GetInt(p, "X", 1000), y0 = GetInt(p, "Y", 7000);
            int stepX = GetInt(p, "STEPX", 1000), stepY = GetInt(p, "STEPY", 1200), perRow = Math.Max(1, GetInt(p, "PERROW", 6));

            var currentDoc = AltiumApi.GlobalVars.Client.GetCurrentView()?.GetOwnerDocument();
            if (currentDoc == null || ids.Length == 0)
            {
                report.AppendLine(ids.Length == 0 ? "ERROR no Parts= given" : "ERROR no current document");
                File.WriteAllText(BatchResultPath, report.ToString());
                return;
            }

            // Fetch every distinct part once (all requests go through ApiCache)
            var api = new EasyedaApi();
            var fetched = new System.Collections.Generic.Dictionary<string, ComponentSelection>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (fetched.ContainsKey(id)) continue;
                try
                {
                    var parts = Task.Run(() => api.SearchProductInfoAsync(id)).GetAwaiter().GetResult();
                    var info = parts.Find(q => string.Equals(q.Part, id, StringComparison.OrdinalIgnoreCase)) ?? (parts.Count > 0 ? parts[0] : null);
                    if (info == null) throw new InvalidOperationException("not found on EasyEDA");
                    var root = Task.Run(() => api.GetComponentJsonAsync(info.Part, CancellationToken.None)).GetAwaiter().GetResult();
                    if (root?.Component == null) throw new InvalidOperationException("no component data");
                    var fp = root.Component.PackageDetail?.Footprint;
                    fetched[id] = new ComponentSelection
                    {
                        PartInfo = info,
                        Root = root,
                        IncludeFootprint = fp != null,
                        Include3dModel = fp?.GetModel() != null,
                    };
                }
                catch (Exception ex)
                {
                    report.AppendLine($"FAIL {id} fetch: {ex.Message}");
                }
            }

            var distinct = new System.Collections.Generic.List<ComponentSelection>(fetched.Values);
            var nameById = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string libPath = null;
            ImportSelections(distinct, currentDoc, false,
                (schLibraryPath, partNames) =>
                {
                    libPath = schLibraryPath;
                    int i = 0;
                    foreach (var kv in fetched)
                    {
                        if (partNames[i] != null) nameById[kv.Key] = partNames[i];
                        i++;
                    }
                },
                (part, ex) => report.AppendLine($"FAIL {part} import: {ex.Message}"));

            if (place && libPath != null)
            {
                var sheet = AltiumApi.GlobalVars.SCHServer.GetCurrentSchDocument();
                if (sheet == null)
                    report.AppendLine("ERROR current document is not a schematic, nothing placed");
                int n = 0;
                foreach (var id in ids)
                {
                    if (sheet == null || !nameById.TryGetValue(id, out var name)) continue;
                    try
                    {
                        var comp = AltiumApi.GlobalVars.SCHServer.LoadComponentFromLibrary(name, libPath);
                        if (comp == null) throw new InvalidOperationException("not in library");
                        sheet.AddSchObject(comp);
                        int x = x0 + (n % perRow) * stepX, y = y0 - (n / perRow) * stepY;
                        comp.MoveToXY(AltiumApi.MilsToCoord(x), AltiumApi.MilsToCoord(y));
                        comp.SetState_Orientation(TRotationBy90.eRotate0);
                        report.AppendLine($"OK   {id} {name} at {x},{y} mil");
                        n++;
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"FAIL {id} place: {ex.Message}");
                    }
                }
                sheet?.GraphicallyInvalidate();
            }
            else
            {
                foreach (var kv in nameById) report.AppendLine($"OK   {kv.Key} {kv.Value} (library only)");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BatchResultPath));
            File.WriteAllText(BatchResultPath, report.ToString());
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseParameters(string args)
        {
            var d = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in (args ?? "").Split('|'))
            {
                int eq = item.IndexOf('=');
                if (eq > 0) d[item.Substring(0, eq).Trim().ToUpperInvariant()] = item.Substring(eq + 1).Trim();
            }
            return d;
        }

        private static int GetInt(System.Collections.Generic.Dictionary<string, string> d, string key, int def)
            => d.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : def;

        // Imports symbol / footprint / 3D model for each selection into Documents\AltiumEE\EasyEDA.*lib,
        // then calls place(schLibraryPath, libRefs) with the library names in selection order.
        private static void ImportSelections(
            System.Collections.Generic.List<ComponentSelection> selections, IServerDocument currentDoc, bool closeDocuments,
            Action<string, System.Collections.Generic.List<string>> place, Action<string, Exception> onError)
        {
            var partNames = new System.Collections.Generic.List<string>();
            var ctx = new CancellationTokenSource();
            var api = new EasyedaApi();
            ImportLog.Reset();

            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string libraryPath = Path.Combine(documentsPath, "AltiumEE");
            Directory.CreateDirectory(libraryPath);
            string pcbLibraryPath = Path.Combine(libraryPath, "EasyEDA.pcblib");
            string schLibraryPath = Path.Combine(libraryPath, "EasyEDA.schlib");

            var pcbDocument = AltiumApi.GlobalVars.Client.OpenDocument("PcbLib", pcbLibraryPath);
            AltiumApi.GlobalVars.Client.ShowDocument(pcbDocument);
            var pcbLib = AltiumApi.GlobalVars.PCBServer.GetCurrentPCBLibrary();

            var schDocument = AltiumApi.GlobalVars.Client.OpenDocument("SchLib", schLibraryPath);
            AltiumApi.GlobalVars.Client.ShowDocument(schDocument);
            var schLib = EESCH.GetCurrentSchLibrary();

            // Process each selected component
            foreach (var selection in selections)
            {
                partNames.Add(null);
                try
                {
                    var root = selection.Root;
                    var owner_id = root.Component.Owner.Uuid;
                    var ee_footprint = root.Component.PackageDetail.Footprint;
                    var ee_symbol = root.Component.Symbol;
                    string package = ee_footprint.Head.Parameters.Package;
                    EeFootprint3dModel model = selection.Include3dModel ? ee_footprint.GetModel() : null;

                    // Prefetch model if we can
                    Task<byte[]> modelTask = model != null ? Task.Run(() => api.LoadModelAsync(model.Uuid, ctx.Token)) : null;
                    Task<byte[]> rawModelTask = model != null ? Task.Run(() => api.LoadRawModelAsync(model.Uuid, ctx.Token)) : null;

                    // Get product info (use cached from search if available)
                    EasyedaApi.ProductInfo productInfo = selection.PartInfo.Info;

                    // Create PCB footprint if requested
                    if (selection.IncludeFootprint)
                    {
                        AltiumApi.GlobalVars.Client.ShowDocument(pcbDocument);
                        var libComp = pcbLib.GetComponentByName(package);
                        bool createdFootprint = false;
                        if (libComp == null)
                        {
                            libComp = EEPCB.CreateFootprintInLib(package, root.Component.PackageDetail.Title);
                            createdFootprint = libComp != null;
                        }

                        if (createdFootprint)
                        {
                            AltiumApi.GlobalVars.PCBServer.PreProcess();
                            var footprintContext = new EeFootprintContext
                            {
                                Box = ee_footprint.BoundingBox,
                                Layers = ee_footprint.Layers,
                                CancelToken = ctx.Token,
                                Exception = (Exception ex) =>
                                {
                                    ImportLog.Error($"footprint '{package}'", ex);
                                    return true;
                                },
                                ModelTask = modelTask,
                                RawModelTask = rawModelTask,
                            };
                            ee_footprint.AddToComponent(libComp, footprintContext);
                            AltiumApi.GlobalVars.PCBServer.PostProcess();
                            pcbDocument.DoFileSave("PcbLib");
                        }
                    }

                    // Create schematic symbol
                    string partName = ee_symbol.Head.Parameters.Name;
                    partNames[partNames.Count - 1] = partName;
                    string description = productInfo?.Description ?? partName;

                    var existingComponent = schLib.GetState_SchComponentByLibRef(partName);
                    if (existingComponent == null)
                    {
                        var component = EESCH.CreateComponent(partName, description, ee_symbol.Head.Parameters.Pre);
                        if (schLib != null && component != null)
                        {
                            AltiumApi.GlobalVars.PCBServer.PreProcess();
                            string passiveKind = CelestialPassives.Classify(ee_symbol, productInfo?.Description ?? selection.PartInfo.Description);
                            if (passiveKind != null)
                                CelestialPassives.CreateComponent(schLib, component, pcbLibraryPath, package, ee_symbol, passiveKind);
                            else
                                SymbolDrawing.CreateComponent(schLib, component, pcbLibraryPath, package, ee_symbol);

                            if (productInfo?.Parameters != null)
                            {
                                foreach (var kvp in productInfo.Parameters)
                                {
                                    if (passiveKind != null && kvp.Key == "Value")
                                        CelestialPassives.AddValueParameter(component, passiveKind, kvp.Value);
                                    else
                                        EESCH.AddParameter(component, kvp.Key, kvp.Value);
                                }
                            }

                            // No Value from LCSC's product info: use the one in the symbol data.
                            bool hasValue = productInfo?.Parameters?.ContainsKey("Value") == true;
                            if (passiveKind != null && !hasValue && !string.IsNullOrWhiteSpace(ee_symbol.Head.Parameters.Value))
                                CelestialPassives.AddValueParameter(component, passiveKind, ee_symbol.Head.Parameters.Value);

                            AltiumApi.GlobalVars.PCBServer.PostProcess();
                            schLib.SetState_Current_SchComponent(component);
                            schLib.GraphicallyInvalidate();
                            schDocument.DoFileSave("SchLib");
                        }
                    }

                }
                catch (Exception ex)
                {
                    onError(selection.PartInfo?.Part ?? selection.PartInfo?.Name, ex);
                }
            }

            // Return to the original document we started in, then place
            AltiumApi.GlobalVars.Client.ShowDocument(currentDoc);
            place(schLibraryPath, partNames);

            // Primitive failures are logged silently to ImportLog.LogPath (no popup)

            // Close the library documents if requested
            if (closeDocuments)
            {
                AltiumApi.GlobalVars.Client.CloseDocument(pcbDocument);
                AltiumApi.GlobalVars.Client.CloseDocument(schDocument);
            }
        }
    }
}
