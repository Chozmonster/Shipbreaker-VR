#!/usr/bin/env bash
# Build an installable ShipbreakerVR drop for Jay.
# Zip root is FLAT (no Mod/ wrapper): winhttp.dll, doorstop_config.ini, BepInEx/, …
# Jay extracts the zip directly into the Steam Shipbreaker folder.
# CRITICAL: doorstop (winhttp.dll) must sit next to Shipbreaker.exe — NOT under CopyToGame/.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="${HOME}/.dotnet:${PATH}"
# Staging folder only (not what Jay sees in the zip).
STAGING_DIR="${ROOT}/dist/staging"
DROP_DIR="${ROOT}/dist"
VERSION="${VERSION:-0.4.8}"
ZIP_NAME="ShipbreakerVR-${VERSION}-jay.zip"

echo "==> Cleaning ${STAGING_DIR}"
rm -rf "${STAGING_DIR}"
mkdir -p "${STAGING_DIR}"

echo "==> Building plugin + patcher → ${STAGING_DIR}"
dotnet build "${ROOT}/ShipbreakerVr.sln" -c Release -p:PublishDir="${STAGING_DIR}/"

if [[ ! -f "${STAGING_DIR}/BepInEx/plugins/ShipbreakerVr/ShipbreakerVr.dll" ]]; then
  echo "ERROR: plugin DLL missing after build" >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# Flatten ModFiles/CopyToGame → staging root
# RaiManager reads a CopyToGame folder; manual zip install must put doorstop
# next to Shipbreaker.exe. Without this, SteamVR shows flat theater mode
# because BepInEx never loads.
# ---------------------------------------------------------------------------
if [[ -d "${STAGING_DIR}/CopyToGame" ]]; then
  echo "==> Flattening CopyToGame → staging root (doorstop next to game exe)"
  cp -a "${STAGING_DIR}/CopyToGame/." "${STAGING_DIR}/"
  rm -rf "${STAGING_DIR}/CopyToGame"
fi

# Also pre-stage OpenXR native binaries into the game-relative paths so the
# first launch does not depend solely on the BepInEx patcher copy step.
PATCHER_COPY="${STAGING_DIR}/BepInEx/patchers/ShipbreakerVrPatcher/CopyToGame"
if [[ -d "${PATCHER_COPY}" ]]; then
  echo "==> Pre-staging OpenXR native files into staging root"
  cp -a "${PATCHER_COPY}/." "${STAGING_DIR}/"
fi

if [[ ! -f "${STAGING_DIR}/winhttp.dll" || ! -f "${STAGING_DIR}/doorstop_config.ini" ]]; then
  echo "ERROR: doorstop files missing at staging root (winhttp.dll / doorstop_config.ini)" >&2
  exit 1
fi

if [[ ! -f "${STAGING_DIR}/Shipbreaker_Data/Plugins/x86_64/UnityOpenXR.dll" ]]; then
  echo "ERROR: UnityOpenXR.dll missing under Shipbreaker_Data/Plugins/x86_64/" >&2
  exit 1
fi

# Install helper for manual installs (no RaiManager required)
cat > "${STAGING_DIR}/INSTALL.txt" << EOF
ShipbreakerVR ${VERSION} (Chozmonster fork)
========================================

This zip has a FLAT root (no Mod\\ folder). Extract directly into your
Steam Shipbreaker folder. You do NOT need the itch.io ShipbreakerVR download.

Quick install (Steam):
1. Close Hardspace: Shipbreaker.
2. Extract this zip INTO your Shipbreaker game folder
   (the folder that contains Shipbreaker.exe), merging folders when asked.
   Typical Steam path:
   C:\\Program Files (x86)\\Steam\\steamapps\\common\\Hardspace Shipbreaker\\
3. VERIFY after extract (this is the #1 theater-mode cause if wrong):
     - winhttp.dll          MUST sit next to Shipbreaker.exe
     - doorstop_config.ini  MUST sit next to Shipbreaker.exe
     - BepInEx\\plugins\\ShipbreakerVr\\ShipbreakerVr.dll exists
   If you see a Mod\\ folder, open it and move its contents up next to the exe.
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

if [[ -f "${STAGING_DIR}/manifest.json" ]]; then
  python3 - <<'PY' "${STAGING_DIR}/manifest.json" "${VERSION}"
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

echo "==> Verifying staging layout"
python3 - <<'PY' "${STAGING_DIR}"
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
wrong = os.path.join(root, "CopyToGame", "winhttp.dll")
if os.path.isfile(wrong):
    print("MISS winhttp.dll must NOT only live under CopyToGame/")
    bad = True
sys.exit(1 if bad else 0)
PY

echo "==> Zipping FLAT root → ${DROP_DIR}/${ZIP_NAME}"
mkdir -p "${DROP_DIR}"
rm -f "${DROP_DIR}/${ZIP_NAME}"
# Zip contents of staging at archive root — NO Mod/ wrapper (Jay preference).
( cd "${STAGING_DIR}" && zip -r "${DROP_DIR}/${ZIP_NAME}" . >/dev/null )

echo "==> Verifying zip has flat root (winhttp.dll at top level, no Mod/)"
python3 - <<'PY' "${DROP_DIR}/${ZIP_NAME}"
import sys, zipfile
zpath = sys.argv[1]
with zipfile.ZipFile(zpath) as z:
    names = z.namelist()
bad = False
# Accept ./winhttp.dll or winhttp.dll
has_doorstop = any(n.replace("\\", "/").lstrip("./") == "winhttp.dll" for n in names)
has_mod_wrapper = any(
    n.replace("\\", "/").startswith("Mod/") or n.replace("\\", "/") == "Mod/"
    for n in names
)
print(("OK  " if has_doorstop else "MISS") + " zip root contains winhttp.dll")
print(("OK  " if not has_mod_wrapper else "MISS") + " zip has NO Mod/ wrapper")
if not has_doorstop or has_mod_wrapper:
    print("Sample entries:", names[:12])
    sys.exit(1)
# Show a few top-level entries
tops = sorted({n.replace("\\", "/").lstrip("./").split("/")[0] for n in names if n.strip("./")})
print("Top-level entries:", ", ".join(tops[:20]))
PY

STORE_ART="/cursor/stores/bc-581a98f7-6064-4a97-99b0-9c1bcd6d7f2f/artifacts"
if [[ -d "/cursor/stores/bc-581a98f7-6064-4a97-99b0-9c1bcd6d7f2f" ]]; then
  mkdir -p "${STORE_ART}"
  cp -f "${DROP_DIR}/${ZIP_NAME}" "${STORE_ART}/"
  echo "==> Copied zip to ${STORE_ART}/${ZIP_NAME}"
fi

# Keep dist/Mod as a convenience symlink/copy for local inspect (optional)
rm -rf "${DROP_DIR}/Mod"
mkdir -p "${DROP_DIR}/Mod"
cp -a "${STAGING_DIR}/." "${DROP_DIR}/Mod/"

echo "==> Done (flat-root zip)"
ls -lh "${DROP_DIR}/${ZIP_NAME}"
echo "Doorstop at staging root:"
ls -lh "${STAGING_DIR}/winhttp.dll" "${STAGING_DIR}/doorstop_config.ini"
