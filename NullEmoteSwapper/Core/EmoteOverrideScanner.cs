using NullEmoteSwapper.Core.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NullEmoteSwapper;

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
        var results = new List<RemappableOption>();
        diagnostics = new ScanDiagnostics();
        var jsonFiles = Directory.GetFiles(modDir, "*.json", SearchOption.TopDirectoryOnly);
        diagnostics.JsonFilesFound = jsonFiles.Length;
        Plugin.Log.Information("[NullEmoteSwapper] Scanner found {JsonFileCount} top-level JSON file(s) in {ModDirectory}.", jsonFiles.Length, modDir);

        foreach (var file in jsonFiles)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[NullEmoteSwapper] Skipping unreadable or invalid JSON file {JsonFile}.", file);
                continue;
            }
            diagnostics.JsonFilesParsed++;

            string modName = obj["Name"]?.ToString() ?? Path.GetFileNameWithoutExtension(file);
            var groups = obj["Groups"] as JArray;

            if (groups != null)
            {
                Plugin.Log.Debug("[NullEmoteSwapper] Parsing modern Penumbra metadata {JsonFile} with {GroupCount} group(s).", file, groups.Count);
                foreach (var group in groups)
                {
                    string groupName = group["Name"]?.ToString() ?? modName;
                    var groupOptions = (JArray?)group["Options"]?.DeepClone() ?? new JArray();
                    var groupFiles = group["Files"] as JObject;
                    if (groupFiles != null)
                    {
                        groupOptions.Insert(0, DefaultOption((JObject)group));
                    }

                    ScanOptions(file, group.Path, groupName, groupOptions, results, diagnostics);
                }

                continue;
            }

            Plugin.Log.Debug("[NullEmoteSwapper] Parsing legacy or root-level metadata {JsonFile}.", file);

            // Legacy Penumbra group format stores options/files at the JSON root.
            var options = (JArray?)obj["Options"]?.DeepClone() ?? new JArray();
            var rootFiles = obj["Files"] as JObject;
            if (rootFiles != null)
            {
                options.Insert(0, DefaultOption(obj));
            }

            ScanOptions(file, string.Empty, modName, options, results, diagnostics);
        }

        Plugin.Log.Information("[NullEmoteSwapper] Scan finished for {ModDirectory}: JSON {ParsedJsonCount}/{FoundJsonCount}, PAP mappings {PapMappingCount}, recognized emote mappings {EmoteMappingCount}, remappable options {OptionCount}.",
            modDir, diagnostics.JsonFilesParsed, diagnostics.JsonFilesFound, diagnostics.PapMappingsFound, diagnostics.EmoteMappingsFound, results.Count);
        return results;
    }

    private static JObject DefaultOption(JObject root)
    {
        var option = new JObject { ["Name"] = "(default)" };
        foreach (var field in new[] { "Files", "FileSwaps", "Manipulations", "Description", "Image" })
        {
            if (root[field] is { } value)
                option[field] = value.DeepClone();
        }
        return option;
    }

    private static void ScanOptions(
        string sourceFile,
        string groupPath,
        string groupName,
        JArray options,
        List<RemappableOption> results,
        ScanDiagnostics diagnostics)
    {
        if (string.Equals(groupName, "NullEmoteSwapper", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(sourceFile).Contains("nulloverride", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var opt in options.OfType<JObject>())
        {
            string optionName = opt["Name"]?.ToString() ?? "Option";
            var filesObj = opt["Files"] as JObject;
            if (filesObj == null)
                continue;

            var entries = new List<ParsedEmoteOverride>();
            foreach (var kv in filesObj)
            {
                string gamePath = kv.Key;
                string papPath = kv.Value?.ToString() ?? string.Empty;
                if (!papPath.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
                    continue;

                diagnostics.PapMappingsFound++;
                var emote = EmotePathResolver.Resolve(gamePath);
                if (emote == null)
            {
                Plugin.Log.Debug("[NullEmoteSwapper] Ignoring PAP mapping because the game path did not resolve to a known emote: {GamePath} -> {PapPath}.", gamePath, papPath);
                    continue;
            }

                diagnostics.EmoteMappingsFound++;
                entries.Add(new ParsedEmoteOverride
                {
                    GroupName = groupName,
                    OptionName = optionName,
                    GamePath = gamePath,
                    ModdedPapPath = papPath,
                    EmoteName = emote.Name,
                    EmoteCommand = emote.Command,
                    EmoteRowId = emote.RowId
                });
            }

            if (entries.Count == 0)
                continue;

            var papGroups = entries
                .GroupBy(e => e.ModdedPapPath, StringComparer.OrdinalIgnoreCase)
                .Select(g => new PapSourceGroup
                {
                    SourcePap = g.Key,
                    GamePaths = g.Select(e => e.GamePath).ToList()
                })
                .ToList();

            results.Add(new RemappableOption
            {
                SourceId = Path.GetFileName(sourceFile) + ":" + groupPath + ":" + opt.Path,
                OriginalOption = (JObject)opt.DeepClone(),
                GroupName = groupName,
                OptionName = optionName,
                Entries = entries,
                PapSources = papGroups,
                IsSafeToRemap = true
            });
            Plugin.Log.Debug("[NullEmoteSwapper] Found remappable option {GroupName} / {OptionName}: {EntryCount} emote PAP path(s), {UniqueEmoteCount} unique trigger(s).",
                groupName, optionName, entries.Count, entries.Select(entry => entry.EmoteRowId).Distinct().Count());
        }
    }
}
