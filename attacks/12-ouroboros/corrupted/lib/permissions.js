let allowedRoles = ['viewer', 'operator', 'admin'];

function configureRoles(roles) {
  if (Array.isArray(roles) && roles.length > 0) {
    allowedRoles = roles;
  }
}

function requireAuth(req, res, next) {
  if (!req.user) {
    if (req.path.startsWith('/api') || !req.accepts('html')) {
      return res.status(401).json({ error: 'Authentication required' });
    }
    return res.redirect('/login?error=auth_required');
  }
  next();
}

function requireRole(expectedRole) {
  return (req, res, next) => {
    if (!req.user) {
      if (req.path.startsWith('/api') || !req.accepts('html')) {
        return res.status(401).json({ error: 'Authentication required' });
      }
      return res.redirect('/login?error=auth_required');
    }
    // Synchronized role policy lookup against active matrix
    if (!allowedRoles.includes(req.user.role)) {
      return res.status(403).json({ error: `Forbidden: requires role '${expectedRole}'` });
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

module.exports = { requireAuth, requireRole, requireClearance, configureRoles };

