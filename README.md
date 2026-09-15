# SCCT Map Manager

Browse, download and manage maps for **Splinter Cell: Chaos Theory Versus**, using the [SCCT Maps collection](https://github.com/Lumbridge/SCCT-Maps).

## Install

[Download the latest release](https://github.com/Lumbridge/SCCT-Map-Manager/releases/latest), extract it, and put **SCCT Map Manager.exe** in your game's `System` folder. Run it from there.

Requires Windows 10/11 x64 and Enhanced SCCT Versus 3.6. To update, close the manager and replace the EXE.

## Using it

- Search or filter the map list. **JP's Maps** are pinned at the top.
- **Enable map** downloads and installs a map. **Disable map** removes it from play; you can enable it again later.
- **Download map** saves a map for later, or updates it if already enabled.
- Check **Include editable source maps** if you want the available editor files.
- **Change folder** switches game installations.

Close the game and editor before enabling, disabling or updating installed maps. Downloaded maps can be enabled offline.

## DLL patch

For custom-map loading crashes, try **DLL patch / restore** in the toolbar. The optional patch backs up your current DLL; use **Restore selected backup** to switch back. It may not fix every crash.

## Backups

Keep `System/SCCTMapManagerData` — it holds downloads, disabled maps and backups. **Open backups** opens the backup folder. Updates stop if they would overwrite your edited maps.

## Build

With the .NET 10 SDK installed:

```powershell
./build.ps1 -Test
```

Output: `artifacts/publish`. Add `-LiveTests` to test downloads against a disposable installation.
