# Changelog

## 1.2.5

First public release of the Tabs skin. It includes everything from 1.1.4 to 1.1.6 and the 1.2.0 to 1.2.4 test builds below.


- **Fixed: the red "!" attention badge was clipped** (on a top-edge tab, the bottom-left of the badge was cut off).
  - **Why:** a tab window was 80 × 66 base units, and the badge sat 3 units from the body's corner. At the normal pop-out, its halo was about 2 px from the window's side. The throw/settle wobble (up to +22 units) and a depth clamp of 76 then pushed the badge past the window edge, so it was cut off in a straight line. On a top-edge tab that is the lower-left corner. Floating beacons had the same problem: the window was exactly the size of the art, the badge's halo came within 2.5 px of the side, and the red aura and ±12° tilts reached the window edge.
  - **Now:** tab windows are 86 × 70 units and the drawn depth is capped at 76 (the wobble included). The badge sits 4–5 units inside the body's outer corner, on the corner away from the screen edge, overlapping the frame. Floating beacons get a 32 px transparent margin on every side (click-through), and the badge and aura are kept on the monitor. The art is the same size as before. Saved positions load unchanged; beacons right at a screen edge may sit up to about 20 px further in.
  - The harness checks the badge and its glow are inside the window and the monitor's work area for every size, display scale 100–300 %, edge, depth and wobble, and on the rendered pixels. `ClickHarness alerts` checks floating beacons at every tilt and pulse.
- **New bots get their own tab, after you confirm.** When a paired bot sends a signed message to a beacon id you don't have, Bot Beacons asks **"New bot <name> wants a tab."** with **Add** (the default) and **Not now**. Add creates the tab, shows the message on it and uses the bot's profile picture if it sent one. Not now shows the message on the default beacon as before and doesn't ask again for that id until restart. Unsigned and local messages, replies and malformed ids never ask. `beaconctl.py send/attention --name NAME` sets the name shown.
- **Removed: making tabs in the app.** The shelf's "+ Add a tab" pane, the New tab dialog and Bots → Add a tab… are gone. A bot creates its tab by sending to its beacon id, and it tells you the id. Hidden tabs come back from Bots → Show or hide. Rename, hide and remove work as before.
- Docs: README, AGENT-GUIDE (new section "No beacon yet?"), SETUP and TECHNICAL updated.
- The setup guide changes listed under 1.1.6 are included (one looks question in the pairing message, a step-by-step wake hook message, the SmartScreen note, and the closing step 13). The Wake hook window's intro is reworded.

## 1.2.4 (test build)

- **Fixed: clicking a floating beacon only sometimes opened its window.** Several rules each ate some clicks:
  - **Double-click.** The second click of a double-click hit the beacon with the bubble already open, so it closed again at once. A click within the Windows double-click time of the click that opened the bubble now leaves it open.
  - **"Caught" after any motion.** For about 1.2 s after a drag, a throw, a dock or the settle that follows, every press counted as catching a moving beacon and could never be a click. Now only stopping a beacon that is actually sliding after a throw is a catch. A press while it settles is held still and still clicks if the hand didn't move.
  - **Swallowed after a close.** For 0.4 s after the bubble closed for any reason, a click only "closed" it (nothing opened). The beacon never takes activation, so a click on it can't close the bubble first. The rule is gone; only a bubble that is open at the press is closed.
  - **Threshold.** A press became a drag at more than 4 px on either axis and had to end within 1.0 s. Now: straight-line distance from the press point in screen pixels, past the larger of Windows' drag size and 7 px × the display scale. A still press (2 px or less) has no time limit; one that wandered a little must end within 1.5 s. The window bobbing, wandering or hopping under a still pointer never counts, because positions are screen coordinates.
  - **Hit area.** The clickable pad is now the art's silhouette swept through ±12° of tilt (sway, alert hops and play tilts draw the sprite rotated), grown by 8 px (was 6) and filled solid row by row, so there are no click-through holes in or around the body. The window's corners stay click-through.
  - **Debug log (opt-in).** `clickLog=1` in `settings.ini` (with the app closed) or the environment variable `BOTBEACONS_CLICKLOG=1` writes `clicklog.txt` in the settings folder. It records each press, how far it moved, how long it took, and the decision. Off by default.
