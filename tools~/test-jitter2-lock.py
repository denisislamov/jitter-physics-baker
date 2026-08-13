#!/usr/bin/env python3
"""Cross-implementation invariants of the canonical Jitter2 source hash.

The same rules are implemented in C# by `JitterPhysicsSourceHasher`. Both sides must
agree, so the properties are asserted here and mirrored by `JitterPhysicsLockTests`.
"""

from __future__ import annotations

import shutil
import tempfile
from pathlib import Path

from jitter2_lock_common import (
    canonical_compile_profile_text,
    collect_inputs,
    compute_source_content_hash,
    glob_matches,
    load_lock,
)

PACKAGE_ROOT = Path(__file__).resolve().parent.parent
LOCK = load_lock(PACKAGE_ROOT / "jitter2.lock.json")
INCLUDE = LOCK["includedFiles"]
EXCLUDE = LOCK["excludedFiles"]
PROFILE = canonical_compile_profile_text(LOCK)

# A synthetic profile, so the assertion states the serialization *rule* rather than the
# current contents of the lock. `JitterPhysicsLockTests` asserts the same case in C#.
SYNTHETIC_PROFILE = {
    "zebra": "last",
    "allowUnsafe": True,
    "count": 7,
    "Apple": "uppercase sorts first",
    "unicode": "\u00fcber",
}

EXPECTED_SYNTHETIC_PROFILE_TEXT = (
    '{"Apple":"uppercase sorts first",'
    '"allowUnsafe":true,'
    '"count":7,'
    '"unicode":"\\u00fcber",'
    '"zebra":"last"}'
)

GLOB_CASES = [
    ("x.cs", "**/*.cs", True),
    ("a/x.cs", "**/*.cs", True),
    ("a/b/x.cs", "**/*.cs", True),
    ("README.md", "**/*.cs", False),
    ("a/x.csx", "**/*.cs", False),
    ("csc.rsp", "**/csc.rsp", True),
    ("Runtime/csc.rsp", "**/csc.rsp", True),
    ("x.meta", "**/*.meta", True),
    ("bin/x.cs", "**/bin/**", True),
    ("a/bin/x.cs", "**/bin/**", True),
    ("a/binary/x.cs", "**/bin/**", False),
]


def source_hash(root: Path) -> str:
    return compute_source_content_hash(collect_inputs(root, INCLUDE, EXCLUDE), PROFILE)


def make_tree(files: list[tuple[str, str]]) -> Path:
    root = Path(tempfile.mkdtemp(prefix="jitter-physics-hash-"))
    for relative, content in files:
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content.encode("utf-8"))
    return root


def check(name: str, condition: bool, failures: list[str]) -> None:
    print(f"{'OK  ' if condition else 'FAIL'} {name}")
    if not condition:
        failures.append(name)


def main() -> int:
    failures: list[str] = []

    check(
        "compile profile serialization rule",
        canonical_compile_profile_text({"compileProfile": SYNTHETIC_PROFILE})
        == EXPECTED_SYNTHETIC_PROFILE_TEXT,
        failures,
    )
    check("real compile profile is serialized", PROFILE.startswith("{"), failures)

    snapshot = collect_inputs(PACKAGE_ROOT / "Jitter2~" / "Runtime", INCLUDE, EXCLUDE)
    check("dormant snapshot is not empty", len(snapshot) > 0, failures)
    check(
        "lock matches the dormant snapshot",
        compute_source_content_hash(snapshot, PROFILE) == LOCK["sourceContentHash"],
        failures,
    )

    for path, pattern, expected in GLOB_CASES:
        check(f"glob {path!r} ~ {pattern!r} == {expected}", glob_matches(path, pattern) is expected, failures)

    lf = make_tree([("Core/World.cs", "using System;\nclass A { }\n")])
    crlf = make_tree([("Core/World.cs", "using System;\r\nclass A { }\r\n")])
    profile_tree = make_tree([("Core/World.cs", "namespace Jitter2 { }\n"), ("Core/csc.rsp", "-unsafe\n")])

    try:
        baseline = source_hash(lf)
        check("CRLF checkout hashes like LF checkout", source_hash(crlf) == baseline, failures)

        (lf / "Core" / "Jitter2.Core.asmdef").write_text('{"name":"Jitter2.Core"}\n', encoding="utf-8")
        (lf / "Core" / "World.cs.meta").write_text("guid: 0123456789\n", encoding="utf-8")
        check("consumer asmdef and meta do not affect identity", source_hash(lf) == baseline, failures)

        (lf / "Core" / "World.cs").write_text("using System;\nclass B { }\n", encoding="utf-8")
        check("source edit changes the hash", source_hash(lf) != baseline, failures)

        profile_baseline = source_hash(profile_tree)
        (profile_tree / "Core" / "csc.rsp").write_text("-unsafe -define:X\n", encoding="utf-8")
        check("csc.rsp edit changes the hash", source_hash(profile_tree) != profile_baseline, failures)
    finally:
        for root in (lf, crlf, profile_tree):
            shutil.rmtree(root, ignore_errors=True)

    if failures:
        print(f"\n{len(failures)} check(s) failed")
        return 1

    print("\nall checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())



