using NullEmoteSwapper.Core.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NullEmoteSwapper.Core;

public static class EmoteOverrideScanner
{
    public sealed class ScanDiagnostics
    {
        public int JsonFilesFound { get; set; }
        public int JsonFilesParsed { get; set; }
        public int PapMappingsFound { get; set; }
        public int EmoteMappingsFound { get; set; }
    }

    public static List<RemappableOption> ScanMod(string modDir, out ScanDiagnostics diagnostics)
    {
        var catalog = new ModOptionCatalog();
        diagnostics = new ScanDiagnostics();
        var files = Directory.GetFiles(modDir, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        diagnostics.JsonFilesFound = files.Count;
        foreach (var file in files)
        {
            if (Path.GetFileNameWithoutExtension(file).Contains("nulloverride", StringComparison.OrdinalIgnoreCase))
                continue;
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(file)); }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[NullEmoteSwapper] Skipping invalid JSON {JsonFile}.", file);
                continue;
            }
            diagnostics.JsonFilesParsed++;
            if (root["Groups"] is JArray groups)
            {
                // Some formats also store common files outside the option groups.
                if (HasPayload(root))
                    ReadGroup(catalog, file, "root", RootPayload(root), diagnostics);
                for (int i = 0; i < groups.Count; i++)
                    if (groups[i] is JObject group)
                        ReadGroup(catalog, file, $"Groups[{i}]", group, diagnostics);
            }
            else if (root["Options"] is JArray || HasPayload(root))
            {
                ReadGroup(catalog, file, "root", root, diagnostics);
            }
        }
        var options = catalog.Groups.SelectMany(group => group.Options).ToList();
        // An effect/texture-only option is useful whenever this mod has a source
        // animation elsewhere. Do not discard it merely because it owns no PAP.
        var hasEmotes = options.Any(option => option.Entries.Count > 0)
            || catalog.Groups.Any(group => FindEntries(group.BasePayload, "", "", null).Count > 0);
        Plugin.Log.Information("[NullEmoteSwapper] Scan found {OptionCount} choices, including effect-only choices, and {EmoteCount} emote mappings.",
            options.Count, diagnostics.EmoteMappingsFound);
        return hasEmotes ? options : new List<RemappableOption>();
    }

    private static bool HasPayload(JObject value) => value["Files"] is JObject
        || value["FileSwaps"] is JObject || value["Manipulations"] is JArray;

    private static JObject RootPayload(JObject root)
    {
        var result = new JObject { ["Name"] = "(default)" };
        foreach (var field in new[] { "Files", "FileSwaps", "Manipulations", "Description", "Image" })
            if (root[field] is { } token)
                result[field] = token.DeepClone();
        return result;
    }

    private static void ReadGroup(ModOptionCatalog catalog, string file, string location,
        JObject data, ScanDiagnostics diagnostics)
    {
        var name = (string?)data["Name"] ?? Path.GetFileNameWithoutExtension(file);
        if (Penumbra.PenumbraGroupWriter.IsGeneratedGroupName(name))
            return;
        var type = (string?)data["Type"] ?? "Single";
        if (!string.Equals(type, "Single", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(type, "Multi", StringComparison.OrdinalIgnoreCase))
        {
            Plugin.Log.Debug("[NullEmoteSwapper] Unsupported option group type {GroupType} in {File}.", type, file);
            return;
        }
        var rawOptions = data["Options"] as JArray;
        var isStandalone = rawOptions == null;
        var group = new ModOptionGroup
        {
            SourceId = Path.GetFileName(file) + ":" + location,
            Name = name,
            Type = type,
            Priority = (int?)data["Priority"] ?? 0,
            BasePayload = isStandalone ? new JObject() : RootPayload(data)
        };
        catalog.Groups.Add(group);
        var options = rawOptions ?? new JArray(RootPayload(data));
        var defaultSettings = (ulong?)data["DefaultSettings"] ?? 0;
        for (int index = 0; index < options.Count; index++)
        {
            if (options[index] is not JObject payload)
                continue;
            var optionName = (string?)payload["Name"] ?? "Option";
            var option = new RemappableOption
            {
                Catalog = catalog,
                SourceGroupId = group.SourceId,
                SourceId = group.SourceId + $":Options[{index}]",
                GroupName = name,
                GroupType = type,
                OptionIndex = index,
                OptionPriority = (int?)payload["Priority"] ?? 0,
                DefaultSelected = isStandalone || (type.Equals("Single", StringComparison.OrdinalIgnoreCase)
                    ? (ulong)index == defaultSettings : index < 64 && (defaultSettings & (1UL << index)) != 0),
                OptionName = optionName,
                OriginalOption = (JObject)payload.DeepClone(),
                Entries = FindEntries(payload, name, optionName, diagnostics),
                IsSafeToRemap = true
            };
            option.PapSources = option.Entries.GroupBy(entry => entry.ModdedPapPath, StringComparer.OrdinalIgnoreCase)
                .Select(entries => new PapSourceGroup { SourcePap = entries.Key, GamePaths = entries.Select(entry => entry.GamePath).ToList() }).ToList();
            group.Options.Add(option);
        }
    }

    internal static List<ParsedEmoteOverride> FindEntries(JObject payload, string groupName,
        string optionName, ScanDiagnostics? diagnostics = null)
    {
        var entries = new List<ParsedEmoteOverride>();
        if (payload["Files"] is not JObject mappings)
            return entries;
        foreach (var mapping in mappings.Properties())
        {
            var source = (string?)mapping.Value ?? string.Empty;
            if (!source.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
                continue;
            if (diagnostics != null) diagnostics.PapMappingsFound++;
            var emote = EmotePathResolver.Resolve(mapping.Name);
            if (emote == null) continue;
            if (diagnostics != null) diagnostics.EmoteMappingsFound++;
            entries.Add(new ParsedEmoteOverride
            {
                GroupName = groupName, OptionName = optionName,
                GamePath = mapping.Name, ModdedPapPath = source,
                EmoteName = emote.Name, EmoteCommand = emote.Command, EmoteRowId = emote.RowId
            });
        }
        return entries;
    }
}
