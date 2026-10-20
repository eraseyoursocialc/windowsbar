import { icon } from '../icons.js';
import { SmoothScroller } from '../motion.js';
import { el } from '../util.js';

/** The Tray tab: files dropped on the island, ready to be dragged out again. */
export class Shelf {
  constructor(root, bridge, paths) {
    this.bridge = bridge;
    this.paths = [];
    this.draggingOut = false;
    this.onChange = () => {};

    root.innerHTML = `
      <div class="shelf-empty">
        ${icon('tray', 24)}
        <div class="shelf-title">Drop files here to keep them handy</div>
        <div class="shelf-sub">Drag them back out whenever you need them</div>
      </div>
      <div class="shelf-items"></div>`;
    this.root = root;
    this.items = root.querySelector('.shelf-items');
    new SmoothScroller(this.items);

    bridge.on('dragdone', () => {
      this.draggingOut = false;
      this.prune();
    });

    bridge.files.existing(paths).then(existing => this.set(existing, false));
  }

  get count() {
    return this.paths.length;
  }

  add(paths) {
    const fresh = paths.filter(p => p && !this.paths.some(q => q.toLowerCase() === p.toLowerCase()));
    if (fresh.length)
      this.set([...this.paths, ...fresh], true, fresh);
  }

  remove(path) {
    this.set(this.paths.filter(p => p !== path), true);
  }

  clear() {
    this.set([], true);
  }

  async prune() {
    const existing = await this.bridge.files.existing(this.paths);
    if (existing.length !== this.paths.length)
      this.set(existing, true);
  }

  set(paths, save, fresh = []) {
    this.paths = paths;
    if (save)
      this.bridge.store.set('shelf', paths);
    this.root.classList.toggle('empty', paths.length === 0);
    this.items.replaceChildren(...paths.map(path => this.card(path, fresh.includes(path))));
    this.onChange();
  }

  card(path, isNew) {
    const name = path.split(/[\\/]/).filter(Boolean).pop() || path;
    const card = el('div', 'file');
    card.title = path;
    card.draggable = true;
    if (isNew)
      card.classList.add('entering');

    const thumb = el('div', 'file-icon');
    thumb.innerHTML = icon('file', 26);
    this.bridge.files.icon(path).then(src => {
      if (src) {
        const img = el('img');
        img.src = src;
        img.alt = '';
        thumb.replaceChildren(img);
      }
    });

    const remove = el('button', 'btn ghost tiny file-remove');
    remove.innerHTML = icon('x', 10);
    remove.title = 'Remove from tray';
    remove.addEventListener('click', e => {
      e.stopPropagation();
      card.classList.add('leaving');
      setTimeout(() => this.remove(path), 180);
    });

    card.append(thumb, el('div', 'file-name', name), remove);
    card.addEventListener('dblclick', () => this.bridge.files.open(path));
    card.addEventListener('dragstart', e => {
      // Hand the real file to Windows so it can be dropped into any folder or app.
      e.preventDefault();
      this.draggingOut = true;
      this.bridge.files.startDrag(path);
    });
    return card;
  }
}
