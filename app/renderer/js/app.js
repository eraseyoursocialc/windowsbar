import { bridge } from './bridge.js';
import { hydrateIcons, icon } from './icons.js';
import { installSprings, springs, SmoothScroller } from './motion.js';
import { chime, countdown, isEditing } from './util.js';
import { MediaWidget } from './widgets/media.js';
import { MirrorWidget } from './widgets/mirror.js';
import { CalendarWidget } from './widgets/calendar.js';
import { NotesWidget } from './widgets/notes.js';
import { TimersWidget } from './widgets/timers.js';
import { TasksWidget } from './widgets/tasks.js';
import { Shelf } from './widgets/shelf.js';

/*
 * The island has five looks:
 *   hidden  nothing on screen
 *   peek    a small tab slid down because the mouse touched the top-middle edge
 *   notice  a short message (timer done, reminder) that drops down on its own
 *   open    fully open
 *   drop    showing drop targets while a file is dragged onto it
 */

const $ = sel => document.querySelector(sel);
const island = $('#island');
const LEAVE_PEEK_MS = 220;
const LEAVE_OPEN_MS = 420;

const SIZE = {
  hidden: { w: 184, h: 0, r: 12 },
  peek: { w: 184, h: 26, r: 13 },
  peekBusy: { w: 236, h: 26, r: 13 },
  notice: { w: 340, h: 36, r: 18 },
  drop: { w: 560, h: 158, r: 30 },
};

const ui = {
  mode: 'hidden',
  tab: 'nook',
  settingsOpen: false,
  cursor: { x: -1, y: -1, hot: false },
  interactive: false,
  outsideSince: 0,
  dwellTimer: 0,
  holdTimer: 0,
  pin: null,
  blocked: false,
  windowFocused: false,
  notice: null,
  noticeTimer: 0,
  overShare: false,
  sticky: false,
};

let data;
let settings;
let media, mirror, calendar, notes, timers, tasks, shelf, stripScroller;

boot();

async function boot() {
  installSprings();
  hydrateIcons();
  data = await bridge.store.load();
  settings = data.settings;

  media = new MediaWidget($('#w-media'), bridge);
  mirror = new MirrorWidget($('#w-mirror'));
  calendar = new CalendarWidget($('#w-calendar'), bridge, data.events ?? {}, plan =>
    notify('bell', plan.text, 8000));
  notes = new NotesWidget($('#w-notes'), bridge, data.notes ?? ['', '', '']);
  timers = new TimersWidget($('#w-timers'), timer =>
    notify('timer', `Timer done · ${countdown(timer.length)}`, 7000));
  tasks = new TasksWidget($('#w-tasks'), bridge, data.tasks ?? []);
  shelf = new Shelf($('#shelf'), bridge, data.shelf ?? []);

  media.onChange = () => refreshTab();
  timers.onTick = () => {
    if (ui.mode === 'peek' || ui.mode === 'notice')
      refreshTab();
  };
  shelf.onChange = () => {
    refreshTab();
    $('#clear-tray').hidden = !(ui.tab === 'tray' && shelf.count > 0 && !ui.settingsOpen);
  };

  stripScroller = new SmoothScroller($('#strip'), { ownWheel: e => !!e.target.closest('[data-own-wheel], .days, .task-list') });

  setupTabs();
  setupSettings();
  setupDrops();
  applyWidgetVisibility();
  watchBattery();

  bridge.on('cursor', c => {
    ui.cursor = c;
    follow();
  });
  bridge.on('fullscreen', on => {
    ui.blocked = on;
    if (on && !ui.pin)
      hide();
  });
  bridge.on('focus', on => {
    ui.windowFocused = on;
    if (!on) {
      // Clicking anywhere else closes an island that was opened from the tray or shortcut.
      if (ui.sticky && !isOverIsland(ui.cursor.x, ui.cursor.y)) {
        ui.sticky = false;
        hide();
      }
      follow();
    }
  });
  bridge.on('command', command);

  document.addEventListener('pointerdown', e => {
    // Typing needs keyboard focus, which the island normally doesn't take.
    if (isEditing(e.target) || e.target.closest('[contenteditable]'))
      bridge.focus();
  }, true);

  setInterval(() => {
    if (ui.mode === 'open')
      media.tick();
    calendar.refresh();
  }, 1000);

  setShape(SIZE.hidden, springs.close);
}

