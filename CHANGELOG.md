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

[Unreleased]: https://github.com/denisislamov/jitter-physics-baker/compare/main...HEAD
