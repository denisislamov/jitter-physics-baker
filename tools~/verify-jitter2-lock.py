#!/usr/bin/env python3
"""Verify jitter2.lock.json against current Jitter2~/Runtime sources."""

from __future__ import annotations

import argparse
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
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    package_root = args.package_root.resolve()
    lock_path = (package_root / args.lock_file).resolve()
    source_root = (package_root / args.source_root).resolve()

    lock_data = load_lock(lock_path)
    expected = lock_data.get("sourceContentHash", "")

    include_patterns = lock_data.get("includedFiles", ["**/*.cs", "**/*.rsp"])
    exclude_patterns = lock_data.get("excludedFiles", [])
    compile_profile_text = canonical_compile_profile_text(lock_data)
    inputs = collect_inputs(source_root, include_patterns, exclude_patterns)
    actual = compute_source_content_hash(inputs, compile_profile_text)

    if expected == actual:
        print(f"OK: {actual}")
        print(f"included files: {len(inputs)}")
        return 0

    print("ERROR: jitter2.lock.json is stale")
    print(f"expected: {expected}")
    print(f"actual:   {actual}")
    print(f"included files: {len(inputs)}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())

