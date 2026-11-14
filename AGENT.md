# AGENT.md — ROUGH AI Participant Kiosk: Web Pane UI Transformation

## 1. Mission

Transform **only the Participant Kiosk's Web pane UI** for the AEGIS Municipal Defense Core website into a polished, highly interactive, premium dashboard inspired by the supplied ELECTRA reference screenshots.

The target is a visual redesign of the existing AEGIS website—not a product rewrite. Preserve AEGIS's identity, terminology, data, routes, workflows, and working behavior. Borrow the reference's visual language (dark purple surfaces, violet/magenta accents, subtle glow, rounded panels, layered cards, clean spacing, polished controls, and dashboard composition), but do **not** turn AEGIS into an EV charging application. All displayed content must remain relevant to AEGIS.

## 2. Absolute scope boundary

The application has three participant panes: **WEB, CODE, and AI**. Work exclusively on **WEB**.

### Files permitted for UI work

You may edit only these files:

**Website views**
- `site/views/layout.html`
- `site/views/index.html`
- `site/views/grid.html`
- `site/views/traffic.html`
- `site/views/vault.html`
- `site/views/login.html`
- `site/views/compromised.html`

**Website static assets**
- `site/public/site.css`
- `site/public/site.js`

### Protected files — do not edit

**Website backend / route / rendering layer (read-only)**
- `site/server.js`
- `site/app.js`
- `site/routes/overview.js`
- `site/routes/grid.js`
- `site/routes/traffic.js`
- `site/routes/vault.js`
- `site/routes/auth.js`
- `site/lib/renderer.js`

**Kiosk host / WebView2 integration (read-only)**
- `kiosk/LabKiosk/MainWindow.xaml`
- `kiosk/LabKiosk/MainWindow.xaml.cs`
- `kiosk/LabKiosk/Services/SiteHost.cs`
- `kiosk/LabKiosk/Services/WebViewEnv.cs`

Also do not edit, rename, move, delete, or regenerate any other project file, configuration, package manifest, lockfile, database, data file, environment file, certificate, secret, or asset outside the permitted list.

**No exceptions by convenience.** If a required UI improvement appears to need a protected file change, stop and explain the exact blocker. Do not make the change yourself. Ask for explicit authorization before proposing any scope expansion.

## 3. Protect existing functionality

This is a UI transformation, not a backend or integration refactor.

- Preserve every existing URL, route, query parameter, form action, input name, element ID/class relied on by scripts, data attribute, template placeholder, and server-rendered variable unless you have verified it is unused.
- Preserve authentication, session behavior, authorization checks, login/logout, vault inspection, filtering, traffic controls, emergency preemption indicators, grid data, audit logs, compromised-state rendering, and all existing API calls.
- Do not rename route paths, API endpoints, JSON fields, template variables, or backend concepts.
- Do not fabricate data, status values, telemetry, citizens, incidents, security events, or successful actions.
- Do not replace server-rendered data with hard-coded demo content.
- Do not remove a feature because it is difficult to style.
- Do not change fetch URLs, request methods, request bodies, headers, polling intervals, or response parsing unless a demonstrable frontend-only bug makes a minimal correction essential. Explain such a correction before making it.
- Keep the existing micro-template syntax compatible with `site/lib/renderer.js`. Do not introduce template syntax that the current renderer cannot process.
- Keep all browser-side code compatible with the WebView2 runtime used by this project. Do not assume support for APIs or CSS features without checking the existing runtime target.
- Do not add dependencies, frameworks, icon packages, map SDKs, chart libraries, build tools, or external CDNs. Prefer existing assets and vanilla HTML/CSS/JavaScript.
- Do not use external network resources for fonts, images, scripts, stylesheets, or maps. The kiosk must remain reliable offline / in its current local-host environment.
- Keep JavaScript behavior resilient: no uncaught errors, no duplicate event handlers after refresh, and no interactions that silently fail.

## 4. Visual direction

Use the supplied ELECTRA screenshots as visual inspiration for quality and composition:

- Premium dark interface with deep aubergine / near-black backgrounds.
- Layered purple panels and restrained violet-to-magenta gradients.
- Neon violet, magenta, and a small amount of cyan for emphasis and system-state cues.
- Subtle borders, controlled glow, soft shadows, rounded corners, and clear visual hierarchy.
- Compact but readable navigation, consistent spacing, carefully aligned cards, refined tables, and polished form controls.
- Strong typography pairing: a legible UI sans-serif for normal content and a restrained monospace treatment for technical identifiers, telemetry, timestamps, and audit data. Use local/system font fallbacks.
- Hover, focus, active, selected, loading, empty, and disabled states should feel intentional.
- Use motion sparingly: short transitions and subtle feedback only. Respect `prefers-reduced-motion`.
- Maintain sufficient contrast and visible keyboard focus. Do not communicate status by color alone.

