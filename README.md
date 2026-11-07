# NullEmoteSwapper

NullEmoteSwapper is a Dalamud plugin for changing emote triggers in Penumbra mods.

## Install through Dalamud

1. Open `/xlsettings` and select **Experimental**.
2. Add this stable URL under **Custom Plugin Repositories**:

   `https://github.com/t0nysama/NullEmoteSwapper/releases/latest/download/repo.json`

3. Save your settings, open `/xlplugins`, and install **NullEmoteSwapper**.

Keep this same repository URL. Plugin updates are delivered through Dalamud when a new release is published.

## Per-option emote copies (0.1.2)

1. Open `/nullemoteswapper`, select a mod, and scan it.
2. In **Source options**, select every option you want to copy. Click a selected row again to deselect it, then choose **Assign emotes to selected options**.
3. Assign a replacement to each emote under each selected option. Options remain independent even if they use the same source emote or have identical display names.
4. Click **Create option copies**. In Penumbra, enable the copies you want under the mod's **NullEmoteSwapper** group.

For example, a dropdown may have **Dance A** and **Dance B**, both using `/dance`. Select both and assign **Dance A /dance → /wave** and **Dance B /dance → /cheer**. This creates two independently selectable copies; each uses that option's own animation files.

Each copy retains the source option's supporting file mappings, file swaps, manipulations, and other option fields. Recognized source emote mappings are replaced by generated PAP copies. The original options and source files stay intact. Existing per-emote assignments within a multi-emote option are supported.

Replacement targets must be unique within an individual option. Different option copies may use the same replacement target, but enable only one copy for that target at a time. Supporting assets that use identical game paths can also conflict when different copies are enabled together; Penumbra's normal conflict resolution applies. Auxiliary option groups are not automatically combined with the selected option.

Generated options are excluded from subsequent scans. Use the existing remove-overrides control to remove generated copies and PAPs. Creation reports how many copies succeeded if an individual option fails.

## Build and validation

Requires .NET 10, the Dalamud API 15 development libraries, and `ECommons.dll` / `VFXEditor.dll` plus their dependencies from the existing plugin package. Place plugin dependencies in `lib/`, or supply their directory:

```powershell
dotnet build NullEmoteSwapper/NullEmoteSwapper.csproj -c Release -p:PluginDependencyDirectory="C:\path\to\plugin-dependencies"
dotnet run --project tests/RegressionTests -c Release
```

Dalamud libraries are resolved from XIVLauncher's development folder by default; `DALAMUD_HOME` can select a different installation. The packaged build is generated at `NullEmoteSwapper/bin/Release/NullEmoteSwapper/latest.zip`.

The regression scenarios compile the production scanner, assignment planner, and metadata writer with stubbed game lookup/logging. They verify option independence, race variants, complete payload copying, preservation of original data, both metadata formats, generated-option filtering, and failure handling. PAP conversion and the UI still require in-game testing.

### Source restoration

This repository previously held the installer manifest only. The source was restored from the local `NullEmoteSwapper-debug-multi-trigger-source.zip` archive from October 27, 2026. Its companion test-build ZIP has SHA-256 `50a89a09d4b9fc8162941ff3d98c766075e863133965a4ba9680a5c06586298e`, matching the published v0.1.1 release asset. The installer manifest and release package are versioned together for Dalamud updates.
