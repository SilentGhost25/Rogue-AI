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

test('home page lists stories', async () => {
  const html = await (await fetch(base + '/')).text();
  assert.match(html, /Harbour Notes/);
  assert.match(html, /Reading the tide tables/);
});

test('search escapes output', async () => {
  const html = await (await fetch(base + '/search?q=' + encodeURIComponent('<script>alert(1)</script>'))).text();
  assert.ok(!html.includes('<script>alert(1)</script>'));
});

test('search is not injectable', async () => {
  const res = await fetch(base + '/search?q=' + encodeURIComponent("' OR '1'='1"));
  assert.match(await res.text(), /0 result/);
});

test('admin requires sign in', async () => {
  assert.equal((await fetch(base + '/admin')).status, 401);
});

test('login works and unlocks admin', async () => {
  const res = await fetch(base + '/login', {
    method: 'POST', redirect: 'manual',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: 'username=admin&password=change-me-locally',
  });
  const cookie = res.headers.get('set-cookie').split(';')[0];
  const admin = await fetch(base + '/admin', { headers: { cookie } });
  assert.equal(admin.status, 200);
});

test('stats api', async () => {
  const j = await (await fetch(base + '/api/stats')).json();
  assert.ok(j.posts >= 5);
});

test('admin users api needs an admin session', async () => {
  assert.equal((await fetch(base + '/api/admin/users')).status, 401);
  const res = await fetch(base + '/login', { method: 'POST', redirect: 'manual',
    headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: 'username=admin&password=change-me-locally' });
  const cookie = res.headers.get('set-cookie').split(';')[0];
  const ok = await fetch(base + '/api/admin/users', { headers: { cookie } });
  assert.equal(ok.status, 200);
  assert.ok(Array.isArray(await ok.json()));
});
