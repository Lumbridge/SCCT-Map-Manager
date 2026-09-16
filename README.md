# SCCT Map Manager

Browse, download and manage maps for **Splinter Cell: Chaos Theory Versus**, using the [SCCT Maps collection](https://github.com/Lumbridge/SCCT-Maps).

## Install

[Download the latest release](https://github.com/Lumbridge/SCCT-Map-Manager/releases/latest), extract it, and put **SCCT Map Manager.exe** in your game's `System` folder. Run it from there.

Requires Windows 10/11 x64 and Enhanced SCCT Versus 3.6. To update, close the manager and replace the EXE.

## Using it

- Search or filter the map list.
- **Enable map** downloads and installs a map. **Disable map** removes it from play; you can enable it again later.
- **Download map** saves a map for later, or updates it if already enabled.
- Check **Include editable source maps** if you want the available editor files.
- **Change folder** switches game installations.

Close the game and editor before enabling, disabling or updating installed maps. Downloaded maps can be enabled offline.

## Textures and static meshes

Open **Tools → Textures & static meshes** for optional editor asset packs, including published Rainbow Six Vegas conversions. This library is separate from the main map list. Search by pack or package name, or filter for textures, static meshes or updates.

Each pack shows its available and installed version, with downloaded versions and release notes in the details. **Check for updates** refreshes the catalog; **Download & install** installs the pack into the selected installation. **Download for later** caches it for offline installation (or updates it immediately if already installed). Close the game and editor before installing or updating.

Downloads are verified, updates keep backups, and conflicting or edited packages are protected. Asset packs stay installed because maps may depend on them.

### Publishing asset packs

Packs use stable IDs and explicit versions such as `v1.0.0`. Small packs can live in `SCCT-Maps/assets/<pack>/v1.0.0/Packages/Textures` and `Packages/StaticMeshes`, with release notes at the version root. The latest numeric version is listed.

For large packages, put `asset-catalog.json` at the SCCT-Maps repository root and host binaries on versioned releases in that repository. Each manifest entry contains `Id`, `Name`, `Category`, `Version`, `Commit`, `NotesPath` and `Files`; each file contains a relative `releases/download/<tag>/<filename>` source, package destination, Git blob SHA-1 and byte size. Release tags end with the pack version. Notes live at `assets/<pack>/<version>/README.md`. The manager pins the manifest and notes to the fetched commit, verifies downloaded bytes, and retains installed versions across restarts. Use a new version for every update and keep old releases available.

`python tools/prepare-editor-assets.py "<source installation>" "<new draft directory>"` prepares the selected local Rainbow Six packages, manifest and notes without changing the source or uploading anything. Review the draft before publishing. GitHub publication requires Ryan's explicit approval.

## DLL patch

For custom-map loading crashes, try **DLL patch / restore** in the toolbar. The optional patch backs up your current DLL; use **Restore selected backup** to switch back. It may not fix every crash.

## Backups

Keep `System/SCCTMapManagerData` - it holds downloads, disabled maps and backups. **Open backups** opens the backup folder. Updates stop if they would overwrite your edited maps.

## Build

With the .NET 10 SDK installed:

```powershell
./build.ps1 -Test
```

Output: `artifacts/publish`. Add `-LiveTests` to test downloads against a disposable installation.
