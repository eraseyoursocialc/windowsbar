import { icon } from '../icons.js';
import { SmoothScroller } from '../motion.js';
import { dayKey, el } from '../util.js';

const DAY_WIDTH = 30;

/**
 * Month, a strip of days to pick from, and that day's plans. Plans are typed in here
 * ("14:30 Gym" or "Call mom") and WindowsBar reminds you when a timed one comes up.
 */
export class CalendarWidget {
  constructor(root, bridge, events, onReminder) {
    this.bridge = bridge;
    this.events = events; // { "2026-10-20": [{ time: "14:30", text: "Gym" }] }
    this.onReminder = onReminder;
    this.selected = startOfDay(new Date());
    this.builtFor = '';
    this.reminded = new Set();

    root.innerHTML = `
      <div class="cal-head">
        <div class="month"></div>
        <div class="days"><div class="days-track"></div></div>
      </div>
      <div class="cal-body">
        <div class="plans"></div>
        <div class="plan-empty">${icon('calendar', 15)}<span></span></div>
      </div>
      <label class="field small">
        ${icon('plus', 12)}
        <input type="text" class="plan-input" placeholder="Add a plan…" maxlength="80">
      </label>`;

    this.month = root.querySelector('.month');
    this.days = root.querySelector('.days');
    this.track = root.querySelector('.days-track');
    this.plans = root.querySelector('.plans');
    this.empty = root.querySelector('.plan-empty');
    this.emptyText = this.empty.querySelector('span');
    this.input = root.querySelector('.plan-input');
    this.scroller = new SmoothScroller(this.days);

    this.input.addEventListener('keydown', e => {
      if (e.key === 'Enter' && this.input.value.trim()) {
        this.add(this.input.value);
        this.input.value = '';
      }
    });

    this.refresh();
  }

  /** Called when the island opens and every few seconds; rebuilds only when the day changes. */
  refresh() {
    const today = dayKey(new Date());
    if (this.builtFor !== today) {
      this.builtFor = today;
      this.selected = startOfDay(new Date());
      this.build(true);
    }
    this.checkReminders();
  }

  build(instant) {
    const locale = undefined; // the system default
    const sel = this.selected;
    const todayKey = dayKey(new Date());
    this.month.textContent = sel.toLocaleDateString(locale, { month: 'short' }).replace('.', '');
    this.track.replaceChildren();

    const count = new Date(sel.getFullYear(), sel.getMonth() + 1, 0).getDate();
    for (let d = 1; d <= count; d++) {
      const date = new Date(sel.getFullYear(), sel.getMonth(), d);
      const key = dayKey(date);
      const cell = el('button', 'day');
      cell.classList.toggle('today', key === todayKey);
      cell.classList.toggle('selected', d === sel.getDate());
      cell.classList.toggle('weekend', date.getDay() === 0 || date.getDay() === 6);
      cell.classList.toggle('has-plans', (this.events[key] ?? []).length > 0);
      cell.append(
        el('span', 'dow', date.toLocaleDateString(locale, { weekday: 'narrow' })),
        el('span', 'num', String(d)));
      cell.addEventListener('click', () => this.select(date));
      this.track.append(cell);
    }

    requestAnimationFrame(() => {
      const target = (sel.getDate() - 1) * DAY_WIDTH - this.days.clientWidth / 2 + DAY_WIDTH / 2;
      this.scroller.scrollTo(Math.max(0, target), instant);
    });
    this.renderPlans();
  }

  select(date) {
    this.selected = startOfDay(date);
    for (const cell of this.track.children)
      cell.classList.toggle('selected', +cell.querySelector('.num').textContent === date.getDate());
    const target = (date.getDate() - 1) * DAY_WIDTH - this.days.clientWidth / 2 + DAY_WIDTH / 2;
    this.scroller.scrollTo(Math.max(0, target));
    this.renderPlans();
  }

  renderPlans() {
    const key = dayKey(this.selected);
    const list = (this.events[key] ?? []).slice().sort((a, b) => (a.time || '').localeCompare(b.time || ''));
    this.plans.replaceChildren(...list.map(plan => {
      const row = el('div', 'plan');
      row.append(el('span', 'plan-time', plan.time || 'All day'), el('span', 'plan-text', plan.text));
      const remove = el('button', 'btn ghost tiny plan-remove');
      remove.innerHTML = icon('x', 11);
      remove.title = 'Remove';
      remove.addEventListener('click', () => this.remove(key, plan));
      row.append(remove);
      return row;
    }));
    const isToday = key === dayKey(new Date());
    this.emptyText.textContent = isToday
      ? 'Nothing for today'
      : `Nothing on ${this.selected.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' })}`;
    this.empty.hidden = list.length > 0;
    this.input.placeholder = isToday ? 'Add a plan… (e.g. 14:30 Gym)' : 'Add a plan for this day…';
  }

  add(raw) {
    const key = dayKey(this.selected);
    const plan = parsePlan(raw.trim());
    this.events = { ...this.events, [key]: [...(this.events[key] ?? []), plan] };
    this.save();
    this.build(false);
  }

  remove(key, plan) {
    const list = (this.events[key] ?? []).filter(p => p !== plan);
    this.events = { ...this.events };
    if (list.length)
      this.events[key] = list;
    else
      delete this.events[key];
    this.save();
    this.build(false);
  }

  save() {
    this.bridge.store.set('events', this.events);
  }

  checkReminders() {
    const now = new Date();
    const hhmm = `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
    for (const plan of this.events[dayKey(now)] ?? []) {
      const id = `${dayKey(now)} ${plan.time} ${plan.text}`;
      if (plan.time === hhmm && !this.reminded.has(id)) {
        this.reminded.add(id);
        this.onReminder(plan);
      }
    }
  }
}

function startOfDay(date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

/** "14:30 Gym", "2pm gym", "9.15 standup" or just "Call mom". */
function parsePlan(text) {
  const m = text.match(/^(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm)?\s+(.+)$/i);
  if (!m || (!m[2] && !m[3]))
    return { time: '', text };
  let hours = +m[1];
  const minutes = +(m[2] ?? 0);
  if (m[3]?.toLowerCase() === 'pm' && hours < 12)
    hours += 12;
  if (m[3]?.toLowerCase() === 'am' && hours === 12)
    hours = 0;
  if (hours > 23 || minutes > 59)
    return { time: '', text };
  return { time: `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`, text: m[4] };
}
