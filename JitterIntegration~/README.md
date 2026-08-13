# Jitter integration

The Jitter-dependent half of the package: the code that turns artifact records into a
Jitter `World`.

It lives in a folder ending with `~` so that Unity never compiles it. That is not a
detail — it is what allows the package to import cleanly into a project that has no
`Jitter2.Core` at all. An always-compiled assembly referencing a missing one would fail
the import before the installer could run.

## Contents

| Path | Purpose |
| --- | --- |
| `Runtime/` | `JitterPhysicsWorldBuilder`: the shared loader for client and server, and `JitterPhysicsServerStartup`: the server's one-call bring-up |
| `UnityAssemblyTemplate/` | the assembly definition the installer writes into the project |

## Server startup

`JitterPhysicsServerStartup.Start(world, provider, options)` owns the *order* a dedicated
server has to follow: obtain the artifact from a provider, check it against what this build
claims to be — runtime semantics id, the level it was launched to host, the rate it steps
at — build the static world, and only then report readiness. It is a startup step inside
the consumer's match server, not a service: the package still never calls `World.Step`.

The returned `JitterPhysicsServerState` has no partially ready form. Connection approval is
gated on `IsReady`, and a caller that ignores it gets a `null` artifact and a world without
geometry rather than a match that starts without walls. `SelfCheck` is the line a
deployment smoke test greps for: level, short artifact hash, short topology fingerprint,
counts, tick rate and elapsed time.

## Installation

`Tools > DataSakura > Jitter Physics > Setup` copies `Runtime/` into
`Assets/DataSakura/JitterPhysics/Integration/` together with the assembly definition from
the template, and records what it wrote in the installation receipt.

The assembly is consumer-owned once installed: the installer only ever updates files that
still match the hashes in the receipt, so a local modification is reported rather than
silently overwritten.

## Assembly cycles

The generated assembly references `Jitter2.Core` **by name**, so it resolves against the
copy the consumer already has, wherever that is.

A consumer whose `Jitter2.Core` itself references game assemblies must therefore not make
those assemblies reference the integration assembly back. Call the world builder from a
layer above both instead; the package deliberately contains no networking types, so
nothing forces the dependency in the other direction.

## Verification

The sources are compiled and tested by `Server~/Tests` under .NET, against the dormant
snapshot in `Jitter2~/`. Unity compiles the same files after installation, so the client
and the server share one implementation rather than two that agree today.


