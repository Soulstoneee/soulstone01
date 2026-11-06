# Test RestMode v1.0 on a desktop PC

RestMode was built and tested on one laptop (see [TESTED-ON.md](TESTED-ON.md)
and [FINAL-REPORT.md](FINAL-REPORT.md)). Desktops are the main target and
behave differently in a few ways, so run this list once on a desktop. It
takes about 40 minutes. Tick each box.

Everything RestMode does is in its log: tray icon › **View log**
(`%LOCALAPPDATA%\RestMode\restmode.log`). Lines with `[WARN ]` or `PROBLEM` matter most.

## Before you start

- [ ] Write down your active power plan: `powercfg /getactivescheme` → ______________
- [ ] Run `powercfg /a`. Note whether **Standby (S3)** or **Standby (S0 Low Power Idle)** is available → ______________
- [ ] Settings › System › Power: note **Power mode** → ______________ and "turn off screen / sleep after" → ______________
- [ ] Optional safety net: Start › "Create a restore point" › Create.
- [ ] Run **RestMode-Setup.exe** (or the portable RestMode.exe). Go through the first-start guide.

## 1. Detection

- [ ] The guide (and Settings › Power) says **desktop** (or "desktop (on a UPS)"), the right CPU (Intel / AMD), the sleep type you noted, the right number of monitors, and your network (wired / Wi-Fi).
- [ ] The guide doesn't ask about a lid. Settings › Safety has no battery section.
- [ ] The log has `plan: writing plugged-in (AC) only - no battery values` after your first Rest.

## 2. Rest and wake (S3 desktops: the most common case)

- [ ] Click **Rest** in the main window. On the main monitor an orange light travels clockwise along the screen edges while "Entering Rest Mode" fades in, then all monitors go dark (about 6 s, plus up to 15 s on Modern Standby PCs). Other monitors are black during the animation, and the light should look smooth (no stutter).
- [ ] Move the mouse: the **Welcome back** screen shows how long you rested and how much was downloaded, then disappears by itself (or press a key to close it at once).
- [ ] Repeat with the **hotkey** (`Ctrl+Alt+Shift+R`), the **tray icon › Rest**, and Start › **RestMode – Rest now**. Wake with a **key**, and with a **controller button** if you have one.
- [ ] After each wake: `powercfg /getactivescheme` shows your plan, `powercfg /list` has no "RestMode (temporary)", the volume is as before.
- [ ] Rest again and move the mouse during the animation: Rest Mode is cancelled, the desktop comes back and `powercfg /getactivescheme` shows your plan. Settings › Rest & lid: turn the **Rest animation** and **Welcome back screen** off, Rest again: the screen goes dark straight away and there's no welcome screen on wake. Turn them back on, try **White**, **Windows accent color** and **Custom** with **Preview**.

## 3. Several monitors (if you have 2+)

- [ ] Every monitor turns off; every monitor comes back on wake.
- [ ] After waking, windows are still on the monitors they were on. (Some DisplayPort monitors "disconnect" when off - note if windows jumped: ______________)

## 4. Downloads keep going (wired Ethernet)

- [ ] Start a big download (Steam game / update or a large file), Rest for **10 minutes**, wake: the download kept going.
- [ ] If "Keep the network adapter powered" is on: one admin prompt appeared **before** the screen went dark - or none if your adapter has no such option (the log then says "nothing to change").
- [ ] Device Manager › your Ethernet adapter › Power Management: after waking, *Allow the computer to turn off this device* is back to what it was.

## 5. Downloads finished → sleep

- [ ] Settings › Downloads: below 50 KB/s, for **2** minutes, then **Sleep**.
- [ ] Start a download, Rest. The PC doesn't sleep while it downloads; 2-3 minutes after it ends it sleeps.
- [ ] Wake it: your plan, power mode and volume are back.
- [ ] Optional: Settings › Downloads › *Watch chosen apps*: `steam`. Rest Mode now ends when Steam stops downloading, even if other traffic continues.

## 6. Fans (desktops have several fans and no built-in laptop fan modes)

- [ ] Settings › Fans › **Detect now**: says no built-in modes (normal for desktops), or names your board's brand as "not supported yet".
- [ ] Without Fan Control: "Rest Mode will use: Windows power mode". Rest, wake: Settings › Power mode is back to what you noted.
- [ ] With Fan Control installed **and running**:
  - [ ] In Fan Control, save two configs: **Normal** (your usual) and **Quiet** (every fan at least 20-30 %, never 0 % / fan stop, a curve reaching 100 % by about 70-75 °C on CPU **and** GPU).
  - [ ] Settings › Fans: *Detect* finds FanControl.exe, ".NET runtime: OK", both configs are in the lists.
  - [ ] **Run test**: the fans slow down for about 10 s, then return.
  - [ ] Rest (approve the prompt, or install the helper task on the Safety page first): the fans slow down within a minute. Wake: they return.
- [ ] With Fan Control **closed**: Rest doesn't start it (log: "Fan Control is not running - fan profile not changed").

## 7. Safety

- [ ] Settings › Safety › **Read now** shows a CPU temperature (and a GPU temperature with an NVIDIA card). If it says "No sensor works", Rest Mode should keep the fans on their normal profile (log line "No temperature sensor works").
- [ ] Rest for 10 minutes; the log has a `Sensors:` line every 5 minutes with sensible temperatures.
- [ ] Watchdog: while resting, switch the power plan from another PC session or tool if you can (or skip). Within 30 s the log says "another program switched the power plan - switching back".

## 8. Crash recovery and the crash limit

- [ ] Command Prompt: `timeout /t 60 && taskkill /f /im RestMode.exe`, then Rest within 60 s. After it's killed, wake the screen and start RestMode again: a notification says settings were restored; your plan is back.
- [ ] (Optional) Kill RestMode three times in a row the same way, then sign out and in: RestMode does **not** start by itself and shows a message instead. Starting it from the Start menu works again.
- [ ] Double-click `%LOCALAPPDATA%\RestMode\RestMode-Reset.cmd`: it reports that everything was restored.

## 9. Standard account (optional, needs a second Windows account)

- [ ] Sign in with a **standard** (non-admin) account and run RestMode: Settings › Safety explains admin steps are skipped; Rest shows **no** password prompt and everything else works.

## 10. Install / uninstall

- [ ] RestMode-Setup.exe added Start menu entries "RestMode" and "RestMode – Rest now", the desktop shortcut (if ticked) and the Settings › Apps entry.
- [ ] Settings › Apps › RestMode › **Uninstall**: everything is restored and removed - Start menu entries, desktop shortcut, autostart, the helper task, `%LOCALAPPDATA%\Programs\RestMode` and `%LOCALAPPDATA%\RestMode`.

## Send back

The log file and anything you noted above.
