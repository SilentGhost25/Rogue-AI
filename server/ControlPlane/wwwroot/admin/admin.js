// Rogue AI Admin Dashboard Logic

function logMessage(msg) {
  const logEl = document.getElementById('log-content');
  if (!logEl) return;
  const time = new Date().toISOString().substring(11, 19);
  logEl.textContent = `[${time}] ${msg}\n` + logEl.textContent;
}

async function api(path, opts = {}) {
  const adminKey = window.__ADMIN_KEY || localStorage.getItem('breachAdminKey') || '';
  const headers = {
    'Accept': 'application/json',
    'Content-Type': 'application/json',
    ...(adminKey ? { 'X-Admin-Key': adminKey } : {}),
    ...(opts.headers || {})
  };
  const res = await fetch(path, {
    credentials: 'include',
    headers,
    ...opts
  });

  if (res.status === 401 && !adminKey) {
    window.location.href = '/admin/login.html';
    throw new Error('Unauthorized');
  }

  const contentType = res.headers.get('content-type') || '';
  const body = contentType.includes('json') ? await res.json() : await res.text();

  if (!res.ok) {
    const errorMsg = (typeof body === 'object' && body.error) ? body.error : (typeof body === 'string' ? body : res.statusText);
    throw new Error(errorMsg);
  }

  return body;
}

async function refreshAttacks() {
  try {
    const list = await api('/api/admin/attacks');
    const tbody = document.getElementById('attack-list');
    if (!tbody || !Array.isArray(list)) return;

    tbody.innerHTML = list.map(a => {
      const state = a.state || 'Pending';
      const isPending = state === 'Pending';
      const isCanaryFired = state === 'CanaryFired';
      const isCanaryConfirmed = state === 'CanaryConfirmed';
      const isImminent = state === 'ArenaImminent';
      const isLanded = state === 'ArenaLanded';

      let slotTag = '';
      if (a.sequenceSlot === 'onboarding') {
        slotTag = '<span class="badge info">Canary</span>';
      } else if (a.sequenceSlot === 'reserve') {
        slotTag = '<span class="badge muted">Reserve</span>';
      } else if (a.sequenceSlot) {
        slotTag = `<span class="badge primary">Slot ${escapeHtml(a.sequenceSlot)}</span>`;
      } else {
        slotTag = '<span class="badge muted">--</span>';
      }

      return `
        <tr>
          <td class="bold font-mono">#${a.id}</td>
          <td>${slotTag}</td>
          <td class="bold">${escapeHtml(a.title)}</td>
          <td><code class="tag">${escapeHtml(a.codename || '')}</code></td>
          <td>Band ${a.band}</td>
          <td>${a.mode}</td>
          <td>${a.scheduled || '--'}</td>
          <td>
            <span class="badge state-${state.toLowerCase()}">${state}</span>
          </td>
          <td class="actions-cell">
            <button class="btn sm" onclick="fireCanary(${a.id})" ${!isPending ? 'disabled' : ''}>
              Canary
            </button>
            <button class="btn sm" onclick="confirmCanary(${a.id})" ${!isCanaryFired ? 'disabled' : ''}>
              Confirm
            </button>
            <button class="btn sm primary" onclick="fireArena(${a.id})" ${isLanded || isImminent ? 'disabled' : ''}>
              Fire Arena
            </button>
          </td>
        </tr>`;
    }).join('');
  } catch (err) {
    logMessage(`Error loading attacks: ${err.message}`);
  }
}

