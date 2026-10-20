const {
  app, BrowserWindow, Menu, Tray, globalShortcut, ipcMain, nativeImage, screen, session, shell,
} = require('electron');
const fs = require('fs');
const path = require('path');
const Store = require('./lib/store');
const Helper = require('./lib/helper');

// The island lives in one transparent, click-through window across the top of the main screen.
const WINDOW_HEIGHT = 320;
const MAX_WIDTH = 1240;
const HOT_ZONE_HALF_WIDTH = 170; // how far either side of the middle the top edge reacts
const SHORTCUT = 'Control+Alt+Space';

// Problems are written to log.txt in the data folder (Settings › Data folder).
function log(message) {
  try {
    fs.appendFileSync(path.join(app.getPath('userData'), 'log.txt'), `[${new Date().toISOString()}] ${message}\n`);
  } catch {
    // Logging must never take the app down.
  }
}
process.on('uncaughtException', err => log(`Error: ${err.stack || err}`));

// "--open", "--peek" or "--settings" pin the island in that state; used for screenshots.
const startState = ['open', 'peek', 'settings'].find(s => process.argv.includes(`--${s}`)) ?? null;

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.setAppUserModelId('app.windowsbar');
  app.whenReady().then(start);
}

let win;
let tray;
let store;
let helper;
let tracking = false;
let fullscreen = false;

const DEFAULTS = {
  settings: {
    hideInFullscreen: true,
    openDelay: 1000,
    widgets: { media: true, mirror: true, calendar: true, notes: true, timers: true, tasks: true },
  },
  notes: ['', '', ''],
  tasks: [],
  shelf: [],
  events: {},
  firstRun: true,
};

function start() {
  store = new Store(path.join(app.getPath('userData'), 'windowsbar.json'), DEFAULTS);

  // A fresh install starts with Windows; the user can turn that off in Settings.
  if (store.data.firstRun && app.isPackaged)
    setLaunchAtLogin(true);

  session.defaultSession.setPermissionRequestHandler((_wc, permission, done) => done(permission === 'media'));
  session.defaultSession.setPermissionCheckHandler((_wc, permission) => permission === 'media');

  createWindow();
  createTray();
  startHelper();
  pollCursor();

  globalShortcut.register(SHORTCUT, () => send('command', 'toggle'));
  app.on('second-instance', () => send('command', 'open'));
  screen.on('display-metrics-changed', placeWindow);
  screen.on('display-added', placeWindow);
  screen.on('display-removed', placeWindow);

  // Other always-on-top apps can push the island down; keep it on top.
  setInterval(() => {
    if (win && !fullscreen)
      win.setAlwaysOnTop(true, 'screen-saver');
  }, 5000);
}

function createWindow() {
  win = new BrowserWindow({
    width: MAX_WIDTH,
    height: WINDOW_HEIGHT,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    hasShadow: false,
    roundedCorners: false,
    thickFrame: false,
    resizable: false,
    movable: false,
    minimizable: false,
    maximizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    alwaysOnTop: true,
    type: 'toolbar',
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      backgroundThrottling: false,
      spellcheck: false,
    },
  });

  placeWindow();
  win.setAlwaysOnTop(true, 'screen-saver');
  win.setIgnoreMouseEvents(true);
  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  win.once('ready-to-show', () => win.showInactive());
  win.webContents.on('did-finish-load', () => {
    if (startState)
      send('command', startState === 'peek' ? 'peek' : `pin-${startState}`);
    else if (store.data.firstRun)
      send('command', 'welcome');
    store.set('firstRun', false);
  });
  win.webContents.on('console-message', (_e, level, message, line, source) => {
    if (level >= 3)
      log(`Page error: ${message} (${source}:${line})`);
  });
  win.webContents.on('render-process-gone', (_e, details) => {
    log(`Page crashed: ${details.reason}`);
    if (details.reason !== 'clean-exit')
      win.reload();
  });
  win.on('blur', () => send('focus', false));
  win.on('focus', () => send('focus', true));
}

function placeWindow() {
  if (!win)
    return;
  const { bounds } = screen.getPrimaryDisplay();
  const width = Math.min(MAX_WIDTH, bounds.width);
  win.setBounds({
    x: Math.round(bounds.x + (bounds.width - width) / 2),
    y: bounds.y,
    width,
    height: WINDOW_HEIGHT,
  });
}

/**
 * Watches the mouse. While the island is hidden, it only reports when the pointer touches the
 * top-middle edge; while it's showing, it streams the position so the page can follow along.
 */
