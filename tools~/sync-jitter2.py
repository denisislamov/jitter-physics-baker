#!/usr/bin/env python3
"""Refresh `Jitter2~/Runtime` from a pinned Jitter2 revision.

The snapshot is never edited by hand. It is produced by this script, so that the
question "which Jitter2 is this package built against" always has an answer that can be
re-derived: a repository, a ref, a commit and a content hash.

Two sources are supported:

* `--source <path>`  copy from a local checkout, which is how a consumer fork (for
  example the vendored Jitter2 of a game project) becomes the snapshot;
* `--repo/--ref`     clone the given ref of the upstream repository.

After syncing, `jitter2.lock.json` is updated with the provenance and the recomputed
canonical source hash, because a snapshot without a matching lock is exactly the silent
drift the lock exists to prevent.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

from jitter2_lock_common import (
    DEFAULT_LOCK_PATH,
    DEFAULT_SOURCE_ROOT,
    canonical_compile_profile_text,
    collect_inputs,
    compute_source_content_hash,
    load_lock,
)

UPSTREAM_REPOSITORY = "https://github.com/notgiven688/jitterphysics2"

# Path of the library inside the upstream repository layout.
UPSTREAM_LIBRARY_SUBPATH = "src/Jitter2"

# Only compile-relevant sources are copied. The upstream csproj, packaging assets and
# build output describe how upstream builds the library; the snapshot is compiled by this
# package's own project files and by a consumer asmdef instead.
COPIED_SUFFIXES = {".cs", ".rsp"}
SKIPPED_DIRECTORIES = {"bin", "obj", "_package", ".git"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--package-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Root folder of com.datasakura.jitter-physics-baker.",
    )
    parser.add_argument("--source", type=Path, help="Local checkout to copy from.")
    parser.add_argument("--repo", default=UPSTREAM_REPOSITORY, help="Repository to clone when --source is absent.")
    parser.add_argument("--ref", help="Tag, branch or commit to clone.")
    parser.add_argument(
        "--library-subpath",
        default=UPSTREAM_LIBRARY_SUBPATH,
        help="Path of the library inside the source tree.",
    )
    parser.add_argument(
        "--patch-set-id",
        help="Identifier of the applied patch set; defaults to the synced ref.",
    )
    return parser.parse_args()


def run_git(arguments: list[str], cwd: Path | None = None) -> str:
    result = subprocess.run(
        ["git", *arguments],
        cwd=str(cwd) if cwd else None,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def resolve_source(args: argparse.Namespace, workspace: Path) -> tuple[Path, str, str]:
    """Returns the checkout root, the repository it came from and its commit."""
    if args.source is not None:
        source = args.source.resolve()
        if not source.is_dir():
            raise SystemExit(f"error: --source {source} is not a directory")
        try:
            commit = run_git(["rev-parse", "HEAD"], cwd=source)
        except (subprocess.CalledProcessError, FileNotFoundError):
            commit = "UNKNOWN"
        return source, str(source), commit

    if not args.ref:
        raise SystemExit("error: --ref is required when --source is not given")

    checkout = workspace / "checkout"
    print(f"cloning {args.repo} at {args.ref}")
    run_git(["clone", "--depth", "1", "--branch", args.ref, "--quiet", args.repo, str(checkout)])
    return checkout, args.repo, run_git(["rev-parse", "HEAD"], cwd=checkout)


def copy_sources(library_root: Path, destination: Path) -> list[str]:
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)

    copied: list[str] = []
    for path in sorted(p for p in library_root.rglob("*") if p.is_file()):
        relative = path.relative_to(library_root)
        if any(part in SKIPPED_DIRECTORIES for part in relative.parts):
            continue
        if path.suffix.lower() not in COPIED_SUFFIXES:
            continue

        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)

        # Line endings are normalized on the way in, so that the snapshot in this
        # repository hashes the same regardless of the platform it was synced on.
        text = path.read_bytes().decode("utf-8")
        target.write_bytes(text.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8"))
        copied.append(str(relative).replace("\\", "/"))

    return copied


def copy_license(checkout: Path, package_root: Path) -> bool:
    for candidate in ("LICENSE", "LICENSE.md", "LICENSE.txt"):
        source = checkout / candidate
        if source.exists():
            target = package_root / "Jitter2~" / "LICENSE.md"
            target.write_text(source.read_text(encoding="utf-8"), encoding="utf-8", newline="\n")
            return True
    return False


def main() -> int:
    args = parse_args()
    package_root = args.package_root.resolve()
    lock_path = package_root / DEFAULT_LOCK_PATH
    destination = package_root / DEFAULT_SOURCE_ROOT

    lock_data = load_lock(lock_path)
    previous_hash = lock_data.get("sourceContentHash", "")

    with tempfile.TemporaryDirectory(prefix="sync-jitter2-") as workspace:
        checkout, repository, commit = resolve_source(args, Path(workspace))

        library_root = checkout / args.library_subpath
        if not library_root.is_dir():
            raise SystemExit(f"error: {library_root} does not exist in the source tree")

        copied = copy_sources(library_root, destination)
        license_copied = copy_license(checkout, package_root)

    if not copied:
        raise SystemExit("error: no sources were copied; check --library-subpath")

    inputs = collect_inputs(
        destination,
        lock_data.get("includedFiles", ["**/*.cs", "**/csc.rsp"]),
        lock_data.get("excludedFiles", []),
    )
    source_hash = compute_source_content_hash(inputs, canonical_compile_profile_text(lock_data))

    lock_data["upstreamRepository"] = repository
    lock_data["upstreamCommit"] = commit
    lock_data["patchSetId"] = args.patch_set_id or f"upstream-{args.ref}" if args.ref else "local-source"
    lock_data["sourceContentHash"] = source_hash

    with lock_path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(lock_data, handle, ensure_ascii=True, indent=2)
        handle.write("\n")

    print(f"copied files      : {len(copied)}")
    print(f"hashed files      : {len(inputs)}")
    print(f"license copied    : {'yes' if license_copied else 'no'}")
    print(f"upstreamCommit    : {commit}")
    print(f"previous hash     : {previous_hash}")
    print(f"sourceContentHash : {source_hash}")
    print(f"changed           : {'yes' if source_hash != previous_hash else 'no'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

