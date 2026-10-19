# XR managed reference assemblies

Prebuilt from Unity package sources (matching ShipbreakerVR’s OpenXR stack) so this repo can compile without a local Unity Editor build under `ShipbreakerVrUnity/Build/`.

- `Unity.XR.Management` — com.unity.xr.management 4.2.1
- `Unity.XR.OpenXR` — com.unity.xr.openxr 1.2.8 (minor compile patch for InputSystem 1.0.2)
- `Unity.InputSystem` — com.unity.inputsystem 1.0.2

Native OpenXR binaries still come from `ShipbreakerVrPatcher/CopyToGame` at install time.
