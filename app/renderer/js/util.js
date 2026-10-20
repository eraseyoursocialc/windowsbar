/** "3:07" or "1:02:45". */
export function clock(totalSeconds) {
  const s = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = String(s % 60).padStart(2, '0');
  return h ? `${h}:${String(m).padStart(2, '0')}:${sec}` : `${m}:${sec}`;
}

/** Same as clock() but rounds up, so a fresh 5:00 timer doesn't flash 4:59. */
export function countdown(ms) {
  return clock(Math.ceil(Math.max(0, ms) / 1000));
}

export function dayKey(date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className)
    node.className = className;
  if (text != null)
    node.textContent = text;
  return node;
}

/** True while the user is typing somewhere (so the island shouldn't close under them). */
export function isEditing(node) {
  return !!node && (node.tagName === 'INPUT' && node.type === 'text' || node.isContentEditable);
}

/** A short, soft chime made on the fly, for finished timers and reminders. */
export function chime() {
  try {
    const ctx = new AudioContext();
    const now = ctx.currentTime;
    for (const [i, freq] of [880, 1318.5].entries()) {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.value = freq;
      gain.gain.setValueAtTime(0, now + i * 0.16);
      gain.gain.linearRampToValueAtTime(0.18, now + i * 0.16 + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.001, now + i * 0.16 + 0.9);
      osc.connect(gain).connect(ctx.destination);
      osc.start(now + i * 0.16);
      osc.stop(now + i * 0.16 + 1);
    }
    setTimeout(() => ctx.close(), 1600);
  } catch {
    // No audio device; stay quiet.
  }
}
