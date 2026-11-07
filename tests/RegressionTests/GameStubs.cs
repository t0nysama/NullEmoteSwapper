// Only game data lookup and logging are replaced. Tests compile the real scanner,
// assignment planner, models, and metadata writer without starting the game.
namespace NullEmoteSwapper
{
    internal static class Plugin
    {
        public static TestLog Log { get; } = new();
    }

    internal sealed class TestLog
    {
        public void Information(string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Warning(Exception exception, string message, params object[] args) { }
    }
}

namespace NullEmoteSwapper.Core
{
    public static class EmotePathResolver
    {
        public static Models.ResolvedEmoteInfo? Resolve(string gamePath)
        {
            var name = Path.GetFileNameWithoutExtension(gamePath).ToLowerInvariant();
            return name switch
            {
                "dance" => new() { RowId = 1, Name = "Dance", Command = "/dance" },
                "sit" => new() { RowId = 2, Name = "Sit", Command = "/sit" },
                _ => null
            };
        }
    }
}
