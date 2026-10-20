const { contextBridge, ipcRenderer, webUtils } = require('electron');

const channels = new Set(['cursor', 'media', 'art', 'fullscreen', 'command', 'dragdone', 'focus']);

/** The small, fixed set of things the island's page is allowed to ask the app for. */
contextBridge.exposeInMainWorld('bar', {
  on(channel, fn) {
    if (channels.has(channel))
      ipcRenderer.on(channel, (_event, data) => fn(data));
  },
  setInteractive: on => ipcRenderer.send('interactive', on),
  setTracking: on => ipcRenderer.send('tracking', on),
  focus: () => ipcRenderer.send('focus'),
  blur: () => ipcRenderer.send('blur'),
  store: {
    load: () => ipcRenderer.invoke('store:load'),
    set: (key, value) => ipcRenderer.send('store:set', key, value),
  },
  login: {
    get: () => ipcRenderer.invoke('login:get'),
    set: on => ipcRenderer.invoke('login:set', on),
  },
  files: {
    icon: path => ipcRenderer.invoke('file:icon', path),
    existing: paths => ipcRenderer.invoke('file:existing', paths),
    open: path => ipcRenderer.send('file:open', path),
    startDrag: path => ipcRenderer.send('file:drag', path),
    pathFor: file => webUtils.getPathForFile(file),
  },
  media: command => ipcRenderer.send('media', command),
  share: paths => ipcRenderer.send('share', paths),
  openDataFolder: () => ipcRenderer.send('data-folder'),
  quit: () => ipcRenderer.send('quit'),
  info: () => ipcRenderer.invoke('info'),
});
