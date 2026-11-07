using Newtonsoft.Json.Linq;
using NullEmoteSwapper.Core;
using NullEmoteSwapper.Core.Models;
using NullEmoteSwapper.Penumbra;

const string Dance = "chara/human/c0101/animation/a0001/bt_common/emote/dance.pap";
const string OtherRaceDance = "chara/human/c0201/animation/a0001/bt_common/emote/dance.pap";
const string Sit = "chara/human/c0101/animation/a0001/bt_common/emote/sit.pap";
const string Wave = "chara/human/c0101/animation/a0001/bt_common/emote/wave.pap";
const string Cheer = "chara/human/c0101/animation/a0001/bt_common/emote/cheer.pap";
int passed = 0;

Run("legacy: shared emote options remain separate, including identical names", dir =>
{
    Write(dir, "group_001_dances.json", Group(Option("Same name", "a.pap"), Option("Same name", "b.pap")));
    var options = EmoteOverrideScanner.ScanMod(dir, out var diagnostics);
    Check(options.Count == 2 && diagnostics.EmoteMappingsFound == 2, "Two independent options must be scanned.");
    Check(options[0].SourceId != options[1].SourceId, "Option identity must use JSON location, not its name/emote.");
    Check(options[0].Entries.Single().ModdedPapPath == "a.pap", "Option A lost its PAP.");
    Check(options[1].Entries.Single().ModdedPapPath == "b.pap", "Option B lost its PAP.");
    var first = OptionAssignmentPlanner.Build(options[0]);
    var second = OptionAssignmentPlanner.Build(options[1]);
    first[0].TargetEmote = new() { RowId = 10, Command = "/wave" };
    Check(second[0].TargetEmote == null, "Changing A must not assign B's target.");
    second[0].TargetEmote = new() { RowId = 11, Command = "/cheer" };
    Check(ReferenceEquals(first[0].SourceOption, options[0]), "Assignments must retain their exact option.");
    Check(first[0].SourceEntries.Single().ModdedPapPath != second[0].SourceEntries.Single().ModdedPapPath,
        "Shared emotes must not merge PAP sources across options.");
});

Run("one option: preserve independent emotes and combine only race variants", dir =>
{
    var option = Option("Multiple emotes", "dance-a.pap");
    option["Files"]![OtherRaceDance] = "dance-b.pap";
    option["Files"]![Sit] = "sit.pap";
    Write(dir, "group_001_dances.json", Group(option));
    var source = EmoteOverrideScanner.ScanMod(dir, out _).Single();
    var assignments = OptionAssignmentPlanner.Build(source);
    Check(assignments.Count == 2, "Different source emotes must retain individual assignments.");
    Check(assignments.Single(a => a.SourceEmoteRowId == 1).SourceEntries.Count == 2,
        "Race variants within an option should share an assignment.");
});

Run("copy: retain supporting files, swaps, manipulations, extension fields, and source data", dir =>
{
    var original = Option("With assets", "dance.pap");
    original["Files"]!["chara/accessory.tex"] = "assets/red.tex";
    original["Files"]!["sound/effect.scd"] = "assets/sound.scd";
    original["FileSwaps"] = new JObject { ["chara/model.mdl"] = "chara/other.mdl", [Wave.ToUpperInvariant()] = "old.pap" };
    original["Manipulations"] = new JArray(new JObject { ["Type"] = "Imc", ["Entry"] = 1 });
    original["CustomField"] = new JObject { ["Value"] = "preserve me" };
    original["Description"] = "Original description";
    Write(dir, "group_001_dances.json", Group(original));
    var source = EmoteOverrideScanner.ScanMod(dir, out _).Single();
    var before = source.OriginalOption.ToString();
    var copy = PenumbraGroupWriter.CreateOptionCopy(source, "Copy", "Swapped", new Dictionary<string, string> { [Wave] = "nulloverride/new.pap" });
    Check(copy["Files"]![Dance] == null, "The copy should no longer bind the old trigger.");
    Check((string?)copy["Files"]![Wave] == "nulloverride/new.pap", "Missing replacement mapping.");
    Check((string?)copy["Files"]!["chara/accessory.tex"] == "assets/red.tex", "Texture mapping was dropped.");
    Check((string?)copy["Files"]!["sound/effect.scd"] == "assets/sound.scd", "Sound mapping was dropped.");
    Check(copy["FileSwaps"]![Wave.ToUpperInvariant()] == null, "A conflicting target file swap must be removed.");
    Check(JToken.DeepEquals(copy["Manipulations"], original["Manipulations"]), "Manipulations changed.");
    Check(JToken.DeepEquals(copy["CustomField"], original["CustomField"]), "Extension field changed.");
    Check(((string?)copy["Description"])!.Contains("Original description"), "Description was lost.");
    copy["Manipulations"]![0]!["Entry"] = 2;
    Check(source.OriginalOption.ToString() == before, "Copying must not mutate the source payload.");
});

