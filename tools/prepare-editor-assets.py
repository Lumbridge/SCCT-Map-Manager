"""Copy the selected Rainbow Six packs into a local publication draft. Never uploads."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("source", type=Path, help="Read-only source installation containing Packages")
parser.add_argument("output", type=Path, help="New draft directory (must not exist)")
args = parser.parse_args()
source = args.source.resolve(strict=True)
output = args.output.resolve()
if output == source or source in output.parents:
    parser.error("The draft must be outside the source installation.")
if output.exists():
    parser.error("Choose a new draft directory; existing drafts are never overwritten.")

packs = [
    ("r6v-calypso-casino", "Rainbow Six Vegas - Calypso Casino", [
        "StaticMeshes/R6V_MP_Casino_01.usx", "StaticMeshes/CalypsoMeshes.usx", "StaticMeshes/CalypsoPlacementMirrors.usx",
        "Textures/CalypsoColours_Expanded.utx", "Textures/CalypsoColours_Partial.utx", "Textures/CalypsoPlacementMaterials.utx"]),
]
for _, _, files in packs:
    for relative in files:
        if not (source / "Packages" / relative).is_file():
            parser.error(f"Missing package: {relative}")

output.mkdir(parents=True)
entries = []
for slug, name, files in packs:
    version = "v1.0.0"
    tag = f"{slug}-{version}"
    release = output / "release-files" / tag
    release.mkdir(parents=True)
    entry = dict(Id=f"assets/{slug}", Name=name, Category="Editor assets", Version=version,
                 Commit="0" * 40, NotesPath=f"assets/{slug}/{version}/README.md", Files=[])
    for relative in files:
        original = source / "Packages" / relative
        target = release / original.name
        shutil.copyfile(original, target)
        size = target.stat().st_size
        digest = hashlib.sha1(f"blob {size}\0".encode("ascii"))
        with target.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        entry["Files"].append(dict(Source=f"releases/download/{tag}/{target.name}",
                                   Destination=f"Packages/{relative}", Hash=digest.hexdigest(), Size=size))
    notes = output / "repository-files" / entry["NotesPath"]
    notes.parent.mkdir(parents=True)
    notes.write_text(f"# {name} {version}\n\nInitial version of the converted editor packages.\n\n"
                     "These are reusable editor assets, not a playable map. Keep the included packages together. "
                     "The mesh packages contain converted materials and textures. Conversion limitations, including "
                     "missing colours or approximated shaders, may remain; review assets in your own map before release.\n\n"
                     "## Included files\n\n" + "".join(f"- `{f}`\n" for f in files), encoding="utf-8")
    entries.append(entry)
manifest = output / "repository-files" / "asset-catalog.json"
manifest.write_text(json.dumps(entries, indent=2) + "\n", encoding="utf-8")
(output / "REVIEW.md").write_text(
    "# Editor asset publication draft\n\nNothing has been uploaded.\n\n"
    "Calypso Casino pack, initially versioned v1.0.0. Packages were copied from the selected source installation. "
    "This draft excludes placement maps, menu entries, and experimental proof/test meshes.\n\n"
    "After Ryan approves publication: upload each release-files/<tag> folder's contents as assets on "
    "the matching SCCT-Maps GitHub release, then commit the repository-files contents to SCCT-Maps. "
    "Do not commit the large binaries to the Git tree. Review the package selection and notes first. "
    "The manifest's Commit is replaced with the actual pinned catalog commit when read.\n\n"
    "For updates, use a new version and release tag, recompute file hashes and sizes, and add release notes. "
    "Keep old releases available for older catalogs.\n", encoding="utf-8")
print(f"Prepared {len(entries)} packs ({sum(f['Size'] for e in entries for f in e['Files']) / 1024**2:.1f} MiB): {output}")
