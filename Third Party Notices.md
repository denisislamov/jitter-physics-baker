# Third Party Notices

This package redistributes the following third-party components.

## Jitter2

- Files: `Jitter2~/Runtime/**` (dormant reference snapshot; Unity never compiles
  folders whose name ends with `~`) and the same sources compiled directly by
  `Server~/Tests`
- Project: https://github.com/notgiven688/jitterphysics2
- Copyright (c) Thorben Linneweber and contributors
- License: MIT (full text in `Jitter2~/LICENSE.md`)

The snapshot exists only so that a project without Jitter2 can install a working
copy explicitly (`Tools > DataSakura > Jitter Physics > Setup`) and so that CI can
compile the exact sources the package was validated against. When a consumer
already has a compatible `Jitter2.Core` assembly, the package references it by
assembly name and never copies or modifies it.

Local modifications relative to upstream are listed in `Jitter2~/PATCHES.md`, and
the exact source set that the package supports is pinned by `jitter2.lock.json`.
