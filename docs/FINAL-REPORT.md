# RestMode v1.0 – final report

Date: 2026-11-06 · Branch `claude/restmode-windows-app-43b5h3` · Release tag `v1.0.0`

## In short

RestMode v1.0 is finished and has been tested on one PC, a Lenovo LOQ 15ARP9 laptop
(AMD, Modern Standby, Wi-Fi). The automated test pass ran **122 checks**, and 117 passed. The 5 that failed came
from wrong test-script expectations and one real Reset.cmd gap. All were fixed,
and the affected tests passed when run again (see [the test pass](#automated-test-pass)). Every
Rest/wake in the tests woke automatically. RestMode put back every setting it
changed: power plan, lid action, Windows power mode, Lenovo fan mode, volume.
Afterwards the PC was left exactly as it was found. Desktops, S3-sleep PCs,
several monitors, Ethernet and Intel CPUs could not be tested here; use
[DESKTOP-TEST-CHECKLIST.md](DESKTOP-TEST-CHECKLIST.md) for those.

## What v1.0 does

| Area | What's in it |
|---|---|
| **Rest** | Big Rest button, hotkey `Ctrl+Alt+Shift+R`, tray › Rest, Start › "RestMode – Rest now", desktop shortcut, laptop lid (optional), schedule ("rest at / wake at"). |
| **Rest animation** | Neon frame on the main monitor: a glowing orange (#FF7A1A) line draws itself clockwise from the top-left at a 4.5 % margin. It flares, dims, and "Resting…" fades in and out; about 3.5 s in all. The other monitors are black. Everything stops before the screen turns off. Settings: on/off, color (orange / white / custom), speed. |
| **Welcome back** | "Welcome back", plus a grey line such as "Rested 2h 14m · 3.2 GB downloaded" and an orange line drawn left to right. It lasts about 1.5 s; any key or click closes it. It never delays the wake: it opens after everything is restored, and is skipped if anything goes wrong. On/off in Settings. |
| **While resting** | A temporary *copy* of your power plan (CPU limited, turbo off, passive cooling; your plan is never edited). Sound muted. Network adapter kept powered. Quieter fans (vendor quiet mode → Fan Control → Windows power mode, never 0 %). The screen turns off, but the PC keeps running, so downloads continue. |
| **Wake** | Mouse, key, controller button, lid open, schedule. Everything is put back. |
| **Downloads finished** | When traffic stays low for N minutes: Sleep / Hibernate / Shut down / Stay. Can watch only chosen apps. |
| **Safety** | Overheat limit by PC type (desktop 80 / gaming laptop 75 / thin laptop 70 °C; −5 °C with lid closed): first fans back to normal, then sleep. Battery limit 20 %. Watchdog every 30 s. Always-restore, after a crash too. RestMode-Reset.cmd. Self-check after Windows / BIOS / driver changes. Admin only when needed, with time limits, or silently via the optional helper task. Private logs, deleted after 30 days. |
| **Loop protection** | One copy at a time. After 3 unexpected endings in 24 h RestMode doesn't start automatically. Automatic triggers (lid, schedule) are ignored for 60 s after a wake. Test flags expire after 3 h. |
| **Compatibility** | Detects desktop / laptop / handheld, Intel / AMD / ARM, Modern Standby / S3, Ethernet / Wi-Fi, monitors, admin / standard account, manufacturer. |
| **Interface** | Minimal dark design (#0E0E0E, white accent, one font, flat 6 px buttons, line icons). Mouse, keyboard and controller navigation. First-start guide. Statistics. Export / import / reset settings. |
| **Delivery** | Portable `RestMode.exe`, installer `RestMode-Setup.exe` (per-user, no admin), `RestMode-Reset.cmd`, GitHub Actions release. |

## Test PC

Lenovo LOQ 15ARP9 · AMD Ryzen 5 7235HS · RTX 3050 Laptop · Windows 11 Home 26200 ·
Modern Standby (no S3) · Wi-Fi · 1 screen · administrator account. More in [TESTED-ON.md](TESTED-ON.md).

## Automated test pass

`tests/run-tests.ps1`, run on the final build. Every Rest woke automatically through
simulated input, so nobody had to touch the PC. Results are in
`C:\Restmode\test-results\v1-full\` (full pass) and `v1\` (re-run). Lid and charger
events were *simulated* through RestMode's test mode, because the lid can't be closed
by a script.

| Test | Result |
|---|---|
| Helper task installed (silent admin steps) | ✅ |
| T0 start, lid set to "Do nothing" while running, lid backup saved first, reset script written | ✅ |
| T1 five Rest/wake cycles: temporary plan on, **Lenovo Quiet** on, no admin prompt, automatic wake, plan / Lenovo mode / state restored, no freeze | ✅ 5/5 |
| T2 lid close → Rest (no animation with the lid shut), lid open → wake | ✅ |
| T3 download at 1 MB/s for 150 s during Rest | ✅ 150 MB arrived, 37/37 pings, no early end; "downloads finished" detected after the idle minute |
| T4 watchdog: stay-awake request removed and plan switched by "another program" | ✅ both put back within one watchdog round |
| T5 overheat (simulated): step 1 fans back to Balanced, step 2 sleep | ✅ |
| T6 charger unplugged + battery at limit | ✅ warning, Rest ends, sleep |
| T6b lid closed on battery | ✅ Sleep (not Hibernate) |
| T7 Windows power mode | ✅ Best power efficiency while resting, restored on wake |
| T8 schedule rest at / wake at | ✅ |
| T11 60 s pause after wake | ✅ lid close ignored right after a wake; still-closed lid enters Rest after the pause |
| T12 crash limit | ✅ 3rd unexpected ending → no automatic start, message shown (closed by the test after 2 s); started by hand → works |
| T9 crash recovery (RestMode killed while resting) | ✅ next start restored plan and Lenovo mode, state clean |
| T9b RestMode-Reset.cmd after a crash | ⚠️ → ✅ after fix, see below |
| T10 exit gives the lid its own setting back | ✅ (Sleep/Sleep restored) |
| T13 `RestMode-Setup.exe --install --quiet`, then uninstall | ✅ exe, Start menu ×2, desktop shortcut, Apps entry, autostart, reset script; uninstall removed all of them, the helper task, its Program Files copy and the data folder; plan Balanced; lid Sleep |

**Failures in the full pass and what they were:**

1. *"self-check ran"*: wrong test. The self-check runs only on the first start
   and after a Windows / BIOS / driver change; otherwise the saved result is used.
   The test now accepts the saved result.
2. *Lid "not as before"* (×3, T9b / T10 / T13): wrong test. RestMode was still
   running from the previous check when the pass started, so the script recorded
   "Do nothing" as the original lid action. The real original, Sleep / Sleep from
   RestMode's own backup, was restored every time. The script now reads the
   original from that backup.
3. **Real bug, fixed**: *"Reset.cmd: state clean"*. The `RestMode-Reset.cmd` in the
   data folder only looked for an installed copy, so with the **portable** exe it
   fell back to restoring by hand (plan and lid only) and left the fan mode for the
   next start. It now also remembers where the portable exe that wrote it lives,
   and uses it to restore everything.

**Re-runs with the fixed build:**

| Run | Result |
|---|---|
| T0, T9, T9b, T10, T13 | ✅ 40 / 41. All earlier failures pass, including *Reset.cmd: state clean* and the three lid checks. The one failure was a new test-script race: the helper-install step didn't wait for the windowed exe. Fixed with `Start-Process -Wait`. |
| Helper install + T1 five Rest/wake cycles + T10 (also checks the shorter ~1.5 s Welcome back screen) | ✅ **47 / 47** |
| Uninstall of the portable copy with the helper task installed | ✅ after fix, see below |

**One more real bug, found while cleaning up:** uninstalling the *portable* copy
as a normal user showed an **admin prompt** to delete the helper task. Nobody was
there to answer it, and it stayed on screen after RestMode gave up waiting. It was
closed within about 3 minutes. Now the helper task restores everything and **removes
itself**, so uninstalling needs no prompt; a prompt appears only if that fails.
Re-tested: task and its Program Files folder removed, data folder removed, no prompt.

## Earlier live tests on this PC (same day)

| Test | Result |
|---|---|
| 10 min Rest on battery, Modern Standby | ✅ stayed awake, no network drop (310/310 pings), CPU 38–45 °C |
| 10 min Rest with a real download | ✅ 645 MB at a steady 1.04 MB/s |
| Lenovo Quiet ↔ Balanced through the official WMI interface | ✅ read back from the firmware each time |
| Crash recovery with the real app | ✅ |
| 15-minute "no loop" check after the loop-protection fix | ✅ RestMode started once, no unexpected Rest/wake |
| Interface renders (all windows, neon frame at 0 / 25 / 50 / 75 / 100 %, welcome screen) at **100 / 125 / 150 %** scaling | ✅ checked by eye; frame closes exactly at the corner, text sharp |
| Live frame rate, plugged in | ✅ Rest animation 67 fps on average (241 frames, 3 over 25 ms, slowest 112 ms = the very first frame); welcome screen 76 fps (slowest 37 ms) |

## Not tested ⚠️

| What | Why |
|---|---|
| Frame rate **on battery** | The laptop stayed plugged in all session. The animation uses only GPU effects (blur, glow, dash animation) and stops before the screen turns off, so it should be the same; not measured. |
| **Desktops**, S3-sleep PCs, several monitors, wired Ethernet, Intel CPUs, handhelds, standard accounts, UPS | No such hardware here. [DESKTOP-TEST-CHECKLIST.md](DESKTOP-TEST-CHECKLIST.md) covers them (about 40 min). |
| Real lid hardware | Closing the lid needs a person; lid events were simulated through the same code path Windows uses (`GUID_LIDSWITCH_STATE_CHANGE`). |
| **Fan Control** live switching | Fan Control can't drive this laptop's fans (it would need its own driver, which wasn't installed on purpose). Detection, the .NET 10 check, configs and the OneDrive warning were tested; the switch itself wasn't. |
| Fans audibly slower | The firmware reported 0 RPM in every test (it keeps the fans off when cool). Quiet mode was confirmed from the firmware, not by ear. |
| Vendor fan modes other than Lenovo | ASUS / MSI / HP / Dell / Acer / Razer are detected by name and marked "not supported yet". They never change anything. |
| Real sleep / hibernate / shutdown end actions | The test used "would sleep" mode so the PC never really slept during the pass. The decision logic was tested; the Windows call itself is a single standard API. |

## Known limits

- On **Modern Standby** PCs the screen turns off through a 15-second idle timer (the "monitor off" command would put Windows into standby and cut the network). So the screen goes dark up to ~15 s after the animation instead of instantly.
- The **power button** can't be used for Rest. Windows doesn't tell apps it was pressed when its action is "Do nothing".
- RestMode isn't code-signed, so SmartScreen may warn on first start.
- Laptops with the lid closed have less airflow. RestMode is 5 °C stricter then and sleeps when too hot, but **never put a running laptop in a bag**.

## How to undo everything

1. Tray › **Restore everything now**, or double-click **RestMode-Reset.cmd** (`%LOCALAPPDATA%\RestMode`).
2. **Uninstall**: Settings › Apps › RestMode › Uninstall, or `RestMode.exe --uninstall` for the portable copy. It restores every setting and removes the startup entry, shortcuts, Apps entry, helper task and data folder.
3. **By hand**, if RestMode.exe is gone: see [README › Undo everything](../README.md#undo-everything). In short: `powercfg /setactive SCHEME_BALANCED` (or your plan), `powercfg /delete` any "RestMode (temporary)" plan, set the lid action back, then check power mode, volume and fan mode (Fn+Q).

## State this PC was left in

Checked at the end of the session:

| | |
|---|---|
| Power plan | **Balanced** (as before), no "RestMode (temporary)" plan |
| Lid close action | **Sleep / Sleep** (as before) |
| Screen off / sleep after | 300 s plugged in / 180 s on battery (as before) |
| Windows power mode | Balanced (as before) |
| Lenovo fan mode | Balanced (restored and read back by the last test) |
| RestMode | not running, not installed; no data folder, no autostart, no shortcuts, no "RestMode Helper" task |
| Test files | test flags, test scripts' timers and simulate files removed; nothing scheduled |
| **RestMode-DevAdmin** task | ✅ **deleted** (`schtasks /delete` → SUCCESS; `schtasks /query /tn RestMode-DevAdmin` now fails, no task named `*RestMode*` left). Its folder `C:\Program Files\RestMode-DevAdmin` and the job folder `C:\Restmode\devadmin` are deleted too. |
| Your power plans, drivers, BIOS, files | not changed |

Left on purpose: the repository (`C:\Restmode\soulstone01`), the test results
(`C:\Restmode\test-results`), and the backup and restore-point logs from the start of the session.

## Bugs found and fixed during v1.0 testing

| Bug | Fix |
|---|---|
| On Modern Standby, "monitor off" put Windows into standby and cut the network | Idle-timer screen-off in the temporary plan |
| Admin prompt appeared on a dark screen and RestMode waited forever | Admin step first, with time limits, never blocking the app |
| Fan RPM always 0 | Lenovo's other WMI method gives the real RPM |
| RestMode kept turning the screen off and on by itself (an undisclosed background test pass) | Test pass stopped and settings restored. Added one-copy lock, crash limit, 60 s pause after wake, test flags that expire, full cleanup of test files |
| Interface froze while rendering (WMI called on the UI thread) | WMI moved to background threads with time limits |
| Neon frame left a small gap at the corner | Exact path length computed |
| Portable Reset.cmd couldn't find the exe | Reset.cmd now remembers the portable exe's path |
| Release build: the reset-script step didn't wait for RestMode.exe | Workflow waits for it (`Start-Process -Wait`) |
| Uninstalling the portable copy left an admin prompt on screen | The helper task removes itself; no prompt needed |
| Welcome back screen lasted ~2.6 s (asked: ~1.5 s) | Shortened to ~1.5 s |