Run("legacy writer: independent option copies and original file unchanged", dir =>
{
    var sourcePath = Write(dir, "group_001_dances.json", Group(Option("A", "a.pap"), Option("B", "b.pap")));
    var before = File.ReadAllBytes(sourcePath);
    var sources = EmoteOverrideScanner.ScanMod(dir, out _);
    Save(dir, sources[0], Wave, "nulloverride/a.pap");
    Save(dir, sources[1], Cheer, "nulloverride/b.pap");
    var generated = JObject.Parse(File.ReadAllText(Directory.GetFiles(dir, "*nulloverride.json").Single()));
    var copies = (JArray)generated["Options"]!;
    Check(copies.Count == 2, "Writer merged two options sharing an emote.");
    Check((string?)copies[0]["Files"]![Wave] == "nulloverride/a.pap", "Wrong mapping for A.");
    Check((string?)copies[1]["Files"]![Cheer] == "nulloverride/b.pap", "Wrong mapping for B.");
    Check(before.SequenceEqual(File.ReadAllBytes(sourcePath)), "Original group file was modified.");
    Check(EmoteOverrideScanner.ScanMod(dir, out _).Count == 2, "Rescan must exclude generated options.");
    Check(!Directory.GetFiles(dir, "*.tmp").Any(), "Temporary metadata files leaked.");
});

Run("modern metadata: preserve group identity and use unique IDs on copies", dir =>
{
    var group = Group(Option("A", "a.pap"), Option("B", "b.pap"));
    group["Id"] = "original-group-id";
    group["Options"]![0]!["Id"] = "original-option-id";
    Write(dir, "meta.json", new JObject { ["FileVersion"] = 4, ["Name"] = "Mod", ["Groups"] = new JArray(group) });
    var sources = EmoteOverrideScanner.ScanMod(dir, out _);
    Save(dir, sources[0], Wave, "nulloverride/a.pap");
    Save(dir, sources[1], Cheer, "nulloverride/b.pap");
    var metadata = JObject.Parse(File.ReadAllText(Path.Combine(dir, "meta.json")));
    var groups = (JArray)metadata["Groups"]!;
    Check(JToken.DeepEquals(groups[0], group), "Original modern group was changed.");
    var copies = (JArray)groups[1]["Options"]!;
    Check(copies.Count == 2, "Modern writer must create two copies.");
    Check((string?)copies[0]["Id"] != "original-option-id", "Copy reused original ID.");
    Check((string?)copies[0]["Id"] != (string?)copies[1]["Id"], "Copy IDs must be unique.");
    Check(EmoteOverrideScanner.ScanMod(dir, out _).Count == 2, "Modern generated group must be excluded from scans.");
});

