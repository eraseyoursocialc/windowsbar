import { icon } from '../icons.js';
import { countdown, el } from '../util.js';

const UNITS = [
  { key: 'h', max: 23 },
  { key: 'm', max: 59 },
  { key: 's', max: 59 },
];

/** Pick a time on three little wheels, then run as many timers as you like. */
export class TimersWidget {
  constructor(root, onFinish) {
    this.onFinish = onFinish;
    this.onTick = () => {};
    this.values = [0, 5, 0];
    this.timers = [];
    this.frame = 0;

    root.innerHTML = `
      <div class="timer-picker">
        <button class="btn ghost tiny back" title="Back to running timers">${icon('x', 11)}</button>
        <div class="wheels"></div>
        <div class="timer-side">
          <button class="chip" data-preset="60">1m</button>
          <button class="chip" data-preset="300">5m</button>
          <button class="btn accent round start" title="Start">${icon('play', 15)}</button>
        </div>
      </div>
      <div class="timer-list">
        <button class="chip add">${icon('plus', 11)}<span>Add Timer</span></button>
        <div class="timer-rows"></div>
      </div>`;

    this.root = root;
    this.wheels = root.querySelector('.wheels');
    this.rows = root.querySelector('.timer-rows');
    this.startBtn = root.querySelector('.start');

    UNITS.forEach((unit, i) => {
      if (i)
        this.wheels.append(el('span', 'wheel-sep', ':'));
      const wheel = el('div', 'wheel');
      wheel.dataset.ownWheel = '';
      wheel.innerHTML = `
        <button class="wheel-prev"></button>
        <div class="wheel-now"><span class="wheel-num"></span><small>${unit.key}</small></div>
        <button class="wheel-next"></button>`;
      wheel.querySelector('.wheel-prev').addEventListener('click', () => this.step(i, -1));
      wheel.querySelector('.wheel-next').addEventListener('click', () => this.step(i, 1));
      let acc = 0;
      wheel.addEventListener('wheel', e => {
        if (wheel.closest('[data-scrolling]'))
          return; // the strip is mid-scroll; let it keep going
        e.preventDefault();
        acc += e.deltaY;
        while (Math.abs(acc) >= 40) {
          this.step(i, acc > 0 ? 1 : -1);
          acc -= Math.sign(acc) * 40;
        }
      }, { passive: false });
      this.wheels.append(wheel);
    });

    root.querySelector('.start').addEventListener('click', () => this.add(this.picked()));
    for (const chip of root.querySelectorAll('[data-preset]'))
      chip.addEventListener('click', () => this.add(+chip.dataset.preset * 1000));
    root.querySelector('.add').addEventListener('click', () => this.showList(false));
    root.querySelector('.back').addEventListener('click', () => this.showList(true));

    this.renderWheels(0);
    this.showList(false);
  }

  picked() {
    const [h, m, s] = this.values;
    return ((h * 60 + m) * 60 + s) * 1000;
  }

  step(index, by) {
    const range = UNITS[index].max + 1;
    this.values[index] = (this.values[index] + by + range) % range;
    this.renderWheels(by, index);
  }

  renderWheels(direction, changed = -1) {
    [...this.wheels.querySelectorAll('.wheel')].forEach((wheel, i) => {
      const range = UNITS[i].max + 1;
      const v = this.values[i];
      wheel.querySelector('.wheel-prev').textContent = `${String((v - 1 + range) % range).padStart(2, '0')}${UNITS[i].key}`;
      wheel.querySelector('.wheel-next').textContent = `${String((v + 1) % range).padStart(2, '0')}${UNITS[i].key}`;
      const num = wheel.querySelector('.wheel-num');
      num.textContent = String(v).padStart(2, '0');
      if (i === changed) {
        // Numbers roll in from the direction you scrolled.
        num.animate(
          [{ transform: `translateY(${direction > 0 ? 9 : -9}px)`, opacity: 0.2 }, { transform: 'none', opacity: 1 }],
          { duration: 220, easing: 'cubic-bezier(.2,.9,.3,1)' });
      }
    });
    this.startBtn.disabled = this.picked() === 0;
  }

  showList(list) {
    const show = list && this.timers.length > 0;
    this.root.classList.toggle('listing', show);
    this.root.classList.toggle('has-timers', this.timers.length > 0);
  }

  add(ms) {
    if (ms <= 0)
      return;
    const timer = { length: ms, endsAt: Date.now() + ms, remaining: ms, running: true };
    this.timers.unshift(timer);
    this.renderRows();
    this.showList(true);
    this.run();
  }

  toggle(timer) {
    if (timer.running) {
      timer.remaining = timer.endsAt - Date.now();
      timer.running = false;
    } else {
      timer.endsAt = Date.now() + timer.remaining;
      timer.running = true;
      this.run();
    }
    this.renderRows();
  }

  remove(timer) {
    this.timers = this.timers.filter(t => t !== timer);
    this.renderRows();
    this.showList(this.timers.length > 0);
    this.onTick();
  }

  /** The running timer that ends first, for the small tab. */
  get soonest() {
    return this.timers.filter(t => t.running).sort((a, b) => a.endsAt - b.endsAt)[0] ?? null;
  }

  run() {
    if (this.frame)
      return;
    const loop = () => {
      const now = Date.now();
      for (const timer of [...this.timers]) {
        if (!timer.running)
          continue;
        timer.remaining = timer.endsAt - now;
        if (timer.remaining <= 0) {
          this.timers = this.timers.filter(t => t !== timer);
          this.onFinish(timer);
          this.renderRows();
          this.showList(this.timers.length > 0);
        }
      }
      this.updateRows();
      this.onTick();
      this.frame = this.timers.some(t => t.running) ? setTimeout(loop, 250) : 0;
    };
    loop();
  }

  renderRows() {
    this.rows.replaceChildren(...this.timers.map(timer => {
      const row = el('div', 'timer-row');
      row.innerHTML = `
        <span class="timer-ico">${icon('timer', 13)}</span>
        <span class="timer-time"></span>
        <button class="btn ghost tiny toggle" title="Pause / resume">${icon(timer.running ? 'pause' : 'play', 11)}</button>
        <button class="btn ghost tiny remove" title="Remove">${icon('x', 11)}</button>
        <span class="timer-bar"><span></span></span>`;
      row.querySelector('.toggle').addEventListener('click', () => this.toggle(timer));
      row.querySelector('.remove').addEventListener('click', () => this.remove(timer));
      timer.row = row;
      return row;
    }));
    this.updateRows();
  }

  updateRows() {
    for (const timer of this.timers) {
      if (!timer.row)
        continue;
      timer.row.querySelector('.timer-time').textContent = countdown(timer.remaining);
      timer.row.querySelector('.timer-bar span').style.transform = `scaleX(${Math.max(0, timer.remaining / timer.length)})`;
      timer.row.classList.toggle('paused', !timer.running);
    }
  }
}
