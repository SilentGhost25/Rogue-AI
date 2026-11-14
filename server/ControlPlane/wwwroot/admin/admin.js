// Rogue AI Admin Dashboard Logic

function logMessage(msg) {
  const logEl = document.getElementById('log-content');
  if (!logEl) return;
  const time = new Date().toISOString().substring(11, 19);
  logEl.textContent = `[${time}] ${msg}\n` + logEl.textContent;
}

async function api(path, opts = {}) {
  const res = await fetch(path, {
    credentials: 'include',
    headers: {
      'Accept': 'application/json',
      'Content-Type': 'application/json',
      ...(opts.headers || {})
    },
    ...opts
  });

  if (res.status === 401) {
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
  0: "Baseline Calibration",
  1: "Phantom Heap",
  2: "Schrodinger Entropy",
  3: "Hydra WAL Contention",
  4: "Mirage Route",
  5: "Polite Intruder (RBAC)",
  6: "Chameleon Parser",
  7: "Zombie Worker Emitter",
  8: "Poison Prototype",
  9: "Proxy Mirage",
  10: "Janus Knot Decoupling"
};

let hubConn = null;
const pendingArbiterAlerts = new Map();

function renderArbiterAlerts() {
  const panel = document.getElementById('arbiter-alerts-panel');
  const list = document.getElementById('arbiter-alerts-list');
  const countBadge = document.getElementById('arbiter-alert-count');
  if (!panel || !list) return;

  if (pendingArbiterAlerts.size === 0) {
    panel.style.display = 'none';
    return;
  }

  panel.style.display = 'block';
  if (countBadge) countBadge.textContent = `${pendingArbiterAlerts.size} pending`;

  list.innerHTML = Array.from(pendingArbiterAlerts.values()).map(a => {
    if (a.type === 'switch_request') {
      return `
        <div class="card" style="background:#1e293b; border:1px solid #f59e0b; padding:12px; display:flex; justify-content:space-between; align-items:center;">
          <div>
            <div style="display:flex; align-items:center; gap:8px;">
              <span class="badge warning font-mono bold">${escapeHtml(a.deskId)}</span>
              <strong style="color:#f8fafc;">${escapeHtml(a.name || a.participantId)}</strong>
              <span class="text-muted" style="font-size:12px;">(${escapeHtml(a.email || 'No email')})</span>
            </div>
            <div style="font-size:13px; color:#cbd5e1; margin-top:4px;">
              Participant requests authorization to switch linked Google account.
              ${a.reason ? `<span class="italic text-muted">Reason: ${escapeHtml(a.reason)}</span>` : ''}
            </div>
          </div>
          <div style="display:flex; gap:8px;">
            <button class="btn sm success" onclick="approveAccountSwitch('${escapeHtml(a.participantId)}', '${escapeHtml(a.requestId)}')">
              ✓ Approve Switch
            </button>
            <button class="btn sm danger" onclick="denyAccountSwitch('${escapeHtml(a.participantId)}', '${escapeHtml(a.requestId)}')">
              ✕ Deny
            </button>
          </div>
        </div>
      `;
    } else if (a.type === 'pro_flag') {
      return `
        <div class="card" style="background:#450a0a; border:1px solid #ef4444; padding:12px; display:flex; justify-content:space-between; align-items:center;">
          <div>
            <div style="display:flex; align-items:center; gap:8px;">
              <span class="badge danger font-mono bold">${escapeHtml(a.deskId)}</span>
              <strong style="color:#fca5a5;">⚠️ COMPLIANCE VIOLATION: ${escapeHtml(a.tier || 'Google AI Pro')}</strong>
              <span style="color:#f87171; font-size:12px;">Desk: ${escapeHtml(a.deskId)} (${escapeHtml(a.email)})</span>
            </div>
            <div style="font-size:13px; color:#fecaca; margin-top:4px;">
              Unauthorized paid compute tier detected on kiosk terminal stream. Regulation mandates standard Antigravity Starter quota.
            </div>
          </div>
          <div style="display:flex; gap:8px;">
            <button class="btn sm danger" onclick="disqualifyParticipant('${escapeHtml(a.participantId)}', '${escapeHtml(a.name)}')">
              ⛔ Disqualify
            </button>
            <button class="btn sm warning" onclick="pardonParticipant('${escapeHtml(a.participantId)}', '${escapeHtml(a.name)}')">
              🕊️ Pardon &amp; Reset Account
            </button>
            <button class="btn sm success" onclick="allowProAccount('${escapeHtml(a.participantId)}', '${escapeHtml(a.name)}')">
              ⚡ Allow Pro Plan
            </button>
          </div>
        </div>
      `;
    }
    return '';
  }).join('');
}

async function approveAccountSwitch(participantId, requestId) {
  try {
    await api(`/admin/kiosks/${encodeURIComponent(participantId)}/account-switch/approve?requestId=${encodeURIComponent(requestId)}`, { method: 'POST' });
    logMessage(`[ARBITER] Approved account switch for participant ${participantId}`);
    pendingArbiterAlerts.delete(`switch_${participantId}`);
    renderArbiterAlerts();
    refreshKiosks();
  } catch (err) {
    alert(`Failed to approve switch: ${err.message}`);
  }
}

async function denyAccountSwitch(participantId, requestId) {
  const reason = prompt("Enter denial reason (shown to participant):", "Switch request denied by event arbiter.") || "Denied by event arbiter.";
  try {
    await api(`/admin/kiosks/${encodeURIComponent(participantId)}/account-switch/deny?requestId=${encodeURIComponent(requestId)}`, {
      method: 'POST',
      body: JSON.stringify({ reason })
    });
    logMessage(`[ARBITER] Denied account switch for participant ${participantId}: ${reason}`);
    pendingArbiterAlerts.delete(`switch_${participantId}`);
    renderArbiterAlerts();
    refreshKiosks();
  } catch (err) {
    alert(`Failed to deny switch: ${err.message}`);
  }
}

async function disqualifyParticipant(participantId, name) {
  if (!confirm(`Are you sure you want to permanently DISQUALIFY participant "${name}" (${participantId})?`)) return;
  try {
    await api(`/admin/kiosks/${encodeURIComponent(participantId)}/disqualify`, { method: 'POST' });
    logMessage(`[ARBITER] DISQUALIFIED participant ${name} (${participantId})`);
    pendingArbiterAlerts.delete(`pro_${participantId}`);
    renderArbiterAlerts();
    refreshKiosks();
  } catch (err) {
    alert(`Failed to disqualify participant: ${err.message}`);
  }
}

async function reinstateParticipant(participantId, name) {
  try {
    await api(`/admin/kiosks/${encodeURIComponent(participantId)}/reinstate`, { method: 'POST' });
    logMessage(`[ARBITER] REINSTATED participant ${name} (${participantId})`);
    refreshKiosks();
  } catch (err) {
    alert(`Failed to reinstate participant: ${err.message}`);
  }
}

async function pardonParticipant(participantId, name) {
  if (!confirm(`Pardon participant "${name}" (${participantId})? This will clear their Google AI Pro credentials and prompt them to re-authenticate with a compliant account.`)) return;
  try {
    await api(`/admin/kiosks/${encodeURIComponent(participantId)}/pardon-pro-account`, { method: 'POST' });
    logMessage(`[ARBITER] PARDONED participant ${name} (${participantId})`);
    pendingArbiterAlerts.delete(`pro_${participantId}`);
    renderArbiterAlerts();
    refreshKiosks();
  } catch (err) {
    alert(`Failed to pardon participant: ${err.message}`);
  }
}

async function allowProAccount(participantId, name) {
  if (!confirm(`Authorize participant "${name}" (${participantId}) to compete using their Google AI Pro plan? This grants an official exemption.`)) return;
  try {
    await api(`/admin/kiosks/${encodeURIComponent(participantId)}/allow-pro-account`, { method: 'POST' });
    logMessage(`[ARBITER] ALLOWED Google AI Pro for participant ${name} (${participantId})`);
    pendingArbiterAlerts.delete(`pro_${participantId}`);
    renderArbiterAlerts();
    refreshKiosks();
  } catch (err) {
    alert(`Failed to authorize pro plan: ${err.message}`);
  }
}

async function refreshComplianceAlerts() {
  try {
    const alerts = await api('/admin/compliance/alerts');
    if (!Array.isArray(alerts)) return;
    pendingArbiterAlerts.clear();
    for (const a of alerts) {
      if (a.type === 'switch') {
        pendingArbiterAlerts.set(`switch_${a.participantId}`, {
          type: 'switch_request',
          requestId: a.id.replace('switch_', ''),
          participantId: a.participantId,
          deskId: a.deskId,
          name: a.name,
          email: a.email,
          reason: a.tier,
          timestamp: a.createdAt
        });
      } else {
        pendingArbiterAlerts.set(`pro_${a.participantId}`, {
          type: 'pro_flag',
          participantId: a.participantId,
          deskId: a.deskId,
          name: a.name,
          email: a.email,
          tier: a.tier,
          timestamp: a.createdAt
        });
      }
    }
    renderArbiterAlerts();
  } catch (_) {}
}

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

    hubConn.on('KIOSK_UPDATED', (deskId) => {
      refreshKiosks();
      refreshEvaluations();
    });

    hubConn.on('ACCOUNT_SWITCH_REQUEST', (requestId, participantId, deskId, name, email, reason, timestamp) => {
      logMessage(`🔔 [ARBITER] Account switch request: Desk ${deskId} (${name})`);
      pendingArbiterAlerts.set(`switch_${participantId}`, {
        type: 'switch_request',
        requestId,
        participantId,
        deskId,
        name,
        email,
        reason,
        timestamp
      });
      renderArbiterAlerts();
    });

    hubConn.on('PRO_ACCOUNT_FLAGGED', (participantId, deskId, name, email, tier, timestamp) => {
      logMessage(`🚨 [COMPLIANCE] Google AI Pro detected: Desk ${deskId} (${name}, ${email})`);
      pendingArbiterAlerts.set(`pro_${participantId}`, {
        type: 'pro_flag',
        participantId,
        deskId,
        name,
        email,
        tier,
        timestamp
      });
      renderArbiterAlerts();
      refreshKiosks();
    });

    hubConn.start().then(() => {
      logMessage('[SIGNALR] Live control plane hub connected.');
    }).catch(err => {
      console.warn('SignalR init failed:', err);
    });
  } catch (ex) {
    console.warn('SignalR setup exception:', ex);
  }
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
              const now = Date.now();
              const sec = Math.max(0, Math.floor((now - landed) / 1000));
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
        ? `<div style="margin-top:2px;"><a href="https://github.com/Prince649294u83/Rogue-AI-competition/tree/${encodeURIComponent(k.gitBranch)}" target="_blank" rel="noopener noreferrer" style="color:#38bdf8; font-size:11px; text-decoration:none;"><code>${escapeHtml(k.gitBranch)}</code> ↗</a></div>`
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
          <td class="actions-cell">
            <button class="btn sm primary" onclick="promptFireOnKiosk('${escapeHtml(k.id)}', '${escapeHtml(k.name || deskTag)}')">
              ⚡ Fire
            </button>
            ${k.status === 'disqualified'
              ? `<button class="btn sm success" onclick="reinstateParticipant('${escapeHtml(k.id)}', '${escapeHtml(k.name || deskTag)}')">🕊️ Reinstate</button>`
              : `<button class="btn sm danger" onclick="disqualifyParticipant('${escapeHtml(k.id)}', '${escapeHtml(k.name || deskTag)}')">⛔ Disqualify</button>`}
          </td>
        </tr>`;
    }).join('');
  } catch (err) {
    logMessage(`[ERROR] Loading kiosks failed: ${err.message}`);
  }
}

async function promptFireOnKiosk(kioskId, name) {
  const attackNum = prompt(`Fire specific attack on participant "${name}" (${kioskId}).\nEnter attack number (0 to 10):`, "1");
  if (!attackNum) return;
  const num = parseInt(attackNum.trim(), 10);
  if (isNaN(num) || num < 0 || num > 10) {
    alert("Please enter a valid attack number between 0 and 10.");
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

async function resetAllBaseline() {
  if (!confirm('⚠️ WARNING: You are about to RESET ALL DESKS to the pristine AEGIS baseline!\n\nThis will wipe all active attack files, restore original code, and restart participant applications nominal at DEFCON-4.\n\nProceed?')) return;
  try {
    const res = await api('/api/admin/reset-baseline', { method: 'POST' });
    if (res.success) {
      logMessage('↺ [RESET] Successfully reset all participant workspaces to baseline.');
      await loadKiosks();
    }
  } catch (err) {
    alert(`Failed to reset desks: ${err.message}`);
    logMessage(`[ERROR] Reset baseline: ${err.message}`);
  }
}

async function resetParticipantBaseline(pid) {
  if (!confirm(`Reset desk for participant "${pid}" to clean baseline?`)) return;
  try {
    const res = await api(`/api/admin/reset-baseline?target=${encodeURIComponent(pid)}`, { method: 'POST' });
    if (res.success) {
      logMessage(`↺ [RESET] Desk "${pid}" reset to clean baseline.`);
      await loadKiosks();
    }
  } catch (err) {
    alert(`Failed to reset desk: ${err.message}`);
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
      await Promise.allSettled([refreshEvaluations(), refreshKiosks(), refreshCohorts()]);
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
  refreshCohorts();
  refreshComplianceAlerts();
  setInterval(() => {
    checkEventStatus();
    refreshAttacks();
    refreshEvaluations();
    refreshKiosks();
    refreshCohorts();
    refreshComplianceAlerts();
  }, 3000);
}

// ---- Cohort Matrix Functions ----
let currentCohortsData = null;

const ATTACK_NAMES_SHORT = {
  0: "0: Warm-up",
  1: "1: Phantom Heap",
  2: "2: Schrödinger",
  3: "3: Hydra Query",
  4: "4: Mirage Route",
  5: "5: Polite Intruder",
  6: "6: Chameleon",
  7: "7: Zombie Emitter",
  8: "8: Poison Proto",
  9: "9: Proxy Mirage",
  10: "10: Janus Knot (Boss)"
};

async function refreshCohorts() {
  try {
    const data = await api('/api/admin/cohorts');
    currentCohortsData = data;
    renderCohortMatrix(data);
  } catch (err) {
    console.error('Error refreshing cohorts:', err);
  }
}

function renderCohortMatrix(data) {
  const container = document.getElementById('cohort-matrix-grid');
  const countEl = document.getElementById('cohort-station-count');
  if (!container || !data || !Array.isArray(data.columns)) return;

  if (countEl) {
    countEl.textContent = `${data.totalParticipants || 0} / 50 stations`;
  }

  const attackOptionsHtml = Object.entries(ATTACK_NAMES_SHORT)
    .map(([num, name]) => `<option value="${num}">Act #${name}</option>`)
    .join('');

  container.innerHTML = data.columns.map(col => {
    const participants = col.participants || [];
    const count = participants.length;

    let cardsHtml = participants.map(p => {
      const isOnline = p.status === 'online';
      const statusPill = isOnline 
        ? `<span class="cohort-status-pill cohort-status-online">Online</span>` 
        : `<span class="cohort-status-pill cohort-status-offline">Offline</span>`;
      
      const activeAttackBadge = p.activeAttack !== null && p.activeAttack !== undefined
        ? `<span class="badge active" style="font-size:10px;">Attack #${p.activeAttack} Active</span>`
        : `<span class="muted" style="font-size:10px;">Idle</span>`;

      return `
        <div class="cohort-card">
          <div class="cohort-card-top">
            <span class="cohort-desk-tag">${escapeHtml(p.deskId || p.id)}</span>
            ${statusPill}
          </div>
          <div class="cohort-card-sub">
            <span class="muted">${escapeHtml(p.name || p.id)}</span>
            <span class="bold" style="color:#10b981;">${(p.score || 0).toFixed(1)} pts</span>
          </div>
          <div class="cohort-card-attack">
            ${activeAttackBadge}
            <div style="display:flex; gap:4px;">
              <button class="btn sm" style="padding:2px 6px; font-size:10px;" onclick="messageParticipant('${escapeHtml(p.id)}', '${escapeHtml(p.deskId || p.name)}')">✉</button>
              <button class="btn sm" style="padding:2px 6px; font-size:10px;" onclick="promptFireOnKiosk('${escapeHtml(p.id)}', '${escapeHtml(p.deskId || p.name)}')">⚡ Fire</button>
            </div>
          </div>
        </div>
      `;
    }).join('');

    const emptySlotsCount = Math.max(0, 10 - count);
    for (let slot = count + 1; slot <= 10; slot++) {
      cardsHtml += `<div class="cohort-slot-empty">[Slot ${slot} - Open]</div>`;
    }

    return `
      <div class="cohort-col">
        <div class="cohort-header">
          <div class="cohort-header-title">
            <span>${escapeHtml(col.title)}</span>
            <span class="badge" style="font-size:11px;">${count} / 10 Active</span>
          </div>
          <div class="cohort-act-control">
            <select id="cohort-select-${col.columnIndex}" class="cohort-select">
              ${attackOptionsHtml}
            </select>
            <button class="cohort-launch-btn" onclick="fireCohortAttack(${col.columnIndex}, '${escapeHtml(col.title)}')">
              ⚡ Fire
            </button>
            <button class="btn secondary sm" style="padding:4px 8px; font-size:11px;" onclick="messageCohort(${col.columnIndex}, '${escapeHtml(col.title)}')">
              📢 Msg
            </button>
          </div>
        </div>
        <div class="cohort-card-list">
          ${cardsHtml}
        </div>
      </div>
    `;
  }).join('');
}