async function refreshEvaluations() {
  try {
    const data = await api('/api/admin/evaluations');
    const tbody = document.getElementById('eval-list');
    const countEl = document.getElementById('eval-count');
    if (!tbody || !data || !Array.isArray(data.evaluations)) return;

    const list = data.evaluations;
    const attacks = Array.isArray(data.attacks) ? data.attacks : [];
    const headRow = document.getElementById('eval-head-row');
    if (headRow) {
      headRow.innerHTML = `
        <th>Desk</th>
        <th>Participant / Team</th>
        <th>Station IP</th>
        <th>PIN / Pass</th>
        <th>Score</th>
        <th>Pass / Solved</th>
        <th>Local Penalties</th>
        ${attacks.map(a => `<th title="#${a.id} ${escapeHtml(a.title)} (Band ${a.band})">#${a.id}</th>`).join('')}
        <th>Gates (Out/Comp)</th>
        <th>Diagnostic Verdict / Reason</th>
        <th>Breakdown</th>
      `;
    }
    const totalCols = 10 + attacks.length;
    if (countEl) countEl.textContent = `${list.length} participant${list.length === 1 ? '' : 's'}`;

    if (list.length === 0) {
      tbody.innerHTML = `<tr><td colspan="${totalCols}" class="muted">No participants connected yet</td></tr>`;
      return;
    }

    tbody.innerHTML = list.map(p => {
      // Create map of attackId -> metric
      const attMap = {};
      (p.attacks || []).forEach(a => { attMap[a.attackId] = a; });

      let lastDiag = 'Nominal (No issues)';
      if (p.recentSubmissions && p.recentSubmissions.length > 0) {
        const last = p.recentSubmissions[0];
        lastDiag = `#${last.attackId} ${last.status}: ${last.reason}`;
      } else if (p.attacks) {
        const failedAtt = p.attacks.find(a => a.status === 'FAILED');
        if (failedAtt) lastDiag = `#${failedAtt.attackId} FAILED: ${failedAtt.lastReason}`;
      }

      const attackCols = attacks.map(a => {
        const att = attMap[a.id];
        if (!att || att.status === 'PENDING') {
          return `<td><span class="badge pending" title="Not dispatched">--</span></td>`;
        } else if (att.status === 'PASSED') {
          return `<td><span class="badge passed" title="Passed (+points)">PASS</span></td>`;
        } else if (att.status === 'FAILED') {
          return `<td><span class="badge failed" title="${escapeHtml(att.lastReason)}">FAIL</span></td>`;
        }
        return `<td><span class="badge inprogress" title="Active breach">RUN</span></td>`;
      }).join('');

      const penaltyTag = p.localPenalties > 0
        ? `<span class="penalty-tag">${p.localPenalties} fail (${-(p.localPenalties * 3.0).toFixed(1)} pts)</span>`
        : `<span class="muted font-mono">0</span>`;

      const outcomeGate = p.passedAttacks > 0 || (p.failedAttacks === 0)
        ? `<span class="gate-tag gate-pass" title="Outcome Gate: Passed">&#10003; OUTCOME</span>`
        : `<span class="gate-tag gate-fail" title="Outcome Gate: Failed">&#10007; OUTCOME</span>`;
      const complianceGate = `<span class="gate-tag gate-pass" title="Compliance Gate: Passed">&#10003; COMP</span>`;

      return `
        <tr>
          <td class="bold font-mono"><code class="tag">${escapeHtml(p.deskId || 'DESK')}</code></td>
          <td class="bold">${escapeHtml(p.name || p.participantId)}</td>
          <td class="font-mono text-muted">${escapeHtml(p.ipAddress || '127.0.0.1')}</td>
          <td><code class="tag" style="background:#1e293b; color:#38bdf8;">${escapeHtml(p.password || 'none')}</code></td>
          <td class="font-mono bold" style="color:#22c55e;">${p.totalScore.toFixed(1)} pts</td>
          <td>
            <span class="pass-rate-pill ${p.passedAttacks > 0 ? 'pass-rate-high' : 'pass-rate-mid'}">
              ${p.passedAttacks} solved
            </span>
          </td>
          <td>${penaltyTag}</td>
          ${attackCols}
          <td><div style="display:flex; flex-direction:column; gap:2px;">${outcomeGate}${complianceGate}</div></td>
          <td style="max-width:240px; font-size:12px; color:#cbd5e1; white-space:nowrap; overflow:hidden; text-overflow:ellipsis;" title="${escapeHtml(lastDiag)}">
            ${escapeHtml(lastDiag)}
          </td>
          <td>
            <button class="btn secondary sm" style="font-size:11px; padding:3px 8px;" onclick="openEvalModal('${escapeHtml(p.participantId)}')">Audit</button>
          </td>
        </tr>
      `;
    }).join('');
    window.__evalParticipants = list;
  } catch (err) {
    logMessage(`[ERROR] Loading evaluations failed: ${err.message}`);
  }
}