Run("missing version in legacy meta must not be treated as modern metadata", dir =>
{
    Write(dir, "meta.json", new JObject { ["Name"] = "Legacy Mod" });
    Write(dir, "group_001_dances.json", Group(Option("A", "a.pap")));
    var source = EmoteOverrideScanner.ScanMod(dir, out _).Single();
    Save(dir, source, Wave, "nulloverride/a.pap");
    Check(Directory.GetFiles(dir, "*nulloverride.json").Length == 1, "Legacy metadata should use a group file.");
});

Run("malformed JSON is skipped while valid options remain available", dir =>
{
    File.WriteAllText(Path.Combine(dir, "broken.json"), "{invalid");
    Write(dir, "group_001_dances.json", Group(Option("A", "a.pap")));
    Check(EmoteOverrideScanner.ScanMod(dir, out var diagnostics).Count == 1, "Valid option was lost.");
    Check(diagnostics.JsonFilesFound == 2 && diagnostics.JsonFilesParsed == 1, "Invalid JSON diagnostics are wrong.");
});

Run("full generated group fails without changing metadata", dir =>
{
    Write(dir, "group_001_dances.json", Group(Option("A", "a.pap")));
    var source = EmoteOverrideScanner.ScanMod(dir, out _).Single();
    var full = Group(Enumerable.Range(0, 32).Select(i => Option(i.ToString(), "a.pap")).ToArray());
    full["Name"] = "NullEmoteSwapper";
    full["Type"] = "Multi";
    var path = Write(dir, "group_002_nulloverride.json", full);
    var before = File.ReadAllBytes(path);
    try { Save(dir, source, Wave, "nulloverride/new.pap"); throw new Exception("Expected capacity failure."); }
    catch (InvalidDataException) { }
    Check(before.SequenceEqual(File.ReadAllBytes(path)), "Failed append changed existing metadata.");
});

Run("root/default option copies keep supporting metadata", dir =>
{
    var root = Option("Default", "dance.pap");
    root["FileSwaps"] = new JObject { ["chara/model.mdl"] = "chara/other.mdl" };
    root["Manipulations"] = new JArray(new JObject { ["Type"] = "Imc" });
    Write(dir, "default_mod.json", root);
    var source = EmoteOverrideScanner.ScanMod(dir, out _).Single();
    var copy = PenumbraGroupWriter.CreateOptionCopy(source, "Copy", "Swapped", new Dictionary<string, string> { [Wave] = "new.pap" });
    Check(JToken.DeepEquals(copy["FileSwaps"], root["FileSwaps"]), "Default file swaps were dropped.");
    Check(JToken.DeepEquals(copy["Manipulations"], root["Manipulations"]), "Default manipulations were dropped.");
});

Run("modern groups with repeated names still produce distinct source identities", dir =>
{
    Write(dir, "meta.json", new JObject { ["FileVersion"] = 4, ["Groups"] = new JArray(
        Group(Option("A", "a.pap")), Group(Option("A", "b.pap"))) });
    var sources = EmoteOverrideScanner.ScanMod(dir, out _);
    Check(sources.Count == 2 && sources.Select(s => s.SourceId).Distinct().Count() == 2,
        "Group and option indices must distinguish repeated names in the same file.");
});

Console.WriteLine($"Passed {passed} regression scenarios.");

JObject Option(string name, string pap) => new() { ["Name"] = name, ["Files"] = new JObject { [Dance] = pap } };
JObject Group(params JObject[] options) => new() { ["Name"] = "Dances", ["Type"] = "Single", ["Options"] = new JArray(options) };
string Write(string directory, string filename, JObject value)
{
    var path = Path.Combine(directory, filename);
    File.WriteAllText(path, value.ToString());
    return path;
}
void Save(string directory, RemappableOption source, string gamePath, string filePath) =>
    PenumbraGroupWriter.CreateOrUpdateNullEmoteSwapperGroup(directory, source,
        source.OptionName + " copy", "Test copy", new Dictionary<string, string> { [gamePath] = filePath });
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Run(string name, Action<string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), "NullEmoteSwapper-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(directory); passed++; Console.WriteLine("PASS " + name); }
    finally { Directory.Delete(directory, true); }
}
