# Changelog

## 1.1.3

- **One reusable menu per beacon.** Each beacon builds its right-click menu once and reuses it. It is shown owned by the beacon (`Show(beacon, point)`), refreshed when it opens, and disposed only with the beacon. The 1.1.1 build-per-click and delayed dispose are gone. The tray keeps its single menu. Submenu items that are refilled on open are now disposed, and each menu item is hooked once by the taskbar guard; before, a reused menu gained one handler per item on every open. Hidden menu windows stay at a fixed number per beacon instead of growing with every right-click. Menus still never get a taskbar button.
- **Silent health probe.** New `beaconctl.py probe` (about a 30 s wait). The app logs `Probe received (silent)` and answers with a signed `probe-ack`, with no bubble, glow, sound or wake. beaconctl and the router ignore acks. Exit code 0 = OK, 1 = no answer.
- **Health checks are silent (docs).** AGENT-GUIDE, SETUP, ROUTER and ROUTER-PROMPT say bots never send status-only lines (such as "still running", "health check OK" or heartbeats) to a beacon. Health checks use `probe`, and the user is told in chat only after it fails twice in a row, at least 10 minutes apart. The one setup test send is labelled "Setup test:", and none is sent after upgrades.

## 1.1.2
- **Fix: catching a sliding beacon.** Pressing on a beacon while it was still sliding from a fling made it jump back to where it was before the throw, sometimes hundreds of pixels. Letting go without moving counted as a click (ping, or the bubble opening or closing), and the landing spot was lost.
  - Pressing on a beacon that is moving now catches it on the spot. A beacon counts as moving while it is flung, or still settling for about 1.2 s afterwards. The beacon stays exactly under the cursor with no 4 px dead zone, and the bob, sway and drift keep fading smoothly.
  - A catch is never a click. Let go quickly (over 300 px/s) and it is thrown again from where you hold it. Otherwise it rests there.
  - Grabs, drags and flings keep the sub-pixel position, so nothing shifts by a pixel when you pick a beacon up or let go.
  - If another app takes the mouse while you hold a beacon (Alt+Tab, for example), the beacon is let go where it is.
  - Clicking or dragging a beacon that is at rest works as before.
- The frame clock thread never changes beacon state. It only queues each tick onto the UI thread, so frames and mouse handling run one at a time.
- New developer hook `flingcatch <id> [classic]` (only with `devHooks=1`, off by default): a dry run that replays six catch cases with a simulated mouse and logs the results.

## 1.1.1
- **Fix: no more taskbar buttons from the right-click menu.** Opening a beacon's right-click menu put a new button on the Windows taskbar, and every submenu it opened (Animations, Bubble style, Beacons, ...) added another. The menu was shown without an owner window, and WinForms menus don't use the tool-window style, so Windows treated each one as an app window.
  - The beacon menu is now shown with the beacon as its owner and is disposed after it closes, instead of leaving a hidden window behind every time. (1.1.3 replaces this with one reused menu per beacon.)
  - Every menu and submenu, including the tray icon's menu and submenus filled in when they open, gets the tool-window style before it is shown (`src/NoTaskbar.cs`).
  - The pairing and Wake hook windows no longer get taskbar buttons either. They stay on top, and the right-click menu brings them back.
  - Bubbles (card and cloud) and beacons were already tool windows with no taskbar button. That's unchanged.

## 1.1.0
- Smooth motion: a shared 60 fps display-paced frame clock, time-based motion, sub-pixel drawing from a cached sprite, and a frame-rate setting (30 / 60 / match display).
- A fling slows smoothly to rest, and the bob, sway and drift fade back in after a drag or fling.
- Every bubble has **Open in chat** (Alt+O) and **Dismiss** (Alt+D) footer links. The big attention buttons are gone.
- `build.bat` only replaces the exe after a successful build.

## 1.0.1
- Pairing keeps waiting when you close its window, and there's a **Cancel pairing** button. Re-pair shows the same code while a session is active. Relay requests retry with backoff.
- Streaming relay connection (one long-lived connection instead of polling), with `relayMode=poll` as a fallback.