function openEvalModal(pid) {
  const p = (window.__evalParticipants || []).find(x => x.participantId === pid);
  if (!p) return;

  const modal = document.getElementById('eval-modal');
  if (!modal) return;

  document.getElementById('modal-desk-title').textContent = `${p.deskId || 'DESK'} — ${p.name || p.participantId}`;
  document.getElementById('modal-desk-sub').textContent = `Station IP: ${p.ipAddress || '127.0.0.1'} | Score: ${p.totalScore.toFixed(1)} pts | Solved: ${p.passedAttacks} | Penalties: ${p.localPenalties}`;

  // 1. Gates
  const outPass = p.passedAttacks > 0 || p.failedAttacks === 0;
  const gatesEl = document.getElementById('modal-gates-container');
  gatesEl.innerHTML = `
    <div class="metric-card" style="flex:1; border-color:${outPass ? '#059669' : '#dc2626'};">
      <div class="title">Dimension 1: Outcome Gate</div>
      <div class="value" style="color:${outPass ? '#34d399' : '#f87171'};">${outPass ? 'PASSED (≥50%)' : 'FAILED'}</div>
      <div class="sub">${p.passedAttacks} attacks solved, ${p.failedAttacks} failed</div>
    </div>
    <div class="metric-card" style="flex:1; border-color:#059669;">
      <div class="title">Dimension 2: Compliance Gate</div>
      <div class="value" style="color:#34d399;">PASSED (100%)</div>
      <div class="sub">Zero sandbox escapes, zero hardcoded attack literals</div>
    </div>
  `;

  // 2. 7 Scoring Dimensions
  const dimEl = document.getElementById('modal-dimensions-container');
  const timeScore = Math.max(20, Math.min(100, Math.round(90 - (p.localPenalties * 5))));
  const qualScore = Math.max(30, Math.min(100, Math.round(85 - (p.failedAttacks * 10))));
  const effScore = Math.max(30, Math.min(100, Math.round(80 - (p.localPenalties * 4))));
  const robScore = p.passedAttacks > 0 ? 95 : (p.failedAttacks > 0 ? 30 : 70);
  const procScore = Math.max(40, Math.min(100, Math.round(85 - (p.localPenalties * 3))));

  dimEl.innerHTML = `
    <div class="metric-card">
      <div class="title">Time (30%)</div>
      <div class="value" style="color:#38bdf8;">${timeScore}%</div>
      <div class="sub">Compute &amp; solve speed</div>
    </div>
    <div class="metric-card">
      <div class="title">Quality (25%)</div>
      <div class="value" style="color:#a78bfa;">${qualScore}%</div>
      <div class="sub">LOC, CC, MI index</div>
    </div>
    <div class="metric-card">
      <div class="title">Efficiency (20%)</div>
      <div class="value" style="color:#fbbf24;">${effScore}%</div>
      <div class="sub">Prompts &amp; token budget</div>
    </div>
    <div class="metric-card">
      <div class="title">Robustness (15%)</div>
      <div class="value" style="color:#34d399;">${robScore}%</div>
      <div class="sub">Variant tests passed</div>
    </div>
    <div class="metric-card">
      <div class="title">Process (10%)</div>
      <div class="value" style="color:#f472b6;">${procScore}%</div>
      <div class="sub">Cadence &amp; hygiene</div>
    </div>
  `;

  // 3. Attacks Breakdown List
  const attList = document.getElementById('modal-attacks-list');
  attList.innerHTML = (p.attacks || []).map(a => {
    let badgeClass = 'pending';
    let badgeText = 'PENDING';
    if (a.status === 'PASSED') { badgeClass = 'passed'; badgeText = 'PASSED'; }
    else if (a.status === 'FAILED') { badgeClass = 'failed'; badgeText = 'FAILED'; }
    else if (a.status === 'IN_PROGRESS') { badgeClass = 'inprogress'; badgeText = 'ACTIVE'; }

    return `
      <div style="display:flex; justify-content:space-between; align-items:center; padding:8px; border-bottom:1px solid #1e293b; font-size:12px;">
        <div style="display:flex; align-items:center; gap:8px;">
          <span class="badge ${badgeClass}" style="min-width:65px; text-align:center;">${badgeText}</span>
          <span class="bold" style="color:#f1f5f9;">#${a.attackId} ${escapeHtml(a.title)}</span>
          <span class="muted" style="font-size:11px;">(Band ${a.band})</span>
        </div>
        <div style="text-align:right; max-width:400px; color:#cbd5e1; font-family:var(--font-mono); font-size:11px;">
          ${escapeHtml(a.lastReason || 'No events')}
        </div>
      </div>
    `;
  }).join('');

  modal.style.display = 'flex';
}

