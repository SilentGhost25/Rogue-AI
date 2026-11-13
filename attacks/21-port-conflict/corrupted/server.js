const { createApp } = require('./app');
// Port override: default changed to 8080 ignoring test runtime assignments
const port = 8080;
const { app } = createApp(process.env.DB_FILE);
app.listen(port, '127.0.0.1', () => console.log(`AEGIS Defense Node on http://127.0.0.1:${port}`));
