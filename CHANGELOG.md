# Changelog

All notable changes to VelNet (Unity) are documented here.

## [2.0.19] - 2026-11-06
### Fixed
- **Application.Quit no longer hangs on Android/IL2CPP (ANR, then killed ~10 s later).** The UDP receive
  thread sat in a blocking `Socket.Receive` that nothing ever closed: `OnApplicationQuit` closed only TCP,
  `DisconnectFromServer` just dropped the UDP socket reference, and `Thread.Abort` can't interrupt a
  blocking syscall. IL2CPP's shutdown joins every managed thread with no timeout, so quit waited forever.
  Quit, disconnect and re-login now shut down and close the UDP socket; each UDP thread owns its socket,
  so a reconnect can't orphan the old thread. VelNet threads are now named and background.

## [2.0.18] - 2026-11-04
### Changed
- **No more NativeWebSocket dependency.** The WebSocket fallback (used when the TCP connection fails) now
  uses a copy of NativeWebSocket built into VelNet (`Runtime/WebSocket/`, namespace `VelNet.WebSockets`,
  internal types, JS entry points `VelNetWebSocket*`). Installing VelNet alone now compiles; before, a project
  also had to add `com.endel.nativewebsocket` from git. Projects that install NativeWebSocket themselves
  keep working: the two copies don't share names, GUIDs or JS symbols. See `THIRD-PARTY-NOTICES.md`.

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
