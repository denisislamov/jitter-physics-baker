# Jitter2 snapshot patch set

This folder is the dormant Jitter2 snapshot used by fallback installation and .NET self-tests.

Current status: snapshot not synced yet.

## Sync policy

1. Source of truth is the EFT Jitter2 folder.
2. Use `tools~/sync-jitter2` (to be added) or equivalent scripted copy.
3. Regenerate `jitter2.lock.json` with `tools~/hash-jitter2.py`.
4. Update this file with patch notes and upstream commit.

