using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NullEmoteSwapper.Core.Models;

namespace NullEmoteSwapper.Core;

public static class ModVariationComposer
{
    public static RemappableOption Compose(ModOptionCatalog catalog,
        IEnumerable<RemappableOption> selection, string? name = null)
    {
        var choices = selection.Distinct().ToList();
        if (choices.Any(choice => !ReferenceEquals(choice.Catalog, catalog)))
            throw new InvalidDataException("All variation choices must belong to the scanned mod.");
        foreach (var group in catalog.Groups.Where(group => group.Type.Equals("Single", StringComparison.OrdinalIgnoreCase)))
            if (choices.Count(choice => choice.SourceGroupId == group.SourceId) > 1)
                throw new InvalidDataException($"Select one choice from the dropdown group '{group.Name}'.");

        var payload = new JObject
        {
            ["Files"] = new JObject(), ["FileSwaps"] = new JObject(), ["Manipulations"] = new JArray()
        };
        // Resolve the effective variation, including shared base files and the
        // selected choices from other groups, before looking for source emotes.
        foreach (var group in catalog.Groups.OrderBy(group => group.Priority))
        {
            Merge(payload, group.BasePayload);
            foreach (var choice in choices.Where(choice => choice.SourceGroupId == group.SourceId)
                         .OrderBy(choice => choice.OptionPriority).ThenBy(choice => choice.OptionIndex))
                Merge(payload, choice.OriginalOption);
        }
        var label = string.IsNullOrWhiteSpace(name)
            ? string.Join(" + ", choices.Select(choice => $"{choice.GroupName}: {choice.OptionName}"))
            : name.Trim();
        if (string.IsNullOrWhiteSpace(label)) label = "Base variation";
        payload["Name"] = label;
        payload["Description"] = "Source choices: " + string.Join("; ", choices.Select(choice => $"({choice.GroupName}) {choice.OptionName}"));
        var entries = EmoteOverrideScanner.FindEntries(payload, "Variation", label);
        if (entries.Count == 0)
            throw new InvalidDataException("This combination has no emote animation. Select the mod's required/base animation option as well as its effect choices.");
        return new RemappableOption
        {
            Catalog = catalog,
            SourceId = "variation:" + Guid.NewGuid().ToString("N"),
            GroupName = "Variation", OptionName = label,
            OriginalOption = payload, Entries = entries, IsSafeToRemap = true
        };
    }

    // Vary one dropdown/checkbox group while keeping the other chosen groups.
    // This makes every effect choice available with the same shared animation.
    public static List<RemappableOption> ExpandGroup(ModOptionCatalog catalog, ModOptionGroup group,
        IEnumerable<RemappableOption> selection)
    {
        var common = selection.Where(choice => choice.SourceGroupId != group.SourceId).ToList();
        return group.Options.Select(choice => Compose(catalog, common.Append(choice))).ToList();
    }

    private static void Merge(JObject target, JObject source)
    {
        foreach (var field in new[] { "Files", "FileSwaps" })
        {
            if (source[field] is not JObject mappings) continue;
            var destination = (JObject)target[field]!;
            var opposite = (JObject)target[field == "Files" ? "FileSwaps" : "Files"]!;
            foreach (var mapping in mappings.Properties())
            {
                foreach (var old in destination.Properties().Where(p => p.Name.Equals(mapping.Name, StringComparison.OrdinalIgnoreCase)).ToList())
                    old.Remove();
                foreach (var old in opposite.Properties().Where(p => p.Name.Equals(mapping.Name, StringComparison.OrdinalIgnoreCase)).ToList())
                    old.Remove();
                destination[mapping.Name] = mapping.Value.DeepClone();
            }
        }
        if (source["Manipulations"] is JArray manipulations)
        {
            var destination = (JArray)target["Manipulations"]!;
            foreach (var manipulation in manipulations)
                if (!destination.Any(existing => JToken.DeepEquals(existing, manipulation)))
                    destination.Add(manipulation.DeepClone());
        }
        foreach (var property in source.Properties().Where(property => property.Name is not
                     ("Name" or "Id" or "Description" or "Priority" or "Files" or "FileSwaps" or "Manipulations")))
            target[property.Name] = property.Value.DeepClone();
    }
}