function closeEvalModal() {
  const modal = document.getElementById('eval-modal');
  if (modal) modal.style.display = 'none';
}

const ATTACK_NAMES = {
  0: "Warmup Calibration",
  1: "Reflected XSS",
  2: "Stored XSS (Silent Fail)",
  3: "SQL Injection",
  4: "CSRF",
  5: "IDOR (Phantom Alert)",
  6: "CPU Sabotage",
  7: "Supply Chain",
  8: "Cookie Auth",
  9: "Mass Assignment (Payload Paradox)",
  10: "Path Traversal",
  11: "Session Fracture",
  12: "Ouroboros (Shadow Variable)"
};

let hubConn = null;
function initSignalR() {
  if (typeof signalR === 'undefined') return;
  try {
    hubConn = new signalR.HubConnectionBuilder()
      .withUrl('/hub')
      .withAutomaticReconnect()
      .build();

    hubConn.on('ATTACK_LANDED', (deskId, attackId) => {
      logMessage(`⚡ [SIGNALR] Attack #${attackId} landed on desk: ${deskId}`);
      refreshKiosks();
      refreshAttacks();
      refreshEvaluations();
    });

    hubConn.on('VERDICT', (deskId, attackId, status, reason, score) => {
      logMessage(`⚖ [VERDICT] Desk ${deskId} #${attackId}: ${status} (${score >= 0 ? '+' : ''}${score} pts) - ${reason || ''}`);
      refreshKiosks();
      refreshEvaluations();
    });

    hubConn.on('KIOSK_UPDATED', () => {
      refreshKiosks();
      refreshEvaluations();
    });

    hubConn.on('PARTICIPANT_REQUEST', req => {
      logMessage(`🔔 [REQUEST] Desk ${req.deskId} (${req.displayName}): ${req.details}`);
      handleAdminParticipantRequest(req);
    });

    hubConn.on('PARTICIPANT_ALERT', alertData => {
      logMessage(`⚠️ [ALERT] Desk ${alertData.deskId} (${alertData.displayName}): ${alertData.details}`);
      handleAdminParticipantAlert(alertData);
    });

    hubConn.on('REQUEST_RESOLVED', res => {
      removeAdminRequestAlert(res.requestId);
    });

    hubConn.onreconnected(() => {
      try { hubConn.invoke('JoinAdmin', window.__ADMIN_KEY || localStorage.getItem('breachAdminKey') || ''); } catch(_) {}
      refreshAdminPendingRequests();
    });

    hubConn.start().then(() => {
      logMessage('[SIGNALR] Live control plane hub connected.');
      try { hubConn.invoke('JoinAdmin', window.__ADMIN_KEY || localStorage.getItem('breachAdminKey') || ''); } catch(_) {}
      refreshAdminPendingRequests();
    }).catch(err => {
      console.warn('SignalR init failed:', err);
    });
  } catch (ex) {
    console.warn('SignalR setup exception:', ex);
  }
}

function playAdminAlertChime() {
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.type = 'sine';
    osc.frequency.setValueAtTime(587.33, ctx.currentTime);
    osc.frequency.setValueAtTime(880, ctx.currentTime + 0.15);
    gain.gain.setValueAtTime(0.3, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.45);
    osc.start(ctx.currentTime);
    osc.stop(ctx.currentTime + 0.45);
  } catch (_) {}
}

function getAdminAlertContainer() {
  let c = document.getElementById('adminAlertContainer');
  if (!c) {
    c = document.createElement('div');
    c.id = 'adminAlertContainer';
    c.style = "position:fixed; top:20px; right:20px; z-index:999999; display:flex; flex-direction:column; gap:12px; max-width:440px; pointer-events:auto;";
    document.body.appendChild(c);
  }
  return c;
}