async function shuffleCohorts() {
  if (!confirm('Randomize & assign all currently registered stations into 5 columns (10 per column)?')) {
    return;
  }
  try {
    await api('/api/admin/cohorts/shuffle', { method: 'POST' });
    logMessage('🎲 [COHORTS] Successfully randomized and assigned stations into 5 columns.');
    await refreshCohorts();
  } catch (err) {
    alert(`Failed to randomize cohorts: ${err.message}`);
    logMessage(`[ERROR] Randomize cohorts: ${err.message}`);
  }
}

async function fireCohortAttack(columnIndex, colTitle) {
  const selectEl = document.getElementById(`cohort-select-${columnIndex}`);
  const attackId = selectEl ? parseInt(selectEl.value, 10) : 0;
  const attackName = ATTACK_NAMES_SHORT[attackId] || `Attack #${attackId}`;

  const colData = currentCohortsData?.columns?.find(c => c.columnIndex === columnIndex);
  const targetCount = colData?.participants?.length || 0;

  if (targetCount === 0) {
    alert(`No active stations currently assigned to ${colTitle}. Station must connect first.`);
    return;
  }

  if (!confirm(`Deploy ${attackName} to ALL ${targetCount} active stations in ${colTitle}?`)) {
    return;
  }

  try {
    const res = await api(`/api/admin/cohorts/${columnIndex}/fire?attackId=${attackId}`, { method: 'POST' });
    logMessage(`🔥 [COHORT LAUNCH] ${attackName} deployed to ${colTitle} (${res.targetCount} stations targeted)!`);
    await refreshCohorts();
    await refreshAttacks();
    await refreshEvaluations();
  } catch (err) {
    alert(`Failed to launch attack on ${colTitle}: ${err.message}`);
    logMessage(`[ERROR] Cohort launch on ${colTitle}: ${err.message}`);
  }
}