- **Fixed: an attention call to a hidden beacon made its sound but showed nothing.** The report was `attention echo`: Echo is the test beacon, hidden unless Bots → Test beacons → Show test beacons is on.
  - **Why:** sounds play for hidden beacons by design (1.1.5), but there was nothing on screen to bounce or glow. The tray badge also leaves out hidden test beacons. The state itself was set: the routing is by id (`echo`), the sound and the attention state are set one line apart, and no throttle or UI-thread hop sits between them.
  - **Now:** a pending attention call brings its beacon on screen even if it is hidden or a hidden test beacon. It shows as a floating beacon (bounce, glow, "!") or as a tab (pops out, "!", glow), depending on the skin. It goes back to hidden when the call is resolved (Open in chat or Dismiss). **Pause beacons** still keeps the desktop clear; the tray badge shows the call.
- **Fixed: clicking into the Add tab dialog closed the shelf, and the dialog with it.**
  - **Why:** the shelf's click-away check only knew about the Edit tab window, so a press inside the New tab dialog counted as a click elsewhere. The dialog was also shown without an owner, so it could go down with the window that closed.
  - **Now:** every dialog the shelf, a card or a beacon opens (New tab, Edit tab, its image picker) holds the shelf and cards open while it is up. A press on one of them is never "elsewhere", and a card or bubble losing focus to one of them stays open. Dialogs are top-level windows owned by a hidden window that never closes.
  - The id field reads "Beacon id (what the bot sends to)", with a hint, and still fills itself in from the name.
- **Shelf header: sound controls grouped.** The speaker now sits beside the sound choices in one outlined group: [speaker] Sound: Custom | Classic | Off.
  - Clicking the speaker turns sound off, or back on to the last choice, and shows the volume. Its waves show the volume, and right-click sets it (Quiet / Normal / Loud).
  - Skin and Open on each have their own outlined group. The full shelf has a third footer row when it is vertical.
  - Tooltips: Custom = Bot Beacons alert sounds, Classic = Windows system sounds, Off = silent. The + pane reads and tips "Add a tab".
- New harness `tools/ClickHarness.cs.txt` (build lines at the top). It checks:
  - the click rules (jitter, a moving window, long presses, catches, double-clicks, display scaling) and the solid hit pad on the real art
  - the dialog hold (focus or presses on our own windows never collapse the shelf or a card)
  - the grouped header and its tips
  - every alert path (floating, tabs, mixed; sound on, off, muted or throttled; hidden, test, silent, restored, unknown id, paused) setting the visual state and reaching the screen

## 1.2.3 (test build)

- **Fixed: the pointer flickered between the I-beam and the arrow over an open tab card.**
  - Cause: the message box on the card (a RichTextBox) had `Cursor = Arrow`. WinForms' RichTextBox handles WM_SETCURSOR by letting the rich edit control set its own cursor (I-beam over text, hand over links) and then, for anything but a link, applying the `Cursor` property too. So every mouse move set the I-beam and then the arrow.
  - Fix: no `Cursor` on the text box. The rich edit control now picks the cursor alone: an I-beam over the message text (it can be selected), a hand over links and the buttons, and the arrow elsewhere on the card. Cursors are set only through WM_SETCURSOR / `Cursor` properties, never by a timer.
  - Checked: nothing re-lays out the card per tick (relayout, render caching, the tab and animation timers), and the classic bubble and cloud bubble don't have the problem.
  - `tools/mono-check.sh` now fails if a `Cursor` is set on a TextBox or RichTextBox.
  - The card also sits clear of the whole tab window (not just the tab body), so no window of the skin overlaps it.