function command(name) {
  switch (name) {
    case 'open':
      openFromCommand();
      break;
    case 'toggle':
      if (ui.mode === 'open')
        hide();
      else
        openFromCommand();
      break;
    case 'settings':
      openFromCommand();
      showSettings(true);
      break;
    case 'welcome':
      notify('sparkle', 'Move your mouse to the top-middle edge to open WindowsBar', 9000);
      break;
    // Screenshot modes: stay put without a mouse.
    case 'peek':
      ui.pin = 'peek';
      peek();
      break;
    case 'pin-open':
      ui.pin = 'open';
      open();
      break;
    case 'pin-settings':
      ui.pin = 'open';
      open();
      showSettings(true);
      break;
  }
}

/**
 * Opened from the tray icon or the keyboard shortcut, the mouse is somewhere else, so stay open
 * until it has visited the island and left, the user clicks elsewhere, or presses Esc.
 */
function openFromCommand() {
  ui.sticky = true;
  open();
  bridge.focus();
}

document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && ui.mode !== 'hidden' && !ui.pin) {
    ui.sticky = false;
    hide();
  }
});

/* ---------- Following the mouse ---------- */

function follow() {
  const { x, y, hot } = ui.cursor;
  const over = isOverIsland(x, y);
  setInteractive(ui.mode !== 'hidden' && ui.mode !== 'notice' && over);

  if (ui.blocked && !ui.pin)
    return;

  switch (ui.mode) {
    case 'hidden':
    case 'notice':
      if (hot)
        peek();
      break;
    case 'peek':
      if (hot || over || ui.pin)
        ui.outsideSince = 0;
      else if (awayFor(LEAVE_PEEK_MS))
        hide();
      break;
    default:
      if (over)
        ui.sticky = false;
      if (over || busy() || (ui.sticky && ui.windowFocused))
        ui.outsideSince = 0;
      else if (awayFor(LEAVE_OPEN_MS))
        hide();
  }
}

function awayFor(ms) {
  ui.outsideSince ||= performance.now();
  return performance.now() - ui.outsideSince >= ms;
}

function isOverIsland(x, y, margin = 8) {
  const r = island.getBoundingClientRect();
  if (r.height < 1)
    return false;
  return x >= r.left - margin && x <= r.right + margin && y >= r.top - margin && y <= r.bottom + margin;
}

function busy() {
  return ui.pin === 'open' ||
    shelf.draggingOut ||
    (ui.windowFocused && (isEditing(document.activeElement)));
}

function setInteractive(on) {
  if (on === ui.interactive)
    return;
  ui.interactive = on;
  bridge.setInteractive(on);
}

/* ---------- Changing shape ---------- */

function setShape({ w, h, r }, sp, fadeOut = false) {
  const t = `${sp.duration}ms ${sp.easing}`;
  island.style.transition = `width ${t}, height ${t}, border-radius ${t}, opacity ${fadeOut ? '160ms ease 120ms' : '120ms ease'}`;
  island.style.width = `${w}px`;
  island.style.height = `${h}px`;
  island.style.borderRadius = `0 0 ${r}px ${r}px`;
  island.style.opacity = fadeOut ? '0' : '1';
}

function openSize() {
  let w = Math.min(1180, window.innerWidth - 40);
  w -= w % 2;
  island.style.setProperty('--open-w', `${w}px`);
  return { w, h: 210, r: 36 };
}

