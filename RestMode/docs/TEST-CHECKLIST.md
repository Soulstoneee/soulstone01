# RestMode test checklist

Run these on a real Windows 10 or 11 PC. Tick each box. The log is at
`%LOCALAPPDATA%\RestMode\restmode.log` (tray icon > **View log**).

**Before you start**
- [ ] Run `powercfg /getactivescheme` and write down the plan name: ______________
- [ ] Note your volume level: ______ %
- [ ] Settings > **Downloads**: set *minutes* to **2** (to make the tests quick).

Tip: some checks need a second window open *while the screen is off*. Use
another device (Remote Desktop/phone) or read the log afterwards.

---

## 1. Enter Rest Mode
- [ ] **Hotkey** (default `Ctrl+Alt+Shift+R`): the screen goes off within ~1-2 s.
- [ ] **Tray menu** > *Enter Rest Mode*: same result.
- [ ] **Desktop shortcut** (Settings > General > *Create desktop shortcut*): same result, and no second tray icon appears.
- [ ] If "Keep network adapters fully powered" is on and the helper task is not installed: one UAC prompt appears *before* the screen turns off. Declining it does **not** stop Rest Mode (the log says the step was skipped).
- [ ] The tray icon turns **orange**.
- [ ] Log shows: saved plan name, each `plan:` line, `Audio muted`, `System kept awake`, `Display turned off`, `Resting.`
- [ ] (Optional, via Remote Desktop / afterwards) `powercfg /getactivescheme` showed **RestMode (temporary)** and `powercfg /requests` listed RestMode under **SYSTEM** (not under DISPLAY).

## 2. Wake with the mouse / keyboard / controller
- [ ] Move the mouse a little more than a few pixels: the screen comes on, the tray icon is **blue** again.
- [ ] `powercfg /getactivescheme` shows **your original plan** again.
- [ ] `powercfg /list` no longer lists **RestMode (temporary)**.
- [ ] Volume and mute state are as before.
- [ ] Repeat, waking with a **key press**.
- [ ] Repeat, waking with an **Xbox/XInput controller button** (if you have one).
- [ ] Waking with the hotkey itself does **not** put the PC straight back into Rest Mode.
- [ ] Log has a `SESSN` line with start time, duration, downloaded/uploaded data, `end=Woken`.

## 3. Screen stays off
- [ ] While resting, tap the mouse very gently (less than the wake threshold): the screen may flash on, then turns off again after ~4 s and Rest Mode continues.

## 4. Auto-sleep after downloads finish
- [ ] Settings > Downloads: threshold **50 KB/s**, **2** minutes, then **Sleep**.
- [ ] Start a large download (Steam game/update, or a big file in a browser).
- [ ] Enter Rest Mode. The PC must **not** sleep while the download runs (wait at least 5 minutes).
- [ ] When the download finishes (and Steam has finished unpacking), the PC goes to **sleep** about 2-3 minutes later.
- [ ] Wake the PC (power button/keyboard): normal plan, normal volume, no RestMode plan in `powercfg /list`.
- [ ] Log shows `Downloads look finished ... -> Sleep`, then `SESSN ... downloaded=<size> ... end=Sleep`.
- [ ] Repeat with **Hibernate** (if enabled on the PC) and **Shut down** (a 60 s Windows shutdown warning appears; moving the mouse cancels it → log says `Shutdown cancelled`).
- [ ] With **Stay in Rest Mode**: the PC stays resting; log says `staying in Rest Mode as configured`.

## 5. Crash recovery
- [ ] Open a Command Prompt and run: `timeout /t 60 && taskkill /f /im RestMode.exe`
- [ ] Within 60 s, enter Rest Mode. Wait until the command kills RestMode (about a minute), then wake the screen.
- [ ] At this point the RestMode plan is still active (expected: the app was killed).
- [ ] Start RestMode again: a notification says settings were restored. Your plan is active again, `powercfg /list` has no RestMode plan, volume is back, paused apps run again.
- [ ] Log shows `did not exit cleanly last time ... Restoring saved settings`.
- [ ] Alternative: kill it the same way, then run `RestMode.exe --restore` instead of starting it normally → same result.

## 6. Sleep / log-off while resting
- [ ] Enter Rest Mode, then press the PC's **power/sleep button** (set to "Sleep"). Wake it: everything is restored (log: `PC resumed from sleep`, `end=SleptExternally`).
- [ ] In a Command Prompt run `timeout /t 60 && shutdown /l`, then enter Rest Mode. After Windows signs you out, sign back in: plan, volume and apps are normal (log: `Emergency restore: Windows is logging off`).

## 7. Network adapters (admin)
- [ ] Device Manager > your network adapter > **Power Management**: note *Allow the computer to turn off this device to save power*: ______
- [ ] Enter Rest Mode (accept UAC if asked), wake up, and check the box during/after (Remote Desktop or `helper.log`): unticked while resting, back to the original value after waking.

## 8. Fan Control (only if installed)
- [ ] Create a config named `Quiet.json` in Fan Control. Settings > Admin / Fans: Fan Control path detected, quiet = `Quiet.json`, normal = your usual config.
- [ ] Entering Rest Mode switches Fan Control to Quiet; waking switches it back.

## 9. Background apps
- [ ] Settings > Apps: enable, add `notepad` with **Pause**. Open Notepad, type something, enter Rest Mode.
- [ ] After waking, Notepad responds normally and the text is still there.
- [ ] Add `chrome` (or `steam`): saving shows a message that it is protected and will never be touched.

## 10. Laptops
- [ ] Enter Rest Mode on battery; wake; check plan + battery settings are unchanged (Control Panel > Power Options > *Change plan settings* shows your old battery values).
- [ ] With *Keep resting with the lid closed*: closing the lid keeps the download going; opening it and pressing a key wakes.
- [ ] Set the battery limit just below the current charge: the laptop hibernates/sleeps when it is reached.

## 11. Settings & startup
- [ ] Change the hotkey; the new one works immediately, the old one doesn't.
- [ ] Tick *Start with Windows*, sign out and in: the tray icon appears.

## 12. Uninstall
- [ ] Run `RestMode.exe --uninstall` and answer the questions.
- [ ] No *RestMode* entry in Task Manager > Startup apps, no desktop shortcut, no *RestMode Helper* task in Task Scheduler, no *RestMode (temporary)* plan.
