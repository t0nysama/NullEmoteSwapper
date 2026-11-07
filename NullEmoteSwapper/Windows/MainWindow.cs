using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using Penumbra.Api.IpcSubscribers;
using NullEmoteSwapper.Core;
using NullEmoteSwapper.Core.Models;
using NullEmoteSwapper.Pap;
using NullEmoteSwapper.Files;

namespace NullEmoteSwapper.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private enum WizardStep
        {
            SelectMod = 0,
            SelectSource = 1,
            SelectTarget = 2
        }

        private readonly Plugin plugin;

        // Penumbra IPC
        private readonly GetModList getModList;
        private readonly ReloadMod reloadMod;

        // State: mods
        private Dictionary<string, string> modList = new();
        private string modSearch = string.Empty;
        private string? selectedModDirectory;
        private string? selectedModName;
        private bool isLoadingMods;
        private bool isScanningMod;
        private string? lastScanError;
        private string? lastScanMessage;
        string penRoot = String.Empty;

        // State: emote scanning
        private List<RemappableOption> remappableOptions = new();
        private bool showUnsafeOptions = false;
        private RemappableOption? selectedOption = null;
        private List<SourceEmoteAssignment> sourceEmoteAssignments = new();
        private bool isCreatingOverrides;

        // Step navigation
        private WizardStep currentStep = WizardStep.SelectMod;

        public MainWindow(Plugin pl)
            : base("NullEmoteSwapper", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            plugin = pl;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(900, 550),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            getModList = new GetModList(Plugin.PluginInterface);
            reloadMod = new ReloadMod(Plugin.PluginInterface);

            _ = LoadModListAsync();
        }

        public void Dispose()
        {
        }

        // ======================================
        // Backend: Load mod list
        // ======================================
        private async Task LoadModListAsync()
        {
            isLoadingMods = true;
            Plugin.Log.Debug("[NullEmoteSwapper] Loading Penumbra mod list.");
            try
            {
                penRoot = ResolvePenumbraRoot();
                Plugin.Log.Debug("[NullEmoteSwapper] Penumbra mod root resolved as {PenumbraRoot}.",
                    string.IsNullOrWhiteSpace(penRoot) ? "<unavailable>" : penRoot);
                await Task.Run(() =>
                {
                    try
                    {
                        modList = getModList.Invoke();
                        Plugin.Log.Information("[NullEmoteSwapper] Penumbra returned {ModCount} mods.", modList.Count);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error(ex, "[NullEmoteSwapper] Failed to load the Penumbra mod list.");
                        Svc.Chat.PrintError($"[NullEmoteSwapper] Failed to load mod list: {ex.Message}");
                        modList = new Dictionary<string, string>();
                    }
                });
            }
            finally
            {
                isLoadingMods = false;
                Plugin.Log.Debug("[NullEmoteSwapper] Penumbra mod list loading finished.");
            }
        }

        private string ResolvePenumbraRoot()
        {
            var root = PenumbraDirectoryResolver.GetPenumbraDirectory();
            if (!string.IsNullOrWhiteSpace(root))
                return root;

            root = plugin.Configuration.PenumbraPath;
            return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root) ? root : string.Empty;
        }

        // ======================================
        // Backend: Scan selected mod
        // ======================================
        private async Task ScanSelectedModAsync()
        {
            if (isScanningMod)
                return;

            var selectedDirectory = selectedModDirectory;
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                lastScanError = "Select a source mod before scanning.";
                Plugin.Log.Warning("[NullEmoteSwapper] Mod scan requested without a selected mod.");
                return;
            }

            if (!modList.ContainsKey(selectedDirectory))
            {
                lastScanError = "The selected mod is no longer in Penumbra's mod list. Refresh the list and select it again.";
                Plugin.Log.Warning("[NullEmoteSwapper] Scan cancelled because mod {ModDirectory} is not in the current Penumbra mod list.", selectedDirectory);
                return;
            }

            isScanningMod = true;
            lastScanError = null;
            lastScanMessage = "Scanning mod JSON files for emote-linked PAP overrides...";
            remappableOptions.Clear();
            selectedOption = null;
            sourceEmoteAssignments.Clear();
            Plugin.Log.Information("[NullEmoteSwapper] Starting scan for mod {ModName} ({ModDirectory}).", selectedModName ?? selectedDirectory, selectedDirectory);
            Svc.Chat.Print($"[NullEmoteSwapper] Scanning {selectedModName ?? selectedDirectory} for emote PAP mappings...");

            try
            {
                var penRoot = ResolvePenumbraRoot();
                if (string.IsNullOrWhiteSpace(penRoot))
                {
                    lastScanError = "Could not locate Penumbra's mod directory. Verify its setting and refresh the mod list.";
                    lastScanMessage = null;
                    Plugin.Log.Error("[NullEmoteSwapper] Scan stopped because Penumbra's mod directory could not be resolved.");
                    Svc.Chat.PrintError("[NullEmoteSwapper] Scan failed: Penumbra's mod directory could not be resolved.");
                    return;
                }

                var modFolder = Path.Combine(penRoot, selectedDirectory);
                if (!Directory.Exists(modFolder))
                {
                    lastScanError = $"The selected mod folder could not be found: {modFolder}";
                    lastScanMessage = null;
                    Plugin.Log.Error("[NullEmoteSwapper] Scan stopped because mod directory {ModFolder} does not exist.", modFolder);
                    Svc.Chat.PrintError($"[NullEmoteSwapper] Scan failed: mod folder was not found ({selectedDirectory}).");
                    return;
                }

                var scanResult = await Task.Run(() =>
                {
                    var options = EmoteOverrideScanner.ScanMod(modFolder, out var diagnostics);
                    return (Options: options, Diagnostics: diagnostics);
                });
                remappableOptions = scanResult.Options;

                var distinctEmoteCount = scanResult.Options
                    .SelectMany(option => option.Entries)
                    .Select(entry => entry.EmoteRowId)
                    .Distinct()
                    .Count();
                Plugin.Log.Information(
                    "[NullEmoteSwapper] Scan finished for {ModDirectory}: {JsonFound} JSON files found, {JsonParsed} parsed, {PapMappings} PAP mappings, {EmoteMappings} recognized emote mappings, {OptionCount} source options, {UniqueEmotes} unique emotes.",
                    selectedDirectory,
                    scanResult.Diagnostics.JsonFilesFound,
                    scanResult.Diagnostics.JsonFilesParsed,
                    scanResult.Diagnostics.PapMappingsFound,
                    scanResult.Diagnostics.EmoteMappingsFound,
                    scanResult.Options.Count,
                    distinctEmoteCount);

                foreach (var option in scanResult.Options)
                {
                    var uniqueEmotes = option.Entries
                        .GroupBy(entry => entry.EmoteRowId)
                        .Select(group => group.First())
                        .Select(entry => $"{entry.EmoteCommand} (row {entry.EmoteRowId})");
                    Plugin.Log.Debug("[NullEmoteSwapper] Source option {GroupName} / {OptionName} contains emotes: {Emotes}.",
                        option.GroupName, option.OptionName, string.Join(", ", uniqueEmotes));
                }

                if (scanResult.Options.Count > 0)
                {
                    lastScanMessage = $"Scan complete: found {scanResult.Options.Count} options from {scanResult.Diagnostics.EmoteMappingsFound} emote-linked PAP mappings across {scanResult.Diagnostics.JsonFilesParsed} JSON files.";
                    currentStep = WizardStep.SelectSource;
                    Svc.Chat.Print($"[NullEmoteSwapper] Scan complete: {scanResult.Options.Count} source option(s), {distinctEmoteCount} unique emote trigger(s).");
                }
                else
                {
                    lastScanMessage = $"Scan complete: checked {scanResult.Diagnostics.JsonFilesParsed} JSON files, found {scanResult.Diagnostics.PapMappingsFound} PAP mappings, and matched {scanResult.Diagnostics.EmoteMappingsFound} to known emote timelines. No emote-based override options were found.";
                    Svc.Chat.PrintError($"[NullEmoteSwapper] Scan complete: no emote PAP mappings recognized ({scanResult.Diagnostics.PapMappingsFound} PAP mappings checked).");
                }
            }
            catch (Exception ex)
            {
                lastScanError = $"Scan failed: {ex.Message}";
                lastScanMessage = null;
                Plugin.Log.Error(ex, "[NullEmoteSwapper] Scan failed for mod {ModDirectory}.", selectedDirectory);
                Svc.Chat.PrintError($"[NullEmoteSwapper] Scan failed: {ex.Message}");
            }
            finally
            {
                isScanningMod = false;
                Plugin.Log.Debug("[NullEmoteSwapper] Scan operation ended for mod {ModDirectory}.", selectedDirectory);
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            Plugin.Log.Debug("[NullEmoteSwapper] Main window opened; refreshing the Penumbra mod list.");
            _ = LoadModListAsync();
        }

        // ======================================
        // Draw root
        // ======================================
        public override void Draw()
        {
            var totalHeight = ImGui.GetContentRegionAvail().Y;

            float headerHeight = totalHeight * 0.18f;
            float contentHeight = totalHeight - headerHeight;

            // =========================
            // HEADER (15%)
            // =========================
            using (ImRaii.Child("NullEmoteSwapperHeader", new Vector2(-1, headerHeight), false))
            {
                DrawHeader();
                ImGui.Spacing();
                DrawStepNavigation();
            }

            // =========================
            // MAIN CONTENT
            // =========================
            using (ImRaii.Child("NullEmoteSwapperContent", new Vector2(-1, contentHeight), true))
            {
                switch (currentStep)
                {
                    case WizardStep.SelectMod:
                        DrawStepCard_SelectMod();
                        break;

                    case WizardStep.SelectSource:
                        DrawStepCard_SelectSource();
                        break;

                    case WizardStep.SelectTarget:
                        DrawStepCard_SelectTarget();
                        break;
                }
            }

        }

        private bool HasCompleteTargetAssignments()
        {
            if (sourceEmoteAssignments.Count == 0
                || sourceEmoteAssignments.Any(assignment => assignment.TargetEmote == null))
                return false;

            return sourceEmoteAssignments.GroupBy(a => a.SourceOption).All(group =>
                group.Select(a => a.TargetEmote!.RowId).Distinct().Count() == group.Count());
        }

        private static string GetGamePathDirectory(string gamePath)
        {
            var platformPath = gamePath.Replace('/', Path.DirectorySeparatorChar);
            return (Path.GetDirectoryName(platformPath) ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }



        // ======================================
        // Header
        // ======================================
        private void DrawHeader()
        {
            // Title
            ImGui.TextColored(new Vector4(0.85f, 0.9f, 1.0f, 1.0f), "NullEmoteSwapper – Emote Override Wizard");

            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));
            ImGui.TextWrapped("NullEmoteSwapper creates its own override group and PAP copies inside each mod. Original options and files stay untouched.");
            ImGui.PopStyleColor();

            if (!string.IsNullOrEmpty(lastScanError))
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
                ImGui.TextWrapped($"Error: {lastScanError}");
                ImGui.PopStyleColor();
            }
        }

        // ======================================
        // Step navigation bar
        // ======================================
        private void DrawStepNavigation()
        {
            bool hasMod = selectedModDirectory != null;
            var filtered = remappableOptions.Where(o => showUnsafeOptions || o.IsSafeToRemap).ToList();
            bool hasSource = hasMod && filtered.Count > 0;
            bool hasSelectedSource = hasSource && sourceEmoteAssignments.Count > 0;
            bool hasTarget = hasSelectedSource && HasCompleteTargetAssignments();

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 5f));

            DrawStepButton("1. Mod", WizardStep.SelectMod, true, hasMod);
            ImGui.SameLine();
            DrawStepButton("2. Source options", WizardStep.SelectSource, hasSource, hasSelectedSource);
            ImGui.SameLine();
            DrawStepButton("3. Target emote", WizardStep.SelectTarget, hasSelectedSource, hasTarget);

            ImGui.PopStyleVar(2);
        }

        private void DrawStepButton(string label, WizardStep step, bool enabled, bool done)
        {
            var accent = new Vector4(0.25f, 0.55f, 0.95f, 1f);
            var accentDone = new Vector4(0.25f, 0.7f, 0.4f, 1f);
            var baseColor = new Vector4(0.18f, 0.18f, 0.22f, 1f);

            float alpha = enabled ? 1f : 0.4f;
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);

            bool isCurrent = currentStep == step;
            var color = isCurrent ? accent : (done ? accentDone : baseColor);

            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, color);

            if (ImGui.Button(label) && enabled)
                currentStep = step;

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
        }

        // ======================================
        // Card helpers
        // ======================================
        private void BeginCard(string id, string title, string subtitle = "")
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.07f, 0.07f, 0.11f, 1f));

            ImGui.BeginChild(id, new Vector2(0, 0), true);

            ImGui.TextColored(new Vector4(0.9f, 0.9f, 1f, 1f), title);
            if (!string.IsNullOrEmpty(subtitle))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.75f, 0.8f, 1f));
                ImGui.TextWrapped(subtitle);
                ImGui.PopStyleColor();
            }

            ImGui.Separator();
            ImGui.Spacing();
        }

        private void EndCard()
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }

        // ======================================
        // STEP 1 – Mod selection
        // ======================================
        private void DrawStepCard_SelectMod()
        {
            BeginCard("NullEmoteSwapperStepMod", "Step 1 – Select source mod",
                "Select the Penumbra mod that contains the dance / emote you want to rebind.");

            if (isLoadingMods)
            {
                ImGui.Text("Loading mods...");
                EndCard();
                return;
            }

            ImGui.PushItemWidth(320f);
            ImGui.InputText("Search mods", ref modSearch, 200);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Refresh list"))
            {
                Plugin.Log.Information("[NullEmoteSwapper] User requested a Penumbra mod list refresh.");
                Svc.Chat.Print("[NullEmoteSwapper] Refreshing the Penumbra mod list...");
                _ = LoadModListAsync();
            }

            ImGui.Spacing();

            var flags = ImGuiTableFlags.RowBg
                        | ImGuiTableFlags.BordersInnerV
                        | ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("NullEmoteSwapperModTable", 2, flags))
            {
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("Folder");
                ImGui.TableHeadersRow();

                foreach (var kv in modList)
                {
                    var dir = kv.Key;
                    var name = kv.Value;

                    if (!string.IsNullOrEmpty(modSearch)
                        && !name.Contains(modSearch, StringComparison.OrdinalIgnoreCase)
                        && !dir.Contains(modSearch, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    bool isSelected = selectedModDirectory == dir;
                    string label = $"{name}##{dir}";

                    if (ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        selectedModDirectory = dir;
                        selectedModName = name;
                        remappableOptions.Clear();
                        selectedOption = null;
                        sourceEmoteAssignments.Clear();
                        lastScanError = null;
                        lastScanMessage = null;
                        Plugin.Log.Information("[NullEmoteSwapper] Selected source mod {ModName} ({ModDirectory}).", name, dir);
                        Svc.Chat.Print($"[NullEmoteSwapper] Selected source mod: {name}.");
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(dir);
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            if (selectedModDirectory == null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.75f, 0.75f, 1f));
                ImGui.Text("Select a mod above to continue.");
                ImGui.PopStyleColor();
                EndCard();
                return;
            }

            if (string.IsNullOrWhiteSpace(penRoot))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.75f, 0.35f, 1f));
                ImGui.TextWrapped("Could not locate the Penumbra mod folder. Check that Penumbra is installed and its mod directory is configured, then refresh the list.");
                ImGui.PopStyleColor();
                EndCard();
                return;
            }

            ImGui.Text($"Selected: {selectedModName} ({selectedModDirectory})");

            if (ImGui.Button(isScanningMod ? "Scanning..." : "Scan mod for emote overrides"))
            {
                if (!isScanningMod)
                    _ = ScanSelectedModAsync();
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));
            ImGui.TextDisabled("Reads JSON + PAP in this mod to find emote-based overrides.");
            ImGui.PopStyleColor();

            if (isScanningMod && !string.IsNullOrEmpty(lastScanMessage))
                ImGui.TextWrapped(lastScanMessage);

            if (!isScanningMod && !string.IsNullOrEmpty(lastScanMessage))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.9f, 0.65f, 1f));
                ImGui.TextWrapped(lastScanMessage);
                ImGui.PopStyleColor();
            }

            if (NullEmoteSwapperFileManager.NullEmoteSwapperExists(Path.Combine(penRoot, selectedModDirectory)))
            {
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.Spacing();
                // Make the remove button red
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.80f, 0.10f, 0.15f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.90f, 0.20f, 0.20f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.70f, 0.05f, 0.10f, 1.0f));

                if (ImGui.Button("Remove NullEmoteSwapper from mod"))
                {
                    var modPath = Path.Combine(penRoot, selectedModDirectory);
                    Plugin.Log.Information("[NullEmoteSwapper] User requested removal of plugin overrides from mod {ModDirectory}.", selectedModDirectory);
                    Svc.Chat.Print("[NullEmoteSwapper] Removing this plugin's override files and metadata...");
                    bool isDeleted = NullEmoteSwapperFileManager.RemoveNullEmoteSwapper(modPath);
                    if (isDeleted)
                    {
                        Plugin.Log.Information("[NullEmoteSwapper] Removed plugin metadata and generated PAP files from mod {ModDirectory}.", selectedModDirectory);
                        Svc.Chat.Print("[NullEmoteSwapper] Removed existing NullEmoteSwapper overrides from the selected mod.");
                    }
                    else
                    {
                        Plugin.Log.Warning("[NullEmoteSwapper] No plugin override files were removed from mod {ModDirectory}.", selectedModDirectory);
                        Svc.Chat.Print("[NullEmoteSwapper] No existing NullEmoteSwapper overrides found in the selected mod.");
                    }

                    try
                    {
                        Plugin.Log.Debug("[NullEmoteSwapper] Requesting Penumbra reload after override removal for {ModDirectory}.", selectedModDirectory);
                        reloadMod.Invoke(selectedModDirectory);
                        Svc.Chat.Print("[NullEmoteSwapper] Penumbra mod reload requested.");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error(ex, "[NullEmoteSwapper] Penumbra reload failed after removal for {ModDirectory}.", selectedModDirectory);
                        Svc.Chat.PrintError($"[NullEmoteSwapper] Override files were removed, but Penumbra reload failed: {ex.Message}");
                    }
                }

                ImGui.PopStyleColor(3); // Button, ButtonHovered, ButtonActive
            }

            EndCard();
        }

        // ======================================
        // STEP 2 – Source option selection
        // ======================================
        private void DrawStepCard_SelectSource()
        {
            BeginCard("NullEmoteSwapperStepSource", "Step 2 – Choose source options",
                "Select one or more options. Each gets its own emote assignments, even when they share an emote.");

            if (selectedModDirectory == null)
            {
                ImGui.Text("No mod selected yet (Step 1).");
                EndCard();
                return;
            }

            if (isScanningMod)
            {
                ImGui.Text("Scanning mod...");
                EndCard();
                return;
            }

            if (remappableOptions.Count == 0)
            {
                ImGui.Text("No emote-based PAP overrides detected in this mod.");
                EndCard();
                return;
            }

            //ImGui.Checkbox("Show unsafe / multi-source options", ref showUnsafeOptions);
            ImGui.Spacing();

            var filtered = remappableOptions
                .Where(o => showUnsafeOptions || o.IsSafeToRemap)
                .ToList();

            if (filtered.Count == 0)
            {
                ImGui.Text("No safe options found with single PAP source.");
                EndCard();
                return;
            }

            var flags = ImGuiTableFlags.RowBg
                        | ImGuiTableFlags.BordersInnerV
                        | ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("NullEmoteSwapperOverrideTable", 3, flags))
            {
                ImGui.TableSetupColumn("Group");
                ImGui.TableSetupColumn("Option");
                ImGui.TableSetupColumn("Emotes");
                ImGui.TableHeadersRow();

                foreach (var opt in filtered)
                {
                    bool isSelected = sourceEmoteAssignments.Any(a => ReferenceEquals(a.SourceOption, opt));

                    string emotesDisplay = string.Join(", ",
                        opt.Entries
                            .GroupBy(entry => entry.EmoteRowId)
                            .Select(group => group.First())
                            .Select(e =>
                            string.IsNullOrEmpty(e.EmoteCommand)
                                ? e.EmoteName
                                : $"{e.EmoteName} ({e.EmoteCommand})"));

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    var label = $"{opt.GroupName}##{opt.SourceId}";

                    if (ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.SpanAllColumns) && !isCreatingOverrides)
                    {
                        selectedOption = opt;
                        if (isSelected)
                            sourceEmoteAssignments.RemoveAll(a => ReferenceEquals(a.SourceOption, opt));
                        else
                            sourceEmoteAssignments.AddRange(OptionAssignmentPlanner.Build(opt));
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(opt.OptionName);

                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(emotesDisplay);
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            ImGui.Text($"Selected {sourceEmoteAssignments.Select(a => a.SourceOption).Distinct().Count()} option(s).");
            if (sourceEmoteAssignments.Count > 0 && ImGui.Button("Assign emotes to selected options"))
                currentStep = WizardStep.SelectTarget;

            if (selectedOption == null || !filtered.Contains(selectedOption))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));
                ImGui.Text("Select options above, then continue to Step 3.");
                ImGui.PopStyleColor();
                EndCard();
                return;
            }

            var selected = selectedOption;

            ImGui.TextColored(new Vector4(0.85f, 0.9f, 1f, 1f), "Selected option");
            ImGui.Text($"Group:  {selected.GroupName}");
            ImGui.Text($"Option: {selected.OptionName}");

            ImGui.Spacing();
            ImGui.Text("Affected emotes / files:");

            var flagsDetails = ImGuiTableFlags.RowBg
                               | ImGuiTableFlags.BordersInnerV
                               | ImGuiTableFlags.SizingStretchSame;

            if (ImGui.BeginTable("NullEmoteSwapperSelectedOptionTable", 4, flagsDetails))
            {
                ImGui.TableSetupColumn("Emote");
                ImGui.TableSetupColumn("Command");
                ImGui.TableSetupColumn("Game path");
                ImGui.TableSetupColumn("Modded PAP");
                ImGui.TableHeadersRow();

                foreach (var e in selected.Entries)
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(e.EmoteName);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(e.EmoteCommand);

                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(e.GamePath);

                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(e.ModdedPapPath);
                }

                ImGui.EndTable();
            }

            EndCard();
        }

        // ======================================
        // STEP 3 – Target emote selection
        // ======================================
        private void DrawStepCard_SelectTarget()
        {
            BeginCard("NullEmoteSwapperStepTarget", "Step 3 – Assign replacement triggers",
                "Assign replacements per option and per emote. Shared source emotes remain independent.");

            if (selectedModDirectory == null)
            {
                ImGui.Text("No mod selected yet (Step 1).");
                EndCard();
                return;
            }

            var filtered = remappableOptions
                .Where(o => showUnsafeOptions || o.IsSafeToRemap)
                .ToList();

            if (filtered.Count == 0)
            {
                ImGui.Text("No source option available. Run Step 2 first.");
                EndCard();
                return;
            }

            if (sourceEmoteAssignments.Count == 0)
            {
                ImGui.Text("No source options selected (Step 2).");
                EndCard();
                return;
            }

            ImGui.Text($"Selected {sourceEmoteAssignments.Select(a => a.SourceOption).Distinct().Count()} option(s), {sourceEmoteAssignments.Count} emote assignment(s).");
            ImGui.TextWrapped("For example: Option A /dance to /wave, and Option B /dance to /cheer. Each option becomes a separate copy in Penumbra.");
            ImGui.Spacing();

            if (sourceEmoteAssignments.Count == 0)
            {
                ImGui.Text("No emote mappings are available in this source option.");
                EndCard();
                return;
            }

            var flags = ImGuiTableFlags.RowBg
                        | ImGuiTableFlags.BordersInnerV
                        | ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("NullEmoteSwapperEmoteAssignmentTable", 4, flags))
            {
                ImGui.TableSetupColumn("Source option");
                ImGui.TableSetupColumn("Source emote");
                ImGui.TableSetupColumn("Source PAPs");
                ImGui.TableSetupColumn("Replacement trigger");
                ImGui.TableHeadersRow();

                foreach (var assignment in sourceEmoteAssignments)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped($"({assignment.SourceOption.GroupName}) {assignment.SourceOption.OptionName}");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(assignment.SourceEmoteName);
                    ImGui.TextDisabled(assignment.SourceEmoteCommand);

                    ImGui.TableNextColumn();
                    var sourcePapCount = assignment.SourceEntries
                        .Select(entry => entry.ModdedPapPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    ImGui.Text(sourcePapCount.ToString());

                    ImGui.TableNextColumn();
                    string preview = assignment.TargetEmote == null
                        ? "Select an emote..."
                        : $"{assignment.TargetEmote.Name} ({assignment.TargetEmote.Command})";
                    if (ImGui.BeginCombo($"##TargetEmote_{assignment.SourceOption.SourceId}_{assignment.SourceEmoteRowId}", preview))
                    {
                        string targetSearch = assignment.TargetSearch;
                        ImGui.PushItemWidth(-1f);
                        if (ImGui.InputText($"Search##TargetEmoteSearch_{assignment.SourceOption.SourceId}_{assignment.SourceEmoteRowId}", ref targetSearch, 100))
                            assignment.TargetSearch = targetSearch;
                        ImGui.PopItemWidth();

                        var results = EmoteLibrary.AllEmotes
                            .Where(emote => string.IsNullOrWhiteSpace(assignment.TargetSearch)
                                || emote.Name.Contains(assignment.TargetSearch, StringComparison.OrdinalIgnoreCase)
                                || emote.Command.Contains(assignment.TargetSearch, StringComparison.OrdinalIgnoreCase))
                            .Take(100);

                        foreach (var emote in results)
                        {
                            bool isSelected = assignment.TargetEmote?.RowId == emote.RowId;
                            if (ImGui.Selectable($"{emote.Name} ({emote.Command})##{assignment.SourceOption.SourceId}_{assignment.SourceEmoteRowId}_{emote.RowId}", isSelected))
                            {
                                assignment.TargetEmote = emote;
                                assignment.TargetSearch = string.Empty;
                                ImGui.CloseCurrentPopup();
                                Plugin.Log.Information("[NullEmoteSwapper] Assigned source emote {SourceCommand} (row {SourceRowId}) to target {TargetCommand} (row {TargetRowId}).",
                                    assignment.SourceEmoteCommand, assignment.SourceEmoteRowId, emote.Command, emote.RowId);
                                Svc.Chat.Print($"[NullEmoteSwapper] Assigned {assignment.SourceEmoteCommand} → {emote.Command}.");
                            }
                        }

                        ImGui.EndCombo();
                    }
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            var duplicateTargets = sourceEmoteAssignments
                .Where(assignment => assignment.TargetEmote != null)
                .GroupBy(assignment => assignment.TargetEmote!.RowId)
                .Where(group => group.Count() > 1)
                .ToList();
            if (duplicateTargets.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.9f, 0.6f, 1f));
                ImGui.TextWrapped("Within one option, each source emote needs a different target. Copies from different options may share a target, but enable only one of those copies at a time in Penumbra.");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            ImGui.TextDisabled("Target emotes keep their normal in-game duration. One-shot targets may end an animation before a full dance finishes.");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.85f, 0.9f, 1f, 1f), "Override summary");
            foreach (var assignment in sourceEmoteAssignments)
            {
                string sourceCommand = string.IsNullOrWhiteSpace(assignment.SourceEmoteCommand)
                    ? assignment.SourceEmoteName
                    : assignment.SourceEmoteCommand;
                string targetCommand = assignment.TargetEmote?.Command ?? "(choose a target)";
                ImGui.TextWrapped($"({assignment.SourceOption.GroupName}) {assignment.SourceOption.OptionName}: {sourceCommand} → {targetCommand}");
            }

            ImGui.Spacing();
            bool canCreate = !isCreatingOverrides && HasCompleteTargetAssignments();
            if (ImGui.Button(isCreatingOverrides ? "Creating option copies..." : "Create option copies") && canCreate)
            {
                var assignments = sourceEmoteAssignments.Select(assignment => new SourceEmoteAssignment
                {
                    SourceOption = assignment.SourceOption,
                    SourceEmoteRowId = assignment.SourceEmoteRowId,
                    SourceEmoteName = assignment.SourceEmoteName,
                    SourceEmoteCommand = assignment.SourceEmoteCommand,
                    SourceEntries = assignment.SourceEntries,
                    TargetEmote = assignment.TargetEmote
                }).ToList();
                _ = CreateOptionCopiesAsync(assignments);
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));
            ImGui.TextDisabled("Creates a separate copy of each selected option, including its supporting files and settings.");
            ImGui.PopStyleColor();

            if (!canCreate)
            {
                ImGui.TextDisabled("Assign every emote a target. Targets must be unique within each source option.");
            }

            EndCard();
        }

        // ======================================
        // Override creation backend
        // ======================================
        private async Task CreateOptionCopiesAsync(List<SourceEmoteAssignment> assignments)
        {
            if (isCreatingOverrides || selectedModDirectory == null)
                return;
            var modDirectory = selectedModDirectory;
            isCreatingOverrides = true;
            try
            {
                int completed = 0;
                foreach (var optionAssignments in assignments.GroupBy(a => a.SourceOption))
                {
                    if (!await CreateOverrideAsync(modDirectory, optionAssignments.Key, optionAssignments.ToList()))
                        break;
                    completed++;
                }
                Svc.Chat.Print($"[NullEmoteSwapper] Created {completed} of {assignments.Select(a => a.SourceOption).Distinct().Count()} option copies. Enable the copies you want in Penumbra’s NullEmoteSwapper group.");
            }
            finally
            {
                isCreatingOverrides = false;
            }
        }

        private async Task<bool> CreateOverrideAsync(string selectedDirectory, RemappableOption source, List<SourceEmoteAssignment> assignments)
        {
            if (!modList.ContainsKey(selectedDirectory))
            {
                Svc.Chat.PrintError("[NullEmoteSwapper] Refresh the mod list and select the source mod again.");
                return false;
            }

            var assignmentSummary = string.Join(", ", assignments.Select(assignment =>
                $"{assignment.SourceEmoteCommand} → {assignment.TargetEmote?.Command ?? "<unassigned>"}"));
            Plugin.Log.Information("[NullEmoteSwapper] Override creation started for mod {ModDirectory}, option {GroupName} / {OptionName}, assignments: {Assignments}.",
                selectedDirectory, source.GroupName, source.OptionName, assignmentSummary);
            Svc.Chat.Print($"[NullEmoteSwapper] Creating {assignments.Count} emote override(s): {assignmentSummary}.");

            var generatedPapFiles = new List<string>();
            return await Task.Run(async () =>
            {
                try
                {
                    var penumbraRoot = ResolvePenumbraRoot();
                    if (string.IsNullOrWhiteSpace(penumbraRoot))
                    {
                        Plugin.Log.Error("[NullEmoteSwapper] Override generation stopped because Penumbra's mod directory could not be resolved.");
                        Svc.Chat.PrintError("[NullEmoteSwapper] Could not locate the Penumbra mod directory. Check Penumbra's mod directory setting and refresh the plugin.");
                        return false;
                    }
                    var modFolder = Path.Combine(penumbraRoot, selectedDirectory);
                    Plugin.Log.Debug("[NullEmoteSwapper] Override target mod folder is {ModFolder}.", modFolder);

                    if (!Directory.Exists(modFolder))
                    {
                        Plugin.Log.Error("[NullEmoteSwapper] Override generation stopped because mod folder {ModFolder} does not exist.", modFolder);
                        Svc.Chat.PrintError($"[NullEmoteSwapper] Mod folder does not exist: {modFolder}");
                        return false;
                    }

                    if (assignments.Count == 0 || assignments.Any(assignment => assignment.TargetEmote == null))
                    {
                        Plugin.Log.Warning("[NullEmoteSwapper] Override generation cancelled: {AssignmentCount} assignment(s), with at least one target missing.", assignments.Count);
                        throw new InvalidOperationException("Every source emote must have a replacement trigger.");
                    }

                    if (assignments.Select(assignment => assignment.TargetEmote!.RowId).Distinct().Count() != assignments.Count)
                    {
                        Plugin.Log.Warning("[NullEmoteSwapper] Override generation cancelled because replacement targets are duplicated: {Assignments}.", assignmentSummary);
                        throw new InvalidOperationException("Each source emote must use a different replacement trigger.");
                    }

                    // 1) Ensure NullEmoteSwapper folders exist.
                    string overrideRoot = Path.Combine(modFolder, "nulloverride");
                    string papDir = Path.Combine(overrideRoot, "paps");

                    Directory.CreateDirectory(overrideRoot);
                    Directory.CreateDirectory(papDir);
                    Plugin.Log.Debug("[NullEmoteSwapper] Verified override output directories {OverrideRoot} and {PapDirectory}.", overrideRoot, papDir);

                    var finalMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var mappingDescriptions = new List<string>();
                    var papJobs = new List<(string TargetGamePath, string SourcePap, string NewPapAbsolute, string NewPapRelative)>();

                    // Resolve every source emote independently so one option can map multiple triggers.
                    foreach (var assignment in assignments)
                    {
                        Plugin.Log.Information("[NullEmoteSwapper] Resolving source {SourceCommand} (emote row {SourceRowId}) to target {TargetCommand} (emote row {TargetRowId}).",
                            assignment.SourceEmoteCommand, assignment.SourceEmoteRowId,
                            assignment.TargetEmote?.Command ?? "<unassigned>", assignment.TargetEmote?.RowId ?? 0);
                        var sourceVariants = assignment.SourceEntries
                            .Where(entry => !string.IsNullOrWhiteSpace(entry.ModdedPapPath))
                            .GroupBy(entry => GetGamePathDirectory(entry.GamePath), StringComparer.OrdinalIgnoreCase)
                            .Select(group => new { GameDirectory = group.Key, Entry = group.First() })
                            .ToList();
                        if (sourceVariants.Count == 0)
                            throw new InvalidDataException($"No source PAP was found for {assignment.SourceEmoteCommand}.");
                        Plugin.Log.Debug("[NullEmoteSwapper] Source {SourceCommand} has {PapVariantCount} PAP variant(s).", assignment.SourceEmoteCommand, sourceVariants.Count);
                        var modRootFullPath = Path.GetFullPath(modFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        foreach (var variant in sourceVariants)
                        {
                            var sourcePap = Path.GetFullPath(Path.Combine(modFolder, variant.Entry.ModdedPapPath.Replace('/', Path.DirectorySeparatorChar)));
                            if (!sourcePap.StartsWith(modRootFullPath, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException($"Source PAP path escapes the selected mod folder: {variant.Entry.ModdedPapPath}");

                            if (!File.Exists(sourcePap))
                                throw new FileNotFoundException($"Source PAP not found for {assignment.SourceEmoteCommand}.", sourcePap);
                            Plugin.Log.Debug("[NullEmoteSwapper] Validated source PAP variant {SourcePap} for game path directory {GameDirectory}.", sourcePap, variant.GameDirectory);
                        }

                        var target = assignment.TargetEmote!;
                        var targetGamePaths = PapResolver.ResolvePapFiles(target.PrimaryTimelineKey);
                        if (targetGamePaths.Count == 0)
                            throw new InvalidDataException($"No game PAP paths were found for {target.Name} ({target.Command}).");
                        Plugin.Log.Debug("[NullEmoteSwapper] Resolved {TargetPathCount} target game PAP path(s) for {TargetCommand} timeline {TimelineKey}.",
                            targetGamePaths.Count, target.Command, target.PrimaryTimelineKey);

                        var sourceByTargetPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var targetGamePath in targetGamePaths)
                        {
                            if (finalMappings.ContainsKey(targetGamePath))
                                throw new InvalidDataException($"More than one replacement resolves to the same game PAP path: {targetGamePath}");

                            var targetDirectory = GetGamePathDirectory(targetGamePath);
                            var sourceVariant = sourceVariants.FirstOrDefault(variant => string.Equals(
                                variant.GameDirectory,
                                targetDirectory,
                                StringComparison.OrdinalIgnoreCase)) ?? sourceVariants[0];
                            var sourcePapPath = Path.GetFullPath(Path.Combine(
                                modFolder,
                                sourceVariant.Entry.ModdedPapPath.Replace('/', Path.DirectorySeparatorChar)));
                            sourceByTargetPath[targetGamePath] = sourcePapPath;
                            Plugin.Log.Debug("[NullEmoteSwapper] Target path {TargetGamePath} will use source PAP {SourcePap}.", targetGamePath, sourcePapPath);
                        }

                        foreach (var variantTargets in sourceByTargetPath.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase))
                        {
                            string newPapName = Guid.NewGuid().ToString("N") + ".pap";
                            string newPapAbsolute = Path.Combine(papDir, newPapName);
                            string newPapRelative = Path.Combine("nulloverride", "paps", newPapName).Replace('\\', '/');
                            papJobs.Add((variantTargets.First().Key, variantTargets.Key, newPapAbsolute, newPapRelative));
                            Plugin.Log.Debug("[NullEmoteSwapper] Planned PAP output {OutputPap} for {TargetPathCount} target game path(s) using {SourcePap}.",
                                newPapRelative, variantTargets.Count(), variantTargets.Key);

                            foreach (var mapping in variantTargets)
                                finalMappings[mapping.Key] = newPapRelative;
                        }

                        string sourceCommand = string.IsNullOrWhiteSpace(assignment.SourceEmoteCommand)
                            ? assignment.SourceEmoteName
                            : assignment.SourceEmoteCommand;
                        mappingDescriptions.Add($"{sourceCommand} → {target.Command}");
                    }

                    // PAP parsing uses Dalamud game data, so run all conversions on the framework thread
                    // and wait for the files to be fully written before updating Penumbra metadata.
                    await Svc.Framework.RunOnFrameworkThread(() =>
                    {
                        foreach (var job in papJobs)
                        {
                            Plugin.Log.Information("[NullEmoteSwapper] Generating PAP override for target game path {TargetGamePath} from source {SourcePap}.",
                                job.TargetGamePath, job.SourcePap);
                            Svc.Chat.Print($"[NullEmoteSwapper] Generating PAP override for {Path.GetFileName(job.TargetGamePath)}...");
                            generatedPapFiles.Add(job.NewPapAbsolute);
                            PapEditor.ApplyOverride(job.TargetGamePath, job.SourcePap, job.NewPapAbsolute);
                            if (!File.Exists(job.NewPapAbsolute) || new FileInfo(job.NewPapAbsolute).Length == 0)
                                throw new InvalidDataException($"PAP override generation failed for {job.TargetGamePath}.");

                            Plugin.Log.Information("[NullEmoteSwapper] Generated PAP file {OutputPap} ({OutputBytes} bytes).",
                                job.NewPapRelative, new FileInfo(job.NewPapAbsolute).Length);
                        }
                    });

                    string optionName = $"({source.GroupName}) {source.OptionName} → {string.Join("; ", mappingDescriptions)} (edited by Null)";
                    string description = $"Emote trigger mappings created by NullEmoteSwapper: {string.Join("; ", mappingDescriptions)}";
                    Plugin.Log.Information("[NullEmoteSwapper] Writing Penumbra option {OptionName} with {MappingCount} game path mappings.", optionName, finalMappings.Count);
                    Penumbra.PenumbraGroupWriter.CreateOrUpdateNullEmoteSwapperGroup(
                        modFolder,
                        source,
                        optionName,
                        description,
                        finalMappings
                    );
                    Plugin.Log.Information("[NullEmoteSwapper] Penumbra option metadata was written successfully for {ModDirectory}.", selectedDirectory);
                    Svc.Chat.Print("[NullEmoteSwapper] Override metadata saved. Reloading the mod in Penumbra...");

                    try
                    {
                        Plugin.Log.Debug("[NullEmoteSwapper] Requesting Penumbra reload for mod {ModDirectory}.", selectedDirectory);
                        reloadMod.Invoke(selectedDirectory);
                        Plugin.Log.Information("[NullEmoteSwapper] Penumbra reload request succeeded for {ModDirectory}.", selectedDirectory);
                        Svc.Chat.Print("[NullEmoteSwapper] Penumbra reload request succeeded.");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error(ex, "[NullEmoteSwapper] Penumbra reload failed for mod {ModDirectory}.", selectedDirectory);
                        Svc.Chat.PrintError($"[NullEmoteSwapper] Warning: Failed to reload mod in Penumbra: {ex.Message}");
                    }

                    Svc.Chat.Print($"[NullEmoteSwapper] Override created successfully for {assignments.Count} emote trigger(s).");
                    Plugin.Log.Information("[NullEmoteSwapper] Override creation completed successfully for mod {ModDirectory} with {AssignmentCount} trigger assignment(s).",
                        selectedDirectory, assignments.Count);
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "[NullEmoteSwapper] Override creation failed for mod {ModDirectory}. Assignments: {Assignments}.", selectedDirectory, assignmentSummary);
                    foreach (var generatedFile in generatedPapFiles)
                    {
                        try
                        {
                            if (File.Exists(generatedFile))
                            {
                                File.Delete(generatedFile);
                                Plugin.Log.Warning("[NullEmoteSwapper] Removed incomplete generated PAP file {GeneratedFile} after failure.", generatedFile);
                            }
                        }
                        catch (Exception cleanupException)
                        {
                            Plugin.Log.Error(cleanupException, "[NullEmoteSwapper] Failed to clean up partial PAP file {GeneratedFile}.", generatedFile);
                        }
                    }

                    Svc.Chat.PrintError($"[NullEmoteSwapper] Override creation failed: {ex.Message}");
                    return false;
                }
            });
        }
    }
}
