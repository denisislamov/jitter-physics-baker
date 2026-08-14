# Getting started

This is the whole path, end to end: install the package, mark up a level, bake it, load it in
Unity, and load the same bytes on a dedicated server.

It assumes nothing beyond a Unity 6000.3 project. Where a step has a rule that will bite you
later if you ignore it, the rule is stated where the step is, not in a footnote.

---

## 1. Add the package

`Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.datasakura.jitter-physics-baker": "https://github.com/denisislamov/jitter-physics-baker.git#0.0.1"
  }
}
```

Or add it as a local path while developing:

```json
"com.datasakura.jitter-physics-baker": "file:../jitter-physics-baker"
```

**The import compiles in a project that has no Jitter2.** That is a design constraint, not a
coincidence: `Contracts`, `ArtifactCodec`, `UnityArtifact`, `Authoring` and `Editor` do not
reference Jitter2 at all. You can add the package, bake levels and inspect artifacts before
deciding which Jitter2 the project will use.

## 2. Provide Jitter2

Open **Tools > DataSakura > Jitter Physics > Setup**. The report at the top tells you what
the project currently has.

| Status | What it means | What to do |
| --- | --- | --- |
| `Missing` | No `Jitter2.Core` in the project | Press **Install Jitter2** |
| `Compatible` | Your own copy, matching the lock | Nothing; the package will use it |
| `Incompatible` | Your own copy, different sources | Re-bake after deciding which one is right |
| `Duplicate` | Two copies | Remove one; Unity cannot resolve the name |

**An external Jitter2 always wins.** If the project already has one, the package references it
by assembly name and never copies, moves or edits it. A tool that "helpfully" replaces a
consumer's physics engine destroys months of local changes, so this one refuses instead.

### What `Install Jitter2` actually installs

A compiled `netstandard2.1` assembly, not sources. Unity fixes game assemblies at C# 9 and the
Jitter2 snapshot is written in a later language, so handing Unity the sources produces several
hundred parse errors. That limit applies to sources Unity compiles, not to an assembly it
loads, so the package compiles the snapshot itself and ships the result as a managed plugin.

Two files are written:

- `Jitter2.Core.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`, unless the project already has one

The second is not optional. It is absent from .NET Standard 2.1 and Unity does not deliver it
to players; without it the editor still runs, because it resolves the assembly from its own
toolchain, and the **player build** fails to load Jitter2. The installer says so if it ends up
missing.

See `Jitter2~/PATCHES.md` for the shims and the seventeen source patches that make the
snapshot compile, and why they cannot change simulation behaviour.

## 3. Install the integration adapter

Press **Install/update integration**. This is the Jitter-dependent half: the code that turns
artifact records into Jitter2 shapes and bodies. It is separate from the package core so that
step 1 can work without Jitter2.

Install it after Jitter2, not before. It references `Jitter2.Core` by name, and installing it
first turns a clean import into a wall of `CS0246`.

## 4. Mark up a level

Three components, and nothing is baked that you did not mark.

### `JitterPhysicsLevel`

One per scene, on any object. It defines what a bake produces.

| Field | Meaning |
| --- | --- |
| Level Id | Identity of the level, in the artifact and in the handshake |
| Geometry Root | Subtree the sources are collected from |
| World Profile | Gravity, tick rate, solver settings |
| Generated Folder | Where the artifact asset is written |

The level id is what the client sends and the server compares. Change it and every baked
artifact for that level becomes a different level.

### `JitterStaticBodySource`

Put one on every object that should become a static body. Its colliders become that body's
shapes.

| Field | Meaning |
| --- | --- |
| Source Id | Stable identity of this body inside the artifact |
| Include Children | Also collect colliders from child objects |
| Friction / Restitution | Surface material for every shape on this body |

Source ids are generated once and then kept. Renaming the GameObject does not change the
artifact, because the id is the identity and the name is only a label. That is what makes a
re-bake after a rename produce identical bytes.

Supported colliders: `BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider`.

A `SphereCollider` under non-uniform scale is approximated by its largest axis, and the bake
warns. A player brushing against slightly larger geometry is cheaper than a player walking
through a wall.

### `JitterPhysicsWorldProfile`

A `ScriptableObject`, shared between levels. **Create > DataSakura > Jitter Physics > World
Profile**.

| Field | Note |
| --- | --- |
| Gravity | Used by the client and the server; do not read Unity's `Physics.gravity` instead |
| Tick Rate | The step both sides advance by |
| Substep Count | Solver substeps per step |
| Solver / Relaxation Iterations | Solver quality |
| Allow Deactivation | Let resting bodies sleep |

These travel inside the artifact. That is deliberate: a server that inherited a different tick
rate from its own configuration would diverge from client prediction by construction.

## 5. Bake

**Tools > DataSakura > Jitter Physics > Baker**, tab **Level & Bake**.

- **Validate** reports problems without writing anything. Every issue can select the object
  that caused it.
- **Validate + Bake** writes the artifact.

A bake either completes or changes nothing. A partially converted level is never written,
because missing geometry shows up as a hole in a wall at runtime rather than as a message at
bake time. The previous artifact survives a failed bake.

Three files are produced:

| File | Purpose |
| --- | --- |
| `<level>.<hash>.jphys.bytes` | The artifact |
| `<level>.<hash>.manifest.json` | Counts, hashes, tick rate |
| `<level>.<hash>.asset` | The Unity asset your scenes reference |

The hash is in the file name because artifacts are content-addressed: two different bakes can
sit side by side without one silently shadowing the other.

### Determinism

Baking the same scene twice must produce the same bytes. The samples expose this as a menu
entry (**Samples > Verify determinism**), and it is worth running after changing the authoring
setup.

