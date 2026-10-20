import { icon } from '../icons.js';

const PAGES = ['◆', '▲', '●'];
const COLORS = ['#ffffff', '#4aa8ff', '#ffd60a', '#ff6482', '#30d158'];
const SIZES = ['3', '4', '5']; // execCommand fontSize steps: normal, large, larger

/** Three small pages of formatted notes, saved as you type. */
export class NotesWidget {
  constructor(root, bridge, pages) {
    this.bridge = bridge;
    this.pages = pages.length === 3 ? pages.map(sanitize) : ['', '', ''];
    this.page = 0;
    this.color = 1;
    this.size = 0;
    this.saveTimer = 0;

    root.innerHTML = `
      <div class="notes-card">
        <div class="notes-editor" contenteditable="true" spellcheck="false" data-placeholder="Notes for me."></div>
        <div class="notes-bar">
          <button class="btn ghost tiny swatch-btn" data-act="color" title="Text color"><span class="swatch"></span></button>
          <span class="spacer"></span>
          <button class="btn ghost tiny fmt" data-act="size" title="Text size">A</button>
          <button class="btn ghost tiny fmt b" data-act="bold" title="Bold (Ctrl+B)">B</button>
          <button class="btn ghost tiny fmt i" data-act="italic" title="Italic (Ctrl+I)">I</button>
          <button class="btn ghost tiny fmt u" data-act="underline" title="Underline (Ctrl+U)">U</button>
        </div>
      </div>
      <div class="note-pages">
        ${PAGES.map((p, i) => `<button class="btn ghost tiny page-dot" data-page="${i}" title="Note ${i + 1}">${p}</button>`).join('')}
      </div>`;

    this.editor = root.querySelector('.notes-editor');
    this.swatch = root.querySelector('.swatch');
    this.dots = [...root.querySelectorAll('.page-dot')];
    this.swatch.style.background = COLORS[this.color];

    this.editor.addEventListener('input', () => this.changed());
    // Keep pasted text plain so notes stay tidy.
    this.editor.addEventListener('paste', e => {
      e.preventDefault();
      document.execCommand('insertText', false, e.clipboardData.getData('text/plain'));
    });

    // Buttons act on the selection without stealing it.
    for (const btn of root.querySelectorAll('[data-act]')) {
      btn.addEventListener('mousedown', e => e.preventDefault());
      btn.addEventListener('click', () => this.format(btn.dataset.act));
    }
    for (const dot of this.dots) {
      dot.addEventListener('mousedown', e => e.preventDefault());
      dot.addEventListener('click', () => this.show(+dot.dataset.page));
    }

    this.show(0);
  }

  show(page) {
    this.flush();
    this.page = page;
    this.editor.innerHTML = this.pages[page];
    this.updatePlaceholder();
    this.dots.forEach((dot, i) => dot.classList.toggle('active', i === page));
  }

  format(action) {
    this.editor.focus();
    document.execCommand('styleWithCSS', false, true);
    if (action === 'color') {
      this.color = (this.color + 1) % COLORS.length;
      this.swatch.style.background = COLORS[this.color];
      document.execCommand('foreColor', false, COLORS[this.color]);
    } else if (action === 'size') {
      this.size = (this.size + 1) % SIZES.length;
      document.execCommand('fontSize', false, SIZES[this.size]);
    } else {
      document.execCommand(action);
    }
    this.changed();
  }

  changed() {
    this.pages[this.page] = this.editor.innerHTML;
    this.updatePlaceholder();
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.flush(), 500);
  }

  flush() {
    clearTimeout(this.saveTimer);
    this.bridge.store.set('notes', this.pages);
  }

  updatePlaceholder() {
    this.editor.classList.toggle('is-empty', !this.editor.textContent.trim());
  }
}

/**
 * Notes are stored as HTML; before showing a saved note again, keep only harmless formatting
 * (bold, italic, underline, colors, sizes, line breaks) and drop everything else.
 */
function sanitize(html) {
  const doc = new DOMParser().parseFromString(`<div>${html ?? ''}</div>`, 'text/html');
  const allowed = new Set(['B', 'STRONG', 'I', 'EM', 'U', 'SPAN', 'FONT', 'DIV', 'P', 'BR']);
  const walk = node => {
    for (const child of [...node.children]) {
      if (!allowed.has(child.tagName)) {
        child.replaceWith(...child.childNodes);
        continue;
      }
      for (const attr of [...child.attributes]) {
        const keep = (attr.name === 'style' && /^[\w\s:;#%,.()-]*$/.test(attr.value) && !/url\s*\(/i.test(attr.value)) ||
          (attr.name === 'color' && /^#?[\w]+$/.test(attr.value)) ||
          (attr.name === 'size' && /^\d$/.test(attr.value));
        if (!keep)
          child.removeAttribute(attr.name);
      }
      walk(child);
    }
  };
  const root = doc.body.firstElementChild;
  walk(root);
  return root.innerHTML;
}
