# VelNet (Unity client) — agent guide

VelNet is a self-hostable Unity networking library. This repo is the **package only** — the repo root
IS the package (`package.json` at root, namespace `VelNet`, asmdef `Runtime/VelNet.asmdef`). The
server is a separate repo, [VelNetServerRust](https://github.com/velaboratory/VelNetServerRust); it is
a thin relay — almost all logic is here on the client.

History note: until **2.0.16** the package was embedded in a Unity test project (`TestVelGameServer/Packages/VelNetUnity`).
That project was removed (it remains in git history) and the package promoted to the repo root, matching
velutils / velshareunity. Don't re-introduce an embedded Unity project; test against a *consuming* project.

## Layout
- `Runtime/` — the library. `VelNetManager.cs`, `VelNetPlayer.cs`, `NetworkObject.cs`,
  `NetworkComponent.cs`, `Util/` (e.g. `NetworkReader`/`NetworkWriter`), `VelVoice/`, `Plugins/`.
  `WebSocket/` is a vendored copy of NativeWebSocket (since 2.0.18) for the TCP-failed fallback; keep its names,
  GUIDs and `VelNetWebSocket*` JS entry points distinct from the real package so both can be installed.
- `Editor/` — editor-only tooling.
- `Samples~/` — Basic / Full / VelVoice / Dissonance example scenes (`~` ⇒ Unity won't import them).
- `Documentation~/` — MkDocs site (published to Cloudflare Pages by `.github/workflows/docs-cf.yml`).
- `.github/workflows/npm-publish.yml` — publishes to `npm.ugavel.com` on push to `main`.

## Architecture & message flow
- **`VelNetManager`** owns the socket(s) and the room. It parses every server message and, before
  applying it, invokes the **static `Action<Message> MessageReceived`** — a single tap used by the
  session recorder. Reliable traffic goes over TCP, fast traffic (pose, voice) over UDP.
- **`VelNetPlayer`** — one per player; exactly one `isLocal`. `HandleMessage` dispatches incoming
  `ObjectSync` / RPC / instantiate / ownership messages to the right `NetworkObject`.
- **`NetworkObject`** — ownable, id = `<creatorUserId>-<counter>` (scene objects: `-1-<counter>`, made
  identically on all clients at startup). Holds an ordered list of `syncedComponents`.
- **`NetworkComponent`** — extend this to sync your state. Sync surfaces:
  - `IPackState.PackState/UnpackState` — **snapshot** (instantiate / late-join / ownership; also
    save-scene + recordings).
  - `SendBytes(byte[])` / `override ReceiveBytes(NetworkReader)` — per-session messages.
  - `SendRPC(name, ...)` — remote method calls.
  - subclass **`SyncState`** for on-change streamed state (see velshareunity / coannotate's WebBrowser).
- **`SendToRoom` never echoes to the sender** — a client never receives its own messages. (This is why
  the recorder must run as a dedicated extra observer client to capture everyone.)

## ⚠️ Threads & quitting (IL2CPP / Android)
`VelNetManager` runs up to four threads, named `VelNet TCP rx/tx` and `VelNet UDP rx/tx` in native stacks.
**Every socket one of them can block on must be shut down and closed on quit, disconnect and re-login**
(`CloseUdpSocket`; closing the `TcpClient` does the same for TCP). IL2CPP's shutdown
(`Runtime::Shutdown` → `Thread::AbortAllThreads`) joins every managed thread, background or not, with no
timeout. Under IL2CPP, `Thread.Abort` is only a request that runs at a managed wait (Sleep, WaitOne,
Monitor), never inside a blocking `Socket.Receive`, and `IsBackground` changes nothing. Closing the socket
wakes the blocked `recvfrom`. Before 2.0.19 nothing closed the UDP socket, so every `Application.Quit` on
Quest hung until Android's ANR kill (~10 s). Each UDP thread owns its socket (the `udpSocket` field only
publishes it), so a reconnect can't orphan the previous thread in `Receive`.

## ⚠️ Foot-guns — read before editing a `NetworkComponent`

These bite precisely when "just adding a feature," and several **silently corrupt old recordings**
(recordings are raw VelNet messages replayed back through these same code paths).

1. **RPCs are dispatched by METHOD INDEX, and recordings store the index, not the name.** `SendRPC`
   resolves the method's position in `GetType().GetMethods()` (≈ declaration order, *including*
   compiler-generated methods) and sends/records that byte; receive does `methodInfos[index].Invoke()`.
   So adding / removing / **reordering** methods — or adding a **lambda / local function / auto-property**
   the compiler emits ahead of the `Rpc*` methods — shifts every later RPC's index and breaks playback
   of existing recordings. **Add new RPCs at the END, never reorder/remove, use named methods.**
2. **`IPackState` evolves safely only by APPENDING.** `NetworkObject.PackState` length-prefixes each
   component and `UnpackState` hands each one a **bounded sub-reader** (`new NetworkReader(reader.ReadBytes(len))`).
   So you may append fields and read them guarded by **`reader.Remaining > 0`**: old recordings / saved
   scenes (shorter payload) skip them with no over-read, and old builds ignore trailing bytes. **Never
   reorder or insert** existing PackState fields.
3. **`SendBytes`/`ObjectSync` is applied only from the OWNER.** Receivers gate on
   `if (isRpc || no.owner == this)`. So before broadcasting runtime state, call
   `networkObject.TakeOwnership()` — otherwise non-owners drop your message. (TCP ordering means the
   ownership change lands before the bytes.) `ReceiveBytes` dispatch is wrapped in try/catch, so a
   throwing/unimplemented handler in an old build is logged, not fatal.
4. **`PackState` is also used by save-scene and recordings** (`PersistenceManager`, `ReplayManager` in
   consuming apps) — so a format change there has the same blast radius as a recording change.

## Recording / replay
A consuming app can record at the network layer by subscribing to `VelNetManager.MessageReceived` and
storing raw `Data` payloads with timestamps; replay re-delivers them to the same handlers
(`UnpackState` / `ReceiveBytes` / instantiate / ownership). The manager also supports **offline mode**
(`offlineMode` + a `FakeServer`) for single-machine replay. Implication: keep the serialization formats
above append-only and index-stable, or you break already-captured sessions.

## Server
Prod relay is **VelNetServerRust** on the `asyncversion` branch (a breaking protocol change informally
called "v2"/Rust2 — not a separate repo), reachable at `vn.ugavel.com:5002`. The server only relays +
orders messages and manages rooms/groups; it does not understand component payloads.

## Develop / test / publish
- **No embedded Unity project here.** Test by referencing this package from a consuming project — e.g.
  a `file:` ref in that project's `Packages/manifest.json` (`"edu.uga.engr.vel.velnet": "file:../VelNetUnity"`),
  or point it at a local clone.
- **Publish = bump + push.** Bump `version` in `package.json`, add a `CHANGELOG.md` entry, push to
  `main`; `npm-publish.yml` runs `npm publish` to `npm.ugavel.com` (org secret `VERDACCIO_TOKEN`). npm
  won't overwrite an existing version, so the bump is what triggers a real publish. Consumers pin a
  version, so a publish never disturbs them until they bump.
- Editing this as a VEL package from inside a consuming app: clone locally, `file:`-ref it, work, then
  bump + push to publish, then re-pin the consumer to the new npm version. **Never commit a consumer on
  a `file:` ref.**
