# WindowsBar

A Dynamic Island–style bar for Windows 10 and 11, inspired by NotchNook on the Mac.

A small black pill sits at the top-center of your screen. Hover over it and it opens into a strip of widgets. Drag a file onto it and it becomes a drop zone.

## What it does

**Collapsed pill ("live activities")**
- Album art and bouncing bars while music plays
- A countdown while a timer runs
- How many files are in the tray
- A short notice when you plug in or unplug the charger, or when a timer ends

**Nook tab** (scroll sideways with the mouse wheel)
- **Music**: artwork, title, artist, previous / play-pause / next, and a seek bar you can drag. Works with Spotify, browsers, Media Player, and anything else that appears in the Windows volume flyout.
- **Mirror**: click the circle to see your webcam, for a quick check before a call. Click again to turn it off. It also turns off when the island closes.
- **Calendar**: this month's days in a scrollable strip. Click a day to see its events.
- **Notes**: three note pages (◆ ▲ ●) with color, size, bold, italic and underline. Saved as you type.
- **Timers**: scroll the hour, minute and second wheels and press play, or use the 1m / 5m buttons. You can run several timers at once.
- **Tasks**: type a task and press Enter. Click the circle to tick it off.

**Tray tab**
- Drop files on the island to keep them handy, and drag them out into any folder or app later. Double-click a file to open it.
- While you drag a file over the island, it shows two targets: **Files Tray** and **Nearby Share**. Nearby Share opens the Windows share sheet and is the Windows equivalent of AirDrop.

**Settings** (gear icon, or the WindowsBar icon next to the clock)
- Start with Windows
- Hide when an app is fullscreen (games, videos, presentations)
- Quit

## Install

1. Open the **Actions** tab of this repository and click the latest green **Build** run.
2. Under **Artifacts**, download **WindowsBar-win-x64** and unzip it.
3. Double-click `WindowsBar.exe`. Nothing else needs to be installed.

Windows may show a blue "Windows protected your PC" screen because the app isn't code-signed. Click **More info**, then **Run anyway**.

## Good to know

- **Mirror shows "No camera"**: go to Settings → Privacy & security → Camera and turn on *Let desktop apps access your camera*.
- **Calendar events**: Windows only lets packaged Store apps read your calendar. Until WindowsBar is packaged, the calendar shows dates but may say "Nothing for today".
- Notes, tasks, tray files and settings are stored in `%AppData%\WindowsBar`. A `log.txt` there records any errors.

## Build it yourself

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet run --project src/WindowsBar
```

To make a single `.exe`:

```
dotnet publish src/WindowsBar/WindowsBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## How it's built

C# with WPF on .NET 8. The Windows features come from:
- **Music**: Windows media session API (`GlobalSystemMediaTransportControlsSessionManager`)
- **Mirror**: `MediaCapture` frame reader
- **Nearby Share**: the Windows share sheet (`DataTransferManager`)
- **Always on top, hidden from Alt+Tab, fullscreen detection**: a few Win32 calls in `Services/Native.cs`

The code lives in `src/WindowsBar`: `NotchWindow` is the island, `Widgets/` holds each widget, and `Services/` holds the logic behind them.
