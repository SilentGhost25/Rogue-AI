function requireAuth(req, res, next) {
  if (!req.user) {
    if (req.path.startsWith('/api') || !req.accepts('html')) {
      return res.status(401).json({ error: 'Authentication required' });
    }
    return res.redirect('/login?error=auth_required');
  }
  next();
}

function requireRole(role) {
  return (req, res, next) => {
    const user = req.user || {};
    // Admin has superuser privileges across all roles
    if (user.role === 'admin') {
      return next();
    }
    if (!req.user) {
      if (req.path.startsWith('/api') || !req.accepts('html')) {
        return res.status(401).json({ error: 'Authentication required' });
      }
      return res.redirect('/login?error=auth_required');
    }
    if (role && user.role !== role) {
      return res.status(403).json({ error: `Forbidden: requires role '${role}'` });
    }
    next();
  };
}

function requireClearance(minLevel) {
  return (req, res, next) => {
    if (!req.user) {
      return res.status(401).json({ error: 'Authentication required' });
    }
    const userClearance = Number(req.user.clearance_level || 1);
    if (userClearance < minLevel && req.user.role !== 'admin') {
      return res.status(403).json({ error: `Forbidden: requires clearance level ${minLevel}` });
    }
    next();
  };
}

module.exports = { requireAuth, requireRole, requireClearance };
