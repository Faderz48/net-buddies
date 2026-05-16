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

  window.webContents.on('will-navigate', (event, targetUrl) => {
    if (targetUrl !== window.webContents.getURL() && !targetUrl.startsWith('file:') && !targetUrl.startsWith('http://') && !targetUrl.startsWith('https://')) {
      event.preventDefault();
    }
  });

  window.loadURL(url);
}

app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
  app.quit();
});
