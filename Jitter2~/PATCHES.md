# Jitter2 snapshot provenance

This folder is the dormant Jitter2 reference snapshot. Unity never imports it (the folder
name ends with `~`); it is used for two things only:

1. the fallback copy installed into projects that have no Jitter2 of their own;
2. the copy `Server~/Tests` compiles directly, so that CI verifies it.

**The snapshot is never edited by hand.** It is produced by `tools~/sync-jitter2.py`, and
`jitter2.lock.json` records what it was produced from. A manual edit would make the lock
describe something that no longer exists, which is precisely the drift the lock prevents.

## Current snapshot

| Field | Value |
| --- | --- |
| Upstream | <https://github.com/notgiven688/jitterphysics2> |
| Tag | `2.8.9` |
| Commit | `c15bc6abfdda90a936975979a42f7a54a211084e` |
| Library path | `src/Jitter2` |
| Files | 96 `.cs` |
| Patch set | `upstream-2.8.9-unpatched` |

Reproduce with:

```sh
tools~/sync-jitter2.py --ref 2.8.9 --patch-set-id upstream-2.8.9-unpatched
```

## Applied patches

None. This is unmodified upstream.

## Known gap: this is not yet the consumer fork

The specification targets a consumer that vendors a *patched* Jitter2 — single precision,
`SolveMode.Deterministic`, single-threaded stepping, and a `JITTER_UNITY` define that
swaps hardware intrinsics for software polyfills so the sources build under Unity's
runtime.

Upstream 2.8.9 contains no `JITTER_UNITY` define and uses
`System.Runtime.Intrinsics.Vector128` directly. The consequences are:

- the snapshot compiles and simulates correctly under .NET (`Server~/Tests` proves it);
- installing it as a Unity fallback has **not** been validated, and is expected to need
  the polyfill patch set first;
- `compileProfile` in the lock therefore declares `"unityDefine": ""`,
  `"polyfillProfile": "none"` and `"intrinsicsProfile": "hardware"` — the truth about
  these sources, not the target state.

When the consumer fork becomes available, sync it with `--source <path>` and update the
compile profile. Both changes alter `sourceContentHash` and therefore
`runtimeCompatibilityId`, which is the intended behaviour: a client and a server built
against different Jitter sources must not be able to claim compatibility.

## Update procedure

1. Pin the revision to sync from.
2. Run `tools~/sync-jitter2.py` with `--ref` (upstream) or `--source` (a local fork).
3. Record the patch set and any deviations in this file.
4. Verify with `tools~/verify-jitter2-lock.py` and `tools~/test-dotnet.sh`.
5. Release the package and the consumer lock update as one atomic change.


