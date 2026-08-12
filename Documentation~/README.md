# Documentation

The package documentation is written stage by stage, next to the feature it describes.
Planned documents, in the order they become writable:

| File | Covers | Written in stage |
| --- | --- | --- |
| `artifact-format-v1.md` | binary layout, manifest fields, identity and limits | artifact contracts |
| `installing-jitter2.md` | discovery, fallback install, receipt, uninstall | installer |
| `authoring-guide.md` | level, sources, world profile, validation rules | authoring |
| `runtime-integration.md` | loading an artifact on the client and the server | world builder |
| `server-source-integration.md` | server projection and artifact providers | server delivery |
| `upgrading-jitter2.md` | snapshot sync, lock recalculation, compatibility id bump | release |

Nothing is stubbed here on purpose: an empty document that claims to describe a format is
worse than no document, because it gets referenced and then silently rots.
