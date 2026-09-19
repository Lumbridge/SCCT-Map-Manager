# Publishing maps and asset packs

The manager reads the [SCCT-Maps](https://github.com/Lumbridge/SCCT-Maps) repository. Maps are picked up from its per-map `Packages` folders; ported maps live under `ports/<game-name>/` and appear in the **Ports** collection with their source game shown, while still installing into the normal SCCT `Packages` folders.

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
