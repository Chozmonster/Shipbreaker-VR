# ShipbreakerVR (Jay / Chozmonster fork)

Fork of [Raicuparta/shipbreaker-vr](https://github.com/Raicuparta/shipbreaker-vr) (MIT) for Hardspace: Shipbreaker PCVR.

**This fork is the install path** — Jay does not need the itch.io download first.

## Week 1 goals (this fork)

- Auto-enter VR on launch (no F3 required; F3 remains a debug toggle)
- World-space menus + gaze laser so main menu → Free Play works in-headset
- Simple zip install drop (flat root — extract into game folder; no `Mod\` wrapper)

Motion controls (grab / cut / throw) are **not** done yet.

## Build

```bash
# Requires .NET SDK 8+
./scripts/build-drop.sh
```

Output:

- `dist/ShipbreakerVR-<ver>-jay.zip` — **flat root** (`winhttp.dll`, `BepInEx\`, …); extract into Steam Shipbreaker folder
- `dist/staging/` / `dist/Mod/` — local inspect copies of the same files

Vendored XR managed refs live under `ShipbreakerVrUnity/Build/.../Managed/` (built from Unity package sources for CI). Native OpenXR binaries ship via `ShipbreakerVrPatcher/CopyToGame`.

## Install / test

See the project docs (`install-and-test.md` in the agent store / Jay handoff).

## License

MIT — includes original Raicuparta copyright; see `LICENSE`.
