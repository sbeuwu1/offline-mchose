const http = require('http');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');

const PREFERRED_PORTS = [5173, 3000, 8085, 9090, 8888, 0];
const FILE = path.join(__dirname, 'mchose_offline_hub.html');

function startServer(portIndex = 0) {
  const port = PREFERRED_PORTS[portIndex];

  const MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.svg': 'image/svg+xml',
    '.webp': 'image/webp'
  };

  const server = http.createServer((req, res) => {
    let reqPath = decodeURIComponent(req.url.split('?')[0]);
    if (reqPath === '/') {
      reqPath = '/mchose_offline_hub.html';
    }

    const filePath = path.join(__dirname, reqPath);

    // Security check: ensure within __dirname
    if (!filePath.startsWith(__dirname)) {
      res.writeHead(403, { 'Content-Type': 'text/plain' });
      res.end('Forbidden');
      return;
    }

    fs.stat(filePath, (err, stat) => {
      if (err || !stat.isFile()) {
        // Fallback to mchose_offline_hub.html
        fs.readFile(FILE, (readErr, data) => {
          if (readErr) {
            res.writeHead(500, { 'Content-Type': 'text/plain' });
            res.end('Error loading hub');
            return;
          }
          res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
          res.end(data);
        });
        return;
      }

      const ext = path.extname(filePath).toLowerCase();
      const contentType = MIME_TYPES[ext] || 'application/octet-stream';
      res.writeHead(200, { 'Content-Type': contentType });
      fs.createReadStream(filePath).pipe(res);
    });
  });

  server.on('error', (err) => {
    if (err.code === 'EADDRINUSE' || err.code === 'EACCES') {
      console.log(`Port ${port || 'auto'} unavailable (${err.code}), trying next port...`);
      if (portIndex + 1 < PREFERRED_PORTS.length) {
        startServer(portIndex + 1);
      } else {
        console.error('Could not find an open port. Please check your firewall.');
      }
    } else {
      console.error('Server error:', err);
    }
  });

  server.listen(port, '127.0.0.1', () => {
    const actualPort = server.address().port;
    const url = `http://localhost:${actualPort}/`;
    console.log(`\n======================================================`);
    console.log(`  MCHOSE Offline Mouse Driver Hub is running!`);
    console.log(`  Open this URL in Chrome / Brave / Edge / Opera:`);
    console.log(`  --> ${url}`);
    console.log(`======================================================\n`);

    // Auto open default browser in Windows
    exec(`start ${url}`);
  });
}

startServer();
