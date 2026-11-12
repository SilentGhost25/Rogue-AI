document.addEventListener('DOMContentLoaded', () => {
  // 1. Live UTC Clock in the AEGIS header
  const headerRight = document.querySelector('.aegis-header .header-right');
  if (headerRight) {
    const clock = document.createElement('div');
    clock.id = 'utc-clock';
    clock.className = 'utc-clock';
    headerRight.prepend(clock);

    const updateClock = () => {
      clock.textContent = new Date().toISOString().replace('T', ' ').substring(0, 19) + ' UTC';
    };
    updateClock();
    setInterval(updateClock, 1000);
  }

  // 2. Active navigation highlighting
  const currentPath = window.location.pathname;
  document.querySelectorAll('.aegis-header .nav-links a').forEach((link) => {
    const isActive = link.getAttribute('href') === currentPath;
    link.classList.toggle('active', isActive);
    if (isActive) {
      link.setAttribute('aria-current', 'page');
    } else {
      link.removeAttribute('aria-current');
    }
  });

  // 3. Safe telemetry profile helper (whitelists only display_name and email)
  window.syncOperatorTelemetry = async (profile = {}) => {
    const safePayload = {
      display_name: String(profile.display_name || ''),
      email: String(profile.email || '')
    };
    const res = await fetch('/api/users/profile', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
      body: JSON.stringify(safePayload)
    });
    return res.ok ? res.json() : null;
  };
});
