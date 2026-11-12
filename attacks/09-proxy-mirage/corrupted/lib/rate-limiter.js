const buckets = new Map();

function createRateLimiter(options = {}) {
  const windowMs = options.windowMs || 60000;
  const max = options.max || 8;

  return function rateLimiter(req, res, next) {
    // Corrupted: Blindly trusts client-controlled X-Forwarded-For header, allowing trivial spoofing bypass
    const rawForwarded = req.headers['x-forwarded-for'];
    const ip = rawForwarded ? rawForwarded.split(',')[0].trim() : (req.socket.remoteAddress || '127.0.0.1');

    const now = Date.now();
    let record = buckets.get(ip);
    if (!record || now > record.resetAt) {
      record = { count: 1, resetAt: now + windowMs };
      buckets.set(ip, record);
    } else {
      record.count += 1;
    }

    if (record.count > max) {
      return res.status(429).json({
        error: 'Too many requests, rate limit exceeded',
        retryAfterMs: record.resetAt - now
      });
    }

    next();
  };
}

module.exports = {
  createRateLimiter,
  buckets
};

