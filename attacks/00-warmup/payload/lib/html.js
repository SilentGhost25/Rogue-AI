// Cyber-defense layout and template helpers.
const esc = (s) => String(s ?? '')
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;')
  .replace(/'/g, '&#39;');

function layout({ title = 'Command', user = null, active = 'overview', threat = 'DEFCON-4', body = '' }) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)} | AEGIS Municipal Defense</title>
<link rel="stylesheet" href="/aegis.css">
</head>
<body>
<header class="aegis-header">
  <div class="header-left">
    <div class="brand">
      <span class="shield-glyph">&#x25C7;</span>
      <span class="brand-title">AEGIS // OPERATOINS</span>
    </div>
    <div class="system-status">
      <span class="threat-badge threat-${threat.toLowerCase()}">${esc(threat)}</span>
      <span class="status-indicator live">SYSTEM ONLINE</span>
    </div>
  </div>
  <nav class="nav-links">
    <a href="/" class="${active === 'overview' ? 'active' : ''}">Overview</a>
    <a href="/traffic" class="${active === 'traffic' ? 'active' : ''}">TrafficGrid</a>
    <a href="/power" class="${active === 'power' ? 'active' : ''}">PowerCore</a>
    <a href="/vault" class="${active === 'vault' ? 'active' : ''}">CitizenVault</a>
    <a href="/security" class="${active === 'security' ? 'active' : ''}">Security Center</a>
  </nav>
  <div class="header-right">
    ${user ? `
      <div class="user-info">
        <span class="username">${esc(user.username)}</span>
        <span class="role-pill role-${esc(user.role)}">${esc(user.role.toUpperCase())}</span>
        <span class="clearance-pill">L${esc(user.clearance_level || 1)}</span>
      </div>
      <form action="/logout" method="post" style="display:inline">
        <button type="submit" class="btn-terminal-sm">LOGOUT</button>
      </form>
    ` : `
      <a href="/login" class="btn-terminal-sm">SIGN IN</a>
    `}
  </div>
</header>
<main class="aegis-container">
  ${body}
</main>
<footer class="aegis-footer">
  <div class="footer-meta">AEGIS v2.4.1 // SMART CITY MUNICIPAL DEFENSE NETWORK</div>
  <div class="footer-time">SESSION ENCRYPTED // NODE 127.0.0.1</div>
</footer>
<script src="/app.js" defer></script>
</body>
</html>`;
}

module.exports = { esc, layout };
