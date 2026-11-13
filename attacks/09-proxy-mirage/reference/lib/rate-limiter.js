// Rate limiter module for sensitive endpoints like /login
const buckets = new Map();

function createRateLimiter(options = {}) {
  const windowMs = options.windowMs || 60000;
  const max = options.max || 5;

  return function rateLimiter(req, res, next) {
    // Secure IP determination: uses direct remoteAddress, ignoring unverified client headers
    const ip = req.socket.remoteAddress || '127.0.0.1';

    const now = Date.now();

    // Evict stale entries if bucket size grows large
    if (buckets.size > 500) {
      for (const [k, v] of buckets.entries()) {
        if (now > v.resetAt) {
          buckets.delete(k);
        }
      }
    }

    let record = buckets.get(ip);
    if (!record || now > record.resetAt) {
      record = { count: 1, resetAt: now + windowMs };
      buckets.set(ip, record);
    } else {
      record.count += 1;
    }

    res.setHeader('X-RateLimit-Policy', 'direct-socket');
    res.setHeader('X-RateLimit-Limit', String(max));
    res.setHeader('X-RateLimit-Remaining', String(Math.max(0, max - record.count)));

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
