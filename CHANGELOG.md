# Changelog

All notable changes to VelNet (Unity) are documented here.

## [2.0.17] - 2026-06-21
### Fixed
- Add the missing `.meta` for `velnet_logo.png`. Unity can't generate a meta inside an immutable
  (npm-installed) package, so 2.0.16 logged "Asset ... velnet_logo.png has no meta file, but it's in
  an immutable folder. The asset will be ignored." in consumers.

## [2.0.16] - 2026-06-21
### Changed
- **Repository restructured to a standalone package.** The package now lives at the repo root
  (mirroring velutils / velshareunity) instead of being embedded in the `TestVelGameServer` Unity test
  project, which was removed (it remains in git history).
- **Publishing** is now an ordinary `npm publish` to `npm.ugavel.com` on push to `main`
  (`.github/workflows/npm-publish.yml`), replacing the `git subtree` → `upm`-branch split.
- Docs site moved to `Documentation~/` (Unity-ignored); `docs-cf.yml` repointed.

### Added
- `README.md` (install + concepts) and `CLAUDE.md` (architecture + networking foot-guns).

No runtime/API changes — `Runtime/` and `Editor/` are unchanged from 2.0.15.
