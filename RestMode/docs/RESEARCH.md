# Research & design: PS4 Rest Mode on a Windows PC

This is the "why" behind RestMode's design: what the PS4 actually does, what
Windows offers, which parts can be copied, and which can't.

## 1. What PS4 Rest Mode really is

| PS4 behaviour | How the PS4 does it |
|---|---|
| Looks off: no picture, quiet, a small orange light | The main APU (CPU + GPU) is suspended to RAM. Video output is off. |
| Very low power (~5-10 W, depends on model and options) | Only RAM refresh, the network controller and a small low-power chip stay awake. |
| Downloads, game updates and patches keep going | Sony gave the PS4 a separate low-power processor that runs networking and downloads while the main APU sleeps. |
| USB ports stay powered (to charge controllers) | Option: *Supply Power to USB Ports* (Always / 3 hours / Off). |
| Network stays on | Options: *Stay Connected to the Internet* and *Enable Turning On PS4 from Network*. |
| Instant wake to where you left off (game still open) | The suspended game is kept in RAM; pressing PS resumes the APU in a few seconds. |

So a PS4 is really doing two things at once: the "big" computer is asleep
(S3-like), and a "small" computer stays on to download.

## 2. What Windows offers

| Mechanism | What it does | Useful for Rest Mode? |
|---|---|---|
| **S0 (working)** | PC fully on. | Yes: the only state in which normal desktop apps (Steam, Epic, browsers, torrent clients) keep downloading. |
| **S3 sleep** | CPU off, RAM refreshed, very low power. | Only as the *end action*. Nothing downloads in S3: a PC has no "second chip" to do it. |
| **S4 hibernate / S5 shutdown** | RAM saved to disk / off. | End action only. |
| **Modern Standby (S0ix, "connected standby")** | Screen off, SoC in deep idle, network can stay connected. Looks a lot like Rest Mode on paper. | **No, can't be relied on.** In Modern Standby the *Desktop Activity Moderator* suspends or throttles normal desktop apps, so Steam, Epic, game launchers and browsers stop downloading. Only Store/UWP-style background tasks and the OS keep running. Many desktops don't support it at all. |
| **Away Mode** (`ES_AWAYMODE_REQUIRED`) | Old Media Center feature: "sleep" turns screen and sound off but keeps the PC running. | Close in spirit, but it changes what Sleep means everywhere and is off by default (needs a registry policy). RestMode does the same thing more directly and reversibly. |
| **`SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED)`** | Stops idle sleep while the app asks for it, but lets the display turn off (no `ES_DISPLAY_REQUIRED`). | **Yes**: keeps downloads alive without keeping the screen on. Visible in `powercfg /requests`. |
| **`SC_MONITORPOWER` (2 = off)** | Turns monitors off right away. | **Yes**: "looks off" instantly. |
| **Power plans** (`powercfg -duplicatescheme`, `PowerWriteACValueIndex`/`DCValueIndex`, `PowerSetActiveScheme`) | Every power setting, separately for AC (plugged in) and DC (battery). | **Yes**: a temporary copy of the user's plan carries all the low-power settings, so the user's own plan is never edited. |
| **Processor power management** | *Maximum/minimum processor state* (`PROCTHROTTLEMAX/MIN`), *boost mode* (`PERFBOOSTMODE`, 0 = disabled), *energy performance preference* (`PERFEPP`), *system cooling policy* (`SYSCOOLPOL`, 0 = passive). | **Yes**: the closest a PC gets to a "low-power APU". Passive cooling slows the CPU before speeding up fans. |
| **PCIe ASPM** (`SUB_PCIEXPRESS` / `ASPM`, 2 = maximum savings) | Puts PCIe links (GPU, NVMe, Wi-Fi) into L1 when idle. | **Yes**, where the BIOS/chipset allows it (often disabled in BIOS on desktops). |
| **Disk idle** (`DISKIDLE`) | Spins down hard drives when idle. | **Yes**: a transfer keeps a disk busy, so it never spins down mid-download. SSDs ignore it. |
| **Network adapter power management** (Device Manager > Power Management > *Allow the computer to turn off this device to save power* = WMI `MSPower_DeviceEnable`) | Lets Windows power down the NIC. | **Yes**: unticked while resting so the network stays fully on; restored afterwards. Admin only. Takes effect without resetting the adapter. |
| **Wireless adapter power saving** (power plan setting) | Wi-Fi radio power-save level. | **Yes**: set to *Maximum Performance* in the temporary plan. |
| **Process priority / EcoQoS ("Efficiency mode")** | Lower priority and power-throttle chosen apps. | **Yes** (optional): the user picks which apps; downloaders and browsers are never touched. |

## 3. What can and can't be copied

