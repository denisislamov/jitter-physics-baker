# Server delivery

This folder is the server-side half of the package. It does **not** contain a physics
server: physics is a stateful part of the match simulation, so the package ships a
source-compatible runtime that is compiled *inside* the consumer's match server, against
the consumer's own Jitter2 copy.

Planned contents:

- `RuntimeSources/` — the projection recipe exported into a consumer server project by
  `Tools > DataSakura > Jitter Physics > Setup > Install Server Runtime Sources...`.
- `Tests/` — a .NET 10 test project that compiles the portable package sources **and**
  `Jitter2~/Runtime` directly, so the dormant snapshot is verified by CI even though Unity
  never builds it.

Two rules apply to everything here:

1. Sources are included by reference. There are no hand-maintained copies of package code
   in this folder, because a copy is a fork that nobody notices.
2. A server build never reads sources from `Library/PackageCache`. That folder is a cache,
   it is not reproducible, and it disappears.
