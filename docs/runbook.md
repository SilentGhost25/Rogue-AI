# Runbook (stub)

Fill in during the pilot. Must cover: start order (Gitea, engine, control plane), pre-flight quota check, Global Pause,
re-sync and re-apply a kiosk, kill switch, backups (SQLite copy every 10 minutes), hot spare server, end-of-event
credential cleanup (`/logout` in `agy`, delete OpenCode and Antigravity credentials, clear Credential Manager).

Hard rules: pilot accounts are never event accounts. Event accounts are idle from 6 Nov.

## Start order
1. `docker compose -f infra/docker-compose.yml up -d` (Gitea, engine) or run the engine with uvicorn.
2. `dotnet run --project server/ControlPlane`, open `/`, enter the admin key.
3. Start kiosks. Each shows "Connected" and registers; tiles appear on the admin page.
4. Before 10:45 run a dry attack on the pilot machines only.

## When something breaks
- Kiosk site pane blank: the participant's last edit broke the site. The status strip says so. They fix it with the agent.
- Kiosk missed an attack: it re-syncs on reconnect. Or `POST /admin/participants/{id}/reapply/{n}`.
- Checker down: participants see "The checker is unavailable"; no penalty is charged. Restart the engine.

## Organizer mode (revision 3)
1. On the organizer laptop run `run.bat organizer` (or `LabKiosk.exe --host`). It starts the engine and control plane and opens the Command Center.
2. Copy the admin key with the button in the window title bar. Participants can open the console from a kiosk with Ctrl+Shift+A.
3. Use Freeze Board for the final hour (projector masks the top five scores; ranks keep moving). Use the target selector to test an attack on one kiosk first.
4. Export the ledger CSV at the end: it is the complete scoring record.
