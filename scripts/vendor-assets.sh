#!/usr/bin/env bash
# Same as vendor-assets.ps1 for a Linux or macOS build machine.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
term="$root/kiosk/LabKiosk/Assets/terminal"; lib="$root/server/ControlPlane/wwwroot/lib"
mkdir -p "$term" "$lib"
curl -fsSL https://cdn.jsdelivr.net/npm/xterm@5.3.0/lib/xterm.js -o "$term/xterm.js"
curl -fsSL https://cdn.jsdelivr.net/npm/xterm@5.3.0/css/xterm.css -o "$term/xterm.css"
curl -fsSL https://cdn.jsdelivr.net/npm/xterm-addon-fit@0.8.0/lib/xterm-addon-fit.js -o "$term/xterm-addon-fit.js"
curl -fsSL https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.7/dist/browser/signalr.min.js -o "$lib/signalr.min.js"
sha256sum "$term"/* "$lib"/*
