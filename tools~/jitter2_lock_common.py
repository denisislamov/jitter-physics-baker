#!/usr/bin/env python3
"""Shared helpers for jitter2 lock hash tooling."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from typing import Any, Iterable


DEFAULT_LOCK_PATH = "jitter2.lock.json"
DEFAULT_SOURCE_ROOT = "Jitter2~/Runtime"


def load_lock(lock_path: Path) -> dict[str, Any]:
    with lock_path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def canonical_compile_profile_text(lock_data: dict[str, Any]) -> str:
    profile = lock_data.get("compileProfile", {})
    return json.dumps(profile, sort_keys=True, ensure_ascii=True, separators=(",", ":"))


def canonical_relative_path(path: Path, root: Path) -> str:
    return str(path.relative_to(root)).replace("\\", "/")


def normalize_content(path: Path) -> bytes:
    data = path.read_bytes()
    if is_text_file(path):
        text = data.decode("utf-8")
        text = text.replace("\r\n", "\n").replace("\r", "\n")
        return text.encode("utf-8")
    return data


def is_text_file(path: Path) -> bool:
    return path.suffix.lower() in {".cs", ".rsp", ".json", ".txt", ".md", ".asmdef"}


def matches_any_pattern(path: str, patterns: Iterable[str]) -> bool:
    return any(glob_matches(path, pattern) for pattern in patterns)


def glob_matches(path: str, pattern: str) -> bool:
    """Deterministic glob matching, defined here rather than taken from pathlib.

    `pathlib.PurePosixPath.match` changed `**` semantics between Python releases and
    never matched a top-level file against `**/*.cs`. The lock hash has to be identical
    in this script and in the C# editor implementation, so the rules are spelled out:

    * `**/` matches zero or more leading directories,
    * `**`  matches anything, including `/`,
    * `*`   matches anything except `/`,
    * `?`   matches a single character except `/`.
    """
    return _compile_glob(pattern).match(path) is not None


def _compile_glob(pattern: str) -> re.Pattern[str]:
    cached = _GLOB_CACHE.get(pattern)
    if cached is not None:
        return cached

    regex: list[str] = ["^"]
    index = 0
    length = len(pattern)
    while index < length:
        character = pattern[index]
        if pattern.startswith("**/", index):
            regex.append("(?:[^/]+/)*")
            index += 3
        elif pattern.startswith("**", index):
            regex.append(".*")
            index += 2
        elif character == "*":
            regex.append("[^/]*")
            index += 1
        elif character == "?":
            regex.append("[^/]")
            index += 1
        else:
            regex.append(re.escape(character))
            index += 1

    regex.append("$")
    compiled = re.compile("".join(regex))
    _GLOB_CACHE[pattern] = compiled
    return compiled


_GLOB_CACHE: dict[str, re.Pattern[str]] = {}


def collect_inputs(root: Path, include_patterns: list[str], exclude_patterns: list[str]) -> list[tuple[str, bytes]]:
    if not root.exists():
        return []

    selected: list[str] = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        relative = canonical_relative_path(path, root)
        if include_patterns and not matches_any_pattern(relative, include_patterns):
            continue
        if exclude_patterns and matches_any_pattern(relative, exclude_patterns):
            continue
        selected.append(relative)

    # Ordinal sort on the canonical relative path, so that the digest order does not
    # depend on the file system enumeration order or on the absolute location of the
    # package. The C# editor implementation sorts the same way.
    selected.sort()

    return [(relative, normalize_content(root / relative)) for relative in selected]


def compute_source_content_hash(inputs: list[tuple[str, bytes]], compile_profile_text: str) -> str:
    digest = hashlib.sha256()

    profile_bytes = compile_profile_text.encode("utf-8")
    digest.update(b"compileProfile\n")
    digest.update(str(len(profile_bytes)).encode("ascii"))
    digest.update(b"\n")
    digest.update(profile_bytes)
    digest.update(b"\n")

    for relative_path, content in inputs:
        path_bytes = relative_path.encode("utf-8")
        digest.update(path_bytes)
        digest.update(b"\n")
        digest.update(str(len(content)).encode("ascii"))
        digest.update(b"\n")
        digest.update(content)
        digest.update(b"\n")

    return "sha256:" + digest.hexdigest()




