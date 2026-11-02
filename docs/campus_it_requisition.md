# ROGUE AI: The Breach Protocol — Campus IT Network & Systems Requisition

**Event Date:** 13 November 2026 (Rehearsal Date: 10 November 2026)  
**Location:** Main Computing Lab / Engineering Annex  
**System Load:** 50 Concurrent Participant Laptops + 1 Master Host Control Laptop

---

## 1. Executive Summary & Action Required

On Friday, 13 November 2026, the department is hosting **ROGUE AI: The Breach Protocol**, a live collegiate defensive cybersecurity challenge. 50 participants will defend a local web application against 7 staged attacks using AI coding assistants (`agy` and `opencode`).

All attack simulations and scoring occur **strictly on the local subnet**. However, participant laptops require outbound HTTPS access to cloud AI API endpoints. To ensure uninterrupted operation and prevent false security alerts on campus networks, we request the network and security policies detailed below.

---

## 2. Firewall / Proxy Whitelist (Outbound HTTPS Port 443)

Please whitelist outbound HTTPS (TCP 443) traffic from the competition subnet (`10.x.x.x` or assigned DHCP pool) to the following Fully Qualified Domain Names (FQDNs):

### A. Google Antigravity & Gemini API Endpoints (`agy`)
- `generativelanguage.googleapis.com` (Core AI inference and streaming tool use)
- `accounts.google.com` (OAuth2 participant login)
- `oauth2.googleapis.com` (Token refresh and credential management)
- `cloudaicompanion.googleapis.com` (Antigravity telemetry & service backend)
- `www.googleapis.com` (Google client discovery APIs)
- `firebaseinstallations.googleapis.com` (SDK client installation authentication)

### B. OpenCode & Secondary Provider Endpoints
- `openrouter.ai` (OpenCode unified model gateway)
- `api.openai.com` (OpenAI model inference)
- `api.anthropic.com` (Claude model inference)
- `huggingface.co` and `api-inference.huggingface.co` (Model repository & weight verification)

### C. Developer & Runtime Infrastructure
- `registry.npmjs.org` (Node package registry verification)
- `github.com` and `api.github.com` (Base repository clones)

### D. Empirical Proxy Capture Verification Procedure
*(Conducted on test laptop with mitmproxy / Fiddler / netstat during active 30-minute session covering CLI agent login, streaming chat prompts, and tool-assisted code refactoring)*
- All endpoints listed above were captured and verified during live CLI agent traffic analysis.
- No undocumented third-party analytics hosts are required.

---

## 3. Critical Network Inquiries

### Question 1: TLS / SSL Interception (Deep Packet Inspection)
* **Our Concern:** Command-line developer tools (`node`, `python`, `git`, and CLI agents) maintain internal CA certificate bundles rather than relying exclusively on the Windows Enterprise Certificate Store.
* **Request:** 
  - **Preferred:** Please configure the firewall to perform **SNI-based domain filtering only** (bypassing SSL/TLS inspection) for the above whitelisted domains on the competition subnet.
  - **Alternative:** If campus security policy strictly mandates TLS interception, please provide the campus root CA certificate in `.cer` or `.pem` format by **Wednesday, 04 November 2026**, so we can pre-install and configure `NODE_EXTRA_CA_CERTS`, `SSL_CERT_FILE`, and `REQUESTS_CA_BUNDLE` in the participant installer.

### Question 2: NAT & Connection Rate Limits
* 50 concurrent laptops will be making simultaneous streaming HTTPS requests through the lab gateway.
* **Request:** Please confirm that the laboratory gateway has sufficient NAT state table capacity and does not enforce per-IP burst throttling (e.g. rate-limiting connections to fewer than 10 requests/second per IP).

---

## 4. Host Machine & Lab Machine Antivirus Exclusions

The competition involves deliberate vulnerability demonstrations (Stored XSS, SQL injection, hardcoded test admin backdoors). Although non-executable and confined to a local sandboxed Node.js website, antivirus heuristics may falsely flag these educational payloads.

Please confirm that laboratory machines or Defender Group Policies allow the following path exclusions:
- `C:\BreachWorkspace\`
- `C:\Program Files\RogueAI-Kiosk\`
- `C:\Users\*\AppData\Local\Programs\RogueAI\`

---

## 5. Contact & Coordination

- **Lead Faculty / Organizer:** [Organizer Name / Email]
- **Technical Lead:** [Technical Lead Name / Email]
- **Target Verification Window:** We request confirmation by **Thursday, 05 November 2026**, to allow smoke testing on 5–10 laptops.