- **Throw to dock.** Fling a floating beacon at a monitor's outer edge and it docks there as a tab, at the point it hit, with a small settle wobble. Edges between monitors don't count. Slow drags work as before. New setting right-click → Layout → **Throw to dock** (on by default).
- **Dock on the edge between two monitors by holding still.** While you drag a beacon or tab, keep the pointer within about 12 px of the shared edge with little movement for half a second. A dock preview appears on that edge of the monitor the pointer is on; let go to dock there. Keep moving and it crosses to the other screen as before. Outer edges still snap straight away, and throwing ignores the shared edges. A tab docked there belongs to its monitor and slides out into it.
- **Tabs show the beacon's own picture.** By default a tab shows its beacon's sprite in a disc tinted with its colour, with a ring in the beacon's colour (amber when it needs you). The colour strip shows on every edge and size.
  - Edit → picture has three choices: **Beacon picture** (default), **Bot's profile picture** (greyed out until the bot sends one) and **Custom image**.
  - Custom images keep working. Older builds saved "bot" for every tab, which can't be told apart from "never changed", so it now means the beacon picture. The new value for the profile picture is `profile`.
- **Click a floating beacon anywhere on its body to open its conversation**, with a hand pointer. The clickable area is the visible picture plus 6 px. Under 4 px of movement and under a second counts as a click; anything else is a drag or throw. Right-click still opens the menu. A click on a quiet beacon now opens its conversation too (Ping is in the menu).
- **In-app updates.**
  - Checks GitHub's latest release for SnazzyGaz/bot-beacons 30 s after start, about every 12 hours, and on **Updates → Check for updates now**. It compares semantic versions; a pre-release counts as older than its release.
  - If there's a newer release, the menu shows **Update available vX** and a balloon pops once per release (click it to update).
  - **Update now** asks first. It downloads the release's `-win.zip`, checks the size, the SHA-256 (when the release has a `<zip>.sha256`, `SHA256SUMS` or `checksums.txt`) and that it's a valid zip with `BotBeacons.exe` in it. Then a small helper waits for the app to exit, swaps the exe (keeping the old one until the new one is in place), restarts it and deletes itself. Pairing, settings.ini and pictures are untouched. Only the exe is replaced.
  - **Updates: Notify / Off** (default Notify). Nothing installs without you clicking. No network calls besides the GitHub API and the release download; offline and rate limits are silent, except for a manual check.
  - The newest published release is v1.1.3, so this build reports that it is newer than the latest release.
- **A tidier right-click menu**, the same on floating beacons, tabs, the shelf and the tray:
  - Reply to <bot>… (the tray lists every bot, with unread counts), Ping <bot> (beacons), Mark all read (only when something is unread)
  - Skin ▸ All floating / All tabs / (Mixed), Make <bot> a tab / float, Bubble: Card / Cloud, Animations ▸
  - Layout ▸ Tab size ▸, Open tabs on ▸, Throw to dock, Open the whole shelf (when there are tabs)
  - Sounds ▸ Custom / Classic / Off, Volume ▸, Quiet hours, Mute <bot>, Test message / attention sound
  - Bots ▸ Edit <bot>… (tabs), Hide <bot>, Test glow, Show or hide ▸, Pause beacons, Test beacons ▸, Add a tab…, Pair / Re-pair with bots…, Wake hook…, Open settings folder
  - Updates ▸ (or **Update available vX** ▸) Update now to vX…, Check for updates now, Notify me / Off, "This is v1.2.3 — status"
  - Start with Windows, About Bot Beacons…, Exit
  - Items that can't do anything at the moment are hidden, not greyed out.
