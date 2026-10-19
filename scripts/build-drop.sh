#!/usr/bin/env bash
# Build an installable ShipbreakerVR drop for Jay (zip + Mod/ folder).
# CRITICAL: doorstop (winhttp.dll) must sit next to Shipbreaker.exe — NOT under CopyToGame/.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="${HOME}/.dotnet:${PATH}"
PUBLISH_DIR="${ROOT}/dist/Mod"
DROP_DIR="${ROOT}/dist"
VERSION="${VERSION:-0.4.3}"
ZIP_NAME="ShipbreakerVR-${VERSION}-jay.zip"

echo "==> Cleaning ${PUBLISH_DIR}"
rm -rf "${PUBLISH_DIR}"
mkdir -p "${PUBLISH_DIR}"

echo "==> Building plugin + patcher → ${PUBLISH_DIR}"
dotnet build "${ROOT}/ShipbreakerVr.sln" -c Release -p:PublishDir="${PUBLISH_DIR}/"

if [[ ! -f "${PUBLISH_DIR}/BepInEx/plugins/ShipbreakerVr/ShipbreakerVr.dll" ]]; then
  echo "ERROR: plugin DLL missing after build" >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# Flatten ModFiles/CopyToGame → Mod root
# RaiManager reads a CopyToGame folder; manual zip install must put doorstop
# next to Shipbreaker.exe. Without this, SteamVR shows flat theater mode
# because BepInEx never loads.
# ---------------------------------------------------------------------------
if [[ -d "${PUBLISH_DIR}/CopyToGame" ]]; then
  echo "==> Flattening CopyToGame → Mod root (doorstop next to game exe)"
  cp -a "${PUBLISH_DIR}/CopyToGame/." "${PUBLISH_DIR}/"
  rm -rf "${PUBLISH_DIR}/CopyToGame"
fi

# Also pre-stage OpenXR native binaries into the game-relative paths so the
# first launch does not depend solely on the BepInEx patcher copy step.
PATCHER_COPY="${PUBLISH_DIR}/BepInEx/patchers/ShipbreakerVrPatcher/CopyToGame"
if [[ -d "${PATCHER_COPY}" ]]; then
  echo "==> Pre-staging OpenXR native files into Mod root"
  cp -a "${PATCHER_COPY}/." "${PUBLISH_DIR}/"
fi

if [[ ! -f "${PUBLISH_DIR}/winhttp.dll" || ! -f "${PUBLISH_DIR}/doorstop_config.ini" ]]; then
  echo "ERROR: doorstop files missing at Mod root (winhttp.dll / doorstop_config.ini)" >&2
  exit 1
fi

if [[ ! -f "${PUBLISH_DIR}/Shipbreaker_Data/Plugins/x86_64/UnityOpenXR.dll" ]]; then
  echo "ERROR: UnityOpenXR.dll missing under Mod/Shipbreaker_Data/Plugins/x86_64/" >&2
  exit 1
fi

# Install helper for manual installs (no RaiManager required)
cat > "${PUBLISH_DIR}/INSTALL.txt" << EOF
ShipbreakerVR ${VERSION} (Chozmonster fork)
========================================

This folder is a complete mod drop. You do NOT need the itch.io ShipbreakerVR download.

Quick install (Steam):
1. Close Hardspace: Shipbreaker.
2. Copy EVERYTHING in this Mod folder into your Shipbreaker game folder
   (the folder that contains Shipbreaker.exe), merging folders when asked.
   Typical Steam path:
   C:\\Program Files (x86)\\Steam\\steamapps\\common\\Hardspace Shipbreaker\\
3. VERIFY after copy (this is the #1 theater-mode cause if wrong):
     - winhttp.dll          MUST sit next to Shipbreaker.exe
     - doorstop_config.ini  MUST sit next to Shipbreaker.exe
     - BepInEx\\plugins\\ShipbreakerVr\\ShipbreakerVr.dll exists
   If winhttp.dll is inside a CopyToGame folder, move it up next to the exe.
4. Boot order (SteamVR-first / VD / ALVR):
     a) Start Virtual Desktop or ALVR and put headset on
     b) Start SteamVR (confirm headset tracking)
     c) Confirm SteamVR is the OpenXR runtime
     d) Launch Shipbreaker from Steam
5. VR should start automatically (no F3). F3 is emergency toggle only.
6. Gaze laser + trigger/click for menus → Free Play. Stay out of R.A.C.E.

If you see a flat theater / virtual desktop monitor in the headset:
  → BepInEx probably did not load. See install-and-test.md troubleshooting.
  → Check for BepInEx\\LogOutput.log after one launch. No log = doorstop wrong.

Debug keys: F3 = VR toggle, F5 = potato quality, F6 = toggle lights
Logs: BepInEx\\LogOutput.log
EOF

if [[ -f "${PUBLISH_DIR}/manifest.json" ]]; then
  python3 - <<'PY' "${PUBLISH_DIR}/manifest.json" "${VERSION}"
import json, sys
path, ver = sys.argv[1], sys.argv[2]
with open(path) as f:
    data = json.load(f)
data["id"] = "shipbreaker-vr-jay"
data["modTitle"] = f"ShipbreakerVR (Jay fork) {ver}"
with open(path, "w") as f:
    json.dump(data, f, indent=2)
    f.write("\n")
PY
fi

echo "==> Verifying drop layout"
python3 - <<'PY' "${PUBLISH_DIR}"
import os, sys
root = sys.argv[1]
required = [
    "winhttp.dll",
    "doorstop_config.ini",
    "BepInEx/plugins/ShipbreakerVr/ShipbreakerVr.dll",
    "BepInEx/plugins/ShipbreakerVr/Unity.XR.OpenXR.dll",
    "BepInEx/patchers/ShipbreakerVrPatcher/ShipbreakerVrPatcher.dll",
    "Shipbreaker_Data/Plugins/x86_64/UnityOpenXR.dll",
    "Shipbreaker_Data/Plugins/x86_64/openxr_loader.dll",
]
bad = False
for rel in required:
    path = os.path.join(root, rel.replace("/", os.sep))
    ok = os.path.isfile(path)
    print(("OK  " if ok else "MISS") + " " + rel)
    bad = bad or not ok
# Fail if leftover CopyToGame at root with doorstop (wrong layout)
wrong = os.path.join(root, "CopyToGame", "winhttp.dll")
if os.path.isfile(wrong):
    print("MISS winhttp.dll must NOT only live under CopyToGame/")
    bad = True
sys.exit(1 if bad else 0)
PY

echo "==> Zipping → ${DROP_DIR}/${ZIP_NAME}"
mkdir -p "${DROP_DIR}"
rm -f "${DROP_DIR}/${ZIP_NAME}"
( cd "${DROP_DIR}" && zip -r "${ZIP_NAME}" Mod >/dev/null )

STORE_ART="/cursor/stores/bc-581a98f7-6064-4a97-99b0-9c1bcd6d7f2f/artifacts"
if [[ -d "/cursor/stores/bc-581a98f7-6064-4a97-99b0-9c1bcd6d7f2f" ]]; then
  mkdir -p "${STORE_ART}"
  cp -f "${DROP_DIR}/${ZIP_NAME}" "${STORE_ART}/"
  # Keep a pointer for the previous broken drop name so we don't confuse Jay
  echo "==> Copied zip to ${STORE_ART}/${ZIP_NAME}"
fi

echo "==> Done"
ls -lh "${DROP_DIR}/${ZIP_NAME}"
echo "Doorstop at Mod root:"
ls -lh "${PUBLISH_DIR}/winhttp.dll" "${PUBLISH_DIR}/doorstop_config.ini"
