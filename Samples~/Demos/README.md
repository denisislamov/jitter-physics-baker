# Samples

Three runnable samples: two that put the baked level under load, and one that checks the
artifact the way a dedicated server would before accepting connections.

Install Jitter2 and the integration adapter first. Then select the package in
**Window > Package Manager**, open **Samples**, and import **Physics Baking Demos**. The
same files can also be installed through `Install/update samples` in
**Tools > DataSakura > Jitter Physics > Setup**; that path checks the prerequisite and
tracks the copied files in the package installation receipt.

The sample assembly references `DataSakura.JitterPhysics.JitterIntegration` by name. Unity's
standard Package Manager import cannot disable its button while that project-owned adapter
is missing, so importing out of order produces a missing-assembly error. The Setup installer
is the guarded alternative when the project has not completed integration setup yet.

The controls work with either **Input Manager (Old)** or **Input System Package** as the
project's active input backend. The samples do not add an Input System package dependency.

## Building a sample

After installing, use **Tools > DataSakura > Jitter Physics > Samples**:

| Menu entry | What it does |
| --- | --- |
| Build and bake: Bouncing Ball | Generates the scene, bakes it, wires the artifact, saves. |
| Build and bake: FPS Shooter | The same, for the shooter level. |
| Bake level in the open scene | Re-bakes after you edit the geometry. |
| Validate level in the open scene | Reports problems without writing anything. |
| Verify determinism: bake the open level twice | Bakes twice and compares the two hashes. |

The scenes are generated from code, not shipped as `.unity` files. A committed scene is a
wall of GUIDs nobody reads in review, and it drifts from the sample scripts silently.
Generating them also makes the determinism check meaningful: the same scene description must
produce the same artifact hash every time.

## 1. Bouncing Ball

Drops spheres onto a floor, a ramp, a step and a few primitives. Press **Space** for another
ball, **Backspace** to clear.

Two things are worth watching, and both are assertions about the bake rather than about
Jitter2:

- a ball must come to rest **on** a surface, not inside or through it, which is what proves
  the collider conversion preserved the authoring transform;
- a resting ball must eventually fall asleep, which is what proves the world settings in the
  artifact were applied instead of Jitter2's defaults.

## 2. FPS Shooter

A first-person player on a level with cover, columns, a ramp and a platform.

| Input | Action |
| --- | --- |
| WASD | Move |
| Mouse | Look |
| Space | Jump |
| LMB | Fire a physical projectile |
| RMB | Fire a hitscan ray |
| Esc | Release the cursor |

This is the sample that matters for a shooter, because it asks the three questions a shooter
actually asks of static geometry: can you stand on it, does it stop you, and can you hit it.
A level that merely loads can still fail all three.

The player is a kinematic capsule moved by code, not a dynamic body pushed by the solver. A
dynamic character slides down slopes, gets shoved by its own projectiles and tips over; the
usual result is a controller that fights the solver rather than using it.

The character resolves movement with rays rather than a shape cast. That is enough to show
the baked walls are solid, and it is not enough for a shipping controller - a ray will miss
corners that a capsule would hit.

## 3. Artifact Verification

Runs on both scenes and prints what a server checks at startup:

- the payload hash recomputed from the bytes in the build, compared with the metadata;
- the binary decoded and validated;
- level id, body count and tick rate cross-checked against the asset;
- the `runtimeCompatibilityId` and the topology fingerprint.

The bake already validated the artifact, which is a different question. What ships is the
file, and between baking and loading it can be replaced by a stale copy, truncated in
transfer, or paired with a manifest from another bake. None of that is visible at bake time.

The fingerprint is the field to compare across machines: a client and a server that print
different fingerprints built different geometry from the same file, and no amount of
identical artifact hashes makes that safe.

## Where things go

```
<install folder>/
  Runtime/     sample components and the tick loop
  Editor/      scene builders and the bake menu
  Scenes/      generated scenes
  Generated/   baked artifacts
```

`Generated/` is produced by baking and can be deleted; the menu recreates it.

## Owning the tick loop

`JitterPhysicsSampleWorld` steps the world in `Update` on the timestep the artifact was baked
for. The package deliberately never does this: a game steps physics from wherever its
simulation lives, which is rarely a `MonoBehaviour`, and a dedicated server does it from its
own loop. The timestep comes from the artifact rather than Unity's fixed timestep because the
server has to advance the same world by the same amount and knows nothing about the Unity
project's settings.
