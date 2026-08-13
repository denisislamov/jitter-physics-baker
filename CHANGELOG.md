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
- **The bake pipeline.** `JitterPhysicsArtifactBuilder` collects marked sources, converts
  their colliders and emits a canonical artifact. Records are ordered by authored id and
  shapes by a structural key built from the hierarchy path, sibling index, component index
  and collider type — never from instance ids or traversal order, so two bakes of an
  unchanged scene are byte-identical and reordering siblings changes nothing.
- **Collider conversion with explicit rules.** Box keeps its full size under absolute
  scale; capsule length excludes the caps and the Unity axis becomes a local rotation;
  mesh vertices are baked into body-local space with the full transform, and a mirrored
  transform has its winding flipped so surfaces do not face inwards. Triggers, zero scale,
  non-finite transforms and unreadable meshes are refused with the offending object
  attached to the message. The single approximation — a sphere under non-uniform scale — is
  conservative and reported as a warning, because a slightly larger wall is better than one
  a player can walk through. A build is all-or-nothing.
- **`JitterPhysicsWorldBuilder`, the shared loader.** Artifact records become Jitter bodies
  and shapes through the public API only; nothing of the engine's internal state is
  restored. It reports body/shape counts, elapsed time and a topology fingerprint that lets
  a client and a server prove they built the same static world. Applying a second artifact
  to the same world is refused instead of merged, and a failed build is rolled back
  completely. The tick loop stays with the consumer. Verified under .NET against the
  dormant snapshot: geometry is created in artifact order, a decoded artifact yields the
  same fingerprint as the original, local shape poses survive, and a dynamic body actually
  comes to rest on the baked ground.
- **The artifact is now written into the project.** `JitterPhysicsBaker` stages the payload
  and the manifest in a temporary folder, decodes the bytes it is about to write and
  re-hashes them from disk, and only then moves them into place — so a truncated write or a
  file mangled on the way in is caught before it can replace a good result. A failed bake
  leaves the previously baked artifact untouched, because a level that used to work must not
  stop working because somebody pressed Bake with a broken scene. Baking in Play Mode is
  refused outright: the scene state there belongs to the simulation, not to the author.
- **`JitterPhysicsArtifactAsset` and `JitterPhysicsArtifactLoader`.** The payload lives in a
  separate `.bytes` `TextAsset` that Unity copies verbatim, so the bytes a client loads are
  the bytes that were hashed; the ScriptableObject only holds a reference and a copy of the
  manifest for the inspector. The loader treats those copied fields as untrusted: it
  re-hashes the payload, re-decodes it and reports a disagreement instead of quietly
  preferring one side, since a metadata field is exactly what a careless merge edits first.
  Re-baking updates the asset in place, so scene references survive.
- **`JitterPhysicsBakeCommand`, the single bake entry point.** The `runtimeCompatibilityId`
  always comes from the compatibility report and cannot be passed in by a caller, so no
  script can talk its way past a red Setup window and produce an artifact claiming a
  compatibility it does not have. Validation still runs when the setup is broken, because
  the authoring problems it reports are worth seeing first. Menu items for baking and
  validating the selected level log every issue against the object that caused it.
- **`IPhysicsArtifactProvider` and `FilePhysicsArtifactProvider`.** One boundary between a
  server's startup and wherever its artifact comes from: startup resolves a provider, asks
  for the artifact and either builds the world or refuses to accept players. A provider
  never returns anything that is not already hashed, decoded, validated and cross-checked
  against its manifest, because a caller holding an artifact object cannot tell how much of
  that happened. The file provider is pointed at the *manifest* rather than the payload —
  the expected hash, the counts and the tick rate live there, and a payload alone cannot be
  cross-checked. It reads the binary from the manifest's own folder and refuses a payload
  name that is not a plain file name, since a manifest is untrusted input and a server must
  not be talked into reading an arbitrary path. Both size caps are enforced against the file
  length before anything is read into memory. How the two files reach the machine —
  published content, a mounted volume, an artifact registry — stays with the consumer; the
  package assumes no deploy system and no directory layout. A new `SourceUnavailable` error
  code separates "the artifact was not delivered" from "the artifact is corrupt", because
  those call for different actions from whoever is on call.
- **`JitterPhysicsServerStartup`, the server's bring-up in one call.** It owns the order a
  dedicated server has to follow — obtain the artifact, check it against what this build
  claims to be, build the static world, report readiness — because that order is exactly
  what gets shortened under deadline pressure, and the result is a match where the server
  has no walls and every client looks like a cheater. The build's runtime id is mandatory;
  the level it was launched to host and the rate it steps at are optional and refused on
  mismatch rather than adopted, since a server that silently accepts the artifact's tick
  rate diverges from a client predicting at another one. `JitterPhysicsServerState` has no
  partially ready form: approval is gated on `IsReady`, and ignoring it yields a `null`
  artifact and an empty world instead of a running match. `SelfCheck` is the line a Docker
  smoke test greps for — level, short artifact hash, short topology fingerprint, counts,
  tick rate, elapsed — with full hashes deliberately left out of the log.
- **Embedded delivery.** `EmbeddedPhysicsArtifactProvider` serves an artifact compiled into
  the server binary, and `EmbeddedArtifactSourceGenerator` turns already baked bytes into
  deterministic generated C#. The generator never bakes and refuses a payload and a manifest
  that disagree, because the point of the export is that the server runs the very bytes the
  client verified. The payload is chunked base64 — a multi-megabyte string literal is hostile
  to a compiler — and a size cap keeps embedding to proof-of-concept levels, where a level
  change being a server recompile is acceptable.
- **The Physics Baker window.** Level & Bake, Artifacts and Diagnostics, built on the same
  commands a script calls rather than on a second copy of the pipeline. Validation issues
  select the object that caused them; artifacts can be inspected, verified, exported as exact
  bytes or as a generated provider, and deleted one explicitly named artifact at a time.
  Diagnostics answers, without starting a match, the three questions that otherwise get
  answered by one: does this project bake the same bytes twice, does every artifact decode,
  and is each of them one this build can actually run.
- **Installer with a receipt.** Installing the dormant Jitter2 snapshot or the Jitter adapter
  goes through a staging folder and records every file with its hash. That record is what
  lets an update tell "a file I wrote" from "a file somebody changed": the first is updated,
  the second stops the operation and is reported by path, and an uninstall removes only what
  the package wrote and nobody touched. An external Jitter2 is never copied, moved or edited
  — the package references it by assembly name, and a tool that replaces a consumer's physics
  engine has destroyed months of local work.
- **Server source projection.** `Contracts`, `ArtifactCodec` and the integration are copied
  into a consumer's server project with a hashed manifest, plus a `Verify` command their CI
  can run to catch "the package was updated but the server copy was not". The no-Unity
  invariant is checked per file rather than assumed, because a stray `using UnityEngine`
  would not fail here — it would fail in a server build that has no engine at all.
- **CI and a manual test plan.** A workflow runs the same four scripts a maintainer runs, plus
  Unity's EditMode and PlayMode suites; `tools/run-unity-tests.sh` runs them locally in batch
  mode. What no test can cover — dialogs, windows, installing into a project, exporting — is
  written down step by step in the development project's manual test plan.

[Unreleased]: https://github.com/denisislamov/jitter-physics-baker/compare/main...HEAD
