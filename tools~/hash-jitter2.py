#!/usr/bin/env python3
"""Compute or update the canonical Jitter2 source hash in jitter2.lock.json."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from jitter2_lock_common import (
    DEFAULT_LOCK_PATH,
    DEFAULT_SOURCE_ROOT,
    canonical_compile_profile_text,
    collect_inputs,
    compute_source_content_hash,
    load_lock,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--package-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Root folder of com.datasakura.jitter-physics-baker package.",
    )
    parser.add_argument(
        "--lock-file",
        default=DEFAULT_LOCK_PATH,
        help="Lock file path relative to --package-root.",
    )
    parser.add_argument(
        "--source-root",
        default=DEFAULT_SOURCE_ROOT,
        help="Source root relative to --package-root.",
    )
    parser.add_argument(
        "--print-only",
        action="store_true",
        help="Print computed hash without updating lock file.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    package_root = args.package_root.resolve()
    lock_path = (package_root / args.lock_file).resolve()
    source_root = (package_root / args.source_root).resolve()

    lock_data = load_lock(lock_path)
    include_patterns = lock_data.get("includedFiles", ["**/*.cs", "**/*.rsp"])
    exclude_patterns = lock_data.get("excludedFiles", [])
    compile_profile_text = canonical_compile_profile_text(lock_data)

    inputs = collect_inputs(source_root, include_patterns, exclude_patterns)
    computed_hash = compute_source_content_hash(inputs, compile_profile_text)

    if args.print_only:
        print(computed_hash)
        print(f"included files: {len(inputs)}")
        return 0

    lock_data["sourceContentHash"] = computed_hash
    with lock_path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(lock_data, handle, ensure_ascii=True, indent=2)
        handle.write("\n")

    print(f"updated {lock_path}")
    print(f"sourceContentHash={computed_hash}")
    print(f"included files={len(inputs)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