function setMode(mode) {
  ui.mode = mode;
  island.dataset.mode = mode;
  ui.outsideSince = 0;
  bridge.setTracking(mode !== 'hidden' && mode !== 'notice');
}

function peek() {
  clearTimeout(ui.dwellTimer);
  clearTimeout(ui.holdTimer);
  setMode('peek');
  refreshTab();

  const base = tabIsBusy() ? SIZE.peekBusy : SIZE.peek;
  setShape(base, springs.peek);

  if (ui.pin === 'peek')
    return;

  // The tab swells a little while you hold the mouse there, then opens.
  const delay = settings.openDelay;
  ui.holdTimer = setTimeout(() => {
    if (ui.mode !== 'peek')
      return;
    const t = `${Math.max(120, delay - 200)}ms cubic-bezier(.45,0,.55,1)`;
    island.style.transition = `width ${t}, height ${t}, border-radius ${t}`;
    island.style.width = `${base.w + 26}px`;
    island.style.height = `${base.h + 6}px`;
    island.style.borderRadius = `0 0 16px 16px`;
  }, 200);
  ui.dwellTimer = setTimeout(() => {
    if (ui.mode === 'peek' && (ui.cursor.hot || isOverIsland(ui.cursor.x, ui.cursor.y)))
      open();
  }, delay);
}

function open() {
  clearTimeout(ui.dwellTimer);
  clearTimeout(ui.holdTimer);
  if (ui.mode === 'open')
    return;
  setMode('open');
  if (ui.settingsOpen)
    showSettings(false, true);
  setShape(openSize(), springs.open);
  calendar.refresh();
  media.tick();
}

function showDropTargets() {
  if (ui.mode === 'drop')
    return;
  clearTimeout(ui.dwellTimer);
  clearTimeout(ui.holdTimer);
  setMode('drop');
  $('#drop-count').textContent = shelf.count === 0 ? 'Keep files handy'
    : shelf.count === 1 ? '1 file' : `${shelf.count} files`;
  setDropTarget(false);
  setShape(SIZE.drop, springs.open);
}

function hide() {
  clearTimeout(ui.dwellTimer);
  clearTimeout(ui.holdTimer);
  if (ui.pin && ui.mode !== 'hidden')
    return;
  setMode('hidden');
  mirror.stop();
  notes.flush();
  if (isEditing(document.activeElement))
    document.activeElement.blur();
  bridge.blur();
  setInteractive(false);
  if (ui.notice)
    showNotice();
  else
    setShape(SIZE.hidden, springs.close, true);
}

/* ---------- The small tab ---------- */

function tabIsBusy() {
  return !!(ui.notice || timers.soonest || media.playing || shelf.count);
}

/** Fills the small tab, most important first: a notice, a timer, music, tray count, or a grab bar. */
function refreshTab() {
  const tab = $('#tab');
  const timer = timers.soonest;
  let left = '';
  let middle = '';
  let right = '';

  if (ui.notice) {
    left = icon(ui.notice.icon, 14);
    middle = ui.notice.text;
  } else if (timer) {
    left = icon('timer', 14);
    middle = countdown(timer.remaining);
    if (media.playing)
      right = '<span class="eq"><i></i><i></i><i></i><i></i></span>';
  } else if (media.playing) {
    left = media.art ? 'art' : icon('music', 14);
    right = '<span class="eq"><i></i><i></i><i></i><i></i></span>';
  } else if (shelf.count) {
    left = icon('tray', 14);
    right = `<span class="tab-count">${shelf.count}</span>`;
  }

  tab.classList.toggle('bare', !left && !middle && !right);
  const leftSlot = tab.querySelector('.tab-left');
  if (left === 'art') {
    const img = document.createElement('img');
    img.className = 'tab-art';
    img.alt = '';
    img.src = media.art;
    leftSlot.replaceChildren(img);
  } else {
    leftSlot.innerHTML = left;
  }
  tab.querySelector('.tab-mid').textContent = middle;
  tab.querySelector('.tab-right').innerHTML = right;
}

