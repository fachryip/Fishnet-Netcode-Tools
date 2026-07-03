# FishNet Network Tools

> A real-time network inspector and bad-connection simulator for FishNet (Fish-Networking) in Unity. See exactly what your multiplayer game is doing, and break it on purpose to find out why.

![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
![Engine: Unity](https://img.shields.io/badge/engine-Unity-black?logo=unity)
![Status: early development](https://img.shields.io/badge/status-early%20development-orange)

> **Status: early development.** v0.1 is being built in the open. The features below describe the v0.1 target, so expect rapid change and rough edges until the first tagged release.

Editor tooling for FishNet developers. Use it while you build, watch live network state, and reproduce the latency bugs that never show up on a fast local connection.

## The problem

In multiplayer, the hard bugs are the ones you cannot see. A SyncVar that updates a frame late. An RPC (remote procedure call) that arrives out of order. An object that despawns mid-update. Interpolation that fights a position correction. These are invisible on a fast local link and only surface under real-world latency.

On FishNet today, chasing them mostly means `Debug.Log` and `NetworkManager` logging. Unity's official Multiplayer Tools and the Netcode-for-Entities profiler only support Unity's own frameworks (Netcode for GameObjects and Netcode for Entities), not FishNet. FishNet Network Tools fills that gap.

## What it does

Two halves designed to be used together.

### 1. Network Inspector

A real-time view of your live network state, from inside the Unity Editor:

- A tree of spawned NetworkObjects, with per-object ownership and observer count.
- Live SyncVar / SyncType values that update as they change.

### 2. Condition Injection

Runtime control over bad-network conditions, so you can reproduce a failure on demand:

- Latency, jitter, packet loss, and out-of-order delivery.
- Drag-to-change sliders while the game is running, so you do not have to reconfigure and replay.
- Planned: realistic presets and per-connection conditions (make one client laggy while the rest stay healthy).

FishNet already ships a basic network simulator in its `TransportManager`. Condition Injection is a visual, runtime layer over that: the value-add is the in-editor UX and tying it to the inspector, not the raw latency simulation itself.

### The point: inject, then watch it break

Use the two halves together. Inject a bad condition and watch, in the inspector, exactly what desyncs: which SyncVar diverges, which RPC reorders, where interpolation breaks. That closes the loop between "I think latency causes this bug" and "here is the value that actually went wrong."

<!-- TODO: add a short demo GIF of inject -> watch-it-desync once v0.1 runs -->

## Requirements

- Unity <!-- TODO: confirm and pin the minimum supported LTS version -->
- FishNet (Fish-Networking): verified on 4.7.2. Other 4.x versions may work; the inspector degrades gracefully if the SyncType API differs.

## Installation

> Coming with the first tagged release.

Planned, via Unity Package Manager (Git URL):

1. Open **Window > Package Manager**.
2. Click **+ > Add package from git URL...**
3. Enter the repository URL <!-- TODO: e.g. https://github.com/<user>/fishnet-network-tools.git -->

Or clone this repository into your project's `Packages/` or `Assets/` folder.

## Quick start

1. Install (see above).
2. Open the tools window from the menu. <!-- TODO: confirm menu path, e.g. Window > FishNet > Network Tools -->
3. Enter Play Mode as a host (or a separate server and client). The inspector populates with your spawned NetworkObjects.
4. Open the Condition panel, raise latency or packet loss, and watch the inspector for what changes.

## Roadmap

**v0.1 (in progress):** the inspector window (NetworkObject tree, ownership and observers, SyncVar readout) plus a one-toggle bad-connection wrapper. FishNet only.

**Later / ideas:**

- Condition presets, scriptable ramps and spikes, and per-connection conditions.
- Desync detector: auto-flag when a client value diverges from the server beyond a threshold.
- Session record and replay (scrub a timeline of state and RPCs).
- RPC activity log (per-method send and receive).
- Per-object bandwidth view.
- Support for additional Unity networking stacks.

## Contributing

Issues and pull requests are welcome. This is a community tool maintained on a best-effort basis, so please be patient with response times. When filing a bug, include your Unity and FishNet versions and a minimal reproduction.

## License

MIT. See [LICENSE](LICENSE).

## Disclaimer

FishNet Network Tools is an independent, community-built add-on. It is not affiliated with or endorsed by FirstGearGames, the makers of FishNet. It is built on FishNet's public API and is designed to complement FishNet, not to modify or replace it.
