# ROGUE AI: The Breach Protocol — Event Deployment Guide

**Target Environment:** Local Venue Network / Production Event Deployment  
**Audience:** Event Technical Directors, Infrastructure Leads, and Lab Proctors  
**Status:** Certified Stage 6 Production Deliverable

---

## 1. System Requirements & Architecture Overview

The ROGUE AI platform runs in an on-premise distributed architecture consisting of one central **Organizer Host** and up to 50 **Participant Kiosks** connected via a local area network (LAN/Wi-Fi).

```
┌─────────────────────────────────────────────────────────────┐
│                       LOCAL VENUE NETWORK                   │
│                                                             │
│   ┌─────────────────────────────────────────────────────┐   │
│   │                 ORGANIZER HOST MACHINE              │   │
│   │  • Control Plane (ASP.NET Core)    : Port 5080 TCP  │   │
│   │  • Discovery Beacon (UDP Broadcast): Port 5082 UDP  │   │
│   │  • Breach Engine (Python FastAPI)  : Port 8000 TCP  │   │
│   │  • Admin Console                   : /admin/        │   │
│   │  • SQLite Master Database          : controlplane.db│   │
│   └──────────────────────┬──────────────────────────────┘   │
│                          │                                  │
│         ┌────────────────┴───────────────┐                  │
│         ▼                                ▼                  │
│  ┌───────────────┐               ┌───────────────┐          │
│  │ PARTICIPANT 01│  · · · · · ·  │ PARTICIPANT 50│          │
│  │ LabKiosk.exe  │               │ LabKiosk.exe  │          │
│  │ Site (:3000)  │               │ Site (:3000)  │          │
│  │ C:\BreachWork │               │ C:\BreachWork │          │
│  └───────────────┘               └───────────────┘          │
└─────────────────────────────────────────────────────────────┘
```

