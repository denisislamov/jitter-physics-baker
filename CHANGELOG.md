# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Package skeleton and the Jitter-free assembly graph.** The package now exists as a
  UPM package with `package.json`, license, third party notices, line-ending policy and
  the five assemblies described by the specification:
  `DataSakura.JitterPhysics.Contracts` and `DataSakura.JitterPhysics.ArtifactCodec`
  (both `noEngineReferences`, so the same sources compile under a plain .NET SDK for the
  dedicated server), `DataSakura.JitterPhysics.UnityArtifact`,
  `DataSakura.JitterPhysics.Authoring` and the Editor-only
  `DataSakura.JitterPhysics.Editor`. None of them reference Jitter2, which is what keeps
  a clean import free of compile errors before the installer has run.
- **EditMode/PlayMode test assemblies** and a package layout self-test, so the structural
  invariants (no Jitter references in the always-compiled assemblies, Editor assembly is
  Editor-only) are verified by CI rather than by review.
- **`tools/publish-package.sh` and `tools/verify-package-meta.py`** in the development
  project: the package is published to its standalone repository with `git subtree split`
  and is validated for truncated `.meta` files and Git LFS pointers first, both of which
  are invisible here but fatal for a consumer installing from a git URL.
- **Artifact contracts and the binary codec (schema 1).** Portable DTOs for world
  settings, static bodies and box/sphere/capsule/mesh shapes, a canonical little-endian
  writer, a bounds-checked reader, a semantic validator, a deterministic manifest codec
  and the transport-agnostic compatibility token. Canonicalization (`-0.0f` folding,
  quaternion sign convention, ordinal record ordering) lives in one place, so a repeated
  bake of an unchanged scene is byte-identical and hashes the same on any machine.
- **`runtimeCompatibilityId`.** Derived from the artifact schema, the Jitter source hash,
  the compile profile and the package's conversion/world-builder semantics versions. It is
  always computed, never hand-written, so a runtime-semantic change cannot keep an id that
  makes an incompatible client and server look compatible.
- **`jitter2.lock.json` and the canonical source hash.** Two independent implementations
  agree byte-for-byte on the hash: `tools~/hash-jitter2.py` / `tools~/verify-jitter2-lock.py`
  for CI, and `JitterPhysicsSourceHasher` for the editor. File selection, ordering, line
  ending normalization and the serialized compile profile are pinned by the package rather
  than inherited from a platform default, and `tools~/test-jitter2-lock.py` asserts the
  invariants both sides rely on.
- **Jitter2 discovery and the compatibility report.** `Tools > DataSakura > Jitter Physics >
  Setup` classifies the project as `Missing`, `Compatible`, `Incompatible`, `Duplicate` or
  `UnsupportedPlugin`, shows the expected and actual source hashes and exports a
  machine-readable JSON report for CI. Discovery goes through assembly metadata rather than
  a fixed folder, and the window only reads: installing anything remains an explicit,
  separate command.
- **`Jitter2~/` dormant snapshot skeleton** with patch/license provenance and a standalone
  `Jitter2.Core` assembly definition template for projects that have no Jitter2 of their own.
- **The snapshot is now populated: upstream Jitter2 `2.8.9`** (commit `c15bc6ab`, 96
  sources), synced by `tools~/sync-jitter2.py` rather than copied by hand, so the question
  "which Jitter2 is this built against" is answered by a repository, a ref, a commit and a
  content hash. `Server~/Tests` compiles it directly and runs smoke tests — a static body
  keeps its pose, a dynamic body comes to rest on it, the build really is single precision
  and two identical runs give identical results — because Unity never builds that folder
  and nothing else would notice if the fallback copy were broken.
  Note that this is *unpatched upstream*: it has no `JITTER_UNITY` define and uses hardware
  intrinsics, so it is not yet validated as a Unity fallback. The lock's compile profile
  says so instead of describing the intended end state; see `Jitter2~/PATCHES.md`.
- **`Server~/Tests`, a .NET 10 test project.** It compiles `Contracts` and `ArtifactCodec`
  by reference and runs the same test files as the Unity test assembly, which is what turns
  "these assemblies are engine-independent" from a claim into a checked property: the
  dedicated server compiles them with a different compiler and runtime than Unity does.
  Run through `tools~/test-dotnet.sh`.
- **Golden-bytes and corrupt-payload tests.** The schema 1 layout is asserted against bytes
  spelled out field by field, so changing the writer fails the build instead of silently
  redefining the format. The corrupt matrix covers bad magic, unknown schema, truncation,
  trailing bytes, empty input, hash mismatch, manifest disagreement and an artifact baked
  for another runtime — each rejected with its own error code and without producing a
  partially decoded artifact.
- **Authoring components.** `JitterPhysicsLevel` owns the level identity, the geometry root
  and the output folder; `JitterStaticBodySource` explicitly marks the root of one static
  body and carries its stable `sourceId` and material constants; `JitterPhysicsWorldProfile`
  holds the world settings that are baked into the artifact. Only colliders under a marked
  source are collected and inactive objects are never included, so a designer can add
  scenery without silently changing the level hash. Identifiers are sanitized once and then
  kept: renaming an object does not change the artifact.

[Unreleased]: https://github.com/denisislamov/jitter-physics-baker/compare/main...HEAD
