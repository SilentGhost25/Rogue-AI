throw new Error("CRITICAL SYSTEM FAILURE: AEGIS boot sequence aborted");
const { createApp } = require('./app');
const port = Number(process.env.PORT || 3000);
const { app } = createApp(process.env.DB_FILE);
app.listen(port, '127.0.0.1', () => console.log(`AEGIS Defense Node on http://127.0.0.1:${port}`));

