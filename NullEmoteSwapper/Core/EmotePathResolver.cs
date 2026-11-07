using NullEmoteSwapper.Core.Models;
using ECommons.DalamudServices;
using System;
using System.IO;

namespace NullEmoteSwapper.Core;

public static class EmotePathResolver
{
    public static ResolvedEmoteInfo? Resolve(string gamePath)
    {
        var cached = EmoteLibrary.ResolveTimeline(gamePath);
        if (cached != null)
            return cached;

        // Keep the original sheet lookup as a fallback for timeline keys that were
        // not present when the startup cache was built.
        string file = Path.GetFileNameWithoutExtension(gamePath);
        if (string.IsNullOrEmpty(file))
            return null;

        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();
        if (sheet == null)
            return null;

        foreach (var emote in sheet)
        {
            foreach (var timeline in emote.ActionTimeline)
            {
                if (timeline.ValueNullable == null)
                    continue;

                string keyName = Path.GetFileNameWithoutExtension(timeline.Value.Key.ToString());
                if (string.Equals(file, keyName, StringComparison.OrdinalIgnoreCase))
                {
                    return new ResolvedEmoteInfo
                    {
                        Name = emote.Name.ExtractText(),
                        Command = emote.TextCommand.ValueNullable?.Command.ToString() ?? string.Empty,
                        RowId = emote.RowId
                    };
                }
            }
        }

        return null;
    }
}
