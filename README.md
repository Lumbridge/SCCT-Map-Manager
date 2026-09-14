# SCCT Map Manager

A portable Windows app for **Splinter Cell: Chaos Theory Versus**. Browse, download,
enable, disable and update maps from [Lumbridge/SCCT-Maps](https://github.com/Lumbridge/SCCT-Maps).

## Run

Place **SCCT Map Manager.exe** in your game's `System` folder and double-click it.
The app automatically uses the parent game installation. **Change folder** selects
another installation. The executable includes its .NET runtime; no separate runtime
installation, Git installation, account or administrator access is required.

Windows 10/11, x64. The map collection targets Enhanced SCCT Versus 3.6.

## Manage maps

- **Search** by map or package name and filter by collection or status.
- **Community** includes the original community pack and its shared supporting assets.
- **Originals** selects the newest versioned release of each original map, including Shipment.
- **Enhanced** contains edited versions of existing levels.
- **Recovered** contains recovered playable maps and optional editable sources.
- **Download map** caches the current version without enabling it. If the map is
  already enabled, downloading an update applies it and keeps it enabled.
- **Enable map** installs the selected version, downloading any uncached files.
- **Disable map** removes the playable map and its matching menu package from the
  game's search paths. Managed editor sources are also disabled. Shared assets stay
  installed so disabling one map does not break another.
- **Enable map** restores a disabled version from the local cache, including an
  existing unmanaged copy. Download an update first to switch to the repository version.
- **Include editable source maps** is off for new installations. Enable it explicitly
  to install `Packages/MapsEd` files. Once installed, that choice is remembered.
- **Open backups** shows original files replaced during installs and updates.

Close the game and editor before changing installed maps. Downloads of inactive
maps can run while the game is open. Read each map's notes for dependencies supplied
by the base game; the manager downloads repository files, not proprietary base-game
assets missing from your installation.

## Existing maps and local edits

Existing map files appear as **Installed outside manager**. Disabling them saves
their actual bytes and menu packages; re-enabling restores those same bytes. This
does not silently replace a custom build with a downloaded map.

Installing a repository version over an existing file creates a backup first.
Later updates refuse to overwrite managed files that have been edited outside the
manager. Preserve your edited copy before updating. Shared dependencies with
different contents cannot be replaced while another managed, enabled map owns them.
Disable the conflicting map before switching variants that use the same package name.

Disabled maps, downloaded versions and backups are stored under
`System/SCCTMapManagerData`. Keep this folder when moving the app or installation;
it contains the files required to restore disabled maps. The manager does not delete
shared dependencies or automatically prune backups/cache files.

The app includes a bundled catalog for browsing on first launch, even if GitHub
is temporarily unavailable. The catalog is cached after a successful refresh. Downloaded maps work offline.
New maps and updates are discovered from the current GitHub repository tree. All
files for an operation come from one pinned commit and are verified against their
Git blob hashes. Symlinks, traversal paths, executables and unsupported asset
destinations are rejected.

Installations use staged downloads, durable backups and an operation journal.
If an operation fails, changes are rolled back. An interrupted process is recovered
on the next launch; recovery stops if it finds additional outside edits rather than
overwriting them. Only one manager can access an installation at a time.

## Build and test

Install the .NET 10 SDK on Windows, then run:

```powershell
./build.ps1 -Test
```

The self-contained, single executable is written to `artifacts/publish`, with a
SHA-256 file alongside it. Run `./build.ps1 -Test -LiveTests` to also fetch the
public map catalog and download/install Shipment in a disposable fixture.

Tests cover local-edit protection, shared ownership, case-insensitive paths,
unmanaged map round trips, source-map opt-in, corrupt downloads, cancellation,
locked-file rollback, recovery after interruption and persistence. Test data remains
under `artifacts/tests` for inspection. No test targets the real game installation.

For a UI smoke check against a disposable installation:

```powershell
& './artifacts/publish/SCCT Map Manager.exe' --root 'path/to/disposable/game' --ui-smoke 'path/to/screenshots'
```

The smoke check refreshes the catalog, tests search/collection filters and writes
a screenshot and report. It does not install or disable maps.

## Repository status

Development is currently local. The public GitHub repository has been created,
but code, commits and release files must not be pushed until Ryan approves.
