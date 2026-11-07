using System;
using System.Collections.Generic;
using System.Linq;
using NullEmoteSwapper.Core.Models;

namespace NullEmoteSwapper.Core;

public static class OptionAssignmentPlanner
{
    public static List<SourceEmoteAssignment> Build(RemappableOption option)
    {
        return option.Entries
            .GroupBy(entry => entry.EmoteRowId)
            .Select(group =>
            {
                var first = group.First();
                return new SourceEmoteAssignment
                {
                    SourceOption = option,
                    SourceEmoteRowId = group.Key,
                    SourceEmoteName = first.EmoteName,
                    SourceEmoteCommand = first.EmoteCommand,
                    SourceEntries = group.ToList()
                };
            })
            .OrderBy(assignment => assignment.SourceEmoteCommand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(assignment => assignment.SourceEmoteName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

}
