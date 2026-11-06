# What has been tested, and on what

RestMode is meant for any Windows 10/11 PC (desktops first, then laptops,
then handhelds/tablets). v1.0 has been tested on **one** PC. This page says
what was checked there and what still needs a real test. The full v1.0 test
run is in [FINAL-REPORT.md](FINAL-REPORT.md).

## Test PC

| | |
|---|---|
| Model | Lenovo LOQ 15ARP9 (gaming laptop) |
| CPU / GPU | AMD Ryzen 5 7235HS / NVIDIA RTX 3050 6GB Laptop |
| Windows | Windows 11 Home (build 26200) |
| Sleep | **Modern Standby** (S0 Low Power Idle), network disconnected in standby by policy. No S3. Hibernate on. |
| Network | Wi-Fi (MediaTek MT7921), no "allow the computer to turn off this device" option |
| Screens | 1 (internal), tested at 100 / 125 / 150 % scaling |
| Account | Administrator |
| Power | Live Rest tests on **battery** and plugged in; v1.0 test pass plugged in |
| Date | 2026-11-06 |

## Tested on this PC ✅

| What | Result |
|---|---|
| Build (`build.cmd`, .NET 8 target) | ✅ ~66 MB single-file exe |
| Rest / wake, 5 cycles in a row, automatic wake | ✅ plan, Lenovo fan mode, volume, state restored every time |
| Modern Standby: PC stays awake with the screen off (10 min, battery) | ✅ no network drop, CPU 38-45 °C |
| Downloads keep going during Rest | ✅ 645 MB at 1.04 MB/s (10 min); 150 MB at 1 MB/s in the v1.0 pass |
| "Downloads finished" detection | ✅ |
| Lenovo Quiet ↔ Balanced (official GameZone WMI) | ✅ confirmed from the firmware |
| Windows power mode → Best power efficiency and back | ✅ |
| Lid close → Rest, lid open → wake, lid "Do nothing" while running and restored on exit | ✅ (lid events simulated) |
| Watchdog: stay-awake request and plan put back | ✅ |
| Overheat steps (fans normal, then sleep) | ✅ (temperature simulated) |
| Charger unplugged warning, battery limit, lid closed on battery → Sleep | ✅ (simulated) |
| Schedule rest at / wake at | ✅ |
| 60 s pause for automatic triggers after a wake | ✅ |
| Crash limit (3 in 24 h → no automatic start) | ✅ |
| Crash recovery on the next start | ✅ |
| RestMode-Reset.cmd (installed and portable) | ✅ (portable fixed in v1.0) |
| Installer (`--install --quiet`) and uninstall | ✅ everything created is removed again |
| Helper task (admin steps without prompts) | ✅ |
| Interface: all windows, neon Rest animation, Welcome back screen at 100 / 125 / 150 % | ✅ checked by eye |
| Animation frame rate, plugged in | ✅ 67 fps (Rest), 76 fps (Welcome back) |
| Device detection | ✅ `gaming laptop, LENOVO LOQ 15ARP9, AMD CPU, discrete GPU, Modern Standby (S0ix), hibernate on, 1 monitor(s), network: Wi-Fi, administrator account` |
| Fan Control detection (path, .NET 10 check, OneDrive warning) | ✅ |

## Not tested on this PC ⚠️

| What | Why |
|---|---|
| Animation frame rate on battery | Laptop stayed plugged in |
| Fan Control actually switching configs | Fan Control can't drive this laptop's fans without its own driver (not installed on purpose) |
| Fans audibly slower | Firmware reports 0 RPM when cool; Quiet mode confirmed from the firmware |
| Real lid hardware, real sleep / hibernate / shutdown end actions | Need a person / would end the test; logic tested in test mode |
| What happens if nobody answers an admin prompt | The helper task was installed, so no prompts appeared; time limits are in the code |

## Never tested (no hardware) ❌

| What | Notes |
|---|---|
| **Desktops** (no battery, AC-only values, several fans) | Main target. See [DESKTOP-TEST-CHECKLIST.md](DESKTOP-TEST-CHECKLIST.md). |
| **Classic S3 sleep PCs** (instant "monitor off") | Most desktops. |
| Wired Ethernet adapter power option | No cable plugged in. |
| Several monitors | Only one screen here. |
| Intel CPUs (incl. hybrid E-cores), ARM | Only AMD here. |
| Handhelds / tablets | |
| Standard (non-admin) account | |
| UPS on a desktop | |
| Built-in fan modes on ASUS / MSI / HP / Dell / Alienware / Acer / Razer | Detected by name only and marked "not supported yet": they never change anything. |
