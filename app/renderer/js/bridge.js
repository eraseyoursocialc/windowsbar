/**
 * The connection to the WindowsBar app. Inside the app this is window.bar (see preload.js).
 * Opened in a plain browser for design work, a stand-in fakes the app: the real mouse drives the
 * island, data lives in localStorage, and a pretend song plays.
 */
export const bridge = window.bar ?? createPreviewBridge();

function createPreviewBridge() {
  // A stand-in wallpaper so the island can be judged against something.
  document.documentElement.style.background = 'linear-gradient(135deg, #5b2bd6 0%, #8a3ffc 45%, #3a1c8f 100%)';
  const listeners = {};
  const emit = (channel, data) => (listeners[channel] ?? []).forEach(fn => fn(data));
  const saved = JSON.parse(localStorage.getItem('windowsbar') || '{}');
  const data = {
    settings: {
      hideInFullscreen: true,
      openDelay: 1000,
      transparency: 0,
      widgets: { media: true, mirror: true, calendar: true, notes: true, timers: true, tasks: true },
    },
    notes: ['', '', ''],
    tasks: [],
    shelf: [],
    events: {},
    firstRun: false,
    ...saved,
  };
  let tracking = false;
  let last = { x: -1, y: -1, hot: false };

  document.addEventListener('mousemove', e => {
    const hot = e.clientY <= 2 && Math.abs(e.clientX - innerWidth / 2) <= 170;
    last = { x: e.clientX, y: e.clientY, hot };
    if (tracking || hot)
      emit('cursor', last);
  });
  // Like the app, keep reporting the pointer while the island is showing.
  setInterval(() => {
    if (tracking)
      emit('cursor', last);
  }, 16);

  // A pretend song, so the music widget has something to show.
  let song = {
    type: 'media', hasSession: true, title: 'My Seaside Morning', artist: 'Easy Cat Jazz',
    playing: true, position: 42, duration: 214, updated: Date.now(), canSeek: true,
  };
  setTimeout(() => {
    emit('media', song);
    emit('art', 'data:image/svg+xml;utf8,' + encodeURIComponent(
      '<svg xmlns="http://www.w3.org/2000/svg" width="120" height="120"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">' +
      '<stop offset="0" stop-color="#ff9a5a"/><stop offset="1" stop-color="#7a3cff"/></linearGradient></defs>' +
      '<rect width="120" height="120" fill="url(#g)"/><circle cx="60" cy="60" r="26" fill="#fff" opacity=".25"/></svg>'));
  }, 300);

  return {
    preview: true,
    on(channel, fn) {
      (listeners[channel] ??= []).push(fn);
    },
    setInteractive() {},
    setTracking(on) {
      tracking = on;
    },
    focus() {},
    blur() {},
    store: {
      load: async () => structuredClone(data),
      set(key, value) {
        data[key] = value;
        localStorage.setItem('windowsbar', JSON.stringify(data));
      },
    },
    login: { get: async () => true, set: async on => on },
    files: {
      icon: async () => '',
      existing: async paths => paths,
      open() {},
      startDrag() {
        setTimeout(() => emit('dragdone'), 100);
      },
      pathFor: file => file.name,
    },
    media(command) {
      const now = Date.now();
      const position = song.position + (song.playing ? (now - song.updated) / 1000 : 0);
      if (command.cmd === 'toggle')
        song = { ...song, playing: !song.playing, position, updated: now };
      if (command.cmd === 'seek')
        song = { ...song, position: command.position, updated: now };
      emit('media', song);
    },
    share() {},
    openDataFolder() {},
    quit() {},
    info: async () => ({ version: 'preview', shortcut: 'Control + Alt + Space', helper: false }),
    emit,
  };
}
