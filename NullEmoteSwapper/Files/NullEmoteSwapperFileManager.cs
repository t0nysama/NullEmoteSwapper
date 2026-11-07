using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ECommons.ChatMethods;
using ECommons.DalamudServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NullEmoteSwapper.Files
{
    /// <summary>
    /// Einzelner PAP-Trefffer in einer Mod:
    /// - JsonFile: welche group_*.json
    /// - GroupName: Name der Gruppe (aus JSON)
    /// - OptionName: Name der Option oder "(root)"
    /// - GamePath: Ingame-Route (Key im Files-Dict)
    /// - ModRelativePath: Pfad zur PAP-Datei innerhalb der Mod
    /// </summary>
    public sealed class NullEmoteSwapperPapReference
    {
        public string JsonFile { get; init; } = string.Empty;
        public string GroupName { get; init; } = string.Empty;
        public string OptionName { get; init; } = string.Empty;
        public string GamePath { get; init; } = string.Empty;
        public string ModRelativePath { get; init; } = string.Empty;
    }

    public static class NullEmoteSwapperFileManager
    {
        /// <summary>
        /// Scannt einen Penumbra-Mod-Ordner nach PAP-Referenzen in allen group_*.json Dateien.
        /// Wir fassen nichts an, wir lesen nur.
        /// </summary>
        public static List<NullEmoteSwapperPapReference> ScanForPapReferences(string modRootPath)
        {
            var result = new List<NullEmoteSwapperPapReference>();

            if (string.IsNullOrWhiteSpace(modRootPath) || !Directory.Exists(modRootPath))
                return result;

            var groupFiles = Directory.GetFiles(modRootPath, "*.json", SearchOption.TopDirectoryOnly);

            foreach (var file in groupFiles)
            {
                PenumbraGroupJson? root;
                try
                {
                    var json = File.ReadAllText(file);
                    root = JsonConvert.DeserializeObject<PenumbraGroupJson>(json);
                }
                catch
                {
                    // UngÃ¼ltige JSON ignorieren
                    continue;
                }

                if (root == null)
                    continue;

                var groupName = root.Name ?? Path.GetFileNameWithoutExtension(file);

                // 1) Root.Files (Optionsebene "global")
                if (root.Files != null)
                {
                    foreach (var kv in root.Files)
                    {
                        var gamePath = kv.Key;
                        var modPath = kv.Value ?? string.Empty;

                        if (modPath.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(new NullEmoteSwapperPapReference
                            {
                                JsonFile = file,
                                GroupName = groupName,
                                OptionName = "(root)",
                                GamePath = gamePath,
                                ModRelativePath = modPath
                            });
                        }
                    }
                }

                // 2) Options[*].Files
                if (root.Options != null)
                {
                    foreach (var opt in root.Options)
                    {
                        if (opt.Files == null)
                            continue;

                        var optionName = string.IsNullOrWhiteSpace(opt.Name) ? "(no name)" : opt.Name!;

                        foreach (var kv in opt.Files)
                        {
                            var gamePath = kv.Key;
                            var modPath = kv.Value ?? string.Empty;

                            if (!modPath.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
                                continue;

                            result.Add(new NullEmoteSwapperPapReference
                            {
                                JsonFile = file,
                                GroupName = groupName,
                                OptionName = optionName,
                                GamePath = gamePath,
                                ModRelativePath = modPath
                            });
                        }
                    }
                }
            }

            // optional: duplizierte EintrÃ¤ge zusammenfassen
            return result
                .OrderBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.OptionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.GamePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool NullEmoteSwapperExists(string modPath)
        {
            if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
                return false;
            
            bool overrideGroupFound = Directory.GetFiles(modPath, "group_*.json", SearchOption.TopDirectoryOnly)
                .Any(f => Path.GetFileNameWithoutExtension(f)
                    .Contains("nulloverride", StringComparison.OrdinalIgnoreCase));

            bool directoryFound = Directory.GetDirectories(modPath, "nulloverride", SearchOption.TopDirectoryOnly).Any();

            return overrideGroupFound || directoryFound || HasV4OverrideGroup(modPath);

        }

        public static bool RemoveNullEmoteSwapper(string modPath)
        {
            if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
                return false;

            bool anyDeleted = false;
            anyDeleted |= RemoveV4OverrideGroup(modPath);

            // 1) Remove generated override group files.
            var groupFiles = Directory.GetFiles(modPath, "group_*.json", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetFileNameWithoutExtension(f)
                    .Contains("nulloverride", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var file in groupFiles)
            {
                try
                {
                    File.Delete(file);
                    anyDeleted = true;
                }
                catch
                {
                    // Ignorieren
                }
            }
            // 2) Remove generated PAP copies.
            var overrideDirs = Directory.GetDirectories(modPath, "nulloverride", SearchOption.TopDirectoryOnly);
            foreach (var dir in overrideDirs)
            {
                try
                {
                    Directory.Delete(dir, true);
                    anyDeleted = true;
                }
                catch
                {
                    // Ignorieren
                }
            }
            return anyDeleted;
        }

        private static bool HasV4OverrideGroup(string modPath)
        {
            var metaPath = Path.Combine(modPath, "meta.json");
            if (!File.Exists(metaPath))
                return false;

            try
            {
                var metadata = JObject.Parse(File.ReadAllText(metaPath));
                return (int?)metadata["FileVersion"] >= 4
                       && metadata["Groups"] is JArray groups
                       && groups.OfType<JObject>().Any(group => Penumbra.PenumbraGroupWriter.IsGeneratedGroupName((string?)group["Name"]));
            }
            catch
            {
                return false;
            }
        }

        private static bool RemoveV4OverrideGroup(string modPath)
        {
            var metaPath = Path.Combine(modPath, "meta.json");
            if (!File.Exists(metaPath))
                return false;

            try
            {
                var metadata = JObject.Parse(File.ReadAllText(metaPath));
                if ((int?)metadata["FileVersion"] < 4 || metadata["Groups"] is not JArray groups)
                    return false;

                var matches = groups.OfType<JObject>()
                    .Where(group => Penumbra.PenumbraGroupWriter.IsGeneratedGroupName((string?)group["Name"]))
                    .ToList();
                if (matches.Count == 0)
                    return false;

                foreach (var group in matches)
                    group.Remove();

                metadata["LastWrite"] = DateTime.UtcNow.ToString("O");
                File.WriteAllText(metaPath, metadata.ToString(Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

}
