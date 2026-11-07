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

Run("full generated group creates another page without changing the existing page", dir =>
{
    Write(dir, "group_001_dances.json", Group(Option("A", "a.pap")));
    var source = EmoteOverrideScanner.ScanMod(dir, out _).Single();
    var full = Group(Enumerable.Range(0, 32).Select(i => Option(i.ToString(), "a.pap")).ToArray());
    full["Name"] = "NullEmoteSwapper";
    full["Type"] = "Multi";
    var path = Write(dir, "group_002_nulloverride.json", full);
    var before = File.ReadAllBytes(path);
    Save(dir, source, Wave, "nulloverride/new.pap");
    Check(before.SequenceEqual(File.ReadAllBytes(path)), "Creating another page changed existing metadata.");
    Check(Directory.GetFiles(dir, "*nulloverride.json").Length == 2, "A new page is required after 32 variations.");
    Check(EmoteOverrideScanner.ScanMod(dir, out _).Count == 1, "All generated pages should be excluded.");
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

Run("effect-only dropdown choices inherit the required PAP and remain independent", dir =>
{
    var required = Group(Option("Required animation", "shared-dance.pap"));
    required["Name"] = "Required";
    var effects = Group(
        new JObject { ["Name"] = "Effect 1", ["Files"] = new JObject { ["vfx/effect.avfx"] = "effect1.avfx" } },
        new JObject { ["Name"] = "Effect 2", ["Files"] = new JObject { ["vfx/effect.avfx"] = "effect2.avfx" } });
    effects["Name"] = "Effects";
    effects["Priority"] = 1;
    Write(dir, "meta.json", new JObject { ["FileVersion"] = 4, ["Groups"] = new JArray(required, effects) });
    var choices = EmoteOverrideScanner.ScanMod(dir, out _);
    Check(choices.Count == 3, "Effect-only choices must be included in the scan.");
    var catalog = choices[0].Catalog;
    var variants = ModVariationComposer.ExpandGroup(catalog, catalog.Groups.Single(g => g.Name == "Effects"), catalog.DefaultSelection);
    Check(variants.Count == 2, "Each effect choice must produce its own variation.");
    for (var index = 0; index < variants.Count; index++)
    {
        Check(variants[index].Entries.Single().ModdedPapPath == "shared-dance.pap", "Missing shared animation.");
        Save(dir, variants[index], Wave, $"nulloverride/variation{index}.pap");
    }
    var metadata = JObject.Parse(File.ReadAllText(Path.Combine(dir, "meta.json")));
    var copies = (JArray)metadata["Groups"]![2]!["Options"]!;
    Check(copies.Count == 2, "Both effects must get independently selectable copies.");
    Check((string?)copies[0]["Files"]!["vfx/effect.avfx"] == "effect1.avfx", "Effect 1 was lost.");
    Check((string?)copies[1]["Files"]!["vfx/effect.avfx"] == "effect2.avfx", "Effect 2 was lost.");
    Check(copies.All(copy => copy["Files"]![Wave] != null), "Both variations may use the same target emote.");
    Check(JToken.DeepEquals(metadata["Groups"]![0], required) && JToken.DeepEquals(metadata["Groups"]![1], effects),
        "Original dropdowns were changed.");
});

Run("default PAP plus multiple effect checkboxes resolves the chosen combination", dir =>
{
    Write(dir, "default_mod.json", Option("Default animation", "base.pap"));
    var effects = Group(
        new JObject { ["Name"] = "Glow", ["Files"] = new JObject { ["vfx/glow.avfx"] = "glow.avfx" } },
        new JObject { ["Name"] = "Music", ["Files"] = new JObject { ["sound/music.scd"] = "music.scd" } });
    effects["Type"] = "Multi";
    Write(dir, "group_001_effects.json", effects);
    var choices = EmoteOverrideScanner.ScanMod(dir, out _);
    var catalog = choices[0].Catalog;
    var variant = ModVariationComposer.Compose(catalog, choices);
    Check(variant.Entries.Single().ModdedPapPath == "base.pap", "Default source PAP is missing.");
    Check(variant.OriginalOption["Files"]!["vfx/glow.avfx"] != null && variant.OriginalOption["Files"]!["sound/music.scd"] != null,
        "A combined variation must retain both selected checkboxes.");
    var single = ModVariationComposer.Compose(catalog, choices.Where(choice => choice.OptionName != "Music"));
    Check(single.OriginalOption["Files"]!["sound/music.scd"] == null, "An unchecked effect leaked into the copy.");
});

Run("multiple emotes and effects support original and swapped triggers in one variation", dir =>
{
    var animation = Option("Both dances", "dance.pap");
    animation["Files"]![Sit] = "sit.pap";
    Write(dir, "default_mod.json", animation);
    Write(dir, "group_001_effects.json", Group(new JObject
    {
        ["Name"] = "Effect", ["Files"] = new JObject { ["vfx/effect.avfx"] = "effect.avfx" }
    }));
    var choices = EmoteOverrideScanner.ScanMod(dir, out _);
    var variant = ModVariationComposer.Compose(choices[0].Catalog, choices);
    Check(OptionAssignmentPlanner.Build(variant).Count == 2, "Each source emote needs its own assignment.");
    var copy = PenumbraGroupWriter.CreateOptionCopy(variant, "Mixed", "Dance swapped, sit original",
        new Dictionary<string, string> { [Wave] = "swapped.pap", [Sit] = "sit.pap" });
    Check(copy["Files"]![Dance] == null && copy["Files"]![Wave] != null, "Dance was not swapped.");
    Check((string?)copy["Files"]![Sit] == "sit.pap", "Keep-original choice was lost.");
    Check(copy["Files"]!["vfx/effect.avfx"] != null, "Effect was lost.");
});

Run("combinations respect dropdown exclusivity and option priority", dir =>
{
    var lower = Group(Option("Lower", "lower.pap"));
    var higher = Group(Option("Higher", "higher.pap"), Option("Alternate", "alternate.pap"));
    lower["Name"] = "Lower group"; lower["Priority"] = 0;
    higher["Name"] = "Higher group"; higher["Priority"] = 2;
    Write(dir, "meta.json", new JObject { ["FileVersion"] = 4, ["Groups"] = new JArray(higher, lower) });
    var choices = EmoteOverrideScanner.ScanMod(dir, out _);
    var catalog = choices[0].Catalog;
    var variant = ModVariationComposer.Compose(catalog, catalog.DefaultSelection);
    Check(variant.Entries.Single().ModdedPapPath == "higher.pap", "Higher-priority group should determine the effective PAP.");
    try { ModVariationComposer.Compose(catalog, choices); throw new Exception("Expected dropdown selection error."); }
    catch (InvalidDataException) { }
});

Run("modern generated pages allow more than 32 variations", dir =>
{
    Write(dir, "meta.json", new JObject { ["FileVersion"] = 4, ["Groups"] = new JArray(Group(Option("A", "a.pap"))) });
    var source = EmoteOverrideScanner.ScanMod(dir, out _).Single();
    for (var i = 0; i < 34; i++) Save(dir, source, Wave, "nulloverride/copy.pap");
    var metadata = JObject.Parse(File.ReadAllText(Path.Combine(dir, "meta.json")));
    Check(metadata["Groups"]!.Count() == 3, "Expected original group and two generated pages.");
    Check(metadata["Groups"]![1]!["Options"]!.Count() == 32 && metadata["Groups"]![2]!["Options"]!.Count() == 2,
        "Variations were lost at the page boundary.");
    Check(EmoteOverrideScanner.ScanMod(dir, out _).Count == 1, "Generated pages leaked into source choices.");
});

if (args.Length == 2 && args[0] == "--inspect-mod")
{
    // Read-only integration check against an installed mod. Nothing is generated
    // in the supplied directory; copying is covered by isolated fixtures above.
    var choices = EmoteOverrideScanner.ScanMod(args[1], out _);
    Check(choices.Count > 0, "Installed mod has no recognizable source animation.");
    var catalog = choices[0].Catalog;
    foreach (var group in catalog.Groups)
    {
        var variations = ModVariationComposer.ExpandGroup(catalog, group, catalog.DefaultSelection);
        Console.WriteLine($"READ-ONLY {group.Name}: {group.Options.Count} choices -> {variations.Count} complete variations.");
        Check(variations.All(variation => variation.Entries.Count > 0), "A variation lost its shared source animation.");
    }
    var effectsGroup = catalog.Groups.Single(group => group.Name == "Variants");
    var effectVariations = ModVariationComposer.ExpandGroup(catalog, effectsGroup, catalog.DefaultSelection);
    for (int i = 0; i < effectVariations.Count; i++)
    {
        var originalFiles = (JObject)effectsGroup.Options[i].OriginalOption["Files"]!;
        foreach (var mapping in originalFiles.Properties())
            Check(JToken.DeepEquals(effectVariations[i].OriginalOption["Files"]![mapping.Name], mapping.Value),
                "An installed effect choice lost its exact VFX/texture mappings.");
        Check(effectVariations[i].Entries.All(entry => File.Exists(Path.Combine(args[1], entry.ModdedPapPath))),
            "An installed variation references a missing source PAP.");
    }
    Console.WriteLine($"READ-ONLY verified exact VFX/texture mappings and source PAP existence for all {effectVariations.Count} installed variations.");
}

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
