# Repository tooling

Scripts that run outside Unity, used by maintainers and CI:

| Script | Purpose | Added in stage |
| --- | --- | --- |
| `hash-jitter2.py` | recompute canonical Jitter2 source hash and write it to `jitter2.lock.json` | Jitter snapshot |
| `verify-jitter2-lock.py` | recompute canonical source hash and compare with `jitter2.lock.json` | Jitter snapshot |
| `test-jitter2-lock.py` | assert the hashing invariants the C# and Python implementations share | Jitter snapshot |
| `sync-jitter2` | refresh `Jitter2~/Runtime` from a pinned upstream revision | Jitter snapshot |
| `validate-package` | package layout, manifests, licenses, `.meta` and LFS checks | release |
| `test-dotnet` | run `Server~/Tests` under .NET 10 | server delivery |

The folder name ends with `~` so Unity never imports it.

## Canonical source hash

`hash-jitter2.py` and the editor's `JitterPhysicsSourceHasher` must produce identical
values, because one runs in CI and the other decides whether a project may bake. The rules
are therefore defined by this package instead of being inherited from a platform helper:

- files are selected by the `includedFiles` / `excludedFiles` globs of the lock, where
  `**/` matches zero or more directories, `**` matches anything, `*` matches anything
  except `/`, and `?` matches one such character;
- selected paths are made relative to the source root, use `/`, and are sorted ordinally;
- text files are normalized to LF before hashing, so a CRLF checkout is not a different
  revision;
- the compile profile is serialized as compact JSON with ordinal key order, identical to
  `json.dumps(profile, sort_keys=True, separators=(",", ":"))`;
- every element is length-prefixed inside the digest, so two different file sets cannot
  collide by concatenating differently.

Consumer-specific files — `.asmdef`, `.meta`, build output — are excluded on purpose: they
describe where the sources live, not what they are.

