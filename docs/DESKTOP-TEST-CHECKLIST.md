# Test on a desktop PC

RestMode was developed and tested on one laptop (see
[TESTED-ON.md](TESTED-ON.md)). Desktops behave differently in a few
important ways. Run this list once on a desktop and tick each box. It takes
about 30 minutes.

All results are also in the log: tray icon > **View log**
(`%LOCALAPPDATA%\RestMode\restmode.log`).

## Before you start

- [ ] Write down your active power plan: run `powercfg /getactivescheme` → ______________
- [ ] Run `powercfg /a` and note the first line under "available": **Standby (S3)** or **Standby (S0 Low Power Idle)** → ______________
- [ ] Settings > Power & battery (Windows 11) or Power & sleep (Windows 10): note the **Power mode** → ______________
- [ ] Optional safety net: create a restore point (Start > "Create a restore point" > Create).
- [ ] Start `RestMode.exe` (no install needed). In Settings > **Downloads**, set the minutes to **2**.

## 1. Detection (Settings > Power, and the first lines of the log)

- [ ] The Power tab says **desktop** (or "desktop (on a UPS)" if you have a UPS), the right CPU (Intel / AMD), the sleep type from `powercfg /a`, your monitor count, and your network (wired / Wi-Fi).
- [ ] The lid and battery options are **greyed out**.
- [ ] Log line `plan: writing plugged-in (AC) only - no battery values`.

## 2. Enter and wake (S3 desktops: the most common case)

- [ ] Press `Ctrl+Alt+Shift+R`. **All monitors** go dark within ~2 s (S3 PCs) or ~15 s (Modern Standby PCs).
- [ ] The tray icon turns orange (check after waking).
- [ ] Wake with the **mouse**: all monitors come back; the tray icon is blue.
- [ ] Repeat, waking with a **key**, and (if you have one) a **controller button**.
- [ ] `powercfg /getactivescheme` shows your plan again and `powercfg /list` has no "RestMode (temporary)".
- [ ] Log ends with `Checked: the PC stayed fully running for the whole session` and a `SESSN` line.

## 3. Multiple monitors (if you have 2+)

- [ ] On entry, **every** monitor turns off (not just the main one).
- [ ] On wake, every monitor comes back, windows are where they were.
- [ ] Monitors connected by DisplayPort sometimes "disconnect" when off: after waking, check that windows didn't all jump to one screen. Note it here: ______________

## 4. Network stays on (wired Ethernet)

- [ ] Start a big download (a Steam game/update, or a large file in a browser).
- [ ] Enter Rest Mode and wait **10 minutes**.
- [ ] Wake: the download kept going (progress moved on, no "network error").
- [ ] If "Keep the connected network adapter fully powered" is on: one UAC prompt appeared **before** the screen went off (or none, if the adapter has no such option - the log then says "nothing to change").
- [ ] Device Manager > your Ethernet adapter > Power Management: after waking, *Allow the computer to turn off this device* is back to what it was.

## 5. Downloads finished → sleep

- [ ] Settings > Downloads: 50 KB/s, 2 minutes, then **Sleep**.
- [ ] Start a download, enter Rest Mode. The PC does **not** sleep while it downloads.
- [ ] ~2-3 minutes after it finishes, the PC sleeps.
- [ ] Wake it (power button / keyboard): your plan, volume and power mode are back.

## 6. Fans (desktops have several fans and no laptop fan modes)

- [ ] Settings > Fans > **Detect built-in fan modes**: says no built-in modes (normal for desktops), or names your board's brand as "not supported yet".
- [ ] Without Fan Control: "On this PC, Rest Mode will use: Windows power mode". Enter Rest Mode, wake: Settings > Power mode is back to what you wrote down.
- [ ] With Fan Control installed **and running**:
  - [ ] In Fan Control, save two configs: **Normal** (your usual) and **Quiet**: every fan at least 20-30 %, never 0 % / stop, a curve that reaches 100 % by ~70-75 °C on CPU **and** GPU temperature.
  - [ ] Settings > Fans: Detect finds FanControl.exe, no runtime warning, both configs appear in the dropdowns.
  - [ ] **Test** button: fans audibly slow down for ~10 s, then return.
  - [ ] Enter Rest Mode (approve the prompt): fans slow down within a minute. Wake: they return to normal.
- [ ] With Fan Control installed but **closed**: entering Rest Mode does not start it (log: "Fan Control is not running - fan profile not changed").

## 7. Admin vs standard account (optional, needs a second account)

- [ ] Sign in with a **standard** (non-admin) account and run RestMode: the Admin tab explains admin steps are skipped; entering Rest Mode shows **no** password prompt and everything else works.

## 8. Crash recovery

- [ ] In Command Prompt: `timeout /t 60 && taskkill /f /im RestMode.exe`, then enter Rest Mode within 60 s.
- [ ] After it's killed, wake the screen and start RestMode again: a notification says settings were restored; your plan and power mode are back.

## 9. Install / uninstall (optional)

- [ ] Settings > General > **Install for this user**: RestMode appears in the Start menu and in Settings > Apps.
- [ ] Uninstall from Settings > Apps: everything is restored and removed, including the installed exe.

## Send back

The log file (`%LOCALAPPDATA%\RestMode\restmode.log`) and anything you noted
above. Lines with `[WARN ]` or `PROBLEM` are the most useful.
