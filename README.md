# RestMode

**PlayStation 4-style Rest Mode for Windows 10 and 11.** Version 1.0.

Press **Rest** and your PC *looks* off: the screen goes dark, the sound is
muted, the processor slows right down and the fans quieten. But it keeps
running, so Steam / Epic / Xbox downloads, game updates, torrents and file
transfers carry on. Move the mouse, press a key or a controller button (or
open the laptop lid) and you're back exactly where you left off, with all
your normal settings.

RestMode works on any Windows 10/11 PC - desktops first, then laptops, mini
PCs and handhelds - and adapts itself to the hardware it finds.

---

## Install

Download from the newest [release](../../releases) (or build it yourself, see [Build](#build)):

| File | What it is |
|---|---|
| **RestMode-Setup.exe** | Installs RestMode for your user (no admin needed): Start menu entries "RestMode" and "RestMode – Rest now", an optional desktop shortcut and "start with Windows". Uninstall from **Settings › Apps › RestMode** - that also puts every setting back. |
| **RestMode.exe** | The same app, **portable**: run it from any folder, nothing is installed. |
| **RestMode-Reset.cmd** | Puts every setting back by hand, even without RestMode.exe. |

The first start opens a short guide: it shows what RestMode found on your PC,
explains what it changes, asks what closing the lid should do (laptops),
finds Fan Control if you have it, and saves a backup of your current settings.

**SmartScreen / antivirus:** RestMode isn't code-signed, so Windows may say
"Windows protected your PC" - click *More info › Run anyway*. Antivirus tools
sometimes flag unsigned apps that change power settings; RestMode is open
source (this repository) and only changes the settings listed below.

## Using it

| To... | Do this |
|---|---|
| **Rest** | The big **Rest** button in the RestMode window, `Ctrl+Alt+Shift+R` (changeable), the tray icon › **Rest**, Start › **RestMode – Rest now**, or the desktop shortcut. Laptops: close the lid (if you chose "Rest" for the lid). |
| **Wake up** | Move the mouse, press any key, press a button on an Xbox / XInput controller (PlayStation pads via DS4Windows / Steam Input), or open the lid. |
| See what happened | Main window › **Statistics**, or tray › **View log** |
| Put everything back right now | Tray › **Restore everything now**, or **RestMode-Reset.cmd** |

The interface is minimal and dark: one large Rest button, a clean settings list. The windows work with the mouse, the arrow keys (Enter to press) and a game
controller (D-pad + A, B to go back). The tray icon is an outline moon while awake and
a filled moon while resting.

## What happens when you press Rest

1. Your power plan, volume, power mode, fan mode and other settings are saved to disk first.
2. A temporary **copy** of your power plan is switched on: processor limited to 25 % (you choose), turbo boost off, "energy saving" preference, passive cooling, hard disks spin down when idle, Wi-Fi at full speed. **Your own plan is never edited.**
3. The fans get quieter (see [Fans](#fans)) - never to 0 %.
4. The network adapter you're using stays fully powered.
5. The sound is muted; optionally chosen apps are slowed down or paused.
6. On the main monitor a glowing orange neon frame draws itself around the screen, "Resting…" fades in and out (about 3.5 s, other monitors black; it can be switched off or recoloured), then the screen turns off. The PC stays fully on.
7. RestMode watches downloads. When traffic stays low for 10 minutes it does what you chose: **Sleep**, **Hibernate**, **Shut down** or **Stay** in Rest Mode. You can also tell it to watch only certain apps (e.g. `steam`, `qbittorrent`).

When you wake it, everything is put back - power plan, power mode, fan mode,
network option, volume, apps - and a short "Welcome back" screen shows how long it rested and how much was downloaded (any key or click closes it).

## Laptops: closing the lid

Settings › **Rest & Lid** › *When I close the lid*:

- **Rest** - closing the lid enters Rest Mode, opening it wakes the PC.
- **Sleep** - the Windows default; RestMode doesn't touch the lid.
- **Do nothing** - closing the lid does nothing.

Windows can't be given a new lid action, so for *Rest* and *Do nothing*
RestMode sets the lid action of your plan to "Do nothing" **while RestMode is
running**, and reacts to the lid itself. Your original setting is saved first
and put back exactly when you choose *Sleep*, exit RestMode, sign out, or
uninstall it (and after a crash, on the next start).

With the lid closed there's less airflow, so the overheat limit is 5 °C
stricter, and the laptop goes to real sleep if it gets too hot. On battery
with the lid closed it sleeps at the battery limit. **Never put a running
laptop in a bag.**

**The power button** is not used for Rest: when its action is "Do nothing",
Windows doesn't tell programs that it was pressed, so there's no safe way to
turn it into a Rest button that can always be undone.

## Fans

RestMode never sets a fan to 0 %. On every PC the CPU limit and *passive
cooling* already let the fans slow down by themselves. On top of that, choose
in Settings › **Fans** (Auto tries them in this order):

| Method | What it does | Works on |
|---|---|---|
| **Built-in fan modes** | Switches the PC's own **Quiet** mode on (the Fn+Q / "Silent" mode) and your previous mode back on wake. Uses the manufacturer's official firmware interface (WMI) - no extra drivers, no direct hardware access. The firmware's Quiet mode still speeds the fans up when it gets hot. | **Lenovo** Legion / LOQ. Detected but not supported yet: ASUS, MSI, HP OMEN / Victus, Dell / Alienware, Acer, Razer. |
| **Fan Control** | Switches [Fan Control](https://getfancontrol.com) (free, by Rem0o) to your *Quiet* config and back to your *Normal* config. | Desktops, and any PC whose fans Fan Control can see. |
| **Windows power mode** | Sets *Power mode* to **Best power efficiency**, and back on wake. | Any Windows 10/11 PC. |

### Fan Control setup (optional)

1. Put Fan Control **outside OneDrive** - ideally `C:\Program Files\FanControl`. RestMode's admin helper starts FanControl.exe as administrator, and only administrators can change files in Program Files; OneDrive can also make the files online-only or create conflict copies of the configs. RestMode warns you in Settings if Fan Control is somewhere else.
2. Fan Control needs a matching .NET Desktop Runtime; RestMode checks it and tells you if it's missing.
3. In Fan Control save two configs: **Normal** (your usual) and **Quiet**: never 0 % or "fan stop", a minimum of 20-30 %, and a curve reaching **100 % by about 70-75 °C** on CPU and GPU.
4. Settings › Fans: *Browse…* / *Folder…* / *Detect*, pick the Quiet and Normal configs, and press **Test** (Quiet for 10 s, then back).
5. Keep Fan Control running. RestMode never starts it by itself.

## Safety features

- **Overheat protection** - every 30 s RestMode reads the CPU/GPU temperature (Windows' thermal sensor and NVIDIA's driver without admin rights; the manufacturer's sensors with the helper). At the limit - desktop 80 °C, gaming laptop 75 °C, thin laptop / handheld 70 °C, adjustable, 5 °C lower with the lid closed - it first puts the fans back to normal, and if it stays hot it puts the PC to sleep. If no sensor works, quiet fan profiles aren't used at all (and it says so).
- **Battery** - a warning when Rest Mode starts on battery or the charger is unplugged; stricter CPU limit on battery; at 20 % (adjustable) the end action runs (sleep if the lid is closed).
- **Watchdog** - every 30 s: the PC is kept awake, the Rest Mode plan is active, the network is up (Wi-Fi is reconnected if it dropped). Anything wrong is repaired and logged.
- **Modern Standby PCs** - many new laptops treat "turn the monitor off" as "go to standby", which would cut the network. RestMode detects them and turns the screen off with a short idle timer instead, and checks after every session that nothing was frozen.
- **Always restores** - every original value is saved before anything changes and put back on wake, after a crash (next start), at sign-out / shutdown and on uninstall.
- **Self-check** - after Windows, BIOS or graphics-driver updates RestMode tests each feature again; anything that no longer works is switched off with a message, the rest keeps working.
- **Admin only when needed** - RestMode never runs as administrator all the time. The few admin steps (network adapter option, built-in fan modes, Fan Control, manufacturer sensors) ask with a time limit, or run without prompts once you install the small helper task (Settings › Safety). Standard accounts are never asked.
- **Private logs** - no user name, PC name or Wi-Fi name; lines older than 30 days are deleted.

## Different kinds of PCs

RestMode detects the PC at every start and every Rest, and adapts. Settings › **Power** shows what it found.

| Detected | What changes |
|---|---|
| Desktop / laptop / handheld (battery? lid?) | Desktops: plugged-in values only, no lid or battery options (a UPS doesn't count as a battery). Laptops: plugged-in and battery values, lid options, battery warnings. Handhelds / tablets: like laptops without the lid. |
| Intel / AMD / ARM | The same Windows processor settings work for all; Intel hybrid "E-core" settings only on Intel; anything a CPU doesn't offer is skipped. If the CPU can't be limited at all, RestMode says so and carries on. |
| Modern Standby vs classic S3 sleep | See *Modern Standby PCs* above. |
| Wired vs Wi-Fi | Only the adapter in use is changed, and only if it has the power-saving option. |
| Several monitors | All turn off and wake together; the animation covers every screen. |
| Admin vs standard account | Standard accounts: admin steps are skipped, everything else works. |
| Manufacturer | Lenovo (built-in fan modes), ASUS / MSI / HP / Dell / Acer / Razer (detected). |

## Customising

Settings has pages for: hotkey, start with Windows, wake options · lid action,
Rest animation and its texts · CPU limit, disk spin-down, battery strictness ·
"downloads finished" threshold, minutes and end action, watched apps · fan
method, built-in modes, Fan Control · overheat limit, battery limit, network,
helper task · **schedule** ("Rest at" / "Wake at" every day) and apps to slow
down · **look**: animation speed and sounds · export / import settings, reset to defaults,
backups · about. **Statistics** shows time in Rest Mode, data downloaded and
average temperatures.

## What a PC can't copy from a PS4

A PS4 has a separate low-power chip that keeps downloading while the main
processor sleeps. **PCs don't have one.** If a PC really sleeps, nothing
downloads. So RestMode keeps the PC *on* and makes it look and act as "off" as possible:

| | PS4 Rest Mode | PC with RestMode |
|---|---|---|
| Screen off, sound off | ✅ | ✅ |
| Downloads and updates continue | ✅ | ✅ (all apps, not just the store) |
| Instant wake to where you left off | ✅ | ✅ |
| Sleep automatically when downloads finish | ❌ | ✅ |
| **Power use about 5-10 W** | ✅ | ❌ A limited desktop still uses roughly 25-80 W (laptops 3-15 W) - still much less than normal use. |
| **Near silent** | ✅ | ⚠️ Quieter; how quiet depends on your fans (built-in modes / Fan Control help). |
| Game frozen in memory | ✅ | ⚠️ A running game keeps running slowly, unless you add a *single-player* game to the apps list with **Pause**. |
| Turn on from the network / Remote Play | ✅ | ❌ Not part of RestMode. |

## Troubleshooting

- **The PC wakes straight back up.** Something sends mouse movement (a sensitive mouse, a vibrating desk, a remote-control app). Raise *Mouse movement needed to wake*.
- **The screen stays on.** An app holds a "display" request (video, a game, presentation mode). RestMode tells you; `powercfg /requests` (admin) shows which app.
- **It went to sleep while downloading.** The download was slower than the threshold, or paused. Lower the KB/s threshold or raise the minutes, or use *Watch chosen apps*.
- **It never ends.** Background traffic is above the threshold (cloud backup, streaming). Raise the threshold or use *Watch chosen apps*.
- **"PROBLEM: Windows froze apps"** in the log: Windows went to standby anyway (power button, lid with "Sleep", a vendor tool). The log lists Windows' own reason.
- **Fan Control isn't switched** - the log says why: not running, config not found, .NET runtime missing, or a standard account.
- **The hotkey doesn't work** - another app uses it; RestMode tells you. Pick another one.
- **A feature was switched off by the self-check** - Settings › About › *Run the self-check again* after fixing the cause (e.g. a driver update).

## Undo everything

RestMode puts everything back by itself. If something still looks wrong:

1. **Tray › Restore everything now**, or run **RestMode-Reset.cmd** (in `%LOCALAPPDATA%\RestMode`, next to the installed exe, and in the release).
2. **Uninstall**: Settings › Apps › RestMode › Uninstall (installed), or `RestMode.exe --uninstall` (portable). It restores every setting and removes the startup entry, shortcuts, Apps-list entry, helper task and its data folder.
3. **By hand**, if RestMode.exe is gone (Command Prompt):
   1. `powercfg /list` - switch back to your plan: `powercfg /setactive SCHEME_BALANCED` (or your plan's GUID), then delete any *RestMode (temporary)* plan: `powercfg /delete <its GUID>`.
   2. Lid: `powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1`, `powercfg /setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1`, `powercfg /setactive SCHEME_CURRENT` (1 = Sleep).
   3. Settings › System › Power › **Power mode**; the sound (taskbar speaker); a gaming laptop's fan mode (e.g. Fn+Q).
   4. Device Manager › your network adapter › Power Management › *Allow the computer to turn off this device* (if you had it ticked).
   5. Admin Command Prompt: `schtasks /delete /tn "RestMode Helper" /f` and delete `C:\Program Files\RestMode Helper`.
   6. Task Manager › Startup apps › RestMode › Disable; delete `%LOCALAPPDATA%\RestMode` and `%LOCALAPPDATA%\Programs\RestMode`.

## Files

Everything lives in `%LOCALAPPDATA%\RestMode`: `settings.json`, `restmode.log`, `helper.log`, `stats.json`, `Backups\`, `RestMode-Reset.cmd`, and while something is changed: `state.json`, `helper-state.json`, `lid-backup.json`.

## Build

You need Windows 10/11 and the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer. From the repository folder run `build.cmd`. The result is **`dist\RestMode.exe`** (one self-contained ~65 MB file). Copy it as `RestMode-Setup.exe` for the installer, and `RestMode.exe --write-reset-script <folder>` writes the reset script. GitHub Actions does all of this on every push, and creates a release for tags like `v1.0.0` (`.github/workflows/restmode.yml`).

## Testing

- [docs/TESTED-ON.md](docs/TESTED-ON.md) - what was tested, on which PC, and what wasn't.
- [docs/FINAL-REPORT.md](docs/FINAL-REPORT.md) - the v1.0 test run.
- [docs/DESKTOP-TEST-CHECKLIST.md](docs/DESKTOP-TEST-CHECKLIST.md) - a 40-minute checklist for a desktop PC.
- [docs/TEST-CHECKLIST.md](docs/TEST-CHECKLIST.md) - the full manual checklist.
- `tests/` - the automated test run used on the test laptop.

## Source code map

| Folder / file | What it does |
|---|---|
| `src/Program.cs` | Start-up, single instance, command line |
| `src/Core/RestModeController.cs` | The main logic: enter, rest, wake, lid, watchdog, overheat, battery, schedule, end actions |
| `src/Core/DeviceProfile.cs`, `PowerCapabilities.cs` | Hardware detection |
| `src/Core/PowerPlanManager.cs`, `PowerGuids.cs` | The temporary low-power plan |
| `src/Core/LidManager.cs` | Lid action while RestMode runs (saved and restored exactly) |
| `src/Core/Safety/*` | Sensors and overheat protection |
| `src/Core/Fans/*` | Built-in fan modes (plug-ins), Windows power mode, the fan-method resolver |
| `src/Core/Restorer.cs`, `RestState.cs`, `AppSettings.cs` | What is saved to disk and how it's put back |
| `src/Core/ActivityMonitor.cs`, `AppWatcher.cs`, `InputWatcher.cs` | Downloads finished? / user back? |
| `src/Core/SelfCheck.cs`, `BackupManager.cs`, `StatsStore.cs`, `NetworkWatch.cs`, `StandbyEvents.cs` | Self-check, backups, statistics, network watchdog, standby check |
| `src/Elevation/*` | The admin helper: network adapter option, fan modes, Fan Control, the optional scheduled task |
| `src/UI/Wpf/*` | The interface: `Theme.xaml` (all colors / fonts / styles), `Icons.cs` (flat line icons), windows, Rest overlay, sounds |
| `src/UI/TrayApp.cs`, `HiddenWindow.cs` | Tray icon, hotkey, Windows notifications |

## License and credits

MIT License, see [LICENSE](LICENSE). The interface uses Windows' own Segoe UI font; the icon and the sounds are original and made for RestMode.
