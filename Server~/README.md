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

## Getting the artifact to the server

The package does not deliver the artifact — it defines how the server accepts one.
`IPhysicsArtifactProvider` (in `Contracts`) is the whole boundary: startup resolves one
provider, asks it for the artifact and either builds the world or refuses to accept
players. Whatever a provider returns has already been hashed, decoded, validated and
cross-checked against its manifest, so no caller has to know how much checking happened.

`FilePhysicsArtifactProvider` (in `ArtifactCodec`) is the delivery path for artifacts that
arrive as content:

```csharp
var provider = new FilePhysicsArtifactProvider(manifestPath);   // e.g. --physics-manifest <path>
PhysicsArtifactLoadResult load = provider.Load(expectedRuntimeCompatibilityId);
if (!load.Succeeded)
{
    // load.Error carries the code, the level id and the hash; stop before Netick approval.
    return;
}

PhysicsWorldBuildResult build = JitterPhysicsWorldBuilder.Apply(world, load.Artifact);
```

It is pointed at the **manifest**, not at the payload, because the payload alone cannot be
cross-checked: the expected hash, the counts and the tick rate all live in the manifest.
The payload is then read from the manifest's own folder under the name the manifest gives,
and a name that is not a plain file name is refused — a manifest is untrusted input, and a
server must not be talked into reading an arbitrary path. Delivery systems that rename
files in transit can pass the payload path explicitly.

How those two files reach the machine is the consumer's decision and stays outside the
package: publish them with the build, mount a volume, or pull them from an artifact
registry. The package assumes no particular game, deploy system or directory layout; it
only assumes the bytes are the exact bytes that were baked, and it verifies that itself.

Once `Jitter2~/Runtime` is synced, this project also compiles the dormant snapshot
directly and gains the world-builder tests, so the snapshot is verified by CI even though
Unity never builds it.

Two rules apply to everything here:

1. Sources are included by reference. There are no hand-maintained copies of package code
   in this folder, because a copy is a fork that nobody notices.
2. A server build never reads sources from `Library/PackageCache`. That folder is a cache,
   it is not reproducible, and it disappears.
