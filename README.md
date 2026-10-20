# WindowsBar

A Dynamic Island–style bar for Windows 10 and 11, inspired by NotchNook on the Mac.

Nothing sits on your screen. Push your mouse to the **top-middle edge**: a small tab slides down. Rest there for a moment and it unfolds into a strip of widgets. Move away and it tucks itself back out of sight.

## What it does

**Nook**: widgets you can scroll through sideways
- **Music**: artwork, title, play/pause/skip and a seek bar. Works with Spotify, browsers, Media Player, and anything else that appears in the Windows volume flyout.
- **Mirror**: click the circle to see your webcam, for a quick check before a call.
- **Calendar**: this month's days. Type a plan such as `14:30 Gym` and WindowsBar reminds you when it's time.
- **Notes**: three pages (◆ ▲ ●) with color, size, bold, italic and underline.
- **Timers**: scroll the hour, minute and second wheels, or tap 1m / 5m. You can run several at once.
- **Tasks**: type a task and press Enter. Tap the circle when it's done.

**Tray**
- Drop files on the island to keep them handy, and drag them back out into any folder or app later. Double-click a file to open it.
- While you drag a file to the top of the screen, the island offers **Files Tray** and **Nearby Share**. Nearby Share opens the Windows share sheet, which is the Windows equivalent of AirDrop.

**The small tab** shows what's going on: a running timer, the song playing, or how many files are in the tray. When a timer finishes or a plan comes up, it drops down with a short chime.

**Settings** (gear icon)
- Start with Windows (on by default)
- Stay away while an app is fullscreen (games, videos, slideshows)
- How long to rest at the top before it opens
- Which widgets appear in Nook

**Other ways to open it**: click the WindowsBar icon next to the clock, or press **Ctrl + Alt + Space**.

## Install

1. Open the **Actions** tab of this repository and click the latest green **Build** run.
2. Under **Artifacts**, download **WindowsBar-Setup** and unzip it.
3. Run `WindowsBar-Setup-….exe`. It installs, adds Start menu and desktop shortcuts, and starts WindowsBar.

Prefer not to install? Download **WindowsBar-Portable** instead and run it directly.

Windows may show a blue "Windows protected your PC" screen because the app isn't code-signed. Click **More info**, then **Run anyway**.

To uninstall: Settings → Apps → Installed apps → WindowsBar → Uninstall.

## Good to know

- **Mirror says "No camera"**: go to Settings → Privacy & security → Camera and turn on *Let desktop apps access your camera*.
- Your notes, tasks, plans and tray are saved in `%AppData%\WindowsBar`. Settings → Data folder opens it.

## How it's built

- `app/`: an [Electron](https://www.electronjs.org/) app. The island is plain HTML, CSS and JavaScript, drawn by Chromium on the graphics card. Motion uses real spring physics (`renderer/js/motion.js`).
- `helper/`: a small .NET Framework 4.8 program (already part of Windows 10 and 11) for the things only Windows can do: media controls, the share sheet and fullscreen detection. The app talks to it in JSON over stdin/stdout.

### Working on it

```
dotnet build helper -c Release     # the helper (Windows)
cd app
npm install
npm start                          # run the app
npm run dist                       # build the installer and portable exe
```

To work on the design in any browser, serve `app/renderer` (for example `python -m http.server`) and open it. A stand-in fakes the app: move the mouse to the top-middle edge, and a pretend song plays.