let lastHot = false;
function pollCursor() {
  if (win && !win.isDestroyed()) {
    const point = screen.getCursorScreenPoint();
    const { bounds } = screen.getPrimaryDisplay();
    const hot = !fullscreen &&
      point.y <= bounds.y + 1 &&
      point.x >= bounds.x && point.x < bounds.x + bounds.width &&
      Math.abs(point.x - (bounds.x + bounds.width / 2)) <= HOT_ZONE_HALF_WIDTH;

    if (tracking || hot !== lastHot) {
      const at = win.getBounds();
      send('cursor', { x: point.x - at.x, y: point.y - at.y, hot });
    }
    lastHot = hot;
  }
  setTimeout(pollCursor, tracking ? 16 : 40);
}

function send(channel, data) {
  if (win && !win.isDestroyed())
    win.webContents.send(channel, data);
}

function createTray() {
  tray = new Tray(nativeImage.createFromPath(path.join(__dirname, 'assets', 'tray.png')));
  tray.setToolTip(`WindowsBar — hover the top-middle of your screen, or press ${SHORTCUT.replaceAll('+', ' + ')}`);
  tray.on('click', () => send('command', 'open'));
  const rebuild = () => tray.setContextMenu(Menu.buildFromTemplate([
    { label: 'Open WindowsBar', click: () => send('command', 'open') },
    { label: 'Settings', click: () => send('command', 'settings') },
    { type: 'separator' },
    {
      label: 'Start with Windows',
      type: 'checkbox',
      checked: getLaunchAtLogin(),
      click: item => setLaunchAtLogin(item.checked),
    },
    { type: 'separator' },
    { label: 'Quit WindowsBar', click: () => app.quit() },
  ]));
  rebuild();
  tray.on('right-click', rebuild);
}

function startHelper() {
  const exe = app.isPackaged
    ? path.join(process.resourcesPath, 'helper', 'WindowsBar.Helper.exe')
    : path.join(__dirname, '..', 'helper', 'bin', 'Release', 'net48', 'WindowsBar.Helper.exe');
  helper = new Helper(exe);
  helper.on('message', message => {
    switch (message.type) {
      case 'media':
        send('media', message);
        break;
      case 'art':
        send('art', message.data);
        break;
      case 'fullscreen':
        fullscreen = message.value && store.data.settings.hideInFullscreen;
        send('fullscreen', fullscreen);
        break;
      case 'error':
        log(`Helper: ${message.message}`);
        break;
    }
  });
  helper.start();
}

// When packaged as the single-file portable version, the real exe lives outside the temp folder it runs from.
function launchPath() {
  return process.env.PORTABLE_EXECUTABLE_FILE || process.execPath;
}

function getLaunchAtLogin() {
  return app.getLoginItemSettings({ path: launchPath() }).openAtLogin;
}

function setLaunchAtLogin(on) {
  app.setLoginItemSettings({ openAtLogin: on, path: launchPath() });
}

// ----- Requests from the island -----

ipcMain.on('interactive', (_e, on) => win?.setIgnoreMouseEvents(!on));
ipcMain.on('tracking', (_e, on) => { tracking = on; });
ipcMain.on('focus', () => win?.focus());
ipcMain.on('blur', () => win?.blur());

ipcMain.handle('store:load', () => store.data);
ipcMain.on('store:set', (_e, key, value) => {
  if (key in DEFAULTS)
    store.set(key, value);
  if (key === 'settings' && !value.hideInFullscreen && fullscreen) {
    fullscreen = false;
    send('fullscreen', false);
  }
});

ipcMain.handle('login:get', () => getLaunchAtLogin());
ipcMain.handle('login:set', (_e, on) => {
  setLaunchAtLogin(on);
  return getLaunchAtLogin();
});

ipcMain.handle('file:icon', async (_e, file) => {
  try {
    return (await app.getFileIcon(file, { size: 'large' })).toDataURL();
  } catch {
    return '';
  }
});
ipcMain.handle('file:existing', (_e, files) => files.filter(f => fs.existsSync(f)));
ipcMain.on('file:open', (_e, file) => shell.openPath(file));
ipcMain.on('file:drag', async (event, file) => {
  let icon = nativeImage.createEmpty();
  try {
    icon = await app.getFileIcon(file, { size: 'normal' });
  } catch {
    // Fall back to the app icon below.
  }
  if (icon.isEmpty())
    icon = nativeImage.createFromPath(path.join(__dirname, 'assets', 'tray.png'));
  event.sender.startDrag({ file, icon });
  event.sender.send('dragdone');
});

ipcMain.on('media', (_e, command) => helper?.send(command));
ipcMain.on('share', (_e, paths) => helper?.send({ cmd: 'share', paths }));
ipcMain.on('data-folder', () => shell.openPath(app.getPath('userData')));
ipcMain.on('quit', () => app.quit());
ipcMain.handle('info', () => ({
  version: app.getVersion(),
  shortcut: SHORTCUT.replaceAll('+', ' + '),
  helper: helper?.available ?? false,
}));

app.on('will-quit', () => {
  globalShortcut.unregisterAll();
  helper?.stop();
  store?.save();
});
