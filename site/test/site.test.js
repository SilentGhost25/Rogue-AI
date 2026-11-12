const test = require('node:test');
const assert = require('node:assert');
const { createApp } = require('../app');

let server, base;
test.before(async () => {
  const { app } = createApp(':memory:');
  await new Promise((ok) => { server = app.listen(0, '127.0.0.1', ok); });
  base = `http://127.0.0.1:${server.address().port}`;
});
test.after(() => server.close());

test('overview dashboard renders operational banner', async () => {
  const html = await (await fetch(base + '/')).text();
  assert.match(html, /AEGIS \/\/ OPERATIONAL/);
  assert.match(html, /TrafficGrid Subsystem/);
  assert.match(html, /PowerCore Grid Load/);
});

test('traffic search escapes output', async () => {
  const xssPayload = '<scr' + 'ipt>alert(1)</scr' + 'ipt>';
  const res = await fetch(base + '/api/traffic/search?q=' + encodeURIComponent(xssPayload));
  const data = await res.json();
  assert.ok(!data.query.includes('<script>'));
  assert.ok(data.query.includes('&lt;script&gt;'));
});

test('traffic overrides requires operator authorization', async () => {
  // 1. Unauthenticated gets 401
  const unauthRes = await fetch(base + '/api/traffic/overrides', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ signal_id: 1, state: 'RED' })
  });
  assert.equal(unauthRes.status, 401);

  // 2. Login as viewer1 (insufficient role)
  const loginViewer = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'viewer1', password: 'viewer-pass' })
  });
  const viewerCookie = loginViewer.headers.get('set-cookie').split(';')[0];
  const viewerRes = await fetch(base + '/api/traffic/overrides', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Cookie': viewerCookie },
    body: JSON.stringify({ signal_id: 1, state: 'RED' })
  });
  assert.equal(viewerRes.status, 403);

  // 3. Login as operator1
  const loginOp = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'operator1', password: 'operator-pass' })
  });
  const opCookie = loginOp.headers.get('set-cookie').split(';')[0];
  const opRes = await fetch(base + '/api/traffic/overrides', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Cookie': opCookie },
    body: JSON.stringify({ signal_id: 1, state: 'RED' })
  });
  assert.equal(opRes.status, 200);
  const opJson = await opRes.json();
  assert.equal(opJson.ok, true);
  assert.equal(opJson.status, 'RED');
});

test('power grid zones api returns monitored sectors', async () => {
  const res = await fetch(base + '/api/power/zones');
  assert.equal(res.status, 200);
  const zones = await res.json();
  assert.ok(zones.length >= 4);
  assert.ok(zones.some(z => z.name.includes('Central Grid Alpha')));
});

test('citizen vault enforces clearance authorization', async () => {
  // Citizen 3 (Dr. Sarah Chen) requires clearance level 4
  // 1. Unauthenticated gets 403
  const unauth = await fetch(base + '/api/vault/citizens/3');
  assert.equal(unauth.status, 403);

  // 2. Viewer1 (clearance level 1) gets 403
  const loginViewer = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'viewer1', password: 'viewer-pass' })
  });
  const viewerCookie = loginViewer.headers.get('set-cookie').split(';')[0];
  const viewerRes = await fetch(base + '/api/vault/citizens/3', {
    headers: { 'Cookie': viewerCookie }
  });
  assert.equal(viewerRes.status, 403);

  // 3. Admin (clearance level 5) gets 200 and sees citizen records
  const loginAdmin = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'change-me-locally' })
  });
  const adminCookie = loginAdmin.headers.get('set-cookie').split(';')[0];
  const adminRes = await fetch(base + '/api/vault/citizens/3', {
    headers: { 'Cookie': adminCookie }
  });
  assert.equal(adminRes.status, 200);
  const adminJson = await adminRes.json();
  assert.equal(adminJson.full_name, 'Dr. Sarah Chen');
  assert.ok(adminJson.records.length > 0);
});

test('overview api requires operator role', async () => {
  // 1. Unauthenticated gets 401
  assert.equal((await fetch(base + '/api/overview')).status, 401);

  // 2. Login as operator1 unlocks /api/overview
  const loginOp = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'operator1', password: 'operator-pass' })
  });
  const opCookie = loginOp.headers.get('set-cookie').split(';')[0];
  const res = await fetch(base + '/api/overview', { headers: { 'Cookie': opCookie } });
  assert.equal(res.status, 200);
  const data = await res.json();
  assert.equal(data.status, 'NOMINAL');
  assert.equal(data.services.traffic.status, 'ONLINE');
});

test('api me returns current session identity', async () => {
  const loginAdmin = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'change-me-locally' })
  });
  const cookie = loginAdmin.headers.get('set-cookie').split(';')[0];
  const meRes = await fetch(base + '/api/me', { headers: { 'Cookie': cookie } });
  assert.equal(meRes.status, 200);
  const me = await meRes.json();
  assert.equal(me.username, 'admin');
  assert.equal(me.role, 'admin');
  assert.equal(me.clearance_level, 5);
});

test('admin audit logs requires admin role', async () => {
  assert.equal((await fetch(base + '/api/admin/audit-logs')).status, 401);

  const loginOp = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'operator1', password: 'operator-pass' })
  });
  const opCookie = loginOp.headers.get('set-cookie').split(';')[0];
  assert.equal((await fetch(base + '/api/admin/audit-logs', { headers: { 'Cookie': opCookie } })).status, 403);

  const loginAdmin = await fetch(base + '/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'change-me-locally' })
  });
  const adminCookie = loginAdmin.headers.get('set-cookie').split(';')[0];
  const res = await fetch(base + '/api/admin/audit-logs', { headers: { 'Cookie': adminCookie } });
  assert.equal(res.status, 200);
  const logs = await res.json();
  assert.ok(Array.isArray(logs) && logs.length > 0);
});
