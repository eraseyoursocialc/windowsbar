/**
 * Real spring physics turned into a CSS easing curve, so animations settle the way
 * things do on iPhone and Mac: fast out of the gate, a soft landing, maybe a tiny overshoot.
 * Lower damping = more bounce. Returns { easing, duration } for CSS transitions.
 */
export function spring({ stiffness = 200, damping = 24, mass = 1 } = {}) {
  const dt = 1 / 240;
  let x = 0;
  let v = 0;
  let t = 0;
  const samples = [0];
  while (t < 2.5) {
    const a = (-stiffness * (x - 1) - damping * v) / mass;
    v += a * dt;
    x += v * dt;
    t += dt;
    samples.push(x);
    if (t > 0.05 && Math.abs(x - 1) < 0.0015 && Math.abs(v) < 0.02)
      break;
  }
  const step = Math.max(1, Math.round(samples.length / 48));
  const points = [];
  for (let i = 0; i < samples.length; i += step)
    points.push(+samples[i].toFixed(4));
  points.push(1);
  return { easing: `linear(${points.join(', ')})`, duration: Math.round(t * 1000) };
}

export const springs = {
  open: spring({ stiffness: 210, damping: 23 }),   // the island unfolding: a hint of bounce
  close: spring({ stiffness: 320, damping: 36 }),  // tucking away: quick, no bounce
  peek: spring({ stiffness: 380, damping: 26 }),   // the little tab popping out
  pop: spring({ stiffness: 420, damping: 20 }),    // buttons and toggles
  soft: spring({ stiffness: 170, damping: 26 }),   // content drifting into place
};

/** Publishes the springs as CSS variables (--spring-open, --spring-open-ms, …). */
export function installSprings(root = document.documentElement) {
  for (const [name, s] of Object.entries(springs)) {
    root.style.setProperty(`--spring-${name}`, s.easing);
    root.style.setProperty(`--spring-${name}-ms`, `${s.duration}ms`);
  }
}

/**
 * Smooth, eased scrolling for a sideways strip. Mouse wheels and touchpads both feed a target
 * position; each frame the strip glides a fraction of the way there, so motion never jumps.
 */
export class SmoothScroller {
  constructor(el, { horizontal = true, ownWheel = () => false } = {}) {
    this.el = el;
    this.horizontal = horizontal;
    this.target = 0;
    this.frame = 0;
    this.latchTimer = 0;
    el.addEventListener('wheel', e => {
      // While an outer strip is mid-scroll, let the gesture keep going there.
      if (el.parentElement?.closest('[data-scrolling]'))
        return;
      if (!el.dataset.scrolling && ownWheel(e))
        return;
      const delta = (Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY) *
        (e.deltaMode === 1 ? 32 : 1);
      const max = horizontal ? el.scrollWidth - el.clientWidth : el.scrollHeight - el.clientHeight;
      if (max <= 0)
        return;
      e.preventDefault();
      this.latch();
      if (!this.frame)
        this.target = this.position;
      this.target = Math.max(0, Math.min(max, this.target + delta));
      this.run();
    }, { passive: false });
  }

  /**
   * Like browsers do, a scroll gesture sticks to the strip that started it, so things sliding
   * under the pointer mid-scroll (a timer wheel, a list) don't grab it.
   */
  latch() {
    this.el.dataset.scrolling = '1';
    clearTimeout(this.latchTimer);
    this.latchTimer = setTimeout(() => delete this.el.dataset.scrolling, 320);
  }

  get position() {
    return this.horizontal ? this.el.scrollLeft : this.el.scrollTop;
  }

  set position(value) {
    if (this.horizontal)
      this.el.scrollLeft = value;
    else
      this.el.scrollTop = value;
  }

  scrollTo(value, instant = false) {
    this.target = value;
    if (instant)
      this.position = value;
    else
      this.run();
  }

  run() {
    if (this.frame)
      return;
    let last = performance.now();
    const step = now => {
      // Frame-rate independent glide: same feel at 60 Hz and 144 Hz.
      const k = 1 - Math.pow(1 - 0.2, (now - last) / 16.67);
      last = now;
      const diff = this.target - this.position;
      if (Math.abs(diff) < 0.5) {
        this.position = this.target;
        this.frame = 0;
        return;
      }
      this.position = this.position + diff * k;
      this.frame = requestAnimationFrame(step);
    };
    this.frame = requestAnimationFrame(step);
  }
}
