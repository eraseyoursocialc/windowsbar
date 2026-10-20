import { icon } from '../icons.js';
import { SmoothScroller } from '../motion.js';
import { el } from '../util.js';

/** A small to-do list: type and press Enter, tap the circle when it's done. */
export class TasksWidget {
  constructor(root, bridge, tasks) {
    this.bridge = bridge;
    this.tasks = tasks;

    root.innerHTML = `
      <label class="field">
        ${icon('plus', 12)}
        <input type="text" class="task-input" placeholder="New task…" maxlength="120">
      </label>
      <div class="task-list"></div>`;

    this.input = root.querySelector('.task-input');
    this.list = root.querySelector('.task-list');
    new SmoothScroller(this.list, { horizontal: false });

    this.input.addEventListener('keydown', e => {
      if (e.key === 'Enter' && this.input.value.trim()) {
        this.tasks = [{ text: this.input.value.trim(), done: false }, ...this.tasks];
        this.input.value = '';
        this.save();
        this.render(this.tasks[0]);
      }
    });

    this.render();
  }

  render(fresh) {
    this.list.replaceChildren(...this.tasks.map(task => {
      const row = el('div', 'task');
      row.classList.toggle('done', task.done);
      const check = el('button', 'check');
      check.innerHTML = icon('check', 10);
      check.title = task.done ? 'Mark as not done' : 'Mark as done';
      check.addEventListener('click', () => {
        task.done = !task.done;
        row.classList.toggle('done', task.done);
        this.save();
      });
      const remove = el('button', 'btn ghost tiny task-remove');
      remove.innerHTML = icon('x', 10);
      remove.title = 'Delete';
      remove.addEventListener('click', () => {
        row.classList.add('leaving');
        setTimeout(() => {
          this.tasks = this.tasks.filter(t => t !== task);
          this.save();
          this.render();
        }, 180);
      });
      row.append(check, el('span', 'task-text', task.text), remove);
      if (task === fresh)
        row.classList.add('entering');
      return row;
    }));
  }

  save() {
    this.bridge.store.set('tasks', this.tasks);
  }
}
