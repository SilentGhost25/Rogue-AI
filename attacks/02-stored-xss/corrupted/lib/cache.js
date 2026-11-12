// In-memory incident feed cache used by TrafficGrid (COMPROMISED: raw HTML description + 60s stale window).
let cachedRows = null;
let expiresAt = 0;
const TTL_MS = 60000;

function getCachedIncidentRows(db, esc) {
  if (cachedRows !== null && Date.now() < expiresAt) {
    return cachedRows;
  }
  const incidents = db
    .prepare('SELECT intersection, description, severity, created_at FROM traffic_incidents ORDER BY id DESC')
    .all();

  cachedRows = incidents
    .map(
      (i) => `
                <tr>
                  <td><strong>${esc(i.intersection)}</strong></td>
                  <td>${i.description}</td>
                  <td><span class="badge badge-${i.severity.toLowerCase() === 'high' ? 'critical' : 'elevated'}">${esc(i.severity)}</span></td>
                  <td>${esc(i.created_at)}</td>
                </tr>`
    )
    .join('');
  expiresAt = Date.now() + TTL_MS;
  return cachedRows;
}

function invalidateIncidentCache() {
  cachedRows = null;
  expiresAt = 0;
}

module.exports = { getCachedIncidentRows, invalidateIncidentCache };
