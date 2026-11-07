using Newtonsoft.Json;
using System.IO;
using Penumbra.Api.IpcSubscribers;

namespace NullEmoteSwapper.Core;

public class PenumbraJsonConfig
{
    [JsonProperty("ModDirectory")]
    public string? ModDirectory { get; set; }
}

public static class PenumbraDirectoryResolver
{
    public static string? GetPenumbraDirectory()
    {
        try
        {
            var modDirectory = new GetModDirectory(Plugin.PluginInterface).Invoke();
            if (!string.IsNullOrWhiteSpace(modDirectory) && Directory.Exists(modDirectory))
                return modDirectory;
        }
        catch
        {
            // Fall back to the Penumbra configuration file when IPC is unavailable.
        }

        var configDir = Plugin.PluginInterface.ConfigDirectory.FullName;
        string? parent = Directory.GetParent(configDir)?.FullName;

        if (string.IsNullOrWhiteSpace(parent))
            return null;

        string penumbraJson = Path.Combine(parent, "Penumbra.json");
        if (!File.Exists(penumbraJson))
            return null;

        try
        {
            var cfg = JsonConvert.DeserializeObject<PenumbraJsonConfig>(File.ReadAllText(penumbraJson));
            return !string.IsNullOrWhiteSpace(cfg?.ModDirectory) && Directory.Exists(cfg.ModDirectory)
                ? cfg.ModDirectory
                : null;
        }
        catch
        {
            return null;
        }
    }
}