### AEGIS identity must remain intact

Retain the AEGIS name, municipal-defense purpose, and the existing sections:
- Overview
- TrafficGrid
- PowerCore
- CitizenVault
- Security Center

Keep the meaning of the current information:
- Overview: subsystem health, grid load, citizen-vault summary, threat posture, recent audit trail.
- TrafficGrid: intersections, signal state, congestion, emergency preemption, roadway incidents, and any existing traffic controls.
- PowerCore: monitored zones, current draw, capacity, status, and power-allocation requests.
- CitizenVault: citizen registry, clearance requirements, protected identifiers, inspection actions, and access restrictions.
- Security Center: threat condition, isolation status, audit/event stream, and security information.

Do not copy ELECTRA-specific EV concepts such as cars, charge goals, charging stations, battery capacity, or EV trip planning into AEGIS.

## 5. Interaction and usability requirements

- Make navigation, cards, tables, filters, buttons, dialogs, and forms visually consistent.
- Preserve existing actions and connect redesigned controls to the existing behavior.
- Do not create decorative controls that look functional but do nothing.
- Where an existing action is unavailable, show a clearly disabled or informational state rather than pretending it works.
- Ensure tables remain usable on narrower screens (for example, with deliberate horizontal overflow rather than broken columns).
- Make the layout adapt to the kiosk viewport and common desktop resolutions. Avoid fixed widths/heights that cause clipping in WebView2.
- Keep critical operational information visible and scannable. Do not bury threat state, offline subsystems, critical congestion, or protected-system warnings under decorative effects.
- Add clear empty, loading, and error states only where the current frontend can determine those states truthfully.
- Provide accessible labels and semantic buttons/links. Ensure keyboard navigation works.

## 6. Safe implementation workflow

Follow this sequence:

1. **Inspect first.** Read all permitted view/CSS/JS files and inspect the protected route/renderer files in read-only mode to understand the data contract and template syntax. Do not edit protected files.
2. **Map dependencies.** Identify which IDs, classes, attributes, forms, and DOM structures are referenced by `site.js` or supplied by server routes. Record them before changing markup.
3. **Establish a baseline.** Note the current page routes, visible sections, existing interactions, and any console/runtime errors.
4. **Plan the redesign.** Briefly state the intended shared layout, page-specific changes, and the exact permitted files to be touched.
5. **Implement incrementally.** Start with `layout.html` and `site.css`, then update individual page templates, and modify `site.js` only when needed for frontend interactions. Keep each change small and reversible.
6. **Validate every page.** Check Overview, TrafficGrid, PowerCore, CitizenVault, Security Center, Login, and Compromised views. Verify navigation, filtering, inspection, authentication flows, existing controls, and dynamic updates.
7. **Check responsive behavior.** Test the actual kiosk/WebView2 viewport and narrower widths. Check for horizontal page overflow, clipped content, overlapping controls, and unreadable tables.
8. **Review the diff.** Confirm that every changed path is on the permitted list. Confirm there are no changes to backend, integration, CODE pane, AI pane, data, configuration, or dependencies.
9. **Report honestly.** Summarize changed files, preserved functionality, validation performed, known limitations, and any issue that requires permission to touch a protected file.

## 7. Stop conditions

Stop without editing further if:
- The current templates or renderer contract are unclear.
- A desired behavior requires backend/API/authentication changes.
- A design request requires changes to the kiosk host or WebView2 integration.
- A required dependency or external service is not already present.
- A protected file appears broken or incompatible.
- You cannot confidently preserve an existing interaction or data binding.

In these cases, explain the issue and request authorization. Never silently widen the scope.

## 8. Definition of done

The work is complete only when:

- The AEGIS Web pane has a cohesive, premium dark-purple dashboard design inspired by the ELECTRA references.
- All five primary AEGIS sections plus Login and Compromised views remain present and visually coherent.
- Existing backend-driven values and template bindings still render correctly.
- Existing routes, authentication, API calls, forms, controls, and security behavior remain intact.
- The UI is usable with mouse and keyboard and adapts to the kiosk viewport.
- No CODE pane, AI pane, backend route, server, renderer, WPF, WebView2 integration, configuration, dependency, or unrelated file has been modified.
- The final diff contains changes only to the nine permitted files listed in Section 2.

**Final instruction:** Preserve function first, improve presentation second. If uncertain, do not modify the protected surface.
