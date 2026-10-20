import { icon } from '../icons.js';
import { clock } from '../util.js';

/** Now playing: artwork, title, controls and a seek bar. Fed by the Windows media session. */
export class MediaWidget {
  constructor(el, bridge) {
    this.bridge = bridge;
    this.state = { hasSession: false, playing: false };
    this.art = '';
    this.seeking = false;
    this.onChange = () => {};

    el.innerHTML = `
      <div class="media-top">
        <div class="art"><span class="art-empty">${icon('music', 26)}</span><img alt=""></div>
        <div class="media-text">
          <div class="title">Nothing playing</div>
          <div class="artist">Play music in any app</div>
          <div class="controls">
            <button class="btn ghost" data-cmd="prev" title="Previous">${icon('prev', 15)}</button>
            <button class="btn ghost play" data-cmd="toggle" title="Play / pause">${icon('play', 19)}</button>
            <button class="btn ghost" data-cmd="next" title="Next">${icon('next', 15)}</button>
          </div>
        </div>
      </div>
      <div class="progress">
        <input type="range" class="seek" min="0" max="1" step="0.1" value="0">
        <div class="times"><span class="pos">0:00</span><span class="dur">0:00</span></div>
      </div>`;

    this.el = el;
    this.img = el.querySelector('img');
    this.title = el.querySelector('.title');
    this.artist = el.querySelector('.artist');
    this.playBtn = el.querySelector('.play');
    this.seek = el.querySelector('.seek');
    this.pos = el.querySelector('.pos');
    this.dur = el.querySelector('.dur');

    for (const btn of el.querySelectorAll('[data-cmd]'))
      btn.addEventListener('click', () => this.command(btn.dataset.cmd));

    this.seek.addEventListener('pointerdown', () => { this.seeking = true; });
    this.seek.addEventListener('input', () => {
      this.paintSeek();
      this.pos.textContent = clock(+this.seek.value);
    });
    this.seek.addEventListener('change', () => {
      this.seeking = false;
      bridge.media({ cmd: 'seek', position: +this.seek.value });
    });

    bridge.on('media', s => this.update(s));
    bridge.on('art', data => {
      this.art = data;
      this.img.src = data || '';
      el.classList.toggle('has-art', !!data);
      setTint(data);
      this.onChange();
    });
  }

  get playing() {
    return this.state.hasSession && this.state.playing;
  }

  command(cmd) {
    // Flip the button straight away so it feels instant; the real state follows a moment later.
    if (cmd === 'toggle' && this.state.hasSession) {
      const position = this.position();
      this.update({ ...this.state, playing: !this.state.playing, position, updated: Date.now() });
    }
    this.bridge.media({ cmd });
  }

  update(s) {
    this.state = s;
    const has = s.hasSession && !!s.title;
    this.el.classList.toggle('empty', !has);
    this.title.textContent = has ? s.title : 'Nothing playing';
    this.artist.textContent = has ? s.artist : 'Play music in any app';
    this.playBtn.innerHTML = icon(s.playing ? 'pause' : 'play', 19);
    this.seek.disabled = !s.canSeek;
    this.tick();
    this.onChange();
  }

  /** Apps only report the position now and then, so count forward from their last report. */
  position() {
    const s = this.state;
    if (!s.duration)
      return 0;
    const elapsed = s.playing && s.updated ? (Date.now() - s.updated) / 1000 : 0;
    return Math.min(s.duration, Math.max(0, s.position + elapsed));
  }

  tick() {
    const s = this.state;
    const show = s.hasSession && s.duration > 0;
    this.el.classList.toggle('no-progress', !show);
    if (!show || this.seeking)
      return;
    this.seek.max = s.duration;
    this.seek.value = this.position();
    this.paintSeek();
    this.pos.textContent = clock(+this.seek.value);
    this.dur.textContent = clock(s.duration);
  }

  paintSeek() {
    const pct = this.seek.max > 0 ? (this.seek.value / this.seek.max) * 100 : 0;
    this.seek.style.setProperty('--p', `${pct}%`);
  }
}

/**
 * Picks the most colorful tone in the artwork and publishes it as --tint, so the music tile and the
 * little equalizer in the tab take on the song's color. Kept bright enough to read on black.
 */
function setTint(src) {
  const root = document.documentElement;
  if (!src) {
    root.style.removeProperty('--tint');
    return;
  }
  const img = new Image();
  img.onload = () => {
    try {
      const size = 16;
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = size;
      const ctx = canvas.getContext('2d', { willReadFrequently: true });
      ctx.drawImage(img, 0, 0, size, size);
      const px = ctx.getImageData(0, 0, size, size).data;
      let r = 0, g = 0, b = 0, total = 0;
      for (let i = 0; i < px.length; i += 4) {
        const max = Math.max(px[i], px[i + 1], px[i + 2]);
        const min = Math.min(px[i], px[i + 1], px[i + 2]);
        // Favour saturated, mid-bright pixels over greys, blacks and whites.
        const weight = ((max - min) / 255) ** 2 * (max / 255) + 0.002;
        r += px[i] * weight;
        g += px[i + 1] * weight;
        b += px[i + 2] * weight;
        total += weight;
      }
      const [h, s] = toHsl(r / total, g / total, b / total);
      root.style.setProperty('--tint', `hsl(${Math.round(h)} ${Math.round(Math.min(s, 0.8) * 100)}% 68%)`);
    } catch {
      root.style.removeProperty('--tint');
    }
  };
  img.src = src;
}

function toHsl(r, g, b) {
  r /= 255; g /= 255; b /= 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  const d = max - min;
  if (!d)
    return [0, 0, l];
  const s = d / (1 - Math.abs(2 * l - 1));
  const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [(h * 60 + 360) % 360, s, l];
}
