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

      return `
        <tr>
          <td class="bold font-mono">#${a.id}</td>
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
    if (countEl) countEl.textContent = `${list.length} participant${list.length === 1 ? '' : 's'}`;

    if (list.length === 0) {
      tbody.innerHTML = `<tr><td colspan="16" class="muted">No participants connected yet</td></tr>`;
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

      // Attack columns 1 to 8
      let attackCols = '';
      for (let i = 1; i <= 8; i++) {
        const att = attMap[i];
        if (!att || att.status === 'PENDING') {
          attackCols += `<td><span class="badge pending" title="Not dispatched">--</span></td>`;
        } else if (att.status === 'PASSED') {
          attackCols += `<td><span class="badge passed" title="Passed (+points)">PASS</span></td>`;
        } else if (att.status === 'FAILED') {
          attackCols += `<td><span class="badge failed" title="${escapeHtml(att.lastReason)}">FAIL</span></td>`;
        } else {
          attackCols += `<td><span class="badge inprogress" title="Active breach">RUN</span></td>`;
        }
      }

      const penaltyTag = p.localPenalties > 0
        ? `<span class="penalty-tag">${p.localPenalties} fail (${-(p.localPenalties * 3.0).toFixed(1)} pts)</span>`
        : `<span class="muted font-mono">0</span>`;

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
          <td style="max-width:280px; font-size:12px; color:#cbd5e1; white-space:nowrap; overflow:hidden; text-overflow:ellipsis;" title="${escapeHtml(lastDiag)}">
            ${escapeHtml(lastDiag)}
          </td>
        </tr>
      `;
    }).join('');
  } catch (err) {
    // Non-fatal if evaluations fail
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
      tbody.innerHTML = `<tr><td colspan="6" class="muted">No kiosks connected yet</td></tr>`;
      return;
    }

    tbody.innerHTML = kiosks.map(k => `
      <tr>
        <td class="bold font-mono"><code class="tag">${escapeHtml(k.deskId || k.kioskId || k.id)}</code></td>
        <td class="bold">${escapeHtml(k.name || k.id)}</td>
        <td class="font-mono text-muted">${escapeHtml(k.ipAddress || '127.0.0.1')}</td>
        <td class="font-mono bold" style="color:#22c55e;">${k.totalScore} pts</td>
        <td>${k.solvedCount} / ${k.landedCount}</td>
        <td>
          ${k.agyExhausted ? '<span class="badge danger">AGY Expired</span>' : '<span class="badge success">AGY Active</span>'}
          <small class="muted font-mono">(${k.openCodePrompts} OC prompts)</small>
        </td>
      </tr>
    `).join('');
  } catch (err) {
    // Non-fatal if kiosks endpoint fails
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

// 2-Hour Event Countdown Timer
let eventStartTime = localStorage.getItem('rogue_event_start');
if (!eventStartTime) {
  eventStartTime = Date.now().toString();
  localStorage.setItem('rogue_event_start', eventStartTime);
}
const EVENT_DURATION_MS = 2 * 60 * 60 * 1000; // 2 hours in ms

function updateEventTimer() {
  const timerEl = document.getElementById('event-timer');
  if (!timerEl) return;
  const elapsed = Date.now() - parseInt(eventStartTime, 10);
  const remainingMs = Math.max(0, EVENT_DURATION_MS - elapsed);
  const totalSeconds = Math.floor(remainingMs / 1000);

  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  const pad = n => String(n).padStart(2, '0');
  timerEl.textContent = `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;

  if (remainingMs <= 0) {
    timerEl.style.color = '#ef4444';
  } else if (remainingMs < 15 * 60 * 1000) {
    timerEl.style.color = '#f59e0b';
  } else {
    timerEl.style.color = '#38bdf8';
  }
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

  document.getElementById('refresh-btn')?.addEventListener('click', () => {
    refreshAttacks();
    refreshEvaluations();
    refreshKiosks();
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

  // Initial load & periodic poll
  refreshAttacks();
  refreshEvaluations();
  refreshKiosks();
  setInterval(() => {
    refreshAttacks();
    refreshEvaluations();
    refreshKiosks();
  }, 3000);
}

document.addEventListener('DOMContentLoaded', init);
