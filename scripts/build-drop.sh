#!/usr/bin/env bash
# Build an installable ShipbreakerVR drop for Jay (zip + Mod/ folder).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="${HOME}/.dotnet:${PATH}"
PUBLISH_DIR="${ROOT}/dist/Mod"
DROP_DIR="${ROOT}/dist"
VERSION="${VERSION:-0.4.0}"
ZIP_NAME="ShipbreakerVR-${VERSION}-jay.zip"

echo "==> Cleaning ${PUBLISH_DIR}"
rm -rf "${PUBLISH_DIR}"
mkdir -p "${PUBLISH_DIR}"

echo "==> Building plugin + patcher → ${PUBLISH_DIR}"
dotnet build "${ROOT}/ShipbreakerVr.sln" -c Release -p:PublishDir="${PUBLISH_DIR}/"

# Patcher also needs to land under BepInEx/patchers
if [[ ! -f "${PUBLISH_DIR}/BepInEx/plugins/ShipbreakerVr/ShipbreakerVr.dll" ]]; then
  echo "ERROR: plugin DLL missing after build" >&2
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
3. Launch SteamVR (or set Meta as OpenXR runtime if using Link).
4. Start Shipbreaker from Steam.
5. Put on your headset — VR should start automatically (no F3).
6. Use gaze laser + trigger/click for menus. Go to Free Play.
7. Stay out of R.A.C.E. while modded.

Debug keys (optional):
- F3: toggle VR on/off
- F5: potato quality
- F6: toggle lights

Logs: BepInEx\\LogOutput.log in the game folder.
EOF

# RaiManager-compatible marker (manifest already copied from ModFiles)
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

echo "==> Zipping → ${DROP_DIR}/${ZIP_NAME}"
mkdir -p "${DROP_DIR}"
rm -f "${DROP_DIR}/${ZIP_NAME}"
( cd "${DROP_DIR}" && zip -r "${ZIP_NAME}" Mod >/dev/null )

# Also copy into project agent store artifacts if present
STORE_ART="/cursor/stores/bc-581a98f7-6064-4a97-99b0-9c1bcd6d7f2f/artifacts"
if [[ -d "/cursor/stores/bc-581a98f7-6064-4a97-99b0-9c1bcd6d7f2f" ]]; then
  mkdir -p "${STORE_ART}"
  cp -f "${DROP_DIR}/${ZIP_NAME}" "${STORE_ART}/"
  echo "==> Copied zip to ${STORE_ART}/${ZIP_NAME}"
fi

echo "==> Done"
ls -lh "${DROP_DIR}/${ZIP_NAME}"
ls -lh "${PUBLISH_DIR}/BepInEx/plugins/ShipbreakerVr/" | head