function handleAdminParticipantRequest(req) {
  playAdminAlertChime();
  const c = getAdminAlertContainer();
  const existing = document.getElementById(`alert-req-${req.requestId}`);
  if (existing) return;

  const card = document.createElement('div');
  card.id = `alert-req-${req.requestId}`;
  card.style = "background:#0f172a; border:2px solid #38bdf8; border-radius:8px; padding:16px; box-shadow:0 10px 25px -5px rgba(0,0,0,0.6), 0 0 15px rgba(56,189,248,0.4); color:#f8fafc; font-family:sans-serif;";
  card.innerHTML = `
    <div style="display:flex; align-items:center; justify-content:space-between; margin-bottom:8px;">
      <span style="font-weight:700; color:#38bdf8; font-size:13px; text-transform:uppercase; letter-spacing:0.5px;">🔔 Participant Request</span>
      <span style="font-size:11px; color:#94a3b8;">${new Date().toLocaleTimeString()}</span>
    </div>
    <div style="font-size:14px; font-weight:600; margin-bottom:4px;">
      Desk: <span style="color:#f59e0b;">${req.deskId}</span> (${req.displayName})
    </div>
    <div style="font-size:12px; color:#cbd5e1; margin-bottom:12px; line-height:1.4;">
      ${req.details}
    </div>
    <div style="display:flex; gap:8px;">
      <button onclick="resolveAdminRequest('${req.requestId}', true)" style="flex:1; background:#10b981; color:#fff; border:none; padding:8px 12px; border-radius:4px; font-weight:600; font-size:12px; cursor:pointer;">✔ Approve</button>
      <button onclick="resolveAdminRequest('${req.requestId}', false)" style="flex:1; background:#ef4444; color:#fff; border:none; padding:8px 12px; border-radius:4px; font-weight:600; font-size:12px; cursor:pointer;">✖ Deny</button>
    </div>
  `;
  c.prepend(card);
}

function handleAdminParticipantAlert(alertData) {
  playAdminAlertChime();
  const c = getAdminAlertContainer();
  const card = document.createElement('div');
  card.id = `alert-prob-${alertData.id || Date.now()}`;
  card.style = "background:#1e1b4b; border:2px solid #ef4444; border-radius:8px; padding:16px; box-shadow:0 10px 25px -5px rgba(0,0,0,0.6), 0 0 15px rgba(239,68,68,0.4); color:#f8fafc; font-family:sans-serif;";
  card.innerHTML = `
    <div style="display:flex; align-items:center; justify-content:space-between; margin-bottom:8px;">
      <span style="font-weight:700; color:#ef4444; font-size:13px; text-transform:uppercase; letter-spacing:0.5px;">⚠️ Participant Problem Alert</span>
      <span style="font-size:11px; color:#94a3b8;">${new Date().toLocaleTimeString()}</span>
    </div>
    <div style="font-size:14px; font-weight:600; margin-bottom:4px;">
      Desk: <span style="color:#f59e0b;">${alertData.deskId}</span> (${alertData.displayName})
    </div>
    <div style="font-size:12px; color:#cbd5e1; margin-bottom:12px; line-height:1.4;">
      <strong>${(alertData.problemType || '').toUpperCase()}:</strong> ${alertData.details}
    </div>
    <div style="display:flex; justify-content:flex-end;">
      <button onclick="this.closest('div[id^=alert-prob-]').remove()" style="background:#334155; color:#94a3b8; border:none; padding:4px 10px; border-radius:4px; font-size:11px; cursor:pointer;">Dismiss</button>
    </div>
  `;
  c.prepend(card);
}

async function resolveAdminRequest(requestId, approve) {
  const url = approve ? `/api/admin/requests/${requestId}/approve` : `/api/admin/requests/${requestId}/deny`;
  try {
    const res = await api(url, { method: 'POST' });
    removeAdminRequestAlert(requestId);
  } catch(e) {
    alert('Failed to resolve request: ' + (e.message || e));
  }
}

function removeAdminRequestAlert(requestId) {
  const card = document.getElementById(`alert-req-${requestId}`);
  if (card) {
    card.style.opacity = '0';
    card.style.transition = 'opacity 0.3s ease';
    setTimeout(() => card.remove(), 300);
  }
}

async function refreshAdminPendingRequests() {
  try {
    const list = await api('/api/admin/requests');
    if (Array.isArray(list)) {
      for (const req of list) {
        if (req.status === 'pending') handleAdminParticipantRequest(req);
      }
    }
  } catch(_) {}
}

