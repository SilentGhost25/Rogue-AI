# Runbook (stub)

Fill in during the pilot. Must cover: start order (Gitea, engine, control plane), pre-flight quota check, Global Pause,
re-sync and re-apply a kiosk, kill switch, backups (SQLite copy every 10 minutes), hot spare server, end-of-event
credential cleanup (`/logout` in `agy`, delete OpenCode and Antigravity credentials, clear Credential Manager).

Hard rules: pilot accounts are never event accounts. Event accounts are idle from 6 Nov.
