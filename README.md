# NullEmoteSwapper

NullEmoteSwapper is a Dalamud plugin for changing emote triggers in Penumbra mods.

## Install through Dalamud

1. Open `/xlsettings` and select **Experimental**.
2. Add this stable URL under **Custom Plugin Repositories**:

   `https://github.com/t0nysama/NullEmoteSwapper/releases/latest/download/repo.json`

3. Save your settings, open `/xlplugins`, and install **NullEmoteSwapper**.

Keep this same repository URL. Plugin updates are delivered through Dalamud when a new release is published.

## Complete option variations (0.1.3)

The plugin includes all file-based dropdown and checkbox choices, including effect/texture/sound options that contain no animation PAP of their own. It combines the selected choices with the required/shared animation before creating a copy.

1. Open `/nullemoteswapper`, select a mod, and scan it.
2. In **Variations**, select the required/base animation and the effect choices you want. A dropdown group allows one choice; a checkbox group allows several.
3. Click **Add selected combination** to queue that complete variation. Repeat with different choices, or use **Add every choice as a separate variation** under a group to queue each of its choices with the other groups' current selections.
4. Choose **Assign emotes to queued variations**. Each source emote in each queued variation has an independent target, including **Keep original**. **Apply to all variations** applies the current target to that same source emote across the queue.
5. Click **Create option copies**. Enable the resulting checkboxes under the mod's **NullEmoteSwapper** groups in Penumbra.

For **Chain Leash VFX**, leave **Required / Base Files** selected, then use **Add every choice as a separate variation** under **Variants**. This queues all 16 leash variations with the shared Imperial Salute animation. Assign `/poke` to one and use **Apply to all variations** if all 16 should use `/poke`. Each generated checkbox includes its own leash effect and texture mappings.

To make several emote versions of the same effect combination, add that combination more than once and assign different targets to the queued copies. For mods replacing multiple emotes, keep or replace each source emote separately within every combination. Selections start with the mod's declared defaults, rather than a particular Penumbra collection's current settings.

Each copy contains the effective selected files, file swaps, and manipulations, plus the generated PAP mappings. Original groups and files stay intact. File and option priorities determine which mapping wins when selected choices overlap. File-based Single and Multi groups are supported.

Replacement targets must be unique within an individual variation. Copies that share a replacement target or conflicting effect/texture paths are alternatives: enable one at a time. This does not create private copies of internally referenced effect resources for simultaneous use at different emotes. Generated options are excluded from future scans. More than 32 copies are split into additional generated groups; the remove-overrides control removes all generated pages and PAPs.

## Build and validation

Requires .NET 10, the Dalamud API 15 development libraries, and `ECommons.dll` / `VFXEditor.dll` plus their dependencies from the existing plugin package. Place plugin dependencies in `lib/`, or supply their directory:

```powershell
dotnet build NullEmoteSwapper/NullEmoteSwapper.csproj -c Release -p:PluginDependencyDirectory="C:\path\to\plugin-dependencies"
dotnet run --project tests/RegressionTests -c Release
```

Dalamud libraries are resolved from XIVLauncher's development folder by default; `DALAMUD_HOME` can select a different installation. The packaged build is generated at `NullEmoteSwapper/bin/Release/NullEmoteSwapper/latest.zip`.

The regression scenarios compile the production scanner, assignment planner, variation composer, and metadata writer with stubbed game lookup/logging. They verify option independence, race variants, complete payload copying, preservation of original data, both metadata formats, generated-option filtering, dropdown exclusivity, shared animations with effect-only variations, multi-checkbox combinations, original/swapped trigger mixtures, and group pagination. PAP conversion and the UI still require in-game testing.

### Source restoration

This repository previously held the installer manifest only. The source was restored from the local `NullEmoteSwapper-debug-multi-trigger-source.zip` archive from October 27, 2026. Its companion test-build ZIP has SHA-256 `50a89a09d4b9fc8162941ff3d98c766075e863133965a4ba9680a5c06586298e`, matching the published v0.1.1 release asset. The installer manifest and release package are versioned together for Dalamud updates.
