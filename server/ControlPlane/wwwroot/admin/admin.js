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
            <button class="btn sm primary" onclick="fireArena(${a.id})" ${!isCanaryConfirmed ? 'disabled' : ''}>
              Fire Arena
            </button>
          </td>
        </tr>`;
    }).join('');
  } catch (err) {
    logMessage(`Error loading attacks: ${err.message}`);
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
      tbody.innerHTML = `<tr><td colspan="5" class="muted">No kiosks connected yet</td></tr>`;
      return;
    }

    tbody.innerHTML = kiosks.map(k => `
      <tr>
        <td class="bold font-mono">${escapeHtml(k.kioskId || k.id)}</td>
        <td>${escapeHtml(k.name || k.id)}</td>
        <td class="font-mono">${k.totalScore} pts</td>
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
    refreshKiosks();
  });

  document.getElementById('clear-log-btn')?.addEventListener('click', () => {
    const logEl = document.getElementById('log-content');
    if (logEl) logEl.textContent = '[Console cleared]';
  });

  // Clock
  setInterval(() => {
    const clockEl = document.getElementById('server-time');
    if (clockEl) {
      clockEl.textContent = 'UTC ' + new Date().toISOString().substring(11, 19);
    }
  }, 1000);

  // Initial load & periodic poll
  refreshAttacks();
  refreshKiosks();
  setInterval(() => {
    refreshAttacks();
    refreshKiosks();
  }, 3000);
}

document.addEventListener('DOMContentLoaded', init);
