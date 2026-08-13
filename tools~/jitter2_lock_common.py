#!/usr/bin/env python3
"""Shared helpers for jitter2 lock hash tooling."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
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
    posix = PurePosixPath(path)
    return any(posix.match(pattern) for pattern in patterns)


def collect_inputs(root: Path, include_patterns: list[str], exclude_patterns: list[str]) -> list[tuple[str, bytes]]:
    if not root.exists():
        return []

    inputs: list[tuple[str, bytes]] = []
    for path in sorted(p for p in root.rglob("*") if p.is_file()):
        relative = canonical_relative_path(path, root)
        if include_patterns and not matches_any_pattern(relative, include_patterns):
            continue
        if exclude_patterns and matches_any_pattern(relative, exclude_patterns):
            continue
        inputs.append((relative, normalize_content(path)))

    return inputs


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