- settings.ini keys: `throwDock=1|0` (default 1), `updates=notify|off` (default notify), `updateNotified=<tag>` (the release the balloon was shown for). Tab prefs: `image` is `beacon|profile|custom` and `seam` is `1` for a tab docked on a shared edge.
- **Dev:**
  - The TabLayout harness adds impact docking, seam dwell vs pass-through, the settle wobble, beacon pictures and rings, and the click target (344k checks). Its build line now includes `src/HitPad.cs`.
  - New `tools/UpdateHarness.cs.txt` (+ `tools/update_fixtures.py`): version compare, release JSON (including GitHub's real answer), asset and checksum picking, URL rules and zip verification (good, stored, missing exe, exe only under source/, not a program, damaged CRC, truncated, not a zip, `..` paths, wrong size or checksum, a real release zip). 40 checks.
  - New control.txt hooks: `throwdock on|off`, `update check`. status.txt shows `throwDock=` and `updates= available= status=`.
- **Known limits:** not yet tried on Windows, including the update swap, which can only be tested on a real release.

## 1.2.2 (test build)

- **Merged 1.1.6 (message size limit):** sealed envelopes are checked against 3,900 bytes, the reply box refuses anything that would seal larger ("Too long to send"), and `seal()` uses `ensure_ascii=False`. See 1.1.6 below.
- **Profile pictures obey the size limit too.** `beaconctl.py avatar` seals every chunk with the 1.1.6 check before posting anything. Chunks are still 1,800 bytes of picture (they seal to about 3,630–3,740 bytes). If one would go over, for example with a very long beacon id, the chunk size shrinks in 200-byte steps until every chunk fits. Below 768 bytes it stops with "Not sent: …" and nothing is posted. New tests in `tools/test_message_size.py` (section 3b).
- **Each beacon floats or is a tab: your choice, by dragging.**
  - Drag a floating beacon so the pointer ends within about 24 px of a monitor's outer edge, and a snap outline shows where it will dock. Let go and it becomes a tab there.
  - Drag a tab more than about 80 px into the screen and the outline becomes the beacon's own size. Let go and it floats at that spot.
  - Edges between side-by-side monitors don't count.
  - Each beacon's choice is saved, and floating beacons and tabs can be on screen together.
  - A beacon's right-click menu also has **Make it a tab / Make it float**.
  - The **Skin** menu is now **Layout: All floating / All tabs**. It shows **Mixed** when beacons differ; picking either "All" resets the per-beacon choices.
- **Bottom tabs no longer cut the picture in half.**
  - Collapsed tabs stick out further (54 instead of 44 base px; alerted 66).
  - The picture has a fixed place inside the tab (`TabLayout.ImageRect`) with room on every side.
  - Work areas now exclude every taskbar the app can find, including the per-monitor taskbars Windows 10/11 show on secondary displays. On the test PC a taskbar was drawn over the bottom tabs.
  - The harness checks that the whole picture is inside the visible tab on all 4 edges, 3 sizes and every depth (resting, hover nudge, alert), both geometrically and by finding the picture's pixels in the rendered tab.
- **The tab card is the conversation.** For tabs, the card that slides out is the bubble; there is no separate dialog.
  - It shows the picture, name, role, time and the full latest message (scrolls if long, links clickable).
  - Click the card and it expands in place with the recent conversation, a reply box, **Send**, **Open in chat** and **Dismiss**.
  - It takes focus only when you click into the reply box.
  - It stays open while you're replying. It slides back on Esc, after a reply is sent, on a second click on its tab, or on a click anywhere else. A half-typed reply is kept for next time.
  - The card sits beside its tab, so the tab stays visible.
  - Floating beacons keep their usual bubble.
- **Click opens; hover only nudges.**
  - A click on a tab slides its card out, and a click on the tab again, Esc or a click elsewhere slides it back. Hovering just nudges the tab out a few px.
  - The grip at the end of a strip opens the whole shelf on click; it stays until Esc or a click elsewhere.
  - New setting **Open tabs on: Click / Hover** (right-click → Layout, or the shelf footer). Click is the default. Hover works like 1.2.1, but opens the conversation card.
  - A drag still starts only after about 6 px of movement with the button held.
- settings.ini keys: `tabModes=id:tab,id2:float` (per-beacon choices that differ from `skin=`) and `tabOpen=click|hover` (default click; replaces `tabHover`).
- **Dev:**
  - The harness gains the dock and undock thresholds on the two-monitor PC layout, picture containment, taskbar trimming and card placement (283k checks).
  - New control.txt hooks: `layout floating|tabs`, `mode <id> tab|float`, `tabopen click|hover` (`tabhover` still works as an alias). `card <id>` opens the conversation card.
  - status.txt shows `layout=` and `tabOpen=`.
- **Known limits:**
  - Not yet tried on Windows.
  - Typing before you click into the card can't open the reply box, because the card never takes focus by itself. Click the card first.

## 1.2.1 (test build)

- **Dragging fixed (root cause).** In 1.2.0 a drag often showed the dashed outline and then left the tab where it was, and dropping it on a second monitor next to the first did the same. Three things caused it:
  1. **Snapping counted the seam between side-by-side monitors as an edge.** The default spot (the primary monitor's right edge) *is* the seam on a two-monitor desk. A drop on the left half of the second monitor snapped to that monitor's left edge, at x=1920, a few pixels from where the tab already was (x≈1840). A drop in the middle of the first monitor also snapped back to its right edge. The outline showed, but the tab didn't visibly move or jump across.
  2. **The shelf slid over the tab you were trying to grab** (after 0.14 s) and hid it, so the press often landed on a shelf row, which couldn't drag.
  3. **The drop was taken from the capture-changed event**, at whatever cursor point the app had then, instead of where the button was released.
  
  Now:
  - Edges that face another monitor are never snap targets: a drop snaps to the nearest *outer* edge of the monitor under the pointer. Tabs saved on a seam (or never placed) show on the outer edge on the same side, e.g. DISPLAY3's right edge.
  - The drag follows the pointer live, driven by a 15 ms poll of the mouse button and cursor, so a lost capture can't end it early. On release it snaps to the monitor under the pointer, saves monitor + edge + position, and lays out at once, exactly where the outline was.
  - A drag can start from the tab, its hover card or its shelf row. The shelf header still moves the whole group, with the same snapping.
- **Bigger and easier to read.** Everything is drawn larger (about 1.4×): names 11.5 pt semibold, messages 10 pt on up to two lines, roles 9 pt; the shelf is about 460 px wide. The Edit tab and New tab windows grow to match (1.3×). New setting **Tab size: Small / Normal / Large** (right-click → Skin, or the shelf footer). Normal is the new default; Small is close to 1.2.0.
- **Hover a tab and only that tab slides out** as a card: picture, name, role, two-line message, time, pencil, badge. Click the card to open the bubble. It slides out after about 0.15 s of hover and closes about 0.4 s after the pointer leaves.
- **The whole shelf** is still there: click the small grip at the end of each strip, or choose **Open shelf** on the tray menu or a tab's right-click menu. New setting **Hover opens: This tab / Whole shelf** (right-click → Skin, or the shelf footer). This tab is the default.
- settings.ini keys added: `tabSize=small|normal|large` (default normal) and `tabHover=tab|shelf` (default tab).
- **Dev:** the harness now runs every check at all three sizes and adds cross-monitor drops on a two-monitor 1920×1080 layout, seam rules, the outward placement, the card and the grip (258k checks). New control.txt hooks: `card <id>`, `openshelf`, `tabsize small|normal|large`, `tabhover tab|shelf`. status.txt shows `tabSize=`, `tabHover=`, the open card and any drag in progress.
- **Known limits:** not yet tried on a real Windows desktop (built and checked on Linux against the .NET 4.8 references); still no per-monitor DPI scaling. The Size setting stands in for it.

## 1.2.0 (test build)

- **New skin: Tabs.** Right-click menu → **Skin** → **Beacons (floating)** / **Tabs (screen edge)**. Beacons stays the default. The choice is saved (`skin=` in settings.ini) and switches live, with no restart. Both skins share the same beacons, messages, glow/attention state, sounds, hiding and relay.
  - **Collapsed:** each shown beacon is a small bookmark tab docked to a screen edge, with its picture and a colour strip.
  - **Shelf:** hover a tab for ~0.15 s (or click a quiet one) and a shelf slides out for that edge. Each row shows the picture, a status dot (green = relay connected), name, role, latest message, time and a pencil. Click a row to open that beacon's bubble. The shelf has **+ Add tab**, and its footer has **Sounds: Custom / Classic / Off** and **Skin**. It closes 0.4 s after the pointer leaves it (and its tabs and open bubble).
  - **Alerts:** an alerted tab pops further out with an amber outline and a red "!" badge, and plays the beacon's sound (the 1.1.5 sound code and throttles). Unread messages nudge the tab out a little with a dot. Clicking a glowing tab opens its bubble directly. Reading clears it exactly as before: unread clears when the bubble opens; an attention call stays until **Open in chat** or **Dismiss**.
  - **Dragging:** drag a tab to any edge of any monitor. A dashed outline shows where it will land, and it snaps to that monitor's working area (taskbar excluded). Each tab can have its own edge; positions are saved per monitor. On top and bottom edges the tabs hang down/up (vertical). Drag the shelf's header to move that whole group of tabs.
  - **Edit tab** (pencil on a row, or **Edit tab…** on a tab's right-click menu): name; tab image (**the bot's profile picture**, or a **custom image** PNG/ICO); colour (swatches or hex); sound (Custom / Classic / Off, with a preview button); **Show on shelf**; **Remove tab**. Edits are local overrides in `%APPDATA%\BotBeacons\tabs.json` and win over beacons.json.
  - **+ Add tab:** bring back hidden tabs, resume when paused, or **New tab…**: a tab of your own with a name and id. Bots can send to that id like any beacon. Remove tab deletes your own tabs; for a beacon from beacons.json it resets your edits and hides it (+ Add tab brings it back).
- **Bots can send their profile picture.** `beaconctl.py avatar BEACON picture.png` squares, shrinks (≤ 96 px, ≤ 24 KB) and sends the picture in signed, encrypted chunks (new message kind `avatar`, silent: no bubble, glow or sound). The PC checks every chunk, the total size (≤ 32 KB) and SHA-256, re-draws it as a 128 px PNG in `%APPDATA%\BotBeacons\avatars\<beacon>.png`, and uses it as the tab's default picture. `avatar BEACON --clear` removes it. The floating Beacons skin keeps its own art.
- **Sound sets.** The Sound menu gains **Sounds: Custom** (each beacon's own sounds from 1.1.5; the default) and **Sounds: Classic** (the Windows system sounds). New setting `soundSet`. A tab can choose its own set; **Off** on a tab is the 1.1.5 per-beacon mute.
- **Rules kept:** tabs, the shelf and the drag outline are layered tool windows that never take focus or show on the taskbar (click = no activate). Menus are owned and reused. Probes, status messages and avatar transfers stay silent. Only the Edit tab and New tab windows take focus, because you open them to type.
- **Known limits of this test build:** no DPI scaling yet (sizes are in pixels); a dock's tabs hide while its shelf is open (the shelf covers that spot); the Edit tab window stays open until Save, Cancel or Esc.
- **Dev:** `tools/TabLayoutHarness.cs.txt` (snap/stack/shelf maths, 361k checks, plus PNG previews of every edge) and `tools/AvatarHarness.cs.txt` (chunk reassembly and rejects). New control.txt hooks: `skin`, `shelf`, `edittab`, `soundset`, `tabsnap`; status.txt gains `soundSet=` and a `skin=… tabs=… shelf=…` line.

## 1.1.6

- **Long messages with emoji or accents no longer vanish.** ntfy.sh caps a message at 4096 bytes and turns anything bigger into a file, which the app can't read; the sender was still told it worked.
  - **beaconctl** wrote every non-ASCII character as `\uXXXX` (6 bytes, 12 for an emoji) and checked only the raw text against 2,400 bytes, without the title. 600 emoji made a 9,838-byte message. It now writes UTF-8 as is and checks the final sealed message (title included) against 3,900 bytes. Anything longer is refused with exit 1 and a clear "Not sent" message that says how much would fit. Nothing is split.
  - **Replies from the app** are checked the same way before sending. The box used to allow 1,500 characters, but 1,500 Chinese characters sealed to 6,298 bytes. Now the text stays in the box with "Too long to send: shorten it by about N%", and nothing is cut off silently.
  - Messages that do arrive as a file are now logged as "too large for the relay" on both sides.
  - Tests: `tools/test_message_size.py` and `tools/SizeHarness.cs.txt`.
- **Setup guide (docs/SETUP.md):**
  - The bot asks once, in the pairing message, which look (orb, robot or cat) each beacon should have, and keeps its round-robin picks if there's no answer. Setup never waits for it.
  - The webhook step is now a numbered message for the user: where the two values are, where to paste them, what "Test OK" looks like, and what each Test error means.
  - Before the user runs the ready-made exe, the bot warns that SmartScreen may appear (More info, then Run anyway) and offers to build from source instead. The README says the same.
  - A new closing step tells the user everything is open to change: any look, any number of beacons, and how the app works, rebuilt and reinstalled by their bot. The README says so too.
- **Docs:** the README says plainly that the bot-side key is a file any of your bots could read. Code comments now point to docs/TECHNICAL.md (docs/README.md never existed). beaconctl's help says "Open in chat", like the app.

## 1.1.5

- **Alert sounds.** A beacon plays a short sound when a message arrives, and a distinct double-pulse version of the same voice for an attention call.
  - **Silent cases:** probes and probe-acks, status/heartbeat kinds, messages with `"silent": true`, and your own replies and pings. These still show up as usual.
  - **Hidden or paused beacons still play.** That is when sound helps most. Mute a beacon to silence it.
- **Voices:** original synthesised sounds (no samples) in `sounds\`: `orb`, `robot` and `cat`, plus a neutral `default` for the rest. Pick them per beacon with the new `sound` / `attentionSound` fields; beacons.json is now version 2. See THEMES.md → Sounds.
- **Sound submenu** on the tray menu and each beacon's menu:
  - Sound on/off
  - Volume: Quiet / Normal / Loud. Only the app's own sounds change, because the samples are scaled before playing; the Windows volume is never touched.
  - Quiet hours, 23:00 to 07:00 local time
  - Beacon menus only: **Mute this beacon**, plus **Preview message sound** and **Preview attention sound**
  - The defaults are sound on, Normal volume and quiet hours off. They're saved as new settings.ini keys: `soundOn`, `soundVolume`, `soundQuietHours`, `soundMuted`.
- **Throttle:**
  - at most one message sound per beacon every 4 s
  - at most one attention sound per beacon every 10 s
  - at most one sound every 1.5 s across all beacons, so a burst gives one sound
- **Playback:** a worker thread scales the sound and then plays it with `PlaySound(SND_MEMORY | SND_ASYNC | SND_NODEFAULT)`. It never touches the UI thread or frame clock, creates no window, and never takes focus.
- **Install:** build.bat copies `sounds\` to `dist\sounds\`, and install.ps1 installs it next to the exe.

## 1.1.4

- The bubble and the right-click menu now open towards the side of the screen that has room, so a screen edge never cuts them off.
  - **Card:** opens to the right of the beacon and hangs down by default. It flips left and/or up when that side would cross an edge of the beacon's monitor (the taskbar is excluded). It's nudged on screen only if no side fits.
  - **Cloud:** opens to the left of the beacon and above it by default. It flips right and/or below in the same way. The trail of circles is mirrored to match, so it still points at the beacon.
  - **Dragging:** an open bubble now follows a dragged beacon and flips sides as you go. It only moves its window unless the side changes, so dragging stays smooth.
  - **Menus:** the right-click menu picks above or below and left or right from the room around the cursor. Each submenu opens to whichever side of the menu has room.
  - **Monitors:** placement uses the work area of the monitor the beacon is on, so it handles several monitors, including ones left of or above the main one, and a taskbar on any side.
- `tools/PlacementHarness.cs.txt` is a Mono check of the placement maths. It covers every edge and corner, beacons hanging partly off screen, a drag sweep, and two-monitor layouts with a non-zero origin. Its header has the build command.

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