function notify(iconName, text, ms) {
  chime();
  ui.notice = { icon: iconName, text };
  clearTimeout(ui.noticeTimer);
  ui.noticeTimer = setTimeout(() => {
    ui.notice = null;
    refreshTab();
    if (ui.mode === 'notice')
      hide();
  }, ms);
  refreshTab();
  if (ui.mode === 'hidden')
    showNotice();
}

function showNotice() {
  setMode('notice');
  refreshTab();
  setShape(SIZE.notice, springs.peek);
}

/* ---------- Tabs ---------- */

function setupTabs() {
  for (const btn of document.querySelectorAll('.seg-btn'))
    btn.addEventListener('click', () => showTab(btn.dataset.tab));
  $('#gear').addEventListener('click', () => showSettings(!ui.settingsOpen));
  $('#clear-tray').addEventListener('click', () => shelf.clear());
  showTab('nook', true);
}

function showTab(tab, instant = false) {
  ui.tab = tab;
  if (ui.settingsOpen)
    showSettings(false, true);
  for (const page of document.querySelectorAll('.page'))
    page.classList.toggle('active', page.dataset.page === tab);
  for (const btn of document.querySelectorAll('.seg-btn'))
    btn.classList.toggle('active', btn.dataset.tab === tab);
  moveSegIndicator(instant);
  if (tab !== 'nook')
    mirror.stop();
  $('#clear-tray').hidden = !(tab === 'tray' && shelf.count > 0);
}

function moveSegIndicator(instant) {
  const active = document.querySelector('.seg-btn.active');
  const indicator = $('.seg-indicator');
  indicator.classList.toggle('gone', !active);
  if (!active)
    return;
  if (instant)
    indicator.style.transition = 'none';
  indicator.style.width = `${active.offsetWidth}px`;
  indicator.style.transform = `translateX(${active.offsetLeft}px)`;
  if (instant)
    requestAnimationFrame(() => { indicator.style.transition = ''; });
}

/* ---------- Settings ---------- */

function setupSettings() {
  const loginToggle = $('#set-login');
  const fullscreenToggle = $('#set-fullscreen');
  const delay = $('#set-delay');
  const delayLabel = $('#set-delay-label');

  bridge.login.get().then(on => { loginToggle.checked = on; });
  loginToggle.addEventListener('change', async () => {
    loginToggle.checked = await bridge.login.set(loginToggle.checked);
  });

  fullscreenToggle.checked = settings.hideInFullscreen;
  fullscreenToggle.addEventListener('change', () => saveSettings({ hideInFullscreen: fullscreenToggle.checked }));

  const showDelay = () => { delayLabel.textContent = `${(+delay.value / 1000).toFixed(1)} s`; };
  delay.value = settings.openDelay;
  delay.style.setProperty('--p', `${((delay.value - delay.min) / (delay.max - delay.min)) * 100}%`);
  showDelay();
  delay.addEventListener('input', () => {
    delay.style.setProperty('--p', `${((delay.value - delay.min) / (delay.max - delay.min)) * 100}%`);
    showDelay();
  });
  delay.addEventListener('change', () => saveSettings({ openDelay: +delay.value }));

  // Transparency updates the bar live while you drag, and is saved when you let go.
  const glass = $('#set-glass');
  const glassLabel = $('#set-glass-label');
  const showGlass = () => {
    glass.style.setProperty('--p', `${(glass.value / glass.max) * 100}%`);
    glassLabel.textContent = `${glass.value}%`;
    applyTransparency(+glass.value);
  };
  glass.value = settings.transparency ?? 0;
  showGlass();
  glass.addEventListener('input', showGlass);
  glass.addEventListener('change', () => saveSettings({ transparency: +glass.value }));

  for (const chip of document.querySelectorAll('.wchip[data-widget]')) {
    chip.classList.toggle('on', settings.widgets[chip.dataset.widget] !== false);
    chip.addEventListener('click', () => {
      const widgets = { ...settings.widgets, [chip.dataset.widget]: !chip.classList.contains('on') };
      if (!Object.values(widgets).some(Boolean))
        return; // keep at least one
      chip.classList.toggle('on');
      saveSettings({ widgets });
      applyWidgetVisibility();
    });
  }

  $('#set-folder').addEventListener('click', () => bridge.openDataFolder());
  $('#set-quit').addEventListener('click', () => bridge.quit());
  bridge.info().then(info => {
    $('#set-shortcut').textContent = info.shortcut;
    $('#set-version').textContent = `WindowsBar ${info.version}`;
  });
}