### 1.1 Organizer Host Hardware & OS
* **Operating System:** Windows 10 / 11 64-bit Pro/Enterprise or Windows Server 2022.
* **Processor:** 8 cores / 16 threads (e.g., Intel i7/i9 12th+ Gen, AMD Ryzen 7/9 5000+ series).
* **RAM:** 16 GB minimum (32 GB recommended).
* **Storage:** 20 GB free SSD storage.
* **Software Prerequisites:**
  * [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or .NET 8.0/9.0 runtime).
  * [Python 3.12+](https://www.python.org/downloads/) (with `pip` in PATH).
  * [Node.js 22 LTS](https://nodejs.org/) (includes `npm`).
  * Microsoft Edge WebView2 Runtime (installed by default on Windows 10/11).
  * Git for Windows (optional for git-mirroring).

### 1.2 Participant Kiosk Hardware & OS
* **Operating System:** Windows 10 / 11 64-bit.
* **Processor:** Quad-core 2.0 GHz or higher.
* **RAM:** 8 GB minimum.
* **Storage:** 5 GB free disk space on drive `C:`.
* **Prerequisites:** Microsoft Edge WebView2 Runtime (pre-installed on modern Windows).

---

## 2. Venue Network & Firewall Configuration

### 2.1 Subnet & Router Requirements
* **Dedicated Subnet:** Deploy a private, isolated subnet (e.g., `192.168.1.0/24` or `10.0.0.0/24`).
* **Static Host IP:** Assign the Organizer Host a static IP or DHCP reservation (e.g., `192.168.1.100`).
* **Router Settings (CRITICAL):**
  * **Disable Client Isolation / AP Isolation:** Kiosks must be able to communicate directly with the host machine.
  * **Enable UDP Broadcast:** Required for the zero-config auto-discovery beacon on port 5082.
  * **Disable Captive Portals:** Captive login screens will prevent kiosks from handshaking during check-in.

### 2.2 Port Matrix

| Port | Protocol | Source | Destination | Purpose | Firewall Action |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **5080** | TCP | Kiosks / Admins | Organizer Host | Control Plane REST API, SignalR Hub, Admin Console | **Allow Inbound on Host** |
| **5082** | UDP | Organizer Host | Subnet Broadcast | Auto-discovery beacon (`RogueAI-Beacon`) | **Allow Inbound/Outbound** |
| **8000** | TCP | Control Plane | Organizer Host (localhost) | Python Breach Engine | Internal Host Only |
| **3000** | TCP | Local Kiosk | Localhost (`127.0.0.1`) | AEGIS Target Application (`C:\BreachWorkspace`) | Localhost Only |

### 2.3 Windows Firewall Commands (Run on Host as Administrator)

```powershell
# Open Control Plane HTTP & SignalR
New-NetFirewallRule -DisplayName "RogueAI-ControlPlane-5080" -Direction Inbound -LocalPort 5080 -Protocol TCP -Action Allow

# Open Auto-Discovery UDP Beacon
New-NetFirewallRule -DisplayName "RogueAI-Discovery-5082" -Direction Inbound -LocalPort 5082 -Protocol UDP -Action Allow
```

---

## 3. Step-by-Step Organizer Host Setup

### Step 1: Clone or Copy the Repository
Place the project repository in a clean directory (e.g., `C:\RogueAI` or root workspace):
```cmd
cd C:\RogueAI
```

### Step 2: Install Base Dependencies
From an administrative PowerShell prompt:
```powershell
# 1. Install website dependencies
cd site
npm install
cd ..

# 2. Install engine dependencies
cd engine
pip install -r requirements.txt
cd ..

# 3. Restore and build server
cd server\ControlPlane
dotnet build -c Release
cd ..\..
```

### Step 3: Configure Admin Credentials & Environment
Generate a secure admin API key and set the session secret:
```powershell
# Option A: Run automated credential generator
powershell -ExecutionPolicy Bypass -File scripts\generate-admin-key.ps1

# Option B: Set environment variables manually
$env:ADMIN_TOKEN = "event_admin_secure_key_2026"
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

### Step 4: Launch the Host Platform
Start the central Control Plane and Python Breach Engine:
```cmd
.\run.bat host
```
*The command starts:*
1. Python Breach Engine on `http://127.0.0.1:8000`.
2. ASP.NET Core Control Plane on `http://0.0.0.0:5080`.
3. Background UDP Beacon broadcasting `http://<HOST_IP>:5080` to port 5082 every 2 seconds.
4. Automatic hourly database snapshot service.

### Step 5: Verify Host Endpoints
Open a browser on the Organizer Host:
* **Admin Dashboard:** `http://localhost:5080/admin/` (Login with configured Admin Key).
* **Live Leaderboard:** `http://localhost:5080/leaderboard`.
* **API Documentation:** `http://localhost:5080/swagger` or `http://localhost:8000/docs`.

---

## 4. Participant Kiosk Deployment

### Method A: Self-Contained Inno Setup Installer (Recommended)
1. Build the installer on the host or build machine:
   ```powershell
   powershell -ExecutionPolicy Bypass -File installer\build-kiosk.ps1
   ```
2. Distribute `installer\Output\RogueAI-Setup.exe` to participant machines via USB flash drive or local file share.
3. Run `RogueAI-Setup.exe` on each participant machine.
4. Launch **Rogue AI LabKiosk** from the desktop shortcut.

### Method B: Portable Execution via `run.bat`
1. Copy the repository or pre-built kiosk folder to the participant machine.
2. Run the play launcher:
   ```cmd
   .\run.bat play
   ```
   *If UDP broadcast discovery is disabled on the network, specify the host IP manually:*
   ```cmd
   .\run.bat play -HostUrl http://192.168.1.100:5080
   ```

### 4.1 Desk Registration Workflow
1. When LabKiosk opens, the onboarding wizard presents the desk registration dialog.
2. Enter the participant's assigned **Desk ID** (e.g., `DESK-01` through `DESK-50`) and **Callsign**.
3. Click **Connect**.
4. The kiosk automatically:
   * Requests enrollment from the Control Plane.
   * Receives and stores a cryptographically secure `DeskToken`.
   * Establishes a persistent SignalR connection to `/hubs/breach`.
   * Unpacks the base AEGIS website into `C:\BreachWorkspace`.
   * Launches the local website on port 3000 inside the embedded WebView2 browser.

---

## 5. Offline & Backup Operating Modes

### 5.1 Fully Offline Mode (No Internet Access)
The platform is designed to operate 100% offline:
* All dependencies (`node_modules`, Python wheels, NuGet packages) are cached locally.
* Cloudflare tunnels (`share_online.bat`) are omitted.
* Kiosks sync directly with the host over the local router.

### 5.2 Hybrid / Remote Mode (Cloudflare Tunnel)
If remote judges, remote scoreboards, or off-site participants need access:
1. Run the secure tunnel launcher:
   ```cmd
   share_online.bat
   ```
2. Accept the confirmation prompt (`Y`).
3. Note the public URL provided by `cloudflared` (e.g., `https://xyz.trycloudflare.com`).
4. Remote users access `/leaderboard` or `/admin/` via HTTPS.

---

## 6. Pre-Event Sanity Checklist

Complete this checklist at **T-60 minutes** before participant check-in:

- [ ] Organizer Host IP is static and recorded.
- [ ] Windows Firewall rules for 5080 (TCP) and 5082 (UDP) are active.
- [ ] Host started via `.\run.bat host` and running without errors.
- [ ] Admin Console loads at `http://localhost:5080/admin/`.
- [ ] Test Kiosk connects successfully from another laptop on the venue Wi-Fi.
- [ ] Test Kiosk displays desk onboarding, connects to SignalR, and renders AEGIS site at `:3000`.
- [ ] Test Kiosk snapshot upload completes without error.
- [ ] Test kiosk purged from database before live event (`POST /api/admin/reset` or clean database).
- [ ] Projector / Large Screen connected and displaying `http://<HOST_IP>:5080/leaderboard`.

