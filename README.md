# VelNet

<img src="velnet_logo.png" width="100" alt="VelNet Logo">

A lightweight, **self-hostable** networking library for Unity — the client half of **VelNet**.
The server is [VelNetServerRust](https://github.com/velaboratory/VelNetServerRust).

VelNet carries transforms, arbitrary per-object state, RPCs, and (optionally) voice over a single
relay connection (TCP for reliable, UDP for fast). You extend a `NetworkComponent` to sync your own
state; the library handles rooms, ownership, instantiation, and late-join.

> **This repository is the package, nothing else.** It used to be embedded in a Unity test project
> (`TestVelGameServer`); that project was removed in **2.0.16** (it still lives in git history). The
> repo root is now the package itself — mirroring [velutils](https://github.com/velaboratory/velutils)
> and velshareunity — and publishes to the VEL npm registry on every version bump.

## Install

VelNet is published to the VEL npm registry (`npm.ugavel.com`). Add the scoped registry and the
dependency to your project's `Packages/manifest.json`:

```jsonc
{
  "scopedRegistries": [
    { "name": "VEL", "url": "https://npm.ugavel.com", "scopes": ["edu.uga"] }
  ],
  "dependencies": {
    "edu.uga.engr.vel.velnet": "2.0.16"
  }
}
```

(Or add it from the Git URL `https://github.com/velaboratory/VelNetUnity.git` — the package is at the
repo root.) The bundled **Samples** — Basic, Full, VelVoice, and Dissonance — are then available from
the Package Manager.

## Concepts

The server is a thin relay; almost all of the logic lives here on the client.

### `VelNetManager`
The hub. Connects to the server, joins/leaves rooms, instantiates `VelNetPlayer`s, and tracks every
`NetworkObject` in the room (plus the prefab list for instantiation and the player prefab). It raises
join / leave / new-master events and exposes the static `MessageReceived` action — a single tap point
that fires for **every** parsed server message (used by tools like the session recorder).

### `VelNetPlayer`
One is instantiated per player in the room. Exactly one is `isLocal`; the rest are remote. Only the
local player may modify the objects it **owns**. It also carries a dedicated `NetworkObject` for the
player itself (set up in the player prefab, not the manager's list) — where avatar/voice behavior goes.

### `NetworkObject` / `NetworkComponent`
A `NetworkObject` is an ownable thing identified by a session-unique id (`<creatorUserId>-<counter>`;
scene objects use `-1-<counter>` and are created identically on every client at startup). You build
behavior by adding `NetworkComponent`s and overriding their sync hooks:
- **`IPackState` (`PackState`/`UnpackState`)** — a *snapshot*, sent on instantiate / late-join /
  ownership transfer (and used by save-scene + recordings).
- **`SendBytes` / `ReceiveBytes`** — fire-and-forget messages during a session (the receiver only
  applies them from the object's **owner**).
- **RPCs (`SendRPC`)** and **`SyncState`** (on-change streamed state) build on top.
`SyncTransform` is the canonical example — it keeps an object's position/rotation in sync.

### Ownership
Any local player can send a take-ownership message for a network id and assumes ownership immediately;
the message is ordered by the server, so simultaneous claims resolve last-writer-wins. Only the owner
should mutate an object.

### Joining (late-join)
When a client joins, every existing owner re-sends `InstantiateWithState` for its objects and the
master re-sends scene-object state/deletes — so a joiner reconstructs the current scene, then receives
deltas live. (VelNet never echoes a client's own messages back to it.)

### Message groups
Clients can scope traffic to named groups — handy for proximity audio (only stream voice to peers
close enough to hear you).

## Documentation

Full guides — quick-start, VelVoice, offline mode, and self-hosting the server — live in
[`Documentation~/`](Documentation~/) (a MkDocs site published to Cloudflare Pages by `docs-cf.yml`).

## Developing & publishing

The repo root is the package, so the workflow matches velutils/velshareunity:
1. Edit `Runtime/` / `Editor/`, test in a consuming project (e.g. via a `file:` reference).
2. Bump `version` in [`package.json`](package.json) and update [`CHANGELOG.md`](CHANGELOG.md).
3. Push to `main` → **`.github/workflows/npm-publish.yml`** runs `npm publish` to `npm.ugavel.com`
   (auth via the org `VERDACCIO_TOKEN` secret). npm refuses to republish an existing version, so this
   is effectively "publish on bump."

See **[CLAUDE.md](CLAUDE.md)** for the architecture in depth and the networking foot-guns (RPC
method-index vs. recordings, `IPackState` snapshot/append rules, owner-gated `SendBytes`).

## License

See [LICENSE](LICENSE).