| PS4 Rest Mode feature | On a PC with RestMode | |
|---|---|---|
| Screen off, no video | Monitors turned off instantly; turned off again if something wakes them without you. | ✅ Copied |
| Sound off | Default playback device muted, restored on wake. | ✅ Copied |
| Downloads keep running | PC stays in S0 (fully on), so *every* app keeps downloading. | ✅ Copied (better than Modern Standby) |
| Wake instantly to where you left off | Nothing was suspended, so waking is instant: screen on, full-speed plan back. | ✅ Copied |
| Network kept awake | Adapters' power saving off, Wi-Fi at max performance. | ✅ Copied |
| USB ports powered | PC is on, so USB stays powered and controllers charge. | ✅ Copied |
| Go fully to sleep when downloads finish | Watches traffic; Sleep / Hibernate / Shutdown when idle. | ✅ (PS4 doesn't even do this) |
| **~5-10 W power draw** | The whole PC stays in S0. Even capped, a desktop idles at roughly 25-80 W with the screen off (a laptop 3-15 W). | ❌ **Can't copy.** A PC has no secondary "download chip"; the main CPU, chipset and RAM must stay on. |
| **Near-silent** | CPU capped at ~25 %, no boost, passive cooling → CPU fan slows down. Case/GPU fans follow BIOS/driver curves; only Fan Control can lower those. | ⚠️ Partly |
| **Game suspended in RAM** | A running game keeps running (slowly). You can add a single-player game to the app list with *Pause* to freeze it. Don't pause online/anti-cheat games. | ⚠️ Partly |
| **CPU/APU in a low-power *sleep* state** | Windows can only cap and idle the CPU while on; deep C-states are used automatically when idle. | ⚠️ Partly |
| Turn on from network (PS4 app / Remote Play) | Wake-on-LAN exists on PCs but is a separate BIOS/NIC feature. | ❌ Out of scope |

## 4. Resulting design

1. **Stay in S0, look off.** `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` + `SC_MONITORPOWER = 2` + mute.
2. **Make S0 as cheap as possible** with a temporary *duplicate* of the user's
   plan (AC and DC values): max CPU 25 %, min 5 %, boost off, EPP = max
   saving, passive cooling, ASPM max, disk idle 3 min, display idle 1 min,
   idle sleep off (RestMode decides), Wi-Fi max performance, optional
   "lid close = do nothing".
3. **Keep the network fully powered** (adapter power-management flag; admin).
4. **Optionally calm the rest of the PC**: low-priority/EcoQoS or pause for
   user-listed apps; Fan Control "Quiet" profile if installed.
5. **Detect "downloads done"**: network (down + up) and disk throughput,
   averaged over 30 s; when both stay below thresholds for N minutes → Sleep /
   Hibernate / Shutdown / Stay. Everything is restored *before* sleeping, so
   the PC wakes up normal.
6. **Wake like a console**: keyboard, mouse (with a small jitter threshold),
   or an XInput controller button.
7. **Fail safe**: every step is independent and logged; state is saved to
   disk after each step; crash, kill, log-off, sleep and resume all lead
   back to the user's original settings (immediately or on next launch).
8. **Least privilege**: the app runs as a normal user. Only the adapter flag
   and Fan Control need admin; they run in a short-lived elevated copy
   (one UAC prompt) or via an optional scheduled task installed once.

## 5. Power setting GUIDs used

| Setting | Subgroup | GUID | Value in Rest Mode |
|---|---|---|---|
| Maximum processor state | `SUB_PROCESSOR` 54533251-82be-4824-96c1-47b60b740d00 | `PROCTHROTTLEMAX` bc5038f7-23e0-4960-96da-33abaf5935ec (+ `...5935ed` for E-cores) | CPU limit % |
| Minimum processor state | SUB_PROCESSOR | `PROCTHROTTLEMIN` 893dee8e-2bef-41e0-89c6-b55d0929964c (+ `...964d`) | 5 % |
| Processor performance boost mode | SUB_PROCESSOR | `PERFBOOSTMODE` be337238-0d82-4146-a960-4f3749d470c7 | 0 = Disabled |
| Energy performance preference | SUB_PROCESSOR | `PERFEPP` 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 (+ `...6864`) | 100 |
| System cooling policy | SUB_PROCESSOR | `SYSCOOLPOL` 94d3a615-a899-4ac5-ae2b-e4d8f634367f | 0 = Passive |
| Link State Power Management | `SUB_PCIEXPRESS` 501a4d13-42af-4429-9fd1-a8218c268e20 | `ASPM` ee12f906-d277-404b-b6da-e5fa1a576df5 | 2 = Maximum power savings |
| Turn off hard disk after | `SUB_DISK` 0012ee47-9041-4b5d-9b77-535fba8b1442 | `DISKIDLE` 6738e2c4-e8a5-4a42-b16a-e040e769756e | 180 s |
| Turn off display after | `SUB_VIDEO` 7516b95f-f776-4464-8c53-06167f40cc99 | `VIDEOIDLE` 3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e | 60 s |
| Sleep / hibernate after | `SUB_SLEEP` 238c9fa8-0aad-41ed-83f4-97be242c8f20 | `STANDBYIDLE` 29f6c1db-... / `HIBERNATEIDLE` 9d7815a6-... | 0 = never |
| Wireless power saving | 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 | 12bbebe6-58d6-4636-95bb-3217ef867c1a | 0 = Max performance |
| Lid close action (optional) | `SUB_BUTTONS` 4f971e89-eebd-4455-a8de-9e59040e7347 | `LIDACTION` 5ca83367-6e45-459f-a27b-476b1d01c936 | 0 = Do nothing |

You can inspect the temporary plan while resting with
`powercfg /query SCHEME_CURRENT SUB_PROCESSOR` (from another PC over remote
desktop, or by reading the log afterwards).