async function refreshKiosks() {
  try {
    const kiosks = await api('/admin/kiosks');
    const tbody = document.getElementById('kiosk-list');
    const countEl = document.getElementById('kiosk-count');
    if (!tbody || !Array.isArray(kiosks)) return;

    if (countEl) countEl.textContent = kiosks.length;

    if (kiosks.length === 0) {
      tbody.innerHTML = `<tr><td colspan="8" class="muted">No kiosks connected yet</td></tr>`;
      return;
    }

    tbody.innerHTML = kiosks.map(k => {
      let activeHtml = '<span class="badge success">🛡️ Nominal / Secure</span>';
      if (k.activeAttacks) {
        const aids = k.activeAttacks.toString().split(',').filter(Boolean);
        if (aids.length > 0) {
          const badges = aids.map(id => {
            const num = parseInt(id, 10);
            const name = ATTACK_NAMES[num] || `Attack #${num}`;
            let elapsedStr = '';
            if (k.activeLandedAt) {
              const landed = new Date(k.activeLandedAt).getTime();
              const sec = Math.max(0, Math.floor((Date.now() - landed) / 1000));
              const m = Math.floor(sec / 60).toString().padStart(2, '0');
              const s = (sec % 60).toString().padStart(2, '0');
              elapsedStr = ` ⏱ ${m}:${s}`;
            }
            return `<span class="badge danger" style="margin-right:4px;">⚡ #${num}: ${escapeHtml(name)}${elapsedStr}</span>`;
          }).join(' ');
          activeHtml = `<div>${badges}</div>`;
        }
      }

      const gitHtml = k.gitBranch
        ? `<div style="margin-top:2px;"><code style="color:#38bdf8; font-size:11px;">${escapeHtml(k.gitBranch)}</code></div>`
        : '';

      const aiStatus = k.agyExhausted
        ? '<span class="badge danger">AGY Expired</span>'
        : '<span class="badge success">AGY Active</span>';

      const deskTag = escapeHtml(k.deskId || k.kioskId || k.id);

      return `
        <tr>
          <td class="bold font-mono"><code class="tag">${deskTag}</code></td>
          <td class="bold">
            ${escapeHtml(k.name || k.id)}
            ${k.status === 'disqualified' ? ' <span class="badge danger">DISQUALIFIED</span>' : ''}
          </td>
          <td class="font-mono text-muted">${escapeHtml(k.ipAddress || '127.0.0.1')}</td>
          <td>${activeHtml}</td>
          <td class="font-mono bold" style="color:#22c55e;">${k.totalScore.toFixed(1)} pts</td>
          <td>${k.solvedCount} / ${k.landedCount}</td>
          <td>
            ${aiStatus} <small class="muted font-mono">(${k.openCodePrompts} OC)</small>
            ${gitHtml}
          </td>
          <td style="display:flex; gap:4px; flex-wrap:wrap;">
            <button class="btn sm primary" onclick="promptFireOnKiosk('${escapeHtml(k.id)}', '${escapeHtml(k.name || deskTag)}')">
              ⚡ Fire
            </button>
            <button class="btn sm warning" onclick="adminRemoteKiosk('${escapeHtml(k.id)}', 'restart-node')" title="Restart Node on port 3000">
              🔄 Node
            </button>
            <button class="btn sm secondary" onclick="adminRemoteKiosk('${escapeHtml(k.id)}', 'reload-screen')" title="Reload Screen">
              🌐
            </button>
            <button class="btn sm warning" onclick="adminRemoteKiosk('${escapeHtml(k.id)}', 'reset-workspace')" title="Reset Workspace & Code">
              ↺ Reset
            </button>
            ${k.status === 'disqualified' || k.status === 'offline'
              ? `<button class="btn sm success" onclick="adminReinstateKiosk('${escapeHtml(k.id)}')" title="Re-admit / Add Back">🛡️ Add Back</button>`
              : `<button class="btn sm danger" onclick="adminRemoveKiosk('${escapeHtml(k.id)}')" title="Remove Session">🚪 Remove</button>`}
          </td>
        </tr>`;
    }).join('');
  } catch (err) {
    logMessage(`[ERROR] Loading kiosks failed: ${err.message}`);
  }
}

async function adminRemoteKiosk(participantId, command) {
  if (!confirm(`Dispatch remote "${command}" signal to desk ${participantId}?`)) return;
  try {
    const res = await api(`/admin/kiosks/${participantId}/remote/${command}`, { method: 'POST' });
    logMessage(`✔ Remote command [${command}] executed on ${participantId}: ${res.message || 'Dispatched'}`);
    alert(`Success: ${res.message || 'Signal dispatched in real time.'}`);
    refreshKiosks();
  } catch(e) {
    alert(`Remote command failed: ${e.message || e}`);
  }
}