This is the property the whole format exists for. A baker that emitted a hash table in
enumeration order, or wrote a timestamp, would still produce a level that loads and plays -
and a client and a server built minutes apart would quietly disagree about it.

### See what changed since the bake

Enable **Tools > DataSakura > Jitter Physics > Show Baked Geometry Overlay** while looking at
the level in Scene View.

- Green wire geometry is the exact last baked snapshot.
- Red wire geometry is current geometry that is new or differs from that snapshot.
- A moved collider shows its old baked pose in green and its current pose in red.
- A collider deleted after the bake leaves a green ghost until the next successful bake.
- An enabled collider under the geometry root but outside every `JitterStaticBodySource` is
  red, because it cannot enter the artifact.

The legend at the bottom of Scene View reports baked, matching and red shape counts for each
loaded level. The overlay is read-only: enabling it never validates, repairs ids or writes an
artifact. Toggle the same menu item off when the comparison is no longer needed.

## 6. Load it in Unity

The package does not own the tick loop, so this is code you write. The shape of it:

```csharp
PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(artifactAsset);
if (!loaded.Succeeded)
{
    // Typed error: Code and Message. Refuse to start; do not fall back to legacy geometry.
    return;
}

var world = new World();
PhysicsWorldBuildResult built = JitterPhysicsWorldBuilder.Apply(world, loaded.Artifact);
if (!built.Succeeded)
{
    world.Dispose();
    return;
}

// built.TopologyFingerprint, built.BodyCount, built.ShapeCount
```

Then step it yourself, on the artifact's tick rate:

```csharp
float timestep = 1f / loaded.Artifact.WorldSettings.TickRate;
world.Step(timestep, multiThread: false);
```

Rules that are not style preferences:

- **Build the static world before creating any dynamic bodies**, and before the first step.
  Order matters to the broadphase, and a body created into a half-built world will not be
  where you think it is.
- **`Apply` is once per world.** A second call is refused rather than merged; merging would
  silently double every wall in the level.
- **A failed `Apply` rolls back.** The world is left empty, never partially built, because a
  partial level looks like it is working.
- **Take the timestep from the artifact**, not from Unity's fixed timestep. The server does
  not know your project settings.

`Samples~/Runtime/JitterPhysicsSampleWorld.cs` is a complete working version of the above.

## 7. Install the samples

**Setup > Install/update samples**, then **Tools > DataSakura > Jitter Physics > Samples >
Build and bake: Bouncing Ball**.

| Sample | Shows |
| --- | --- |
| Bouncing Ball | Bodies land on the baked surfaces and go to sleep |
| FPS Shooter | Walking on, being stopped by, and shooting at baked geometry |
| Artifact Verification | What a server checks before accepting players |

They install through the package rather than the Package Manager's **Import** button, because
the sample assembly references the adapter by name; importing it into a project without the
adapter yields a missing-assembly error that names nothing useful.

## 8. Run it on a dedicated server

The server never opens Unity, so it needs the sources and the bytes.

**Sources.** Setup > **Install server runtime sources...** projects `Contracts`,
`ArtifactCodec` and the adapter into a folder inside your server project. They compile with a
plain .NET SDK against your Jitter2 assembly. No `PackageCache` paths, no build-file edits:
the projection is ordinary source files in a folder an SDK-style project already globs.

**Bytes.** Two ways to deliver them:

- a file, next to the server, loaded by `FilePhysicsArtifactProvider`;
- embedded into the binary, exported from the **Artifacts** tab.

**Startup order.** Load the artifact, verify it, build the static world, and only then accept
connections:

```csharp
var provider = new FilePhysicsArtifactProvider(manifestPath);
JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(world, provider, options);
if (!state.IsReady)
{
    // Exit. A server that accepts players into an empty world is worse than one that
    // does not start.
    return 1;
}
```

The self-check line printed on success is meant for container logs and carries short hashes
only.

`Server/JitterPhysicsWebViewer` in this repository is a complete working server: it loads an
artifact, builds the world, steps it and renders it in a browser.

## 9. Make client and server agree

Two values decide compatibility, and both matter.

| Value | Answers |
| --- | --- |
| `artifactHash` | "Are we running the same level file?" |
| `runtimeCompatibilityId` | "Would we simulate it the same way?" |

The second is derived, never written by hand, from the schema version, the Jitter2 source
hash, the precision and compile profile, and the collider, shape and world-builder semantic
versions. Two builds can hold byte-identical artifacts and still simulate differently; the id
is what catches that.

Send both in your handshake, compare both, and refuse the connection before the player spawns.
`PhysicsCompatibilityToken` encodes them without depending on your transport.

**When the id changes, re-bake.** Upgrading the package, syncing a new Jitter2 snapshot or
changing the compile profile all change it, and that is the intended behaviour: a client and a
server built against different Jitter sources must not be able to claim compatibility.

## 10. Diagnose problems

Baker window, **Diagnostics** tab:

| Check | Catches |
| --- | --- |
| Codec roundtrip | The artifact is no longer canonical |
| Repeat determinism | The bake is not reproducible |
| Runtime compatibility | The artifact is stale for this build - re-bake it |

`Validate installation` in Setup compares the receipt with what is on disk, which is what a
consumer's CI runs to catch "the package was updated but the installed copy was not".

Locally modified files are reported, never overwritten. A local change that an update silently
reverts works right up until it does not.

## What this package does not claim

The static topology is identical on both sides and the artifact is byte-exact. A bit-exact
`World.Step` between Unity's runtime and .NET is **not** claimed: that depends on the runtime,
the JIT and the floating-point environment, none of which this package controls.

What it guarantees is that both sides start from the same geometry and know, before the match
begins, whether they agree.
