# Repository tooling

Scripts that run outside Unity, used by maintainers and CI:

| Script | Purpose | Added in stage |
| --- | --- | --- |
| `hash-jitter2.py` | recompute canonical Jitter2 source hash and write it to `jitter2.lock.json` | Jitter snapshot |
| `verify-jitter2-lock.py` | recompute canonical source hash and compare with `jitter2.lock.json` | Jitter snapshot |
| `sync-jitter2` | refresh `Jitter2~/Runtime` from a pinned upstream revision | Jitter snapshot |
| `validate-package` | package layout, manifests, licenses, `.meta` and LFS checks | release |
| `test-dotnet` | run `Server~/Tests` under .NET 10 | server delivery |

The folder name ends with `~` so Unity never imports it.
