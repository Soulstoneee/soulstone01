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
3. Tells Windows "don't go to sleep" but lets the **screen turn off**, then turns the screen off
   (instantly on classic S3 PCs; after ~15 s of no input on Modern Standby PCs, see [below](#different-kinds-of-pcs)).
4. Mutes the sound.
5. Keeps the network adapter you're using (wired or Wi-Fi) fully powered (unticks *Allow the computer to turn off this device to save power*, if it has that option).
6. Optionally slows down or pauses apps you choose (never downloaders, game launchers, browsers or Windows itself).
7. Makes the fans quieter: the laptop's own **Quiet** mode if it has one, or a [Fan Control](https://getfancontrol.com) "Quiet" profile, or the Windows power mode **Best power efficiency** (see [Fans](#fans)).

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

RestMode is a single **portable** file: it runs from any folder (even a USB
stick) without installing anything. Installing is optional.

1. **Get `RestMode.exe`**, either:
   - from GitHub: **Actions** tab > newest **RestMode** run > **Artifacts** > `RestMode-win-x64` (unzip it), or
   - build it yourself (see [Build](#build) below).
2. Double-click it. A blue ⏻ icon appears in the system tray (click the `^` arrow
   if it is hidden) and the settings window opens the first time.
3. **Optional install** (no admin needed): Settings > **General** > *Install for this user*,
   or run `RestMode.exe --install`. It copies RestMode to
   `%LOCALAPPDATA%\Programs\RestMode`, adds it to the Start menu and to
   Windows Settings > **Apps** (uninstall it from there). If you stay portable,
   keep the exe in a permanent folder (not Downloads) if you use *Start with
   Windows* or the helper task.
4. In Settings:
   - **General**: tick *Start RestMode with Windows*, and click *Create "Enter Rest Mode" desktop shortcut* if you want one.
   - **Fans**: check what "On this PC, Rest Mode will use" says (see [Fans](#fans)).
   - **Admin**: click **Allow without UAC prompts (install helper task)**
     and approve the one admin prompt. Without it, Windows asks for admin
     approval when you enter and leave Rest Mode if an admin step is needed (see [Admin rights](#admin-rights)).
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

## Fans

Windows has no standard way to control case or graphics card fans. RestMode does this:

1. **Always, on every PC**: the CPU limit, turbo off and *passive cooling* make the processor run cool, so fans that follow the CPU temperature slow down on their own. On most PCs this is the biggest change.
2. **Plus one of these**, chosen in Settings > **Fans** > *In Rest Mode, quieten fans with*:

| Option | What it does | Works on |
|---|---|---|
| **Auto** (default) | Built-in modes if found, else Fan Control if it is set up and running, else the Windows power mode. | Any PC |
| **Built-in fan modes** | Switches the laptop's own Quiet mode on (the Fn+Q / "Silent" mode), and your previous mode back on wake. Uses the manufacturer's firmware interface (WMI), the same one their app uses: no extra drivers, no low-level hardware access. The firmware's Quiet mode still speeds the fans up when it gets hot. | **Lenovo** Legion / LOQ (GameZone). Detected but **not supported yet**: ASUS, MSI, HP OMEN/Victus, Dell/Alienware, Acer, Razer. |
| **Fan Control** | Switches [Fan Control](https://getfancontrol.com) (free, by Rem0o) to your Quiet config and back to your Normal config (`FanControl.exe -c Quiet.json`). | Desktops and any PC whose fans Fan Control can see. |
| **Windows power mode** | Sets Settings > Power > *Power mode* to **Best power efficiency**, and back on wake. No admin needed. | Any Windows 10/11 PC (some PCs ignore it while a custom plan is active; the log says so). |
| **Off** | Fans are left alone. | |

Click **Detect built-in fan modes** to see what your PC has. The line *On this PC, Rest Mode will use* shows the result for your settings.

### Fan Control setup

Fan Control is optional; RestMode works without it.

1. Install Fan Control **outside OneDrive**, ideally in `C:\Program Files\FanControl`.
   RestMode's admin helper starts FanControl.exe as administrator, and only
   administrators can change files in Program Files. A folder on the Desktop,
   in Downloads or in `C:\Tools` can be changed by any program, and OneDrive can
   make the files online-only or create conflict copies of the configs.
   RestMode shows a warning in Settings if Fan Control is somewhere else.
2. Check the .NET runtime: Fan Control builds need a matching .NET Desktop
   Runtime. RestMode reads `FanControl.runtimeconfig.json` and tells you if it is missing.
3. In Fan Control, save two configs:
   - **Normal**: your everyday settings.
   - **Quiet**: gentle curves, but safe: **never 0 % or "fan stop"**, a minimum of about 20-30 %, and a curve that reaches **100 % by about 70-75 °C** on both CPU and GPU temperature. Fan Control is what enforces this, so double-check the curves.
4. RestMode Settings > **Fans**: pick the FanControl.exe (or its folder) with *Browse* / *Folder* / *Detect*, choose the Quiet and Normal configs from the lists, and click **Test** (Quiet for 10 s, then back).
5. Leave Fan Control running (e.g. its own *Start with Windows*). RestMode **never starts Fan Control by itself**: if it isn't running, the fan step is skipped so your fans keep doing what they did before.

If Fan Control can't see your fans (common on laptops, where the laptop controls them itself), use the built-in modes or the Windows power mode instead.

## Different kinds of PCs

RestMode detects the PC every time it starts and every time it enters Rest
Mode, and adapts. Nothing is set up for one particular model. Settings >
**Power** shows what it found.

| | What RestMode detects | What it changes |
|---|---|---|
| **Desktop vs laptop vs handheld/tablet** | Battery present (a UPS doesn't count), lid present | Desktops: plugged-in (AC) power values only, no lid or battery options. Laptops: AC and battery values, lid setting respected, warning when entering Rest Mode on battery, ends Rest Mode at your battery limit. Handhelds/tablets (battery, no lid): like laptops without the lid part. |
| **Intel vs AMD vs ARM** | CPU vendor | The same Windows processor settings work for all (boost off = Turbo Boost / Precision Boost off). Intel hybrid "E-core" settings are only written on Intel. Any setting a CPU doesn't offer is skipped, and if the CPU speed can't be limited at all, RestMode says so and carries on. |
| **Modern Standby (S0ix) vs classic S3 sleep** | Same as `powercfg /a` | **S3**: the screen is switched off instantly. **Modern Standby**: Windows treats the instant "monitor off" command like pressing the power button and goes to standby, which freezes apps and (usually) disconnects the network. So on these PCs the temporary plan turns the screen off after ~15 s without input instead, and the PC stays fully on. After every session RestMode checks that apps were never frozen and logs Windows' own standby events. |
| **Wired vs Wi-Fi** | Which adapter is connected | Only the adapter in use is changed, and only if it has the power-saving option. |
| **Several monitors** | Number of monitors | All of them are turned off and woken together (Windows switches every display). |
| **Admin vs standard account** | Account type | Standard accounts: admin steps are skipped (no password prompts at night); everything else works. |

## Admin rights

RestMode runs as a normal user. Only these things need admin rights:

- the network adapter *"allow the computer to turn off this device"* checkbox,
- switching a laptop's built-in fan mode, and
- switching the Fan Control profile.

(The Windows power mode and everything else need no admin rights.)

For those, RestMode starts a short-lived admin copy of itself (`RestMode.exe --helper sync`):

- **Without the helper task**: Windows shows a UAC prompt when you enter Rest Mode (before the screen turns off) and when you wake. If you say No, that step is skipped and everything else still works. If the PC goes to sleep on its own, the adapter/fan settings are put back the next time you use the PC (one prompt).
- **Standard (non-administrator) accounts** are never prompted: the admin steps are skipped.
- **With the helper task** (Settings > Admin > *Allow without UAC prompts*): one approval when installing; after that no prompts. It is an on-demand task called **RestMode Helper** in Task Scheduler. For safety it runs a copy of RestMode.exe kept in `C:\Program Files\RestMode Helper` (only administrators can change files there, so no other program can swap it for something else). **Reinstall the task after updating RestMode.exe** or moving Fan Control.
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

**Installed copy:** Windows Settings > **Apps** > RestMode > Uninstall.

**Portable copy:** run

```bat
RestMode.exe --uninstall
```

(Win+R, then drag `RestMode.exe` into the box and add ` --uninstall`.) It
restores every setting (power plan, power mode, fan mode, network adapter,
volume, apps), removes the startup entry, shortcuts and helper task, and
offers to delete its settings and logs. Then delete `RestMode.exe` (the
installed copy deletes itself).

**Only put settings back** (e.g. after a crash, without uninstalling):

```bat
RestMode.exe --restore
```

**By hand**, if RestMode.exe is gone. Open *Command Prompt* and:

1. See your plans: `powercfg /list`
2. Switch to yours (or to Balanced): `powercfg /setactive SCHEME_BALANCED`
3. Delete any plan named *RestMode (temporary)*: `powercfg /delete <its GUID>`
4. Turn the sound back on (speaker icon in the taskbar), and set Settings > Power > *Power mode* back to what you use. On a gaming laptop, pick your usual fan mode with its key (e.g. Fn+Q) or app.
5. Device Manager > Network adapters > your adapter > **Power Management**: tick *Allow the computer to turn off this device to save power* again if you had it ticked before.
6. Remove the helper task (admin Command Prompt): `schtasks /delete /tn "RestMode Helper" /f` and delete the folder `C:\Program Files\RestMode Helper`
7. Remove autostart: Task Manager > **Startup apps** > RestMode > Disable (or delete the `RestMode` value in `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`).
8. Delete the folder `%LOCALAPPDATA%\RestMode` (and `%LOCALAPPDATA%\Programs\RestMode` if it was installed).

If an app is still frozen (paused), end it in Task Manager or restart the PC.

## Troubleshooting

- **The PC wakes straight back up.** Something is sending mouse movement (a vibrating desk, a sensitive mouse, a "keep awake" or remote-control app). Raise *Mouse movement to wake* in Settings.
- **The screen comes back on by itself.** RestMode turns it off again after a few seconds. If a specific app keeps doing it, check `powercfg /requests` for apps holding a DISPLAY request.
- **It went to sleep while a download was still running.** The download was slower than the threshold, or paused. Lower *Downloads are finished when network is below* or raise the minutes.
- **It never goes to sleep.** Some background traffic is above the threshold (cloud backup, streaming, other PCs). Raise the KB/s thresholds or set the disk threshold to 0. The log shows the measured speed every 5 minutes.
- **"Windows refused to sleep"** in the log: a driver or policy blocks sleep. Run `powercfg /requests` and `powercfg /a` in an admin Command Prompt.
- **The hotkey doesn't work.** Another app uses it; RestMode tells you so. Pick another in Settings.
- **ASPM / boost / cooling-policy lines say "skipped"** in the log: your PC or BIOS doesn't offer that setting. That's normal.
- **The PC went into standby and the download stopped** (log: `PROBLEM: Windows froze apps`). The log lists Windows' own reason. On Modern Standby PCs RestMode never sends "monitor off", but the power button, closing the lid, or a manufacturer tool can still start standby.
- **"An app is keeping the screen on"** (Modern Standby PCs): something holds a display request (video, a game, presentation mode). Rest Mode keeps running; `powercfg /requests` (admin) shows which app.
- **Fan Control isn't switched**: the log says why (not running, config not found, .NET runtime missing, standard account). See [Fan Control setup](#fan-control-setup).

## Build

You need Windows 10/11 and the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer (a newer SDK builds it too).

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

- **[docs/TESTED-ON.md](docs/TESTED-ON.md)**: what has actually been tested, on which PC, and what hasn't.
- **[docs/DESKTOP-TEST-CHECKLIST.md](docs/DESKTOP-TEST-CHECKLIST.md)**: a 30-minute checklist for a desktop PC.
- **[docs/TEST-CHECKLIST.md](docs/TEST-CHECKLIST.md)**: the full checklist: enter, wake with mouse, auto-sleep after a download, crash recovery, and more.

## Source code map

| Folder / file | What it does |
|---|---|
| `src/Program.cs` | Start-up, single instance, command line (`--enter`, `--restore`, `--install`, `--uninstall`) |
| `src/Core/DeviceProfile.cs`, `PowerCapabilities.cs` | Hardware detection: desktop/laptop/handheld, CPU vendor, S3 vs Modern Standby, network, monitors, account type |
| `src/Core/StandbyEvents.cs` | Reads Windows' standby events after each session |
| `src/Core/Fans/*` | Fan plug-ins: built-in manufacturer modes (`BuiltInFanModes.cs`), Windows power mode, and the priority resolver (`FanPlanner.cs`) |
| `src/Util/Installer.cs` | Optional per-user install / Apps-list entry |
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
| `src/Elevation/*` | Admin helper: network adapter power flag, built-in fan modes, Fan Control, the optional scheduled task |
| `src/UI/*` | Tray icon, hidden message window, settings window |
| `docs/RESEARCH.md` | How PS4 Rest Mode works vs. Windows power management, and the design |

## License

MIT License, see [LICENSE](LICENSE).
