using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NullEmoteSwapper.Core.Models;
using Lumina.Excel.Sheets;

namespace NullEmoteSwapper.Core
{
    /// <summary>
    /// Global in-memory cache of all player emotes for search / selection.
    /// Call EmoteLibrary.Initialize() once during plugin startup.
    /// </summary>
    public static class EmoteLibrary
    {
        public static List<LuminaEmote> AllEmotes { get; private set; } = new();

        private static readonly Dictionary<string, ResolvedEmoteInfo> EmotesByTimeline = new(StringComparer.OrdinalIgnoreCase);

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();
            if (sheet == null)
                return;

            var list = new List<LuminaEmote>();
            EmotesByTimeline.Clear();

            foreach (var emote in sheet)
            {
                if (emote.Name.IsEmpty)
                    continue;

                string name = emote.Name.ExtractText();
                string command = emote.TextCommand.ValueNullable?.Command.ToString() ?? string.Empty;

                var resolved = new ResolvedEmoteInfo
                {
                    Name = name,
                    Command = command,
                    RowId = emote.RowId
                };

                foreach (var timeline in emote.ActionTimeline)
                {
                    if (timeline.ValueNullable == null)
                        continue;

                    var timelineName = Path.GetFileNameWithoutExtension(timeline.Value.Key.ToString());
                    if (!string.IsNullOrWhiteSpace(timelineName))
                        EmotesByTimeline.TryAdd(timelineName, resolved);
                }

                // We only care about "real" player-emotes with a command.
                if (string.IsNullOrWhiteSpace(command))
                    continue;

                // Try to find a loop timeline first.
                string primaryTimelineKey = string.Empty;

                var loopTimeline = emote.ActionTimeline
                    .FirstOrDefault(t =>
                        t.ValueNullable != null &&
                        t.Value.Key.ToString().Contains("loop", StringComparison.OrdinalIgnoreCase));

                if (loopTimeline.ValueNullable != null)
                {
                    primaryTimelineKey = loopTimeline.Value.Key.ToString();
                }
                else
                {
                    // Fallback: any valid timeline key.
                    var anyTimeline = emote.ActionTimeline
                        .FirstOrDefault(t => t.ValueNullable != null);

                    if (anyTimeline.ValueNullable != null)
                        primaryTimelineKey = anyTimeline.Value.Key.ToString();
                }

                string category = emote.EmoteCategory.ValueNullable?.Name.ExtractText()
                                  ?? "Unknown";

                list.Add(new LuminaEmote
                {
                    Name = name,
                    Command = command,
                    RowId = emote.RowId,
                    PrimaryTimelineKey = primaryTimelineKey,
                    Category = category,
                });
            }

            AllEmotes = list
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _initialized = true;
        }

        public static ResolvedEmoteInfo? ResolveTimeline(string timelinePath)
        {
            var timelineName = Path.GetFileNameWithoutExtension(timelinePath);
            return !string.IsNullOrWhiteSpace(timelineName)
                   && EmotesByTimeline.TryGetValue(timelineName, out var emote)
                ? emote
                : null;
        }
    }
}
