# DataSakura Jitter Physics Baker

Deterministic, editor-time baking of a level's **static** collision geometry into a
versioned, content-addressed binary artifact, plus one shared loader that rebuilds the
exact same static topology in a [Jitter2](https://github.com/notgiven688/jitterphysics2)
`World` on the Unity client and on a .NET dedicated server.

The package does **not** own the simulation. `World.Step` stays with the consumer: the
server keeps stepping its authoritative world and the client keeps predicting, exactly as
before. What the package removes is hand-written static geometry that has to be kept
identical in two code bases by hand.

## Getting started

**[Documentation~/getting-started.md](Documentation~/getting-started.md)** walks the whole
path: adding the package, providing Jitter2, marking up a level, baking it, loading it in
Unity, and running the same bytes on a dedicated server.

Three runnable samples are installed from the detailed installation view opened from the
**Setup** tab; see `Samples~/README.md`.

## Status

Early development (`0.0.1`). The assembly graph, the artifact contracts and the editor
bootstrap are being built stage by stage; see `CHANGELOG.md` for what already exists.

## Requirements

- Unity 6000.3 or newer.
- A `Jitter2.Core` assembly in the project, or the fallback copy this package installs on
  request. Baking and world building require Jitter2; **importing the package does not.**

## Design in one screen

- **Bake produces descriptors, not Jitter state.** The artifact stores world settings and
  ordered body/shape records. The loader rebuilds the world through public Jitter API.
  Nothing from Jitter's internals (handles, trees, contacts, islands) is ever serialized.
- **The package core never references Jitter.** `Contracts`, `ArtifactCodec`,
  `UnityArtifact`, `Authoring` and `Editor` compile in a project that has no Jitter2 at
  all, which is what makes a clean import possible. Jitter-dependent code lives in
  `JitterIntegration~/` and is installed by an explicit command.
- **External Jitter wins.** When the project already has a compatible `Jitter2.Core`, the
  package references it by assembly name and never copies, moves or edits it. The dormant
  snapshot in `Jitter2~/` is only for projects that have none, and for CI.
- **Fail fast.** A missing, corrupt or incompatible artifact stops the client before it
  connects and stops the server before it accepts players. There is no silent fallback to
  legacy geometry and no hot reload of a running match world.
- **Determinism claim, stated precisely.** The artifact is byte-exact and the static
  topology is identical on both sides. A bit-exact `World.Step` between Unity and .NET is
  *not* claimed: the server is authoritative and reconciliation absorbs the drift.

## Layout

```text
Runtime/Contracts       artifact DTO and identity rules   (no UnityEngine)
Runtime/ArtifactCodec   binary codec, limits, SHA-256     (no UnityEngine)
Runtime/UnityArtifact   artifact asset and project paths
Authoring/              level, sources and world profile
Editor/                 bootstrap, baking, inspectors, export
Tests/                  EditMode and PlayMode tests
Jitter2~/               dormant Jitter2 reference snapshot (not compiled by Unity)
JitterIntegration~/     Jitter-dependent adapter, installed on request
Server~/                server source projection and .NET tests
Samples~/  Documentation~/  tools~/
```

## Loading on a server

`JitterPhysicsServerStartup.Start(world, provider, options)` is the whole bring-up: it
resolves one `IPhysicsArtifactProvider`, checks the artifact against what the build claims
to be — runtime semantics id, the level it was launched to host, the rate it steps at —
builds the static world and only then reports `IsReady`. Connection approval is gated on
that flag, and there is no partially ready state to gate on by mistake. `SelfCheck` is the
one line a deployment smoke test looks for.

`FilePhysicsArtifactProvider` covers artifacts delivered as content: it is given a manifest
path (typically `--physics-manifest <path>`) and reads the payload named by that manifest
from the same folder. Delivering those two files — published content, a mounted volume, an
artifact registry — is the consumer's decision and the package makes no assumption about
it. See `Server~/README.md`.

## Editor entry points

- `Tools > DataSakura > Jitter Physics > Physics Baker` — the single authoring surface. Its
  workflow matches the other DataSakura authoring packages: **Overview** explains the level
  and shows the cached readiness result, **Sources** owns explicit static-body markup,
  **Bake** owns the shared world profile and deterministic build, **Tools** contains manual
  diagnostics, **Setup** explains Jitter2 compatibility and opens explicit installation
  actions, and **Artifacts** verifies or exports the exact bytes. Opening or repainting any
  tab performs no project mutation.
- `Tools > DataSakura > Jitter Physics > Setup` — opens the **Setup** tab of the main window.
  The compatibility summary shows which `Jitter2.Core` this project uses, whether its
  canonical source hash matches `jitter2.lock.json`, the resulting `runtimeCompatibilityId`,
  and why baking is blocked. The detailed view can copy or export the report as JSON for CI
  and contains the explicit installation actions.
- `Tools > DataSakura > Jitter Physics > About` — package, schema and assembly state,
  including whether a `Jitter2.Core` is present and whether it is duplicated.
- `Tools > DataSakura > Jitter Physics > Validate Selected Level` — runs the whole build
  without writing anything, and logs every issue against the object that caused it. Safe to
  run while the setup is still red: the authoring problems are worth seeing first.
- `Tools > DataSakura > Jitter Physics > Bake Selected Level` — validates, builds and writes
  the artifact for the selected `JitterPhysicsLevel`. The `runtimeCompatibilityId` is taken
  from the compatibility report and cannot be supplied by a caller, so a red Setup window
  blocks baking rather than being worked around. The write is staged and re-hashed from
  disk before it replaces the previous artifact, a failed bake leaves that artifact intact,
  and baking in Play Mode is refused.
- `Tools > DataSakura > Jitter Physics > Show Baked Geometry Overlay` — toggles a read-only
  Scene View comparison. Green is the exact last baked snapshot; red is current geometry
  that is new or changed. Removed geometry remains as a green ghost until the next bake, and
  unmarked colliders under the geometry root are red because they cannot enter the artifact.
- `Tools > DataSakura > Jitter Physics > Install > ...` — install the fallback Jitter2 copy
  or the Jitter adapter, install and verify the server runtime sources, validate the
  installation, and remove what the package owns. Every action is explicit, an external
  Jitter2 is never touched, and a file modified after installation stops an update instead of
  being overwritten.

## License

MIT, see `LICENSE.md`. Third-party components are listed in `Third Party Notices.md`.
