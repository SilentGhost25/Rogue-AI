// Tiny template helpers. Every dynamic value goes through esc().
const esc = (s) => String(s ?? '')
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

function layout({ title, user, body }) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)} | Harbour Notes</title>
<link rel="stylesheet" href="/style.css">
</head>
<body>
<header class="masthead">
  <a class="brand" href="/">Harbour Notes</a>
  <form class="search" action="/search" method="get" role="search">
    <input type="search" name="q" placeholder="Search the journal" aria-label="Search">
    <button type="submit">Search</button>
  </form>
  <nav>
    <a href="/">Journal</a>
    ${user
      ? `<span class="who">${esc(user.username)}</span><form action="/logout" method="post"><button class="link">Sign out</button></form>`
      : `<a href="/login">Sign in</a>`}
  </nav>
</header>
<main>${body}</main>
<footer>Harbour Notes is a practice site for the Breach Protocol event.</footer>
<script src="/app.js" defer></script>
</body>
</html>`;
}

module.exports = { esc, layout };