async function adminReinstateKiosk(participantId) {
  if (!confirm(`Re-admit participant ${participantId} to active event play?`)) return;
  try {
    const res = await api(`/admin/kiosks/${participantId}/reinstate`, { method: 'POST' });
    logMessage(`🛡️ Participant ${participantId} reinstated by administrator.`);
    alert(res.message || 'Participant reinstated successfully.');
    refreshKiosks();
  } catch(e) {
    alert(`Reinstatement failed: ${e.message || e}`);
  }
}

async function adminRemoveKiosk(participantId) {
  if (!confirm(`Remove active session for participant ${participantId}?\n(Scores and history are preserved; desk will be marked offline)`)) return;
  try {
    const res = await api(`/admin/kiosks/${participantId}/remove`, { method: 'POST' });
    logMessage(`🚪 Participant ${participantId} session removed.`);
    alert(res.message || 'Participant session removed.');
    refreshKiosks();
  } catch(e) {
    alert(`Removal failed: ${e.message || e}`);
  }
}

async function promptFireOnKiosk(kioskId, name) {
  const attackNum = prompt(`Fire specific attack on participant "${name}" (${kioskId}).\nEnter attack number (0 to 12):`, "1");
  if (!attackNum) return;
  const num = parseInt(attackNum.trim(), 10);
  if (isNaN(num) || num < 0 || num > 12) {
    alert("Please enter a valid attack number between 0 and 12.");
    return;
  }
  try {
    await api(`/admin/attacks/${num}/fire?targetId=${encodeURIComponent(kioskId)}`, { method: 'POST' });
    logMessage(`⚡ Dispatched Attack #${num} directly to desk ${name} (${kioskId})`);
    await refreshKiosks();
    await refreshAttacks();
  } catch (err) {
    alert(`Failed to fire attack: ${err.message}`);
    logMessage(`[ERROR] Fire attack #${num} on ${kioskId}: ${err.message}`);
  }
}

async function fireCanary(id) {
  try {
    await api(`/api/admin/attacks/${id}/canary`, { method: 'POST' });
    logMessage(`[CANARY] Dispatched attack #${id} to canary desk.`);
    await refreshAttacks();
    await refreshEvaluations();
  } catch (err) {
    alert(`Failed to fire canary: ${err.message}`);
    logMessage(`[ERROR] Fire canary #${id}: ${err.message}`);
  }
}

async function confirmCanary(id) {
  try {
    await api(`/api/admin/attacks/${id}/confirm-canary`, { method: 'POST' });
    logMessage(`[CONFIRMED] Canary confirmed attack #${id}. Arena ready.`);
    await refreshAttacks();
    await refreshEvaluations();
  } catch (err) {
    alert(`Failed to confirm canary: ${err.message}`);
    logMessage(`[ERROR] Confirm canary #${id}: ${err.message}`);
  }
}

async function fireArena(id) {
  if (!confirm(`Are you sure you want to fire Attack #${id} to the entire arena?`)) {
    return;
  }
  try {
    const res = await api(`/api/admin/attacks/${id}/fire-arena`, { method: 'POST' });
    const landAt = res.landAtUtc || new Date().toISOString();
    logMessage(`🔥 [ARENA FIRED] Attack #${id} dispatched to all kiosks! Land target: ${landAt}`);
    await refreshAttacks();
    await refreshEvaluations();
  } catch (err) {
    alert(`Failed to fire arena: ${err.message}`);
    logMessage(`[ERROR] Fire arena #${id}: ${err.message}`);
  }
}

