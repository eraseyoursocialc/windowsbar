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
