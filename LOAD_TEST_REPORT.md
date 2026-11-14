# ROGUE AI — 50-PARTICIPANT CAPACITY & LOAD TEST REPORT (STAGE 5)

**Document:** Simulated Capacity, Concurrency, and System Responsiveness Report  
**Date:** November 14, 2026  
**Auditor:** Autonomous Systems Agent (Antigravity)  
**Target Environment:** Local Event Host (Windows 11, x64, 16-Core Host, SSD)  
**Workload Model:** 50 Concurrent Participant Kiosks + 4 Concurrent Administrators  
**Test Runner:** `scripts/stage5_load_test.py`  

---

## 1. Executive Summary

A controlled stress test was executed simulating the full event capacity requirements:
* **50 Concurrent Participant Kiosks:** Executed desk token enrollment, 3 rounds of workspace code snapshots (150 uploads total), AI prompt and quota telemetry transmission (150 events total), and final fix submissions.
* **4 Concurrent Administrators:** Monitored live participant kiosks (`/admin/kiosks`), live scoreboard (`/api/leaderboard`), audit ledger exports (`/admin/export/ledger`), and automated database backup health (`/admin/backup/status`).
* **Database Target:** Dedicated isolated test database with WAL journal mode enabled.

**Result:** The platform processed **510 requests in 3.83 seconds** with an effective sustained throughput of **133.2 requests/second**, achieving a **100% success rate** with **zero database lock conflicts** and **zero failed kiosk operations**.

---

## 2. Quantitative Performance Metrics

### A. Throughput & Volume
* **Total Elapsed Time:** `3.83 seconds`
* **Total HTTP Transactions:** `510 requests`
* **Throughput:** `133.2 requests / second`
* **Error Rate:** `0.00% (0 errors across 510 transactions)`

### B. Operation Breakdown

| Operation Type | Initiator | Total Planned | Succeeded | Failed | Success Rate |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Desk Token Enrollment** | 50 Kiosks | 50 | **50** | 0 | **100.0%** |
| **Workspace Snapshots (3 Rds)** | 50 Kiosks | 150 | **150** | 0 | **100.0%** |
| **AI Prompt & Quota Telemetry** | 50 Kiosks | 150 | **150** | 0 | **100.0%** |
| **Fix Submission Queueing** | 50 Kiosks | 50 | **50** | 0 | **100.0%** |
| **Admin Status & Telemetry Grid** | Admin #1 | 28 | **28** | 0 | **100.0%** |
| **Admin Live Leaderboard Poll** | Admin #2 | 28 | **28** | 0 | **100.0%** |
| **Admin Ledger CSV Export** | Admin #3 | 27 | **27** | 0 | **100.0%** |
| **Admin Backup Health Check** | Admin #4 | 27 | **27** | 0 | **100.0%** |
| **Total Transactions** | — | **510** | **510** | **0** | **100.0%** |

### C. Latency Percentiles (End-to-End HTTP Round-Trip)

| Metric | Measured Value | Standard Threshold | Assessment |
| :--- | :--- | :--- | :--- |
| **Minimum Latency** | `11.4 ms` | `< 50 ms` | **Excellent** |
| **P50 (Median) Latency** | `65.6 ms` | `< 250 ms` | **Optimal** |
| **Mean Latency** | `176.6 ms` | `< 500 ms` | **Optimal** |
| **P95 Latency** | `795.8 ms` | `< 1500 ms` | **Within Target** |
| **P99 Latency** | `2,331.8 ms` | `< 3000 ms` | **Acceptable under peak burst** |

---

## 3. Database Consistency & Resource Evaluation

1. **SQLite Concurrency & WAL Performance:**
   * SQLite configured with `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;`.
   * **Lock Conflict Count:** **0** (Zero `SQLITE_BUSY` or `database is locked` exceptions recorded).
2. **Append-Only Triggers Enforced Under Load:**
   * An unauthorized `UPDATE event_log SET type = 'HACKED'` attempted during test execution was **aborted by SQLite trigger** (`event_log is append-only`).
   * An unauthorized `DELETE FROM score_ledger` was **aborted by SQLite trigger** (`score_ledger is append-only`).
3. **Workspace File I/O Throughput:**
   * Processed 150 Base64-decoded snapshot payloads into `server/ControlPlane/data/workspaces/`. All SHA256 hashes matched without filesystem collisions.

---

## 4. Hardware Limitations & Rehearsal Caveat

> [!IMPORTANT]
> **Simulated Load vs. Real-World Event Rehearsal:**
> While this test confirms that the Control Plane server, SQLite WAL engine, token validator, and snapshot store can handle 50 concurrent connections at > 130 req/sec on host hardware, a software simulation does not simulate physical Wi-Fi packet dropouts, switch buffering delays, or OS-level sleep states.
> **Recommendation:** Conduct an on-site rehearsal with at least 5–10 physical participant laptops connected to the event Wi-Fi router on setup day.

