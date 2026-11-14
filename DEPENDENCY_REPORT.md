# ROGUE AI — DEPENDENCY & ENVIRONMENT REPORT

## 1. System-Level Runtimes & Host Environment

| Runtime / Tool | Required Version | Installed / Detected Version | Location / Source | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Operating System** | Windows 10 / 11 x64 | Windows 11 Build 26100 (x64) | Host OS | **Compatible** |
| **.NET SDK** | .NET 9.0+ | .NET SDK 11.0.100 | `C:\Program Files\dotnet\dotnet.exe` | **Compatible** (via `RollForward=Major`) |
| **.NET Runtime** | .NET 9.0 Core | ASP.NET Core 11.0.0 Runtime | Shared runtime directory | **Compatible** |
| **Node.js** | `>= 22.13.0` | Node.js v22.14.0 | System PATH / `vendor/nodejs` | **Compatible** (Built-in SQLite enabled) |
| **Python** | `>= 3.11.0` | Python 3.14.0 / 3.12+ | System PATH | **Compatible** |
| **WebView2** | Evergreen Runtime | Evergreen 130.0+ | System WebView2 Runtime | **Compatible** |
| **Inno Setup** | 6.2+ | ISCC Compiler (Optional for build) | Path / Program Files | **Needed for installer build only** |

---

## 2. Package & Library Manifests

### A. Base Website (`site/package.json`)
* `express`: `4.21.1` (Pinned)
* Built-in Node modules utilized: `node:sqlite`, `node:test`, `node:http`, `node:path`.
* Audited status: 70 packages, 0 build steps, pure CommonJS.

### B. Python Breach Engine (`engine/requirements.txt`)
* `fastapi`: `0.115.0`
* `uvicorn`: `0.30.6`
* `pydantic`: `2.9.2`
* `pyyaml`: `6.0.2`
* `pytest`: `8.3.3`
* `psutil`: `7.1.3`
* Additional verified packages in environment: `websockets` (`16.0`), `ctypes` (standard library).

### C. Control Plane (`server/ControlPlane/ControlPlane.csproj`)
* `BCrypt.Net-Next`: `4.0.3` (Admin credential verification)
* `Microsoft.Data.Sqlite`: `9.0.2` (SQLite WAL engine)
* `YamlDotNet`: `16.3.0` (Manifest parser)
* `<TargetFramework>`: `net9.0` with `<RollForward>Major</RollForward>`

### D. Control Plane Tests (`server/ControlPlane.Tests/ControlPlane.Tests.csproj`)
* `Microsoft.NET.Test.Sdk`: `17.11.1`
* `xunit`: `2.9.2`
* `xunit.runner.visualstudio`: `2.8.2`
* `<TargetFramework>`: `net9.0` with `<RollForward>Major</RollForward>`

### E. Participant Kiosk (`kiosk/LabKiosk/LabKiosk.csproj`)
* `Microsoft.Web.WebView2`: `1.0.2903.40`
* `Microsoft.AspNetCore.SignalR.Client`: `9.0.2`
* `AvalonEdit`: `6.3.1.120`
* `<TargetFramework>`: `net9.0-windows` with `<RollForward>Major</RollForward>`

---

## 3. Bundled Vendor Assets (`vendor/`)

* **Portable Node.js (`vendor/nodejs/`):**
  * Contains standalone `node.exe` and `npm.cmd` for air-gapped participant deployment.
* **Cloudflare Tunnel (`vendor/cloudflared.exe`):**
  * Present for remote demonstration tunneling (`share_online.bat`).
  * *Audit Risk:* Must not be run concurrently with live competition without strict authentication shielding.

---

## 4. Environment Variables & Configuration Keys

| Key | Default / Fallback | Recommended Event Value | Current Exposure |
| :--- | :--- | :--- | :--- |
| `BREACH_ADMIN_KEY` | `LocalDevAdminKey_2026_Secure123` | High-entropy 32-char token | Hardcoded dev fallback present |
| `ADMIN_USERNAME` | `admin` | Custom organizer handle | Config default |
| `ADMIN_PASSWORD_HASH` | Hash for `admin123456` | Custom bcrypt hash | Default bcrypt hash in `appsettings.json` |
| `GIT_MIRROR_URL` | Hardcoded GitHub competition repo | Organizer private Gitea / GitHub repo | Hardcoded public user URL in code |
| `BREACH_WORKSPACES` | `data/workspaces` | Dedicated fast SSD volume | Config default |
| `BREACH_DATABASE` | `data/breach.db` | Dedicated directory with hourly backup | Config default |

