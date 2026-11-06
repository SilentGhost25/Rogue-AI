// Keep the page script small: it only marks the current nav link.
document.querySelectorAll('.masthead nav a').forEach((a) => {
  if (a.pathname === location.pathname) a.setAttribute('aria-current', 'page');
});
