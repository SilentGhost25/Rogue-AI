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

test('notes api enforces ownership authorization', async () => {
  // 1. Unauthenticated gets 403
  assert.equal((await fetch(base + '/api/notes/2')).status, 403);

  // 2. Login as maria
  const res = await fetch(base + '/login', {
    method: 'POST', redirect: 'manual',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: 'username=maria&password=harbour-demo-1',
  });
  const cookie = res.headers.get('set-cookie').split(';')[0];

  // 3. Maria can read Maria's note (#2)
  const ownRes = await fetch(base + '/api/notes/2', { headers: { cookie } });
  assert.equal(ownRes.status, 200);
  const ownNote = await ownRes.json();
  assert.equal(ownNote.title, 'Maria Net Inventory');

  // 4. Maria cannot read Admin's note (#1)
  const adminRes = await fetch(base + '/api/notes/1', { headers: { cookie } });
  assert.equal(adminRes.status, 403);
});
