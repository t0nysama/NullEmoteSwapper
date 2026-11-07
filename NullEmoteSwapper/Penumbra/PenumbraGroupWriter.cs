using NullEmoteSwapper.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NullEmoteSwapper.Penumbra;

public static class PenumbraGroupWriter
{
    // Clone the complete option so textures, sounds, swaps, manipulations, and
    // extension fields survive. Only its recognized emote paths are replaced.
    public static JObject CreateOptionCopy(RemappableOption source, string name,
        string description, IReadOnlyDictionary<string, string> mappings)
    {
        var copy = (JObject)source.OriginalOption.DeepClone();
        copy["Name"] = name;
        var originalDescription = (string?)copy["Description"];
        copy["Description"] = string.IsNullOrWhiteSpace(originalDescription)
            ? description : originalDescription + "\n\n" + description;
        var files = copy["Files"] as JObject ?? new JObject();
        if (copy["Files"] is not JObject)
            copy["Files"] = files;
        var swaps = copy["FileSwaps"] as JObject ?? new JObject();
        if (copy["FileSwaps"] is not JObject)
            copy["FileSwaps"] = swaps;
        copy["Manipulations"] ??= new JArray();
        var replacedPaths = source.Entries.Select(entry => entry.GamePath)
            .Concat(mappings.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in files.Properties().Where(p => replacedPaths.Contains(p.Name)).ToList())
            property.Remove();
        foreach (var property in swaps.Properties().Where(p => replacedPaths.Contains(p.Name)).ToList())
            property.Remove();
        foreach (var mapping in mappings)
            files[mapping.Key] = mapping.Value;
        return copy;
    }

    public static void CreateOrUpdateNullEmoteSwapperGroup(string modFolder,
        RemappableOption source, string optionName, string description,
        IReadOnlyDictionary<string, string> finalMappings)
    {
        var option = CreateOptionCopy(source, optionName, description, finalMappings);
        var metaPath = Path.Combine(modFolder, "meta.json");
        var metadata = File.Exists(metaPath) ? JObject.Parse(File.ReadAllText(metaPath)) : null;
        if ((int?)metadata?["FileVersion"] >= 4)
        {
            var groups = metadata!["Groups"] as JArray ?? new JArray();
            if (metadata["Groups"] is not JArray)
                metadata["Groups"] = groups;
            var group = groups.OfType<JObject>().FirstOrDefault(g =>
                string.Equals((string?)g["Name"], "NullEmoteSwapper", StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = NewGroup();
                group["Id"] = Guid.NewGuid().ToString();
                groups.Add(group);
            }
            AddOption(group, option, true);
            metadata["LastWrite"] = DateTime.UtcNow.ToString("O");
            WriteJson(metaPath, metadata);
        }
        else
        {
            var groupFiles = Directory.GetFiles(modFolder, "group_*.json", SearchOption.TopDirectoryOnly);
            var targetPath = groupFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Contains("nulloverride", StringComparison.OrdinalIgnoreCase));
            if (targetPath == null)
            {
                // Group indices are not necessarily contiguous.
                var index = 1;
                while (groupFiles.Any(f => Path.GetFileName(f).StartsWith($"group_{index:D3}_", StringComparison.OrdinalIgnoreCase)))
                    index++;
                targetPath = Path.Combine(modFolder, $"group_{index:D3}_nulloverride.json");
            }
            var group = File.Exists(targetPath) ? JObject.Parse(File.ReadAllText(targetPath)) : NewGroup();
            AddOption(group, option, false);
            WriteJson(targetPath, group);
        }
        Plugin.Log.Information("[NullEmoteSwapper] Copied source option {SourceId} to {OptionName} with {MappingCount} replacement PAP mappings.",
            source.SourceId, optionName, finalMappings.Count);
    }

    private static JObject NewGroup() => new()
    {
        ["Version"] = "1.0.0",
        ["Name"] = "NullEmoteSwapper",
        ["Description"] = "Option copies with independent emote assignments.",
        ["Type"] = "Multi",
        ["Priority"] = 9999,
        ["DefaultSettings"] = 0,
        ["Options"] = new JArray()
    };

    private static void AddOption(JObject group, JObject option, bool modern)
    {
        if (!string.Equals((string?)group["Type"], "Multi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The existing NullEmoteSwapper group is not a Multi group.");
        var options = group["Options"] as JArray ?? new JArray();
        if (group["Options"] is not JArray)
            group["Options"] = options;
        // Penumbra Multi groups use a 32-bit selection mask.
        if (options.Count >= 32)
            throw new InvalidDataException("The NullEmoteSwapper group already contains 32 options. Remove unused copies before adding more.");
        if (modern)
            option["Id"] = Guid.NewGuid().ToString();
        else
            option.Remove("Id");
        options.Add(option);
    }

    private static void WriteJson(string path, JObject value)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, value.ToString(Formatting.Indented));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
