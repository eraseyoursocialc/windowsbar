const fs = require('fs');
const path = require('path');

/** Everything WindowsBar remembers, kept in one JSON file and written shortly after each change. */
class Store {
  constructor(file, defaults) {
    this.file = file;
    this.data = merge(structuredClone(defaults), read(file));
    this.timer = null;
  }

  set(key, value) {
    this.data[key] = value;
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.save(), 400);
  }

  save() {
    clearTimeout(this.timer);
    try {
      fs.mkdirSync(path.dirname(this.file), { recursive: true });
      // Write then rename, so a crash mid-write never leaves a broken file.
      fs.writeFileSync(this.file + '.tmp', JSON.stringify(this.data, null, 2));
      fs.renameSync(this.file + '.tmp', this.file);
    } catch (err) {
      console.error('Could not save data', err);
    }
  }
}

function read(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return {};
  }
}

/** Fills in anything missing from saved data with the defaults, one level into objects. */
function merge(defaults, saved) {
  for (const [key, value] of Object.entries(saved ?? {})) {
    const base = defaults[key];
    defaults[key] = base && typeof base === 'object' && !Array.isArray(base) && value && typeof value === 'object'
      ? { ...base, ...value }
      : value;
  }
  return defaults;
}

module.exports = Store;
