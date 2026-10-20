const { spawn } = require('child_process');
const fs = require('fs');
const readline = require('readline');
const { EventEmitter } = require('events');

/**
 * Runs WindowsBar.Helper.exe and talks to it one JSON message per line.
 * Emits 'message' for everything it reports (media, artwork, fullscreen).
 * Restarts it a few times if it stops unexpectedly.
 */
class Helper extends EventEmitter {
  constructor(exe) {
    super();
    this.exe = exe;
    this.proc = null;
    this.restarts = 0;
    this.stopped = false;
  }

  get available() {
    return process.platform === 'win32' && fs.existsSync(this.exe);
  }

  start() {
    if (!this.available || this.stopped)
      return;
    this.proc = spawn(this.exe, [], { stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true });
    readline.createInterface({ input: this.proc.stdout }).on('line', line => {
      try {
        this.emit('message', JSON.parse(line));
      } catch {
        // Ignore anything that isn't a message.
      }
    });
    this.proc.on('exit', () => {
      this.proc = null;
      if (!this.stopped && this.restarts++ < 5)
        setTimeout(() => this.start(), 1000);
    });
    this.proc.on('error', err => console.error('Helper failed', err));
  }

  send(message) {
    if (this.proc?.stdin.writable)
      this.proc.stdin.write(JSON.stringify(message) + '\n');
  }

  stop() {
    this.stopped = true;
    this.proc?.stdin.end();
  }
}

module.exports = Helper;