function applyTransparency(percent) {
  document.documentElement.style.setProperty('--glass', String(1 - percent / 100));
}

function saveSettings(change) {
  settings = { ...settings, ...change };
  bridge.store.set('settings', settings);
}

function showSettings(on, instant = false) {
  ui.settingsOpen = on;
  island.classList.toggle('settings-open', on);
  for (const page of document.querySelectorAll('.page'))
    page.classList.toggle('active', on ? page.dataset.page === 'settings' : page.dataset.page === ui.tab);
  for (const btn of document.querySelectorAll('.seg-btn'))
    btn.classList.toggle('active', !on && btn.dataset.tab === ui.tab);
  moveSegIndicator(instant);
  $('#gear').innerHTML = icon(on ? 'x' : 'gear', on ? 15 : 16);
  $('#gear').title = on ? 'Close settings' : 'Settings';
  $('#clear-tray').hidden = on || !(ui.tab === 'tray' && shelf.count > 0);
  if (on)
    mirror.stop();
}

function applyWidgetVisibility() {
  const strip = $('#strip');
  let i = 0;
  for (const widget of strip.querySelectorAll('.w')) {
    const on = settings.widgets[widget.dataset.widget] !== false;
    widget.hidden = !on;
    widget.classList.toggle('first', on && i === 0);
    if (on)
      widget.style.setProperty('--i', i++);
  }
  if (settings.widgets.mirror === false)
    mirror.stop();
}

/* ---------- Battery ---------- */

async function watchBattery() {
  const label = $('#battery');
  try {
    const battery = await navigator.getBattery();
    const show = () => {
      const pct = Math.round(battery.level * 100);
      label.hidden = battery.level === 1 && battery.charging && battery.chargingTime === 0 && !navigator.userAgent.includes('Windows');
      label.innerHTML = `${battery.charging ? icon('bolt', 11) : ''}<span>${pct}%</span>`;
    };
    battery.addEventListener('levelchange', show);
    battery.addEventListener('chargingchange', show);
    show();
  } catch {
    label.hidden = true;
  }
}

/* ---------- Dropping files on the island ---------- */

function setupDrops() {
  const hasFiles = e => [...(e.dataTransfer?.types ?? [])].includes('Files') && !shelf.draggingOut;

  island.addEventListener('dragenter', e => {
    if (!hasFiles(e))
      return;
    e.preventDefault();
    showDropTargets();
  });
  island.addEventListener('dragover', e => {
    if (!hasFiles(e))
      return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
    ui.outsideSince = 0;
    if (ui.mode !== 'drop')
      showDropTargets();
    const r = $('#share-zone').getBoundingClientRect();
    setDropTarget(e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom);
  });
  island.addEventListener('drop', e => {
    if (!hasFiles(e))
      return;
    e.preventDefault();
    const paths = [...e.dataTransfer.files].map(f => bridge.files.pathFor(f)).filter(Boolean);
    if (!paths.length)
      return;
    if (ui.overShare) {
      hide();
      bridge.share(paths);
      return;
    }
    shelf.add(paths);
    showTab('tray');
    open();
  });
}

function setDropTarget(share) {
  ui.overShare = share;
  $('#share-zone').classList.toggle('target', share);
  $('#tray-zone').classList.toggle('target', !share);
}
