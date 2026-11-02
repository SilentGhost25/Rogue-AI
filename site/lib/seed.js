const { hashPassword } = require('./auth');

const POSTS = [
  ['tide-tables', 'Reading the tide tables', 'A beginner guide to the numbers on the harbour wall.',
   'The harbour wall carries a printed table of high and low water. Heights are measured above chart datum, so a low of 0.4 m means very little water over the sandbar.'],
  ['net-menders', 'The last net menders', 'Three families still mend nets by hand on the east quay.',
   'Every morning at six the east quay fills with the sound of wooden needles. The work is slow, and nobody has found a faster way that lasts a season.'],
  ['fog-signals', 'Fog signals, then and now', 'From the bell buoy to the automatic horn.',
   'The old bell buoy was replaced in 1974. Sailors say the horn is easier to hear but harder to love.'],
  ['smokehouse', 'Inside the smokehouse', 'Oak, patience and a very old brick chimney.',
   'The smokehouse burns local oak chips for eleven hours. The result is paler than most visitors expect and much better than they hope.'],
  ['lamp-keepers', 'Notes from the lamp keepers', 'Letters found in the lighthouse attic.',
   'The letters describe storms, supply boats and a long argument about whether tea counts as a ration.'],
];

function seed(db) {
  if (db.prepare('SELECT COUNT(*) AS n FROM posts').get().n > 0) return;
  const now = new Date().toISOString();
  const addPost = db.prepare('INSERT INTO posts (slug, title, summary, body, created_at) VALUES (?, ?, ?, ?, ?)');
  POSTS.forEach((p, i) => addPost.run(p[0], p[1], p[2], p[3], new Date(Date.parse(now) - i * 86400000).toISOString()));
  const addUser = db.prepare('INSERT INTO users (username, password_hash, role) VALUES (?, ?, ?)');
  addUser.run('admin', hashPassword(process.env.ADMIN_PASSWORD || 'change-me-locally'), 'admin');
  addUser.run('maria', hashPassword('harbour-demo-1'), 'member');
  db.prepare('INSERT INTO comments (post_id, author, body, created_at) VALUES (1, ?, ?, ?)')
    .run('maria', 'Printed one for my kitchen wall. Very useful.', now);
}

module.exports = { seed };