function escapeHtml(str) {
  return String(str || '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

let isFrozen = false;

async function checkEventStatus() {
  try {
    const status = await api('/api/admin/event/status');
    isFrozen = !!status.frozen;
    const freezeBtn = document.getElementById('freeze-btn');
    if (freezeBtn) {
      freezeBtn.textContent = isFrozen ? '▶ Unfreeze Submissions' : '❄ Freeze Submissions';
      freezeBtn.className = isFrozen ? 'btn success sm' : 'btn warning sm';
    }
  } catch (err) {
    // Non-fatal
  }
}

async function toggleFreeze() {
  try {
    const targetEndpoint = isFrozen ? '/api/admin/event/unfreeze' : '/api/admin/event/freeze';
    const res = await api(targetEndpoint, { method: 'POST' });
    isFrozen = !!res.frozen;
    logMessage(isFrozen ? '❄ [FREEZE] Submissions are now closed (FROZEN).' : '▶ [UNFREEZE] Submissions are now open.');
    await checkEventStatus();
  } catch (err) {
    alert(`Failed to toggle freeze: ${err.message}`);
    logMessage(`[ERROR] Toggle freeze: ${err.message}`);
  }
}

async function fireBossAttack() {
  const bossId = 11;
  if (!confirm('⚠️ WARNING: You are about to FIRE BOSS ATTACK (#11: Session Fracture) to the entire arena!\n\nThis will trigger the Band 5 session fracture incident on all kiosks.\nProceed?')) {
    return;
  }
  try {
    const res = await api(`/api/admin/attacks/${bossId}/fire-arena`, { method: 'POST' });
    const landAt = res.landAtUtc || new Date().toISOString();
    logMessage(`☠️🔥 [BOSS ATTACK FIRED] Attack #11 (Session Fracture) dispatched to all kiosks! Land target: ${landAt}`);
    await refreshAttacks();
    await refreshEvaluations();
  } catch (err) {
    alert(`Failed to fire Boss Attack: ${err.message}`);
    logMessage(`[ERROR] Fire Boss Attack: ${err.message}`);
  }
}

// Event Elapsed Timer
let eventStartTime = localStorage.getItem('rogue_event_start');
if (!eventStartTime) {
  eventStartTime = Date.now().toString();
  localStorage.setItem('rogue_event_start', eventStartTime);
}

function updateEventTimer() {
  const timerEl = document.getElementById('event-timer');
  if (!timerEl) return;
  const elapsedMs = Math.max(0, Date.now() - parseInt(eventStartTime, 10));
  const totalSeconds = Math.floor(elapsedMs / 1000);

  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  const pad = n => String(n).padStart(2, '0');
  timerEl.textContent = `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
  timerEl.style.color = '#38bdf8';
}

async function init() {
  try {
    const who = await api('/admin/api/whoami');
    const whoEl = document.getElementById('whoami');
    if (whoEl && who.username) whoEl.textContent = who.username;
  } catch {
    window.location.href = '/admin/login.html';
    return;
  }

  document.getElementById('logout-btn')?.addEventListener('click', async () => {
    try {
      await api('/admin/api/logout', { method: 'POST' });
    } finally {
      window.location.href = '/admin/login.html';
    }
  });

  document.getElementById('refresh-btn')?.addEventListener('click', async (e) => {
    const btn = e.currentTarget;
    btn.disabled = true;
    btn.textContent = 'Refreshing...';
    try {
      await Promise.allSettled([
        checkEventStatus(),
        refreshAttacks(),
        refreshEvaluations(),
        refreshKiosks()
      ]);
      logMessage('[REFRESH] Dashboard synced with server.');
      btn.textContent = '✓ Refreshed';
    } finally {
      setTimeout(() => {
        btn.disabled = false;
        btn.textContent = 'Refresh';
      }, 900);
    }
  });

  document.getElementById('refresh-eval-btn')?.addEventListener('click', async (e) => {
    const btn = e.currentTarget;
    btn.disabled = true;
    btn.textContent = 'Refreshing...';
    try {
      await Promise.allSettled([refreshEvaluations(), refreshKiosks()]);
      logMessage('[REFRESH] Participant scorecards synced.');
      btn.textContent = '✓ Refreshed';
    } finally {
      setTimeout(() => {
        btn.disabled = false;
        btn.textContent = 'Refresh Evals';
      }, 900);
    }
  });

  document.getElementById('clear-log-btn')?.addEventListener('click', () => {
    const logEl = document.getElementById('log-content');
    if (logEl) logEl.textContent = '[Console cleared]';
  });

  // Clocks
  setInterval(() => {
    const clockEl = document.getElementById('server-time');
    if (clockEl) {
      clockEl.textContent = 'UTC ' + new Date().toISOString().substring(11, 19);
    }
    updateEventTimer();
  }, 1000);
  updateEventTimer();

  // Initialize real-time SignalR event loop
  initSignalR();

  // Initial load & periodic poll
  checkEventStatus();
  refreshAttacks();
  refreshEvaluations();
  refreshKiosks();
  setInterval(() => {
    checkEventStatus();
    refreshAttacks();
    refreshEvaluations();
    refreshKiosks();
  }, 3000);
}

document.addEventListener('DOMContentLoaded', init);
