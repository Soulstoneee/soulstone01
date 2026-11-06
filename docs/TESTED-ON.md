# What has been tested, and on what

RestMode is meant for any Windows 10/11 PC (desktops first, then laptops,
then handhelds/tablets). So far it has been tested on **one** PC. This page
says exactly what was checked there and what still needs a real test.

## Test PC

| | |
|---|---|
| Model | Lenovo LOQ 15ARP9 (laptop) |
| CPU / GPU | AMD Ryzen 5 7235HS / NVIDIA RTX 3050 6GB Laptop |
| Windows | Windows 11 Home (build 26200) |
| Sleep | **Modern Standby** (S0 Low Power Idle), network **disconnected** in standby by policy. No S3. Hibernate on. |
| Network | Wi-Fi (MediaTek MT7921). Its Wi-Fi card has no "allow the computer to turn off this device" option. |
| Screens | 1 (internal) |
| Account | Administrator |
| Power | On **battery** for all live tests |
| Date | 2026-11-06 |

## Tested on this PC ✅

| What | Result |
|---|---|
| Build with `build.cmd` (.NET 11 preview SDK building the .NET 8 project) | ✅ 63 MB single-file exe, no warnings |
| **Live test 1** (original code) | ❌ Found a serious bug: on Modern Standby PCs the "monitor off" command made Windows **enter standby** (Kernel-Power 506, reason `SC_MONITORPOWER`), Wi-Fi disconnected, all apps froze until a key was pressed. |
| **Fix**: on Modern Standby PCs the screen is turned off by a 15-second idle timer instead | ✅ |
| **Live test 3** (fix, 2½ min, battery) | ✅ Screen off within ~1 s of idle, ping 89/90 OK with no gaps, no Wi-Fi disconnect, test script and apps kept running, automatic wake worked, plan restored. |
| **Live test 4** (fix, 10 min, battery, with temperature/fan logging) | ✅ Ping 310/310 OK, no gaps, no "connectivity disconnected" and no battery drain-budget events, RestMode's own freeze check: none. CPU 44-54 °C before → **38-45 °C** in Rest Mode, GPU 35-44 °C. Fans were at 0 RPM before, during and after (the firmware keeps them off when cool). |
| **Live test 5** (10 min, battery, CPU warm-up first, real download, Lenovo Quiet) | ✅ Download kept going the whole time: **645 MB at a steady 1.04 MB/s**, ping 309/309. CPU 72 °C after warm-up → 38-43 °C in Rest Mode. ❌ Found a bug: the admin prompt appeared *after* the 15 s screen-off plan was active, so it sat on a dark screen, and RestMode froze while waiting for it (wake ignored, plan stayed active). |
| **Fix**: admin step runs *before* the Rest Mode plan (screen stays on), the prompt wait never blocks the app and gives up after 60 s (entering) / 120 s (waking), and a late "Yes" can no longer apply anything | ✅ |
| **Crash recovery** (after killing the frozen RestMode) | ✅ Next start found the unfinished session and restored the Balanced plan and deleted the temporary one by itself. |
| **Live test 6** (fixed build, Lenovo Quiet, 2 admin prompts) | ✅ Prompt shown with the screen on → **Lenovo fan mode Balanced → Quiet** (confirmed by reading it back from the firmware) → screen off → woken with a key → plan restored → **Quiet → Balanced** restored and confirmed. Ping 93/93, download 177 MB at 1.04 MB/s. No other program switched the plan. |
| Restore after every test | ✅ Original plan (Balanced) active, temporary plan deleted, volume/mute as before, no leftover state files. |
| Device detection | ✅ `laptop, LENOVO LOQ 15ARP9, AMD CPU, Modern Standby (S0ix), hibernate on, 1 monitor(s), network: Wi-Fi, administrator account` |
| Built-in fan mode detection | ✅ Lenovo GameZone interface found (no admin needed to detect). |
| Network adapter step skipped when not needed | ✅ No admin work for the Wi-Fi card (it has no power option). |
| Fan Control path checks | ✅ Finds FanControl.exe in a folder or as a file, .NET 10 runtime check passes, warns about the OneDrive location, says it isn't running and has no configs. |
| Settings window (all 6 tabs) | ✅ Rendered and checked; layout issues fixed. |
| Network adapter pre-check | ✅ Wi-Fi has no power option → no admin prompt needed. |

## Written but NOT yet tested on a real PC ⚠️

| What | Why not yet |
|---|---|
| Fans actually slowing down in Rest Mode | The firmware reported **0 RPM for both fans in every test**, even at 72 °C on battery. Either this LOQ keeps its fans off up to a higher temperature on battery, or its fan-speed reading doesn't work. Needs a test plugged in and under heavier load, listening to the fans. |
| What happens if nobody answers the admin prompt | The 60 s / 120 s time limits are in the code but weren't triggered in a live test. |
| Windows power mode "Best power efficiency" switch + restore | New code; not yet run in a live session. |
| Fan Control switching (Quiet/Normal, Test button) | Fan Control isn't running on the test PC and has no configs yet. Likely can't drive this laptop's fans anyway. |
| Optional installer (`--install`) and uninstall from Settings > Apps | Shows dialogs; needs someone at the PC. |
| Plugged-in (AC) behaviour on this laptop | All live tests were on battery. |
| Lid closed while resting, battery limit end action | Not run. |
| Sleep / hibernate / shutdown end actions after downloads finish | Not run with the new code. |
| Crash recovery, log-off while resting | Not run with the new code (logic unchanged). |

## Never tested (no hardware) ❌

| What | Notes |
|---|---|
| **Desktops** (no battery, AC-only values, several fans) | Main target. See [DESKTOP-TEST-CHECKLIST.md](DESKTOP-TEST-CHECKLIST.md). |
| **Classic S3 sleep PCs** (instant "monitor off") | Most desktops. The original behaviour is kept there. |
| Wired Ethernet adapter power option | The test PC's Ethernet port was unplugged. |
| Several monitors | Only one screen here. |
| Intel CPUs (incl. hybrid E-cores), ARM | Only AMD here. |
| Handhelds / tablets (battery, no lid) | |
| Standard (non-admin) account | |
| UPS on a desktop (battery that isn't a laptop battery) | |
| Built-in fan modes on ASUS / MSI / HP / Dell / Alienware / Acer / Razer | Detected by manufacturer name only and marked "not supported yet": they never change anything. |
