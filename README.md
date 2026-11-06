# RestMode

**PlayStation 4-style Rest Mode for Windows 10 and 11.**

Press a hotkey and your PC *looks* off: the screen goes dark, the sound is
muted and the processor is slowed right down so the fans quieten. But it keeps
running, so your Steam/Epic/Xbox downloads, game updates, torrents and file
transfers carry on. When the downloads are done, RestMode can put the PC to
sleep, hibernate it or shut it down. Touch the mouse, press a key or a
controller button and you're back exactly where you left off, with all your
normal settings.

---

## What it does

When you **enter Rest Mode** (hotkey `Ctrl+Alt+Shift+R`, the tray icon, or the desktop shortcut):

1. Saves your current power plan, volume and other settings to disk.
2. Switches to a temporary **copy** of your power plan with low-power settings
   (for both plugged-in and battery):
   - maximum processor speed 25 % (you can change it), minimum 5 %
   - turbo boost off, "energy saving" preference, **passive cooling** (slow the CPU down before speeding fans up)
   - PCI Express link power management: maximum savings
   - hard disks turn off when idle (a running transfer keeps them on)
   - Wi-Fi adapter at maximum performance, so downloads stay fast
3. Tells Windows "don't go to sleep" but lets the **screen turn off**, then turns the screen off immediately.
4. Mutes the sound.
5. Keeps the network adapters fully powered (unticks *Allow the computer to turn off this device to save power*).
6. Optionally slows down or pauses apps you choose (never downloaders, game launchers, browsers or Windows itself).
7. Optionally switches [Fan Control](https://getfancontrol.com) to a "Quiet" profile.

While resting it watches network and disk traffic. When traffic stays below
**50 KB/s** (network) and **2 MB/s** (disk) for **10 minutes**, it assumes
downloads are done and does what you chose: **Sleep**, **Hibernate**, **Shut
down**, or **Stay** in Rest Mode. Every session is written to a small log.

When you **wake it up** (mouse, keyboard, or an Xbox-style controller
button), everything is put back: screen on, your power plan, volume, app
priorities, network adapter settings and fan profile.

## What a PC can't copy from the PS4

A PS4 has a separate little chip that handles downloads while the main
processor sleeps. **PCs don't have one.** If a PC really sleeps, nothing
downloads. So RestMode keeps the PC *on* and makes it look and act as "off"
as possible. That means:

| | PS4 Rest Mode | PC with RestMode |
|---|---|---|
| Screen off, sound off | ✅ | ✅ |
| Downloads and updates continue | ✅ | ✅ (all apps, not just the store) |
| Instant wake to where you left off | ✅ | ✅ |
| Network stays on, USB ports stay powered | ✅ | ✅ |
| Sleep automatically when downloads finish | ❌ | ✅ |
| **Power use about 5-10 W** | ✅ | ❌ A capped desktop still uses roughly **25-80 W** (laptops 3-15 W), because the processor, motherboard and memory have to stay on. Still much less than normal use. |
| **Near silent** | ✅ | ⚠️ The CPU fan slows down. Case and graphics card fans follow your BIOS/driver settings, unless you use Fan Control. |
| **Game frozen in memory** | ✅ | ⚠️ A running game keeps running (slowly). You can add a *single-player* game to the app list with **Pause**. Don't pause online games or games with anti-cheat. |
| Turn on from the network / Remote Play | ✅ | ❌ Not part of RestMode (use Wake-on-LAN separately). |

**Why not Windows "Modern Standby"?** Some laptops have a standby mode that
keeps Wi-Fi connected. But in that mode Windows pauses normal desktop apps,
so Steam, Epic, browsers and torrent clients stop downloading. That's why
RestMode doesn't rely on it. Details: [docs/RESEARCH.md](docs/RESEARCH.md).

## Install

RestMode is a single file. There is no installer.

1. **Get `RestMode.exe`**, either:
   - from GitHub: **Actions** tab > newest **RestMode** run > **Artifacts** > `RestMode-win-x64` (unzip it), or
   - build it yourself (see [Build](#build) below).
2. Put it somewhere permanent, e.g. `C:\Users\<you>\AppData\Local\Programs\RestMode\RestMode.exe`.
   (Don't run it from the Downloads folder if you plan to use *Start with Windows* or the helper task.)
3. Double-click it. A blue ⏻ icon appears in the system tray (click the `^` arrow
   if it is hidden) and the settings window opens the first time.
4. In Settings:
   - **General**: tick *Start RestMode with Windows*, and click *Create "Enter Rest Mode" desktop shortcut* if you want one.
   - **Admin / Fans**: click **Allow without UAC prompts (install helper task)**
     and approve the one admin prompt. Without it, Windows asks for admin
     approval each time you enter and leave Rest Mode (see [Admin rights](#admin-rights)).
5. Click **Save**.

Windows SmartScreen may warn that the app is unrecognised because it isn't
code-signed. Click *More info* > *Run anyway* if you built it or got it from
your own repository.

## Using it

| To... | Do this |
|---|---|
| Enter Rest Mode | `Ctrl+Alt+Shift+R`, or tray icon > **Enter Rest Mode**, or the desktop shortcut |
| Wake up | Move the mouse, press any key, or press a button on an Xbox / XInput controller (PlayStation pads work through DS4Windows or Steam Input) |
| Change settings | Double-click the tray icon |
| See what happened | Tray icon > **View log** |
| Put everything back right now | Tray icon > **Restore everything now** |

The tray icon is **orange** while resting, like the PS4's rest light.

### Settings in plain words

- **Maximum processor state**: how fast the CPU may run while resting. 20-30 % is plenty for downloads. Lower = quieter and cooler.
- **Turn off hard disks after**: spinning hard drives stop when nothing is reading or writing. SSDs ignore this.
- **Downloads are finished when...**: if network traffic stays under this many KB/s **and** the disk is quiet for this many minutes, the end action runs. If your PC does a lot of background traffic (cloud backup, IP cameras), raise the threshold.
- **Then**: Sleep, Hibernate, Shut down (with a 60-second warning you can cancel by moving the mouse), or Stay in Rest Mode.
- **On battery, end Rest Mode at %**: protects laptops from running flat.
- **Laptops: keep resting with the lid closed**: lets downloads continue with the lid shut. Don't put a running laptop in a bag.
- **Mouse movement to wake**: a few pixels ignores desk bumps; 0 = any movement.
- **Apps tab**: apps to slow down (*Low priority* = idle priority + Windows "Efficiency mode") or freeze (*Pause*) while resting. They are always put back when you wake. Pausing is skipped automatically if the app is busy transferring data. These are **never** touched, even if you list them: Windows system processes, Steam, Epic, EA, Ubisoft, GOG, Battle.net, Xbox, Riot, torrent and download managers, OneDrive/Dropbox/Google Drive, copy tools and every web browser (RestMode can't tell whether a browser is downloading, so it leaves them all alone).

## Fan control

Windows has no standard way to control case or graphics card fans. RestMode does two things:

1. **Always**: the CPU limit, turbo off and *passive cooling* make the processor run cool, so its fan slows down on its own. On most PCs this is the biggest change.
2. **If you use [Fan Control](https://getfancontrol.com) by Rem0o** (free): RestMode switches it to a quiet profile on entry and back to your normal profile on wake, using Fan Control's command line (`FanControl.exe -c Quiet.json`).
   - In Fan Control, make a config with gentle curves and save it as e.g. `Quiet.json`.
   - In RestMode Settings > **Admin / Fans**: check the FanControl.exe path (click *Detect*), set *Quiet profile* = `Quiet.json` and *Normal profile* = your everyday config (e.g. `userConfig.json`).
   - Fan Control runs as administrator, so this uses the admin helper too.

If Fan Control isn't installed, this step is simply skipped.

## Admin rights

RestMode runs as a normal user. Only two things need admin rights:

- the network adapter *"allow the computer to turn off this device"* checkbox, and
- switching the Fan Control profile.

For those, RestMode starts a short-lived admin copy of itself (`RestMode.exe --helper sync`):

- **Without the helper task**: Windows shows a UAC prompt when you enter Rest Mode (before the screen turns off) and when you wake. If you say No, that step is skipped and everything else still works. If the PC goes to sleep on its own, the adapter/fan settings are put back the next time you use the PC (one prompt).
- **With the helper task** (Settings > Admin / Fans > *Allow without UAC prompts*): one approval when installing; after that no prompts. It is an on-demand task called **RestMode Helper** in Task Scheduler. For safety it runs a copy of RestMode.exe kept in `C:\Program Files\RestMode Helper` (only administrators can change files there, so no other program can swap it for something else). **Reinstall the task after updating RestMode.exe** or moving Fan Control.
- If you turn both features off, RestMode never asks for admin rights.

## Safety: your settings always come back

- RestMode **never edits your own power plan**. It makes a temporary copy (`RestMode (temporary)`), switches to it, and on wake switches back and deletes the copy.
- Before changing anything, it writes what it is about to change to `%LOCALAPPDATA%\RestMode\state.json`, and updates it after every step.
- **Crash or forced close**: the next time RestMode starts (e.g. at the next sign-in, if *Start with Windows* is on), it reads that file and restores everything, then tells you.
- **Sign-out / restart / shutdown while resting**: it restores everything before Windows closes it.
- **The PC goes to sleep while resting** (power button, lid, battery): everything is restored as soon as it wakes.
- **Any single step fails** (an unusual PC, a missing setting): that step is skipped and logged; the others still run.

## Files

Everything lives in `%LOCALAPPDATA%\RestMode` (paste that into the Explorer address bar):

| File | What it is |
|---|---|
| `settings.json` | Your settings |
| `restmode.log` | What happened (one `SESSN` line per Rest Mode session, plus details) |
| `helper.log` | What the admin helper did |
| `state.json` | Only exists while resting (or after a crash): what to restore |
| `helper-state.json` | Only exists while adapter/fan settings are changed |

Example session line:

```
2026-11-06 23:41:02 [SESSN] start=2026-11-06 21:12 duration=2h29m downloaded=48.3 GB uploaded=0.9 GB end=Sleep (downloads finished: traffic below 50 KB/s for 10 min)
```

## Uninstall / restore defaults

**Normal way:** run

```bat
RestMode.exe --uninstall
```

(Win+R, then drag `RestMode.exe` into the box and add ` --uninstall`.) It
restores every setting, removes the startup entry, desktop shortcut and helper
task, and offers to delete its settings and logs. Then delete `RestMode.exe`.

**Only put settings back** (e.g. after a crash, without uninstalling):

```bat
RestMode.exe --restore
```

**By hand**, if RestMode.exe is gone. Open *Command Prompt* and:

1. See your plans: `powercfg /list`
2. Switch to yours (or to Balanced): `powercfg /setactive SCHEME_BALANCED`
3. Delete any plan named *RestMode (temporary)*: `powercfg /delete <its GUID>`
4. Turn the sound back on (speaker icon in the taskbar).
5. Device Manager > Network adapters > your adapter > **Power Management**: tick *Allow the computer to turn off this device to save power* again if you had it ticked before.
6. Remove the helper task (admin Command Prompt): `schtasks /delete /tn "RestMode Helper" /f` and delete the folder `C:\Program Files\RestMode Helper`
7. Remove autostart: Task Manager > **Startup apps** > RestMode > Disable (or delete the `RestMode` value in `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`).
8. Delete the folder `%LOCALAPPDATA%\RestMode`.

If an app is still frozen (paused), end it in Task Manager or restart the PC.

## Troubleshooting

- **The PC wakes straight back up.** Something is sending mouse movement (a vibrating desk, a sensitive mouse, a "keep awake" or remote-control app). Raise *Mouse movement to wake* in Settings.
- **The screen comes back on by itself.** RestMode turns it off again after a few seconds. If a specific app keeps doing it, check `powercfg /requests` for apps holding a DISPLAY request.
- **It went to sleep while a download was still running.** The download was slower than the threshold, or paused. Lower *Downloads are finished when network is below* or raise the minutes.
- **It never goes to sleep.** Some background traffic is above the threshold (cloud backup, streaming, other PCs). Raise the KB/s thresholds or set the disk threshold to 0. The log shows the measured speed every 5 minutes.
- **"Windows refused to sleep"** in the log: a driver or policy blocks sleep. Run `powercfg /requests` and `powercfg /a` in an admin Command Prompt.
- **The hotkey doesn't work.** Another app uses it; RestMode tells you so. Pick another in Settings.
- **ASPM / boost / cooling-policy lines say "skipped"** in the log: your PC or BIOS doesn't offer that setting. That's normal.

## Build

You need Windows 10/11 and the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

From the repository folder, run:

```bat
build.cmd
```

The result is **`dist\RestMode.exe`**: one self-contained file (~65 MB) that runs on any Windows 10/11 x64 PC without installing .NET. The command it runs:

```bat
dotnet publish src\RestMode.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

Other options (PowerShell):

```powershell
.\build.ps1 -Small    # ~1 MB exe, needs the ".NET 8 Desktop Runtime" installed
.\build.ps1 -Arm64    # Windows on ARM
```

Every push that changes the code also builds the exe on GitHub Actions (`.github/workflows/restmode.yml`).

## Testing

See **[docs/TEST-CHECKLIST.md](docs/TEST-CHECKLIST.md)**: enter, wake with mouse, auto-sleep after a download, crash recovery, and more.

## Source code map

| Folder / file | What it does |
|---|---|
| `src/Program.cs` | Start-up, single instance, command line (`--enter`, `--restore`, `--uninstall`) |
| `src/Core/RestModeController.cs` | The main logic: enter, rest, wake, end actions, sleep/resume, log-off |
| `src/Core/PowerPlanManager.cs` | Duplicates your plan and writes the low-power values (AC + DC); restores and cleans up |
| `src/Core/PowerGuids.cs` | Power setting GUIDs (same as `powercfg /aliases`) |
| `src/Core/Restorer.cs` | The one routine that puts everything back |
| `src/Core/AudioController.cs` | Mute/restore via Windows Core Audio |
| `src/Core/DisplayController.cs` | Screen off/on |
| `src/Core/InputWatcher.cs` | Wake detection: keyboard, mouse (with jitter threshold), XInput controllers |
| `src/Core/ActivityMonitor.cs` | Network + disk throughput, "downloads finished" detection |
| `src/Core/ProcessThrottler.cs` | Optional low priority / pause for listed apps, with the protected list |
| `src/Core/RestState.cs`, `AppSettings.cs` | What is saved to disk |
| `src/Elevation/*` | Admin helper: network adapter power flag, Fan Control, the optional scheduled task |
| `src/UI/*` | Tray icon, hidden message window, settings window |
| `docs/RESEARCH.md` | How PS4 Rest Mode works vs. Windows power management, and the design |

## License

MIT License, see [LICENSE](LICENSE).
