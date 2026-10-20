import { icon } from '../icons.js';

/** Click the circle to see yourself; click again (or close the island) to turn the camera off. */
export class MirrorWidget {
  constructor(el) {
    el.innerHTML = `
      <button class="mirror" title="Mirror: a quick look at your webcam">
        <video muted playsinline></video>
        <span class="mirror-label">${icon('camera', 20)}<span class="mirror-text">Mirror</span></span>
      </button>`;
    this.button = el.querySelector('.mirror');
    this.video = el.querySelector('video');
    this.text = el.querySelector('.mirror-text');
    this.stream = null;
    this.button.addEventListener('click', () => (this.stream ? this.stop() : this.start()));
  }

  async start() {
    this.text.textContent = 'Starting…';
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'user' },
        audio: false,
      });
      this.video.srcObject = this.stream;
      await this.video.play();
      this.button.classList.add('live');
      this.text.textContent = 'Mirror';
    } catch {
      this.stream = null;
      this.text.textContent = 'No camera';
      this.button.title = 'Couldn\'t open the camera. Check Settings › Privacy › Camera › "Let desktop apps access your camera".';
    }
  }

  stop() {
    this.stream?.getTracks().forEach(t => t.stop());
    this.stream = null;
    this.video.srcObject = null;
    this.button.classList.remove('live');
    this.text.textContent = 'Mirror';
  }
}
