# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Package skeleton and the Jitter-free assembly graph.** The package now exists as a
  UPM package with `package.json`, license, third party notices, line-ending policy and
  the five assemblies described by the specification:
  `DataSakura.JitterPhysics.Contracts` and `DataSakura.JitterPhysics.ArtifactCodec`
  (both `noEngineReferences`, so the same sources compile under a plain .NET SDK for the
  dedicated server), `DataSakura.JitterPhysics.UnityArtifact`,
  `DataSakura.JitterPhysics.Authoring` and the Editor-only
  `DataSakura.JitterPhysics.Editor`. None of them reference Jitter2, which is what keeps
  a clean import free of compile errors before the installer has run.
- **EditMode/PlayMode test assemblies** and a package layout self-test, so the structural
  invariants (no Jitter references in the always-compiled assemblies, Editor assembly is
  Editor-only) are verified by CI rather than by review.
- **`tools/publish-package.sh` and `tools/verify-package-meta.py`** in the development
  project: the package is published to its standalone repository with `git subtree split`
  and is validated for truncated `.meta` files and Git LFS pointers first, both of which
  are invisible here but fatal for a consumer installing from a git URL.

[Unreleased]: https://github.com/denisislamov/jitter-physics-baker/compare/main...HEAD
