using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NullEmoteSwapper.Core.Models;

public sealed class ModOptionCatalog
{
    public List<ModOptionGroup> Groups { get; } = new();
    public IEnumerable<RemappableOption> DefaultSelection =>
        Groups.SelectMany(group => group.Options).Where(option => option.DefaultSelected);
}

public sealed class ModOptionGroup
{
    public string SourceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "Single";
    public int Priority { get; init; }
    public JObject BasePayload { get; init; } = new();
    public List<RemappableOption> Options { get; } = new();
}
