using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NullEmoteSwapper.Core.Models;

public class RemappableOption
{
    // JSON location, independent of display names and shared emote IDs.
    public ModOptionCatalog Catalog { get; set; } = null!;
    public string SourceGroupId { get; set; } = string.Empty;
    public string GroupType { get; set; } = "Single";
    public int OptionIndex { get; set; }
    public int OptionPriority { get; set; }
    public bool DefaultSelected { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public JObject OriginalOption { get; set; } = new();
    public string GroupName { get; set; } = string.Empty;
    public string OptionName { get; set; } = string.Empty;

    // OBSOLET: wird nicht mehr als technische Entscheidung genutzt
    public bool IsSafeToRemap { get; set; }

    public List<ParsedEmoteOverride> Entries { get; set; } = new();

    // ✅ NEU – technische Wahrheit über Quellen
    public List<PapSourceGroup> PapSources { get; set; } = new();
}
