# Server delivery

This folder is the server-side half of the package. It does **not** contain a physics
server: physics is a stateful part of the match simulation, so the package ships a
source-compatible runtime that is compiled *inside* the consumer's match server, against
the consumer's own Jitter2 copy.

Planned contents:

- `RuntimeSources/` — the projection recipe exported into a consumer server project by
  `Tools > DataSakura > Jitter Physics > Setup > Install Server Runtime Sources...`.

Current contents:

- `Tests/` — a .NET 10 test project that compiles the portable package sources by
  reference and runs the shared test files. It is the evidence that `Contracts` and
  `ArtifactCodec` really are engine-independent: the same tests that run in Unity are
  executed here by a plain .NET SDK, with a different compiler and runtime.

Run it with `dotnet test` from `Server~/Tests`, or through `tools~/test-dotnet.sh`.

Once `Jitter2~/Runtime` is synced, this project also compiles the dormant snapshot
directly and gains the world-builder tests, so the snapshot is verified by CI even though
Unity never builds it.

Two rules apply to everything here:

1. Sources are included by reference. There are no hand-maintained copies of package code
   in this folder, because a copy is a fork that nobody notices.
2. A server build never reads sources from `Library/PackageCache`. That folder is a cache,
   it is not reproducible, and it disappears.
