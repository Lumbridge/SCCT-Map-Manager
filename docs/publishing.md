# Publishing maps and asset packs

The manager reads the [SCCT-Maps](https://github.com/Lumbridge/SCCT-Maps) repository. Maps are picked up from its per-map `Packages` folders; ported maps live under `ports/<game-name>/<map-name>/` and appear in the **Ports** collection with their source game shown, while still installing into the normal SCCT `Packages` folders.

## Ported maps

Give each map its own folder, for example:

```text
ports/
  rainbow-six-vegas/
    calypso-casino/
      README.md
      Packages/Maps/CalyD.sdc
      Packages/MapsEd/CalyD.sdc
      Packages/Textures/CalyD-i.utc
  splinter-cell-double-agent/
    blackwing/
      README.md
      Packages/Maps/SCDA_BLKG1.sdc
```

Keep each map's dependencies inside its own folder. Maps from the same game are listed and installed independently. Folder names can use readable hyphenated names; the manager displays spaces.

For ports hosted as release assets, use `ports/<game-name>/<map-name>` as the manifest `Id`, identify the source `Game`, and set `NotesPath` to `ports/<game-name>/<map-name>/README.md`. The folder contains notes and checksums; the packages remain on their existing versioned releases. Existing flat port identities are recognized so installed maps retain their download and enable/disable state.

## Asset packs

Packs have a stable ID and an explicit version such as `v1.0.0`. The latest numeric version is what the manager lists.

**Small packs** can live in the repository at `assets/<pack>/v1.0.0/Packages/Textures` and `Packages/StaticMeshes`, with release notes at the version root.

**Large packs** are described by `asset-catalog.json` at the repository root, with the binaries attached to versioned releases in that repository:

- Each manifest entry has `Id`, `Name`, `Category`, `Version`, `Commit`, `NotesPath` and `Files`.
- Each file has a relative `releases/download/<tag>/<filename>` source, a package destination, its Git blob SHA-1 and byte size.
- Release tags end with the pack version, and notes live at `assets/<pack>/<version>/README.md`.

The manager pins the manifest and notes to the fetched commit, verifies every downloaded byte, and remembers installed versions across restarts. Publish a new version for every update and keep old releases available.

## Preparing a pack

```powershell
python tools/prepare-editor-assets.py "<source installation>" "<new draft directory>"
```

This copies the selected local Rainbow Six packages into a draft with a manifest and notes. It changes nothing in the source installation and uploads nothing; review the draft before publishing. Publishing to GitHub needs Ryan's explicit approval.
