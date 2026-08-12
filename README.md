# DataSakura Jitter Physics Baker

Deterministic, editor-time baking of a level's **static** collision geometry into a
versioned, content-addressed binary artifact, plus one shared loader that rebuilds the
exact same static topology in a [Jitter2](https://github.com/notgiven688/jitterphysics2)
`World` on the Unity client and on a .NET dedicated server.

The package does **not** own the simulation. `World.Step` stays with the consumer: the
server keeps stepping its authoritative world and the client keeps predicting, exactly as
before. What the package removes is hand-written static geometry that has to be kept
identical in two code bases by hand.

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

## Editor entry points

- `Tools > DataSakura > Jitter Physics > About` — package, schema and assembly state,
  including whether a `Jitter2.Core` is present and whether it is duplicated.

## License

MIT, see `LICENSE.md`. Third-party components are listed in `Third Party Notices.md`.