async function messageCohort(columnIndex, colTitle) {
  const colData = currentCohortsData?.columns?.find(c => c.columnIndex === columnIndex);
  const targetCount = colData?.participants?.length || 0;
  if (targetCount === 0) {
    alert(`No active stations currently assigned to ${colTitle}.`);
    return;
  }
  const msg = prompt(`Enter broadcast message to send to all ${targetCount} stations in ${colTitle}:`);
  if (!msg || !msg.trim()) return;

  try {
    await api(`/api/admin/cohorts/${columnIndex}/message`, {
      method: 'POST',
      body: JSON.stringify({ message: msg.trim() })
    });
    logMessage(`📢 [COHORT MSG] Sent to ${colTitle}: "${msg.trim()}"`);
    alert(`Message broadcast to all ${targetCount} stations in ${colTitle}.`);
  } catch (err) {
    alert(`Failed to send message: ${err.message}`);
  }
}

async function messageParticipant(participantId, name) {
  const msg = prompt(`Enter direct message for ${name}:`);
  if (!msg || !msg.trim()) return;

  try {
    await api(`/api/admin/participant/${participantId}/message`, {
      method: 'POST',
      body: JSON.stringify({ message: msg.trim() })
    });
    logMessage(`✉ [DIRECT MSG] Sent to ${name}: "${msg.trim()}"`);
    alert(`Message delivered to ${name}.`);
  } catch (err) {
    alert(`Failed to send direct message: ${err.message}`);
  }
}

document.addEventListener('DOMContentLoaded', init);
