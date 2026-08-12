# Repository tooling

Scripts that run outside Unity, used by maintainers and CI:

| Script | Purpose | Added in stage |
| --- | --- | --- |
| `sync-jitter2` | refresh `Jitter2~/Runtime` from a pinned upstream revision | Jitter snapshot |
| `verify-jitter2-lock` | recompute the canonical source hash and compare it with `jitter2.lock.json` | Jitter snapshot |
| `validate-package` | package layout, manifests, licenses, `.meta` and LFS checks | release |
| `test-dotnet` | run `Server~/Tests` under .NET 10 | server delivery |

The folder name ends with `~` so Unity never imports it.
