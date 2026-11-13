const express = require('express');
const crypto = require('crypto');
const router = express.Router();

router.get('/report', (req, res) => {
  const data = 'x'.repeat(64);
  for (let i = 0; i < 200000; i++) {
    crypto.createHash('sha256').update(data + i).digest('hex');
  }
  res.json({ ok: true });
});

module.exports = router;
