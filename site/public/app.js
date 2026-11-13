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

/* ==========================================================================
   AEGIS // 3D Smart City Digital Twin & Command Center Engine
   High-performance Canvas 3D isometric city, neon routes, holographic shield,
   interactive telemetry, and reactive cyber-defense visualizations.
   ========================================================================== */
document.addEventListener('DOMContentLoaded', () => {
  const SVG_NS = 'http://www.w3.org/2000/svg';

  function iconSvg(name, size = 16) {
    const paths = {
      shield: 'M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3z',
      overview: 'M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z',
      traffic: 'M12 2a4 4 0 0 0-4 4v10a4 4 0 0 0 8 0V6a4 4 0 0 0-4-4zm0 15a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm0-5a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm0-5a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z',
      power: 'M13 2L4 14h7l-1 8 9-12h-7l1-8z',
      vault: 'M18 8h-1V6a5 5 0 0 0-10 0v2H6a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V10a2 2 0 0 0-2-2zm-6 9a2 2 0 1 1 0-4 2 2 0 0 1 0 4zm3-9H9V6a3 3 0 0 1 6 0v2z',
      security: 'M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z',
      arrow: 'M5 12h14M12 5l7 7-7 7',
      alert: 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z'
    };
    const svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', String(size));
    svg.setAttribute('height', String(size));
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '2');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    svg.setAttribute('aria-hidden', 'true');
    svg.classList.add('nav-ico');
    const p = document.createElementNS(SVG_NS, 'path');
    p.setAttribute('d', paths[name] || paths.shield);
    svg.appendChild(p);
    return svg;
  }

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  // 1. Upgrade Header Logo & Subtitle
  const brandEl = document.querySelector('.aegis-header .brand');
  if (brandEl) {
    const glyph = brandEl.querySelector('.shield-glyph');
    if (glyph) {
      glyph.textContent = '';
      const s = iconSvg('shield', 20);
      s.classList.remove('nav-ico');
      glyph.appendChild(s);
    }
    const titleSpan = brandEl.querySelector('.brand-title');
    if (titleSpan && !brandEl.querySelector('.brand-text-block')) {
      const wrap = el('div', 'brand-text-block');
      titleSpan.replaceWith(wrap);
      wrap.appendChild(titleSpan);
      wrap.appendChild(el('span', 'brand-sub', 'Municipal Defense Core'));
    }
  }

  // Upgrade Top Navigation Bar with Reference Icons
  const navMap = {
    '/': 'overview',
    '/traffic': 'traffic',
    '/power': 'power',
    '/vault': 'vault',
    '/security': 'security'
  };
  document.querySelectorAll('.aegis-header .nav-links a').forEach((link) => {
    const ico = navMap[link.getAttribute('href')];
    if (ico && !link.querySelector('.nav-ico')) {
      link.prepend(iconSvg(ico, 16));
    }
  });

  const path = window.location.pathname;

  // =========================================================================
  // 2. OVERVIEW PAGE — 3D SMART CITY DIGITAL TWIN & COMMAND CENTER (Reference 1 & 2)
  // =========================================================================
  if (path === '/') {
    const container = document.querySelector('.aegis-container');
    if (!container) return;
    container.classList.add('enhanced-twin');

    // Build the 3D City Hero Banner
    const hero = el('div', 'aegis-hero-twin');

    const topBar = el('div', 'twin-top-bar');
    const titleBlock = el('div', 'twin-title-block');
    titleBlock.appendChild(el('div', 'twin-kicker', '⚡ AEGIS // SMART CITY OPERATIONS'));
    titleBlock.appendChild(el('h1', 'twin-headline', 'MUNICIPAL DEFENSE CORE'));
    titleBlock.appendChild(el('p', 'twin-lead', 'Operational control center for critical urban infrastructure subsystems & autonomous defense mesh.'));

    const chips = el('div', 'twin-quick-chips');
    [
      { lbl: 'City Grid', val: '● ONLINE', cls: 'is-green' },
      { lbl: 'Telemetry', val: '100%', cls: 'is-cyan' },
      { lbl: 'Integrity', val: 'SECURE', cls: 'is-green' },
      { lbl: 'Latency', val: '68 ms', cls: 'is-cyan' }
    ].forEach((c) => {
      const ch = el('div', 'twin-chip');
      ch.appendChild(el('span', 'twin-chip-lbl', c.lbl));
      ch.appendChild(el('span', `twin-chip-val ${c.cls}`, c.val));
      chips.appendChild(ch);
    });
    titleBlock.appendChild(chips);
    topBar.appendChild(titleBlock);
    hero.appendChild(topBar);

    // 3D Canvas Stage with Floating Beacons
    const canvasWrap = el('div', 'twin-canvas-container');
    const stage = el('div', 'twin-canvas-stage');
    const canvas = document.createElement('canvas');
    canvas.id = 'aegisCityCanvas';
    stage.appendChild(canvas);

    // Subsystem Beacons floating over 3D City
    const beaconsWrap = el('div', 'twin-beacons-overlay');
    const beacons = [
      { name: 'TrafficGrid', icon: 'traffic', cls: 'beacon-traffic', x: 26, y: 34, href: '/traffic' },
      { name: 'PowerCore', icon: 'power', cls: 'beacon-power', x: 44, y: 22, href: '/power' },
      { name: 'CitizenVault', icon: 'vault', cls: 'beacon-vault', x: 74, y: 32, href: '/vault' },
      { name: 'Security Center', icon: 'security', cls: 'beacon-security', x: 80, y: 64, href: '/security' }
    ];
    beacons.forEach((b) => {
      const bNode = el('a', `twin-beacon ${b.cls}`);
      bNode.href = b.href;
      bNode.style.left = `${b.x}%`;
      bNode.style.top = `${b.y}%`;
      const icoBox = el('span', 'twin-beacon-icon');
      icoBox.appendChild(iconSvg(b.icon, 12));
      bNode.appendChild(icoBox);
      bNode.appendChild(el('span', '', b.name));
      beaconsWrap.appendChild(bNode);
    });
    stage.appendChild(beaconsWrap);
    canvasWrap.appendChild(stage);

    const caption = el('div', 'twin-pedestal-caption');
    caption.appendChild(el('div', 'twin-pedestal-label', 'AEGIS'));
    caption.appendChild(el('div', 'twin-pedestal-sub', 'Smart City Municipal Defense Core'));
    canvasWrap.appendChild(caption);

    hero.appendChild(canvasWrap);
    container.prepend(hero);

    // Initialize 3D Isometric City Engine on Canvas
    init3DCityCanvas(canvas, stage);

    // Enhance the 4 KPI Cards with Sparklines & Action Arrows (Reference 1 & 2)
    const cards = document.querySelectorAll('.stats-grid .card');
    const cardThemes = [
      { cls: 'card-kpi-traffic', href: '/traffic', stroke: '#22d3ee', fill: 'rgba(34, 211, 238, 0.15)' },
      { cls: 'card-kpi-power', href: '/power', stroke: '#d946ef', fill: 'rgba(217, 70, 239, 0.18)' },
      { cls: 'card-kpi-vault', href: '/vault', stroke: '#a855f7', fill: 'rgba(168, 85, 247, 0.18)' },
      { cls: 'card-kpi-threat', href: '/security', stroke: '#34d399', fill: 'rgba(52, 211, 153, 0.15)' }
    ];
    cards.forEach((card, idx) => {
      const t = cardThemes[idx];
      if (!t) return;
      card.classList.add(t.cls);

      // Sparkline SVG Wave
      if (!card.querySelector('.card-sparkline-wrap')) {
        const wrap = el('div', 'card-sparkline-wrap');
        const svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('viewBox', '0 0 200 36');
        svg.setAttribute('preserveAspectRatio', 'none');
        svg.classList.add('card-sparkline-svg');

        const wavePath = document.createElementNS(SVG_NS, 'path');
        const areaPath = document.createElementNS(SVG_NS, 'path');
        const waves = [
          'M 0 24 Q 25 10 50 20 T 100 12 T 150 22 T 200 14',
          'M 0 20 Q 30 28 60 14 T 120 22 T 160 10 T 200 18',
          'M 0 16 Q 40 8 80 22 T 140 12 T 180 20 T 200 15',
          'M 0 22 Q 25 15 50 18 T 100 8 T 150 18 T 200 12'
        ];
        const w = waves[idx % waves.length];
        wavePath.setAttribute('d', w);
        wavePath.setAttribute('fill', 'none');
        wavePath.setAttribute('stroke', t.stroke);
        wavePath.setAttribute('stroke-width', '2.5');
        wavePath.setAttribute('stroke-linecap', 'round');

        areaPath.setAttribute('d', `${w} L 200 36 L 0 36 Z`);
        areaPath.setAttribute('fill', t.fill);

        svg.appendChild(areaPath);
        svg.appendChild(wavePath);
        wrap.appendChild(svg);

        const sub = card.querySelector('.card-sub');
        if (sub) sub.before(wrap);
      }

      // Action Arrow Link
      if (!card.querySelector('.kpi-action-link')) {
        const link = el('a', 'kpi-action-link');
        link.href = t.href;
        link.title = 'Open Subsystem';
        link.appendChild(iconSvg('arrow', 14));
        card.appendChild(link);
      }
    });

    // Inject the 3 Deep Operational Monitoring Panels (Matching Reference 1 & 2)
    const opsGrid = el('div', 'aegis-ops-grid');

    // Panel 1: TRAFFICGRID — LIVE FLOW (Schematic Interactive Map)
    const pTraffic = el('div', 'ops-panel');
    const pTrafficHead = el('div', 'ops-panel-head');
    const pTrafficTitle = el('div', 'ops-panel-title');
    pTrafficTitle.appendChild(iconSvg('traffic', 15));
    pTrafficTitle.appendChild(el('span', '', 'TRAFFICGRID — LIVE FLOW'));
    pTrafficHead.appendChild(pTrafficTitle);
    pTrafficHead.appendChild(el('span', 'ops-panel-meta', 'REAL-TIME MESH'));
    pTraffic.appendChild(pTrafficHead);

    const trafficMapWrap = el('div', 'traffic-schematic-wrap');
    const trafficMapSvg = document.createElementNS(SVG_NS, 'svg');
    trafficMapSvg.setAttribute('viewBox', '0 0 360 190');
    trafficMapSvg.classList.add('traffic-schematic-svg');

    trafficMapSvg.innerHTML = `
      <defs>
        <linearGradient id="tfRoute1" x1="0%" y1="0%" x2="100%" y2="0%">
          <stop offset="0%" stop-color="#06b6d4"/>
          <stop offset="100%" stop-color="#3b82f6"/>
        </linearGradient>
        <linearGradient id="tfRoute2" x1="0%" y1="100%" x2="100%" y2="0%">
          <stop offset="0%" stop-color="#d946ef"/>
          <stop offset="100%" stop-color="#ec4899"/>
        </linearGradient>
      </defs>
      <!-- Grid lines -->
      <path d="M 20 40 L 340 40 M 20 95 L 340 95 M 20 150 L 340 150" stroke="rgba(168,85,247,0.12)" stroke-width="1"/>
      <path d="M 60 20 L 60 170 M 150 20 L 150 170 M 240 20 L 240 170 M 310 20 L 310 170" stroke="rgba(168,85,247,0.12)" stroke-width="1"/>
      <!-- Active Arterial Roads -->
      <path d="M 20 95 Q 120 70 180 110 T 340 85" fill="none" stroke="url(#tfRoute1)" stroke-width="3" stroke-linecap="round"/>
      <path d="M 80 170 Q 150 120 220 75 T 320 30" fill="none" stroke="url(#tfRoute2)" stroke-width="3" stroke-linecap="round"/>
      <!-- Intersection nodes -->
      <circle cx="95" cy="85" r="5" fill="#10b981" filter="drop-shadow(0 0 6px #10b981)"/>
      <circle cx="170" cy="105" r="6" fill="#06b6d4" filter="drop-shadow(0 0 8px #06b6d4)"/>
      <circle cx="250" cy="65" r="5" fill="#f59e0b" filter="drop-shadow(0 0 6px #f59e0b)"/>
      <!-- Incident Alert Beacon -->
      <polygon points="268,135 280,155 256,155" fill="#ef4444" filter="drop-shadow(0 0 8px #ef4444)"/>
      <text x="268" y="152" fill="#fff" font-size="10" font-weight="bold" text-anchor="middle">!</text>
    `;
    trafficMapWrap.appendChild(trafficMapSvg);
    pTraffic.appendChild(trafficMapWrap);

    const trafficLegend = el('div', 'traffic-schematic-legend');
    [
      { color: '#06b6d4', text: '4 Intersections' },
      { color: '#f59e0b', text: '1 Congested' },
      { color: '#ef4444', text: '1 Offline' }
    ].forEach((leg) => {
      const itm = el('div', 'traffic-legend-item');
      const dot = el('span', 'traffic-legend-dot');
      dot.style.background = leg.color;
      dot.style.boxShadow = `0 0 8px ${leg.color}`;
      itm.appendChild(dot);
      itm.appendChild(el('span', '', leg.text));
      trafficLegend.appendChild(itm);
    });
    const tfLink = el('a', 'btn-terminal-sm', 'View Details →');
    tfLink.href = '/traffic';
    trafficLegend.appendChild(tfLink);
    pTraffic.appendChild(trafficLegend);
    opsGrid.appendChild(pTraffic);

    // Panel 2: POWERCORE — GRID LOAD (24h Area Chart Panel)
    const pPower = el('div', 'ops-panel');
    const pPowerHead = el('div', 'ops-panel-head');
    const pPowerTitle = el('div', 'ops-panel-title');
    pPowerTitle.appendChild(iconSvg('power', 15));
    pPowerTitle.appendChild(el('span', '', 'POWERCORE — GRID LOAD'));
    pPowerHead.appendChild(pPowerTitle);
    pPowerHead.appendChild(el('span', 'ops-panel-meta', '24H TELEMETRY'));
    pPower.appendChild(pPowerHead);

    const pChartWrap = el('div', 'power-chart-wrap');
    const pChartSvg = document.createElementNS(SVG_NS, 'svg');
    pChartSvg.setAttribute('viewBox', '0 0 360 190');
    pChartSvg.classList.add('power-chart-svg');

    pChartSvg.innerHTML = `
      <defs>
        <linearGradient id="pAreaGrad" x1="0%" y1="0%" x2="0%" y2="100%">
          <stop offset="0%" stop-color="#d946ef" stop-opacity="0.55"/>
          <stop offset="60%" stop-color="#8b5cf6" stop-opacity="0.25"/>
          <stop offset="100%" stop-color="#080314" stop-opacity="0"/>
        </linearGradient>
      </defs>
      <!-- Grid scale -->
      <line x1="30" y1="20" x2="340" y2="20" stroke="rgba(255,255,255,0.06)" stroke-dasharray="3 3"/>
      <line x1="30" y1="65" x2="340" y2="65" stroke="rgba(255,255,255,0.06)" stroke-dasharray="3 3"/>
      <line x1="30" y1="110" x2="340" y2="110" stroke="rgba(255,255,255,0.06)" stroke-dasharray="3 3"/>
      <line x1="30" y1="155" x2="340" y2="155" stroke="rgba(255,255,255,0.1)"/>
      <text x="10" y="24" fill="#726094" font-size="9" font-family="monospace">100%</text>
      <text x="10" y="69" fill="#726094" font-size="9" font-family="monospace">65%</text>
      <text x="10" y="114" fill="#726094" font-size="9" font-family="monospace">30%</text>
      <!-- Wave Area -->
      <path d="M 30 130 Q 80 145 130 115 T 220 50 T 290 90 T 340 75 L 340 155 L 30 155 Z" fill="url(#pAreaGrad)"/>
      <path d="M 30 130 Q 80 145 130 115 T 220 50 T 290 90 T 340 75" fill="none" stroke="#d946ef" stroke-width="3" filter="drop-shadow(0 0 8px rgba(217,70,239,0.8))"/>
      <circle cx="220" cy="50" r="5" fill="#fff" stroke="#d946ef" stroke-width="2.5" filter="drop-shadow(0 0 10px #d946ef)"/>
      <!-- Time axis -->
      <text x="35" y="172" fill="#726094" font-size="9" font-family="monospace">00:00</text>
      <text x="135" y="172" fill="#726094" font-size="9" font-family="monospace">08:00</text>
      <text x="235" y="172" fill="#726094" font-size="9" font-family="monospace">16:00</text>
      <text x="315" y="172" fill="#726094" font-size="9" font-family="monospace">24:00</text>
    `;
    pChartWrap.appendChild(pChartSvg);
    pPower.appendChild(pChartWrap);

    const pFooter = el('div', 'power-chart-footer');
    const pf1 = el('div', 'power-kpi-item');
    const pIco1 = el('div', 'power-kpi-icon');
    pIco1.appendChild(iconSvg('power', 16));
    pf1.appendChild(pIco1);
    const pText1 = el('div');
    pText1.appendChild(el('div', 'power-kpi-val', '586.4 MW'));
    pText1.appendChild(el('div', 'power-kpi-lbl', 'Current Municipal Draw'));
    pf1.appendChild(pText1);

    const pf2 = el('div', 'power-kpi-item');
    const pIco2 = el('div', 'power-kpi-icon');
    pIco2.appendChild(iconSvg('shield', 16));
    pf2.appendChild(pIco2);
    const pText2 = el('div');
    pText2.appendChild(el('div', 'power-kpi-val', '730.0 MW'));
    pText2.appendChild(el('div', 'power-kpi-lbl', 'Total System Capacity'));
    pf2.appendChild(pText2);

    pFooter.appendChild(pf1);
    pFooter.appendChild(pf2);
    pPower.appendChild(pFooter);
    opsGrid.appendChild(pPower);

    // Panel 3: THREAT MONITOR & RADAR (Reference 1 & 2)
    const pThreat = el('div', 'ops-panel');
    const pThreatHead = el('div', 'ops-panel-head');
    const pThreatTitle = el('div', 'ops-panel-title');
    pThreatTitle.appendChild(iconSvg('security', 15));
    pThreatTitle.appendChild(el('span', '', 'THREAT MONITOR'));
    pThreatHead.appendChild(pThreatTitle);
    pThreatHead.appendChild(el('span', 'ops-panel-meta', 'DEFCON POSTURE'));
    pThreat.appendChild(pThreatHead);

    const threatBody = el('div', 'threat-monitor-body');

    // Circular DEFCON Arc Ring
    const ringWrap = el('div', 'threat-ring-wrap');
    const ringSvg = document.createElementNS(SVG_NS, 'svg');
    ringSvg.setAttribute('viewBox', '0 0 100 100');
    ringSvg.classList.add('threat-ring-svg');
    ringSvg.innerHTML = `
      <circle cx="50" cy="50" r="40" fill="none" stroke="rgba(168,85,247,0.15)" stroke-width="8"/>
      <circle cx="50" cy="50" r="40" fill="none" stroke="#22d3ee" stroke-width="8" stroke-dasharray="251.2" stroke-dashoffset="65" stroke-linecap="round" filter="drop-shadow(0 0 10px #22d3ee)"/>
      <circle cx="50" cy="50" r="40" fill="none" stroke="#d946ef" stroke-width="8" stroke-dasharray="251.2" stroke-dashoffset="180" stroke-linecap="round" filter="drop-shadow(0 0 10px #d946ef)"/>
    `;
    ringWrap.appendChild(ringSvg);

    const ringCenter = el('div', 'threat-ring-center');
    ringCenter.appendChild(el('div', 'threat-ring-val', 'DEFCON-4'));
    ringCenter.appendChild(el('div', 'card-sub', 'NORMAL READINESS'));
    ringWrap.appendChild(ringCenter);
    threatBody.appendChild(ringWrap);

    // Threat Stats
    const threatStats = el('div', 'threat-stats-col');
    [
      { label: '● Intrusions', val: '0', color: '#10b981' },
      { label: '● Anomalies', val: '3', color: '#a855f7' },
      { label: '● Watchlist', val: '5', color: '#f59e0b' }
    ].forEach((s) => {
      const row = el('div', 'threat-stat-row');
      row.appendChild(el('span', '', s.label));
      const valSpan = el('span', '', s.val);
      valSpan.style.color = s.color;
      valSpan.style.fontWeight = '700';
      row.appendChild(valSpan);
      threatStats.appendChild(row);
    });
    threatBody.appendChild(threatStats);
    pThreat.appendChild(threatBody);

    // Audio/Sensor Equalizer visualizer
    const eq = el('div', 'sensor-equalizer');
    for (let i = 0; i < 28; i++) {
      const b = el('div', 'eq-bar');
      const h = 4 + Math.sin(i * 0.45) * 12 + Math.random() * 8;
      b.style.height = `${Math.max(4, Math.min(22, h))}px`;
      eq.appendChild(b);
    }
    pThreat.appendChild(eq);
    opsGrid.appendChild(pThreat);

    // Place opsGrid between stats-grid and the Recent Audit Trail card
    const statsGrid = container.querySelector('.stats-grid');
    if (statsGrid) statsGrid.after(opsGrid);
  }

  // =========================================================================
  // 3D ISOMETRIC CITY CANVAS RENDERER (Clean, Vanilla HTML5 Canvas & Perspective)
  // =========================================================================
  function init3DCityCanvas(canvas, container) {
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    let width = 0;
    let height = 0;
    let animId = null;
    let angle = 0;
    let mouseX = 0;
    let mouseY = 0;
    let targetTiltX = 0;
    let targetTiltY = 0;
    let curTiltX = 0;
    let curTiltY = 0;

    // Track mouse for interactive 3D parallax tilt
    container.addEventListener('mousemove', (e) => {
      const rect = container.getBoundingClientRect();
      const x = (e.clientX - rect.left) / rect.width - 0.5;
      const y = (e.clientY - rect.top) / rect.height - 0.5;
      targetTiltX = x * 0.28;
      targetTiltY = y * 0.2;
    });
    container.addEventListener('mouseleave', () => {
      targetTiltX = 0;
      targetTiltY = 0;
    });

    function resize() {
      const rect = container.getBoundingClientRect();
      width = Math.floor(rect.width);
      height = Math.floor(rect.height);
      const dpr = Math.min(2, window.devicePixelRatio || 1);
      canvas.width = width * dpr;
      canvas.height = height * dpr;
      ctx.scale(dpr, dpr);
    }
    resize();
    window.addEventListener('resize', resize);

    // Pre-generate city buildings in grid coordinates (x, y, w, d, h, hue)
    const buildings = [];
    const gridSize = 7;
    for (let gx = -3; gx <= 3; gx++) {
      for (let gy = -3; gy <= 3; gy++) {
        // Leave central plaza and diagonal road lanes open
        if (Math.abs(gx) === 0 && Math.abs(gy) === 0) continue;
        if (Math.abs(gx) === 1 && gy === 0) continue;
        if (gx === 0 && Math.abs(gy) === 1) continue;

        const dist = Math.hypot(gx, gy);
        const baseH = dist <= 1.5 ? 90 + Math.random() * 80 : 35 + Math.random() * 55;
        buildings.push({
          gx,
          gy,
          w: 26 + Math.random() * 10,
          d: 26 + Math.random() * 10,
          h: baseH,
          windows: Math.floor(baseH / 12),
          glowColor: dist < 2 ? '#22d3ee' : Math.random() > 0.4 ? '#d946ef' : '#8b5cf6'
        });
      }
    }

    // Sort buildings for painter's algorithm
    buildings.sort((a, b) => (a.gx + a.gy) - (b.gx + b.gy));

    // Particle field rising from pedestal
    const particles = [];
    for (let i = 0; i < 45; i++) {
      particles.push({
        x: (Math.random() - 0.5) * 360,
        y: Math.random() * 160,
        z: (Math.random() - 0.5) * 360,
        vy: 0.4 + Math.random() * 0.6,
        size: 1 + Math.random() * 2,
        alpha: Math.random()
      });
    }

    // Animation Loop
    function render() {
      angle += 0.004;
      curTiltX += (targetTiltX - curTiltX) * 0.06;
      curTiltY += (targetTiltY - curTiltY) * 0.06;

      ctx.clearRect(0, 0, width, height);

      const cx = width / 2;
      const cy = height * 0.62;

      // 1. Draw Holographic Shield Energy Dome (Reference 1 & 2)
      ctx.save();
      ctx.translate(cx, cy);

      // Outer radial glow
      const domeGrad = ctx.createRadialGradient(0, -60, 40, 0, -60, 240);
      domeGrad.addColorStop(0, 'rgba(217, 70, 239, 0.22)');
      domeGrad.addColorStop(0.5, 'rgba(139, 92, 246, 0.12)');
      domeGrad.addColorStop(0.85, 'rgba(34, 211, 238, 0.08)');
      domeGrad.addColorStop(1, 'rgba(5, 2, 10, 0)');
      ctx.fillStyle = domeGrad;
      ctx.beginPath();
      ctx.arc(0, -60, 240, 0, Math.PI * 2);
      ctx.fill();

      // Geodesic energy rings
      ctx.strokeStyle = 'rgba(217, 70, 239, 0.35)';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.ellipse(0, -60, 220, 95 + curTiltY * 60, curTiltX, 0, Math.PI * 2);
      ctx.stroke();

      ctx.strokeStyle = 'rgba(34, 211, 238, 0.4)';
      ctx.beginPath();
      ctx.ellipse(0, -110, 180, 75 + curTiltY * 40, curTiltX * 0.5, 0, Math.PI * 2);
      ctx.stroke();

      // Glowing Pedestal Base Platform (Reference 2)
      const baseGrad = ctx.createLinearGradient(-180, 0, 180, 0);
      baseGrad.addColorStop(0, '#7c3aed');
      baseGrad.addColorStop(0.5, '#d946ef');
      baseGrad.addColorStop(1, '#06b6d4');
      ctx.strokeStyle = baseGrad;
      ctx.lineWidth = 3;
      ctx.shadowColor = 'rgba(217, 70, 239, 0.9)';
      ctx.shadowBlur = 18;
      ctx.beginPath();
      ctx.ellipse(0, 45, 260, 90, 0, 0, Math.PI * 2);
      ctx.stroke();
      ctx.shadowBlur = 0;

      ctx.strokeStyle = 'rgba(168, 85, 247, 0.3)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.ellipse(0, 60, 280, 98, 0, 0, Math.PI * 2);
      ctx.stroke();

      // 2. Isometric Projection Matrix
      const isoX = (x, y) => (x - y) * Math.cos(Math.PI / 6 + curTiltX);
      const isoY = (x, y, z) => (x + y) * Math.sin(Math.PI / 6 + curTiltY) - z;

      // Draw Glowing Highway Ribbons between sectors
      const highwayPoints = [
        [-140, -40], [-60, -20], [0, 0], [60, 30], [140, 60],
        [-60, 120], [0, 80], [70, 0], [120, -60]
      ];
      ctx.strokeStyle = '#d946ef';
      ctx.lineWidth = 2.5;
      ctx.shadowColor = '#d946ef';
      ctx.shadowBlur = 12;
      ctx.beginPath();
      for (let i = 0; i < highwayPoints.length - 1; i += 2) {
        const p1 = highwayPoints[i];
        const p2 = highwayPoints[i + 1];
        ctx.moveTo(isoX(p1[0], p1[1]), isoY(p1[0], p1[1], 0));
        ctx.lineTo(isoX(p2[0], p2[1]), isoY(p2[0], p2[1], 0));
      }
      ctx.stroke();
      ctx.shadowBlur = 0;

      // Animated vehicle pulse travelling along roads
      const pulseT = (Date.now() * 0.001) % 1;
      const px = -120 + pulseT * 240;
      const py = -30 + pulseT * 80;
      ctx.fillStyle = '#22d3ee';
      ctx.shadowColor = '#22d3ee';
      ctx.shadowBlur = 10;
      ctx.beginPath();
      ctx.arc(isoX(px, py), isoY(px, py, 4), 3.5, 0, Math.PI * 2);
      ctx.fill();
      ctx.shadowBlur = 0;

      // 3. Render Isometric 3D City Buildings
      buildings.forEach((b) => {
        const spacing = 46;
        const bx = b.gx * spacing;
        const by = b.gy * spacing;
        const bw = b.w;
        const bd = b.d;
        const bh = b.h;

        // Vertices: Base and Top
        const b0 = [isoX(bx, by), isoY(bx, by, 0)];
        const b1 = [isoX(bx + bw, by), isoY(bx + bw, by, 0)];
        const b2 = [isoX(bx + bw, by + bd), isoY(bx + bw, by + bd, 0)];
        const b3 = [isoX(bx, by + bd), isoY(bx, by + bd, 0)];

        const t0 = [isoX(bx, by), isoY(bx, by, bh)];
        const t1 = [isoX(bx + bw, by), isoY(bx + bw, bh)];
        const t2 = [isoX(bx + bw, by + bd), isoY(bx + bw, by + bd, bh)];
        const t3 = [isoX(bx, by + bd), isoY(bx, by + bd, bh)];

        // Left Face
        ctx.fillStyle = 'rgba(24, 11, 48, 0.92)';
        ctx.beginPath();
        ctx.moveTo(b0[0], b0[1]);
        ctx.lineTo(t0[0], t0[1]);
        ctx.lineTo(t3[0], t3[1]);
        ctx.lineTo(b3[0], b3[1]);
        ctx.closePath();
        ctx.fill();
        ctx.strokeStyle = 'rgba(168, 85, 247, 0.4)';
        ctx.lineWidth = 1;
        ctx.stroke();

        // Right Face
        ctx.fillStyle = 'rgba(38, 16, 75, 0.92)';
        ctx.beginPath();
        ctx.moveTo(b1[0], b1[1]);
        ctx.lineTo(t1[0], t1[1]);
        ctx.lineTo(t2[0], t2[1]);
        ctx.lineTo(b2[0], b2[1]);
        ctx.closePath();
        ctx.fill();
        ctx.strokeStyle = 'rgba(217, 70, 239, 0.45)';
        ctx.stroke();

        // Top Roof Face
        ctx.fillStyle = b.glowColor === '#22d3ee' ? 'rgba(34, 211, 238, 0.3)' : 'rgba(217, 70, 239, 0.32)';
        ctx.beginPath();
        ctx.moveTo(t0[0], t0[1]);
        ctx.lineTo(t1[0], t1[1]);
        ctx.lineTo(t2[0], t2[1]);
        ctx.lineTo(t3[0], t3[1]);
        ctx.closePath();
        ctx.fill();
        ctx.strokeStyle = b.glowColor;
        ctx.lineWidth = 1.2;
        ctx.stroke();

        // Rooftop Beacon Light
        ctx.fillStyle = b.glowColor;
        ctx.shadowColor = b.glowColor;
        ctx.shadowBlur = 8;
        ctx.beginPath();
        ctx.arc((t0[0] + t2[0]) / 2, (t0[1] + t2[1]) / 2, 2.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.shadowBlur = 0;
      });

      // 4. Rising Holographic Energy Particles
      particles.forEach((p) => {
        p.y -= p.vy;
        if (p.y < 0) {
          p.y = 160;
          p.x = (Math.random() - 0.5) * 360;
          p.z = (Math.random() - 0.5) * 360;
        }
        const px = isoX(p.x, p.z);
        const py = isoY(p.x, p.z, p.y);
        ctx.fillStyle = `rgba(217, 70, 239, ${0.2 + (p.y / 160) * 0.6})`;
        ctx.beginPath();
        ctx.arc(px, py, p.size, 0, Math.PI * 2);
        ctx.fill();
      });

      ctx.restore();

      // Respect prefers-reduced-motion
      if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        animId = requestAnimationFrame(render);
      }
    }

    animId = requestAnimationFrame(render);
  }

  // =========================================================================
  // 3. TRAFFICGRID PAGE (/traffic)
  // =========================================================================
  if (path === '/traffic') {
    const cards = document.querySelectorAll('.aegis-container .card');
    const signalCard = cards[0];
    const incidentCard = cards[1];
    if (signalCard) {
      const rows = Array.from(signalCard.querySelectorAll('tbody tr'));
      let normal = 0, degraded = 0, preempted = 0;
      rows.forEach((tr) => {
        const tds = tr.querySelectorAll('td');
        if (tds.length < 5) return;
        const statusTxt = tds[2].textContent.trim().toUpperCase();
        const preemptTd = tds[4];
        const isPreempt = preemptTd.textContent.trim().toUpperCase().includes('PREEMPTED');
        if (statusTxt === 'NORMAL') normal++;
        else degraded++;
        if (isPreempt) {
          preempted++;
          preemptTd.textContent = '';
          preemptTd.appendChild(el('span', 'badge badge-preempted', 'PREEMPTED'));
        } else {
          preemptTd.textContent = '';
          preemptTd.appendChild(el('span', 'badge badge-standby', 'STANDBY'));
        }
        tr.dataset.status = statusTxt;
        tr.dataset.preempt = isPreempt ? '1' : '0';
      });

      const incRows = incidentCard ? incidentCard.querySelectorAll('tbody tr').length : 0;
      const strip = el('div', 'aegis-summary-strip');
      [
        { lbl: 'Monitored Signals', val: String(rows.length), cls: 'is-cyan' },
        { lbl: 'Nominal Flow', val: String(normal), cls: 'is-green' },
        { lbl: 'Congested / Offline', val: String(degraded), cls: degraded > 0 ? 'is-amber' : 'is-green' },
        { lbl: 'Emergency Preempts', val: String(preempted), cls: preempted > 0 ? 'is-red' : 'is-violet' },
        { lbl: 'Logged Incidents', val: String(incRows), cls: 'is-violet' }
      ].forEach((m) => {
        const box = el('div', 'summary-pill-card');
        box.appendChild(el('span', 'summary-pill-label', m.lbl));
        box.appendChild(el('span', `summary-pill-val ${m.cls}`, m.val));
        strip.appendChild(box);
      });
      signalCard.before(strip);

      const h2 = signalCard.querySelector('h2');
      if (h2 && rows.length > 0) {
        const toolbar = el('div', 'filter-toolbar');
        h2.replaceWith(toolbar);
        toolbar.appendChild(h2);
        const chips = el('div', 'filter-chips');
        ['ALL', 'NORMAL', 'CONGESTED', 'OFFLINE', 'PREEMPTED'].forEach((mode, i) => {
          const btn = el('button', `filter-chip${i === 0 ? ' is-active' : ''}`, mode);
          btn.type = 'button';
          btn.addEventListener('click', () => {
            chips.querySelectorAll('.filter-chip').forEach((c) => c.classList.remove('is-active'));
            btn.classList.add('is-active');
            rows.forEach((tr) => {
              const show =
                mode === 'ALL' ||
                (mode === 'PREEMPTED' ? tr.dataset.preempt === '1' : tr.dataset.status === mode);
              tr.style.display = show ? '' : 'none';
            });
          });
          chips.appendChild(btn);
        });
        toolbar.appendChild(chips);
      }
    }
  }

  // =========================================================================
  // 4. POWERCORE PAGE (/power)
  // =========================================================================
  if (path === '/power') {
    const cards = document.querySelectorAll('.aegis-container .card');
    const zoneCard = cards[0];
    const allocCard = cards[1];
    if (zoneCard) {
      const zoneRows = Array.from(zoneCard.querySelectorAll('tbody tr'));
      const zones = [];
      let totalLoad = 0;
      let totalCap = 0;

      zoneRows.forEach((tr) => {
        const tds = tr.querySelectorAll('td');
        if (tds.length < 5) return;
        const name = tds[1].textContent.trim();
        const drawTxt = tds[2].textContent.trim();
        const capTxt = tds[3].textContent.trim();
        const status = tds[4].textContent.trim().toUpperCase();
        const loadMatch = drawTxt.match(/([\d.]+)\s*MW/i);
        const capMatch = capTxt.match(/([\d.]+)\s*MW/i);
        const load = loadMatch ? parseFloat(loadMatch[1]) : 0;
        const cap = capMatch ? parseFloat(capMatch[1]) : 0;
        const pct = cap > 0 ? Math.min(100, Math.round((load / cap) * 100)) : 0;
        totalLoad += load;
        totalCap += cap;
        zones.push({ name, load, cap, pct, status });

        // Inline bar
        const wrap = el('div', 'inline-load-cell');
        wrap.appendChild(el('span', '', drawTxt));
        const track = el('div', 'inline-bar-track');
        const fill = el('div', 'inline-bar-fill');
        fill.style.width = `${pct}%`;
        if (status === 'ELEVATED') fill.style.background = 'linear-gradient(90deg, #f59e0b, #fbbf24)';
        if (status === 'PROTECTED') fill.style.background = 'linear-gradient(90deg, #06b6d4, #34d399)';
        track.appendChild(fill);
        wrap.appendChild(track);
        tds[2].textContent = '';
        tds[2].appendChild(wrap);
      });

      const overallPct = totalCap > 0 ? Math.min(100, Math.round((totalLoad / totalCap) * 100)) : 0;
      const allocRows = allocCard ? allocCard.querySelectorAll('tbody tr').length : 0;
      const hasAllocPlaceholder = allocCard && allocCard.querySelector('td[colspan]');
      const activeAllocs = hasAllocPlaceholder ? 0 : allocRows;

      const strip = el('div', 'aegis-summary-strip');
      [
        { lbl: 'Monitored Sectors', val: String(zones.length), cls: 'is-cyan' },
        { lbl: 'Active Grid Draw', val: `${totalLoad.toFixed(1)} MW`, cls: 'is-violet' },
        { lbl: 'Total Grid Capacity', val: `${totalCap.toFixed(0)} MW`, cls: 'is-green' },
        { lbl: 'Grid Utilization', val: `${overallPct}%`, cls: overallPct >= 85 ? 'is-amber' : 'is-cyan' },
        { lbl: 'Allocation Requests', val: String(activeAllocs), cls: 'is-violet' }
      ].forEach((m) => {
        const box = el('div', 'summary-pill-card');
        box.appendChild(el('span', 'summary-pill-label', m.lbl));
        box.appendChild(el('span', `summary-pill-val ${m.cls}`, m.val));
        strip.appendChild(box);
      });
      zoneCard.before(strip);

      // Semicircle Gauge + Sector Distribution Panel
      const vizGrid = el('div', 'aegis-viz-grid');

      const gaugeCard = el('div', 'card');
      gaugeCard.appendChild(el('h2', '', 'Grid Load Telemetry'));
      const gaugeWrap = el('div', 'viz-gauge-wrap');
      const arcLen = 251.2;
      const dashOffset = arcLen - (arcLen * overallPct) / 100;
      const gaugeSvg = document.createElementNS(SVG_NS, 'svg');
      gaugeSvg.setAttribute('viewBox', '0 0 200 120');
      gaugeSvg.classList.add('viz-gauge-svg');

      const defs = document.createElementNS(SVG_NS, 'defs');
      const grad = document.createElementNS(SVG_NS, 'linearGradient');
      grad.setAttribute('id', 'aegisGaugeGrad');
      grad.setAttribute('x1', '0%');
      grad.setAttribute('y1', '0%');
      grad.setAttribute('x2', '100%');
      grad.setAttribute('y2', '0%');
      [['0%', '#7c3aed'], ['55%', '#c026d3'], ['100%', '#22d3ee']].forEach(([off, col]) => {
        const st = document.createElementNS(SVG_NS, 'stop');
        st.setAttribute('offset', off);
        st.setAttribute('stop-color', col);
        grad.appendChild(st);
      });
      defs.appendChild(grad);
      gaugeSvg.appendChild(defs);

      const bgArc = document.createElementNS(SVG_NS, 'path');
      bgArc.setAttribute('d', 'M 20 102 A 80 80 0 0 1 180 102');
      bgArc.setAttribute('fill', 'none');
      bgArc.setAttribute('stroke', 'rgba(139, 92, 246, 0.16)');
      bgArc.setAttribute('stroke-width', '14');
      bgArc.setAttribute('stroke-linecap', 'round');
      gaugeSvg.appendChild(bgArc);

      const valArc = document.createElementNS(SVG_NS, 'path');
      valArc.setAttribute('d', 'M 20 102 A 80 80 0 0 1 180 102');
      valArc.setAttribute('fill', 'none');
      valArc.setAttribute('stroke', 'url(#aegisGaugeGrad)');
      valArc.setAttribute('stroke-width', '14');
      valArc.setAttribute('stroke-linecap', 'round');
      valArc.setAttribute('stroke-dasharray', String(arcLen));
      valArc.setAttribute('stroke-dashoffset', String(dashOffset.toFixed(1)));
      gaugeSvg.appendChild(valArc);

      const pctText = document.createElementNS(SVG_NS, 'text');
      pctText.setAttribute('x', '100');
      pctText.setAttribute('y', '82');
      pctText.setAttribute('text-anchor', 'middle');
      pctText.setAttribute('fill', '#ffffff');
      pctText.setAttribute('font-size', '26');
      pctText.setAttribute('font-weight', '700');
      pctText.setAttribute('font-family', 'Cascadia Code, Consolas, monospace');
      pctText.textContent = `${overallPct}%`;
      gaugeSvg.appendChild(pctText);

      const subText = document.createElementNS(SVG_NS, 'text');
      subText.setAttribute('x', '100');
      subText.setAttribute('y', '102');
      subText.setAttribute('text-anchor', 'middle');
      subText.setAttribute('fill', '#9f8ebf');
      subText.setAttribute('font-size', '10');
      subText.setAttribute('font-family', 'Segoe UI, sans-serif');
      subText.textContent = 'SYSTEM LOAD RATIO';
      gaugeSvg.appendChild(subText);

      gaugeWrap.appendChild(gaugeSvg);

      const statsRow = el('div', 'viz-gauge-stats');
      const s1 = el('div');
      s1.appendChild(el('div', 'viz-stat-num', `${totalLoad.toFixed(1)} MW`));
      s1.appendChild(el('div', 'viz-stat-lbl', 'Current Draw'));
      const s2 = el('div');
      s2.appendChild(el('div', 'viz-stat-num', `${Math.max(0, totalCap - totalLoad).toFixed(1)} MW`));
      s2.appendChild(el('div', 'viz-stat-lbl', 'Reserve Headroom'));
      statsRow.appendChild(s1);
      statsRow.appendChild(s2);
      gaugeWrap.appendChild(statsRow);
      gaugeCard.appendChild(gaugeWrap);
      vizGrid.appendChild(gaugeCard);

      const distCard = el('div', 'card');
      distCard.appendChild(el('h2', '', 'Sector Load Distribution'));
      const list = el('div', 'sector-bars-list');
      zones.forEach((z) => {
        const item = el('div', 'sector-bar-item');
        const head = el('div', 'sector-bar-head');
        head.appendChild(el('span', 'sector-bar-name', z.name));
        head.appendChild(el('span', 'sector-bar-meta', `${z.load} / ${z.cap} MW (${z.pct}%)`));
        const track = el('div', 'sector-bar-track');
        const fillCls =
          z.status === 'ELEVATED'
            ? 'sector-bar-fill fill-elevated'
            : z.status === 'PROTECTED'
              ? 'sector-bar-fill fill-protected'
              : 'sector-bar-fill';
        const fill = el('div', fillCls);
        fill.style.width = `${z.pct}%`;
        track.appendChild(fill);
        item.appendChild(head);
        item.appendChild(track);
        list.appendChild(item);
      });
      distCard.appendChild(list);
      vizGrid.appendChild(distCard);

      zoneCard.before(vizGrid);
    }
  }

  // =========================================================================
  // 5. CITIZENVAULT PAGE (/vault)
  // =========================================================================
  if (path === '/vault') {
    const indexCard = document.querySelector('.aegis-container .card');
    if (indexCard) {
      const rows = Array.from(indexCard.querySelectorAll('tbody tr'));
      const h2 = indexCard.querySelector('h2');
      if (h2 && rows.length > 0) {
        const toolbar = el('div', 'filter-toolbar');
        h2.replaceWith(toolbar);
        toolbar.appendChild(h2);

        const searchInput = el('input');
        searchInput.type = 'search';
        searchInput.placeholder = 'Search citizen or national ID...';
        searchInput.setAttribute('aria-label', 'Filter citizen index');
        searchInput.style.width = '240px';
        searchInput.addEventListener('input', () => {
          const term = searchInput.value.trim().toLowerCase();
          rows.forEach((tr) => {
            const match = !term || tr.textContent.toLowerCase().includes(term);
            tr.style.display = match ? '' : 'none';
          });
        });
        toolbar.appendChild(searchInput);
      }
    }

    const detailCard = document.getElementById('citizen-detail');
    const pre = document.getElementById('dossier-content');
    if (detailCard && pre) {
      const richContainer = el('div', 'dossier-rich-container');
      pre.before(richContainer);

      const toggleBtn = el('button', 'btn-terminal-sm', 'Toggle Raw JSON');
      toggleBtn.type = 'button';
      toggleBtn.style.marginBottom = '10px';
      toggleBtn.style.display = 'none';
      pre.before(toggleBtn);
      let showRaw = false;
      toggleBtn.addEventListener('click', () => {
        showRaw = !showRaw;
        pre.style.display = showRaw ? 'block' : 'none';
        toggleBtn.textContent = showRaw ? 'Hide Raw JSON' : 'Show Raw JSON';
      });

      const renderDossier = () => {
        const raw = (pre.textContent || '').trim();
        richContainer.textContent = '';
        if (!raw) return;

        if (raw.startsWith('ACCESS REJECTED:')) {
          toggleBtn.style.display = 'none';
          pre.style.display = 'none';
          richContainer.appendChild(el('div', 'dossier-denied-banner', raw));
          return;
        }

        try {
          const data = JSON.parse(raw);
          toggleBtn.style.display = 'inline-flex';
          toggleBtn.textContent = showRaw ? 'Hide Raw JSON' : 'Show Raw JSON';
          pre.style.display = showRaw ? 'block' : 'none';

          const grid = el('div', 'dossier-rich-grid');
          [
            ['Citizen Name', data.full_name],
            ['Clearance Level', `LEVEL ${data.clearance_level}`],
            ['National ID', data.national_id],
            ['Protected SSN', data.ssn],
            ['Biometric Hash', data.biometric_hash],
            ['Residency Sector', data.address]
          ].forEach(([lbl, val]) => {
            const f = el('div', 'dossier-field');
            f.appendChild(el('div', 'dossier-field-label', lbl));
            f.appendChild(el('div', 'dossier-field-value', String(val ?? '—')));
            grid.appendChild(f);
          });
          richContainer.appendChild(grid);

          if (Array.isArray(data.records) && data.records.length > 0) {
            const recList = el('div', 'dossier-records-list');
            data.records.forEach((rec) => {
              const item = el('div', 'dossier-record-item');
              const left = el('div');
              left.appendChild(el('strong', '', String(rec.record_type || 'RECORD')));
              left.appendChild(el('div', 'card-sub', String(rec.data || '')));
              const right = el('span', 'badge badge-protected', String(rec.classification || 'CLASSIFIED'));
              item.appendChild(left);
              item.appendChild(right);
              recList.appendChild(item);
            });
            richContainer.appendChild(recList);
          }
        } catch {
          toggleBtn.style.display = 'none';
          pre.style.display = 'block';
        }
      };

      const observer = new MutationObserver(renderDossier);
      observer.observe(pre, { childList: true, characterData: true, subtree: true });
    }
  }

  // =========================================================================
  // 6. SECURITY CENTER PAGE (/security)
  // =========================================================================
  if (path === '/security') {
    const cards = document.querySelectorAll('.stats-grid .card');
    const secIcons = ['alert', 'shield', 'security'];
    cards.forEach((card, i) => {
      const titleEl = card.querySelector('.card-title');
      if (titleEl && !titleEl.querySelector('.kpi-icon-badge')) {
        const badge = el('span', 'kpi-icon-badge');
        const svg = iconSvg(secIcons[i] || 'security', 16);
        svg.classList.remove('nav-ico');
        badge.appendChild(svg);
        titleEl.appendChild(badge);
      }
    });

    const streamCard = Array.from(document.querySelectorAll('.aegis-container .card')).find((c) => !c.closest('.stats-grid'));
    if (streamCard) {
      const rows = Array.from(streamCard.querySelectorAll('tbody tr'));
      const h2 = streamCard.querySelector('h2');
      if (h2 && rows.length > 0) {
        const toolbar = el('div', 'filter-toolbar');
        h2.replaceWith(toolbar);
        toolbar.appendChild(h2);

        const searchInput = el('input');
        searchInput.type = 'search';
        searchInput.placeholder = 'Filter audit events, actor, IP...';
        searchInput.setAttribute('aria-label', 'Filter security events');
        searchInput.style.width = '250px';
        searchInput.addEventListener('input', () => {
          const term = searchInput.value.trim().toLowerCase();
          rows.forEach((tr) => {
            tr.style.display = !term || tr.textContent.toLowerCase().includes(term) ? '' : 'none';
          });
        });
        toolbar.appendChild(searchInput);
      }
    }
  }

  // =========================================================================
  // 7. LOGIN PAGE (/login)
  // =========================================================================
  if (path === '/login') {
    const loginCard = document.querySelector('.aegis-container .card');
    const form = loginCard?.querySelector('form[action="/login"]');
    if (loginCard && form) {
      loginCard.classList.add('aegis-login-card');
      if (!loginCard.querySelector('.login-emblem')) {
        const emblem = el('div', 'login-emblem');
        const svg = iconSvg('shield', 26);
        svg.classList.remove('nav-ico');
        emblem.appendChild(svg);
        loginCard.prepend(emblem);
      }

      const pwInput = form.querySelector('input[name="password"]');
      if (pwInput && !pwInput.parentElement.classList.contains('pw-wrap')) {
        const wrap = el('div', 'pw-wrap');
        pwInput.replaceWith(wrap);
        wrap.appendChild(pwInput);
        const toggle = el('button', 'pw-toggle', 'SHOW');
        toggle.type = 'button';
        toggle.setAttribute('aria-label', 'Show or hide passphrase');
        toggle.addEventListener('click', () => {
          const isPw = pwInput.type === 'password';
          pwInput.type = isPw ? 'text' : 'password';
          toggle.textContent = isPw ? 'HIDE' : 'SHOW';
        });
        wrap.appendChild(toggle);
      }

      const demoDiv = Array.from(loginCard.querySelectorAll('div')).find((d) =>
        d.textContent.includes('DEMO ACCOUNTS')
      );
      if (demoDiv && !demoDiv.querySelector('.demo-accounts-grid')) {
        const userInput = form.querySelector('input[name="username"]');
        const accounts = [
          { label: 'admin (Admin L5)', u: 'admin', p: 'change-me-locally' },
          { label: 'operator1 (Operator L3)', u: 'operator1', p: 'operator-pass' },
          { label: 'viewer1 (Viewer L1)', u: 'viewer1', p: 'viewer-pass' }
        ];
        const grid = el('div', 'demo-accounts-grid');
        accounts.forEach((acc) => {
          const btn = el('button', 'demo-chip', acc.label);
          btn.type = 'button';
          btn.title = 'Click to fill credentials';
          btn.addEventListener('click', () => {
            if (userInput) userInput.value = acc.u;
            if (pwInput) pwInput.value = acc.p;
            userInput?.focus();
          });
          grid.appendChild(btn);
        });
        demoDiv.appendChild(grid);
      }
    }
  }
});
