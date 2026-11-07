using System.Collections.Generic;

namespace NullEmoteSwapper.Core.Models;

public sealed class SourceEmoteAssignment
{
    public RemappableOption SourceOption { get; init; } = null!;
    public uint SourceEmoteRowId { get; init; }
    public string SourceEmoteName { get; init; } = string.Empty;
    public string SourceEmoteCommand { get; init; } = string.Empty;
    public List<ParsedEmoteOverride> SourceEntries { get; init; } = new();
    public LuminaEmote? TargetEmote { get; set; }
    public string TargetSearch { get; set; } = string.Empty;
}

