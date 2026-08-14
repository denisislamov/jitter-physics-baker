#!/usr/bin/env python3
"""Make the Jitter2 snapshot compile against netstandard2.1.

Unity runs on a .NET Standard 2.1 surface. The snapshot targets modern .NET and reaches for a
handful of APIs that arrived in .NET 5 through .NET 8. Most of those gaps are missing *types*,
which `Jitter2~/Compat` supplies without touching upstream code. What is left are calls to static
members added to types that already exist, and those cannot be shimmed from outside: the call site
has to change.

This script makes exactly those edits. Every one of them is a local, behaviour-preserving
rewrite - an argument check spelled out longhand, an enum member replaced by its value, a JIT hint
dropped. None of them touch the simulation.

The script is idempotent and is meant to run right after `sync-jitter2.py`, so a snapshot refresh
never silently loses the patches. It reports which of them it applied and fails if a patch no
longer matches, because a silently skipped patch would surface much later as a build error nobody
connects to the sync.

Usage:
    python3 patch-jitter2-netstandard.py [--check]

    --check  report what would change and exit non-zero if anything would, without writing.
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path

RUNTIME = Path(__file__).resolve().parent.parent / "Jitter2~" / "Runtime"


@dataclass(frozen=True)
class Patch:
    """A single search/replace, scoped to one file, with the reason it exists."""

    relative_path: str
    old: str
    new: str
    why: str
    expected: int = 0  # 0 means "at least one"


PATCHES: list[Patch] = [
    # ---- MethodImplOptions.AggressiveOptimization: added in .NET Core 3.0 -------------------
    # A hint to the JIT about tiering. It has no observable semantics, and the numeric value is
    # stable, so passing it directly keeps the attribute intact on runtimes that do understand it.
    *[
        Patch(
            path,
            "MethodImplOptions.AggressiveOptimization",
            "(MethodImplOptions)512 /* AggressiveOptimization: absent from netstandard2.1 */",
            "MethodImplOptions.AggressiveOptimization is not in netstandard2.1",
        )
        for path in (
            "Dynamics/Contact.cs",
            "Collision/NarrowPhase/NarrowPhase.cs",
            "Collision/NarrowPhase/ConvexPolytope.cs",
            "Collision/DynamicTree/DynamicTree.cs",
        )
    ],
    # ---- Generic math on double: added in .NET 7 -------------------------------------------
    Patch(
        "Collision/DynamicTree/DynamicTree.cs",
        "double.Min(",
        "Math.Min(",
        "double.Min is a generic-math member from .NET 7",
        expected=2,
    ),
    # ---- ref-returning views over 'this': ref safety and visibility --------------------------
    # Two changes in one line, for two reasons.
    #
    # Unsafe.AsRef(in T) carries its argument's escape scope, so returning a ref derived from
    # 'this' is rejected. Going through a raw pointer is what the original does at the machine
    # level anyway; this only stops the compiler from tracking a lifetime it cannot verify.
    #
    # The members also stop being public. VectorReal resolves to the shim's Vector128<float>, and
    # a public member would put that type in the assembly's surface, where it collides by name
    # with the real System.Runtime.Intrinsics for any consumer targeting .NET 5 or later - the
    # dedicated server among them. The view is a SIMD implementation detail with no meaning once
    # there is no SIMD, so keeping it internal costs nothing and removes the collision outright.
    Patch(
        "Collision/DynamicTree/TreeBox.cs",
        "public readonly ref VectorReal VectorMin => "
        "ref Unsafe.As<JVector, VectorReal>(ref Unsafe.AsRef(in this.Min));",
        "internal readonly unsafe ref VectorReal VectorMin => "
        "ref Unsafe.AsRef<VectorReal>(Unsafe.AsPointer(ref Unsafe.AsRef(in this.Min)));",
        "ref safety rules reject returning a ref derived from 'this'",
        expected=1,
    ),
    Patch(
        "Collision/DynamicTree/TreeBox.cs",
        "public readonly ref VectorReal VectorMax => "
        "ref Unsafe.As<JVector, VectorReal>(ref Unsafe.AsRef(in this.Max));",
        "internal readonly unsafe ref VectorReal VectorMax => "
        "ref Unsafe.AsRef<VectorReal>(Unsafe.AsPointer(ref Unsafe.AsRef(in this.Max)));",
        "ref safety rules reject returning a ref derived from 'this'",
        expected=1,
    ),
    # ---- Throw helpers: added in .NET 6 and .NET 8 -------------------------------------------
    # Spelled out longhand. The exception type, the parameter name and the conditions are the ones
    # the helpers use, so callers observe no difference.
    Patch(
        "Dynamics/RigidBody.cs",
        "ArgumentNullException.ThrowIfNull(shapes);",
        "if (shapes is null) throw new ArgumentNullException(nameof(shapes));",
        "ArgumentNullException.ThrowIfNull is from .NET 6",
        expected=2,
    ),
    Patch(
        "Dynamics/RigidBody.cs",
        "ArgumentNullException.ThrowIfNull(shape);",
        "if (shape is null) throw new ArgumentNullException(nameof(shape));",
        "ArgumentNullException.ThrowIfNull is from .NET 6",
        expected=4,
    ),
    Patch(
        "World.cs",
        "ArgumentNullException.ThrowIfNull(island);",
        "if (island is null) throw new ArgumentNullException(nameof(island));",
        "ArgumentNullException.ThrowIfNull is from .NET 6",
        expected=1,
    ),
    Patch(
        "Collision/Shapes/TransformedShape.cs",
        "ArgumentNullException.ThrowIfNull(shape);",
        "if (shape is null) throw new ArgumentNullException(nameof(shape));",
        "ArgumentNullException.ThrowIfNull is from .NET 6",
        expected=1,
    ),
    Patch(
        "Collision/Shapes/TriangleShape.cs",
        "ArgumentNullException.ThrowIfNull(mesh);",
        "if (mesh is null) throw new ArgumentNullException(nameof(mesh));",
        "ArgumentNullException.ThrowIfNull is from .NET 6",
        expected=1,
    ),
    Patch(
        "Collision/Shapes/TriangleShape.cs",
        "ArgumentOutOfRangeException.ThrowIfNegative(index);",
        "if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));",
        "ArgumentOutOfRangeException.ThrowIfNegative is from .NET 8",
        expected=1,
    ),
    Patch(
        "Collision/Shapes/TriangleShape.cs",
        "ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, mesh.Indices.Length);",
        "if (index >= mesh.Indices.Length) throw new ArgumentOutOfRangeException(nameof(index));",
        "ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual is from .NET 8",
        expected=1,
    ),
    Patch(
        "World.cs",
        "ObjectDisposedException.ThrowIf(disposed, this);",
        "if (disposed) throw new ObjectDisposedException(GetType().FullName);",
        "ObjectDisposedException.ThrowIf is from .NET 7",
        expected=1,
    ),
    # ---- Enum.IsDefined<T>(T): added in .NET 5 ----------------------------------------------
    Patch(
        "World.Deterministic.cs",
        "if (!Enum.IsDefined(value))",
        "if (!Enum.IsDefined(typeof(SolveMode), value))",
        "the generic Enum.IsDefined overload is from .NET 5",
        expected=1,
    ),
    # ---- OperatingSystem.IsWindows(): added in .NET 5 ----------------------------------------
    # Fully qualified rather than importing the namespace. Adding a using would make this patch's
    # search text a substring of its replacement, and it would then re-apply on every run.
    Patch(
        "Parallelization/ThreadPool.cs",
        "!OperatingSystem.IsWindows()",
        "!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform("
        "System.Runtime.InteropServices.OSPlatform.Windows)",
        "OperatingSystem.IsWindows is from .NET 5",
        expected=1,
    ),
    # ---- Interlocked on ulong: only the signed overloads exist in netstandard2.1 -------------
    # Reinterpreting the storage as long keeps the operation a single atomic instruction; two's
    # complement makes the wrapped result identical bit for bit.
    Patch(
        "World.cs",
        "return Interlocked.Increment(ref _idCounter);",
        "return unchecked((ulong)Interlocked.Increment("
        "ref System.Runtime.CompilerServices.Unsafe.As<ulong, long>(ref _idCounter)));",
        "Interlocked has no ulong overload in netstandard2.1",
        expected=1,
    ),
    Patch(
        "World.cs",
        "ulong max = Interlocked.Add(ref _idCounter, count64) + 1;",
        "ulong max = unchecked((ulong)Interlocked.Add("
        "ref System.Runtime.CompilerServices.Unsafe.As<ulong, long>(ref _idCounter), (long)count64)) + 1;",
        "Interlocked has no ulong overload in netstandard2.1",
        expected=1,
    ),
]


def apply(check_only: bool) -> int:
    applied = 0
    already = 0
    failed: list[str] = []

    for patch in PATCHES:
        path = RUNTIME / patch.relative_path
        if not path.is_file():
            failed.append(f"{patch.relative_path}: file not found")
            continue

        text = path.read_text(encoding="utf-8")

        if patch.new in text and patch.old not in text:
            already += 1
            continue

        count = text.count(patch.old)
        if count == 0:
            failed.append(
                f"{patch.relative_path}: pattern not found and not already applied "
                f"({patch.why}): {patch.old[:70]!r}"
            )
            continue

        if patch.expected and count != patch.expected:
            failed.append(
                f"{patch.relative_path}: expected {patch.expected} occurrences, found {count} "
                f"({patch.why})"
            )
            continue

        if not check_only:
            path.write_text(text.replace(patch.old, patch.new), encoding="utf-8")
        applied += 1

    for message in failed:
        print(f"FAIL {message}", file=sys.stderr)

    verb = "would apply" if check_only else "applied"
    print(f"{verb} {applied} patch(es), {already} already in place, {len(failed)} failed")

    if failed:
        return 1
    if check_only and applied:
        return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="report pending patches without writing, exit non-zero if any are pending",
    )
    arguments = parser.parse_args()

    if not RUNTIME.is_dir():
        print(f"snapshot not found: {RUNTIME}", file=sys.stderr)
        return 1

    return apply(arguments.check)


if __name__ == "__main__":
    raise SystemExit(main())



