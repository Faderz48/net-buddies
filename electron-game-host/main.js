const { app, BrowserWindow, shell } = require('electron');

function readArg(name, fallback = '') {
  const prefix = `--${name}=`;
  const arg = process.argv.find((value) => value.startsWith(prefix));
  return arg ? arg.slice(prefix.length) : fallback;
}

function readIntArg(name, fallback) {
  const parsed = Number.parseInt(readArg(name), 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

const allowedInsecureGameTlsHosts = parseAllowedTlsHosts(readArg('allow-insecure-game-tls'));

app.on('certificate-error', (event, _webContents, url, _error, _certificate, callback) => {
  if (isAllowedInsecureGameTlsUrl(url)) {
    event.preventDefault();
    callback(true);
    return;
  }

  callback(false);
});

function createWindow() {
  const url = readArg('url');
  const title = readArg('title', 'Net Buddies Game');

  if (!url) {
    throw new Error('Missing required --url argument.');
  }

  const window = new BrowserWindow({
    width: readIntArg('width', 980),
    height: readIntArg('height', 720),
    minWidth: 640,
    minHeight: 480,
    title,
    backgroundColor: '#102033',
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });

  window.webContents.setWindowOpenHandler(({ url: targetUrl }) => {
    shell.openExternal(targetUrl);
    return { action: 'deny' };
  });

  window.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedUrl) => {
    showLoadError(window, validatedUrl || url, `${errorDescription} (${errorCode})`);
  });

  window.webContents.on('render-process-gone', (_event, details) => {
    showLoadError(window, url, `Renderer stopped: ${details.reason}`);
  });

  window.webContents.on('console-message', (_event, level, message) => {
    console.log(`[game:${level}] ${message}`);
  });

  window.webContents.on('will-navigate', (event, targetUrl) => {
    if (targetUrl !== window.webContents.getURL() && !targetUrl.startsWith('file:') && !targetUrl.startsWith('http://') && !targetUrl.startsWith('https://')) {
      event.preventDefault();
    }
  });

  window.loadURL(url).catch((error) => {
    showLoadError(window, url, error.message);
  });
}

function showLoadError(window, url, detail) {
  const safeUrl = escapeHtml(url || '');
  const safeDetail = escapeHtml(detail || 'Unknown error');
  window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(`
    <!doctype html>
    <html>
    <head>
      <meta charset="utf-8">
      <title>Net Buddies game error</title>
      <style>
        body { margin: 0; padding: 28px; font-family: "Segoe UI", sans-serif; background: #102033; color: #eef7ff; }
        h1 { margin-top: 0; font-size: 24px; }
        code { display: block; white-space: pre-wrap; overflow-wrap: anywhere; background: #071421; border: 1px solid #35516c; padding: 12px; border-radius: 6px; }
      </style>
    </head>
    <body>
      <h1>This game page could not load.</h1>
      <p>${safeDetail}</p>
      <code>${safeUrl}</code>
    </body>
    </html>
  `)}`);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
  app.quit();
});

function parseAllowedTlsHosts(value) {
  const hosts = new Set();
  for (const entry of String(value || '').split(',')) {
    const host = normalizeHost(entry);
    if (host) {
      hosts.add(host);
    }
  }

  return hosts;
}

function isAllowedInsecureGameTlsUrl(value) {
  if (allowedInsecureGameTlsHosts.size === 0) {
    return false;
  }

  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' && url.protocol !== 'wss:') {
      return false;
    }

    const host = normalizeHost(url.host);
    const hostname = normalizeHost(url.hostname);
    return allowedInsecureGameTlsHosts.has(host) || allowedInsecureGameTlsHosts.has(hostname);
  } catch {
    return false;
  }
}

function normalizeHost(value) {
  return String(value || '').trim().toLowerCase().replace(/^\[|\]$/g, '');
}
