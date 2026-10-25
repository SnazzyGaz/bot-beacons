# Bot Beacons: technical notes

## Using it

- **Drag** a beacon to move it. Each one remembers its own position.
- **Glow** means unread messages. **Click** a glowing beacon to open its bubble. The bubble shows the beacon's name and purpose, the latest messages (up to 50 per session), a reply box (Enter to send) and "End-to-end encrypted" once paired.
- **Click away**, press Esc, or click the beacon again to close the bubble. Opening it clears the glow of normal messages.
- **Footer links:** every bubble (messages, ping answers, the reply view and attention calls, in both bubble styles) has two small links on its status line. **Open in chat** (Alt+O) opens that bot's chat in the Grok Bot app, marks the beacon read and closes the bubble. **Dismiss** (Alt+D) marks it read and closes the bubble without opening the app. The links are never focusable and clicking them doesn't activate the bubble or move focus. They turn amber while an attention call is pending.
- **Focus:** the app never takes keyboard focus on its own. Incoming messages and alerts only glow and never open a bubble. A bubble takes focus only when you open it yourself or click into its reply box.
- **Ping:** right-click a beacon → **Ping <name>** pings its bot (before 1.2.3 a click on a quiet beacon did this; since 1.2.3 a click anywhere on a beacon's body opens its conversation). The beacon wiggles, the ping goes through the relay and wakes the router, and the bot answers with "How can I help?" or its own ping action. You can ping each beacon at most once every 30 seconds.
- **Needs attention:** a fast double-beat pulse with a white flare and a red **!** badge means a bot needs you to act in the Grok Bot app. The bubble shows the reason. Its **Open in chat** link opens the bot's chat through `grokbot://app/v1/agent?id=<agentId>` and brings the Grok Bot window to the front (or only brings it forward if the `grokbot:` link isn't registered). Attention is **sticky**: it survives clicks, closing the bubble and restarts (`attention.txt`). Only the **Open in chat** or **Dismiss** footer link ends it; Esc, clicking away, X and clicking the beacon leave it pulsing.
- **Hide / show:** right-click a beacon → **Bots** → **Hide <name>**. Right-click → **Bots** also has:
  - a **Show or hide** checklist (ticked = shown, with unread or attention notes), plus **Show all** / **Hide all**
  - **Pause beacons (hide all)**, also toggled by double-clicking the tray icon. Un-pausing restores exactly the previous set.
  - a **Test beacons** submenu: "Show test beacons" plus a one-by-one checklist

  These are saved in `settings.ini` as `hidden=id1,id2` and `paused=0|1`. Hidden beacons keep running underneath. Messages and attention calls still arrive and queue (unread count, glow state), and the tray icon shows them: an amber dot for unread, a pulsing red "!" dot for attention. The tooltip says, for example, "Research Bot: 2 unread (hidden)". There are no balloons or windows, so nothing takes focus. Sounds still play (1.1.5). 1.2.4: a pending attention call is the exception: it brings the beacon (or its tab) on screen, hidden test beacons included, until Open in chat or Dismiss. Then it goes back to hidden; while paused it stays tray-only. A single click on the tray icon opens the first visible beacon with news, or brings back a hidden one that has news. Showing a beacon restores its glow, and its bubble if it was open (without focus).
- **Right-click menu** (1.1.3: built once per beacon and reused, owned by the beacon, and refreshed when it opens; 1.2.3: one structure for beacons, tabs, the shelf and the tray, built by `Program.BuildMenu`): Reply to <bot>… (the tray lists every bot), Ping <bot>, Mark all read; **Skin** ▸ (All floating / All tabs / Mixed, Make <bot> a tab or float, Bubble: Card / Cloud, Animations ▸); **Layout** ▸ (Tab size ▸, Open tabs on ▸, Throw to dock, Open the whole shelf); **Sounds** ▸ (Custom / Classic / Off, Volume ▸, Quiet hours, Mute <bot>, tests); **Bots** ▸ (Edit <bot>…, Hide <bot>, Test glow, Show or hide ▸, Pause beacons, Test beacons ▸, Pair / Re-pair with bots…, Wake hook…, Open settings folder); **Updates** ▸; Start with Windows; About Bot Beacons…; Exit. Items that can't do anything at the moment are hidden.

## Layout

```
src/            C# source (.NET Framework 4.8, C# 5; built with the csc.exe that ships with Windows)
  Program.cs      start-up, tray, routing messages to beacons, dev hooks, log
  BeaconForm.cs   one transparent layered window per beacon: animation, glow, unread, history
  Anim.cs         idle actions and twitches from the "anim" block
  Bubble.cs, CloudBubble.cs, CloudRender.cs   the dark card and the cloud bubble
  NoTaskbar.cs   keeps menus and submenus off the taskbar (tool-window style, owned by the beacon)
  FrameClock.cs, FastBlit.cs   the shared frame clock and the sub-pixel sprite sampler
  BeaconConfig.cs beacons.json, art loading and layered composition
  Relay.cs        ntfy stream/sending, message checks, pairing protocol
  Crypto.cs       AES-256-CBC + HMAC-SHA256, key derivation, DPAPI
  Settings.cs     settings.ini + the DPAPI-protected link
  PairForm.cs     pairing-code window
  Wake.cs, WakeForm.cs   wake-on-reply webhook and its dialog
  Startup.cs      Start with Windows (HKCU Run value "BotBeacons")
  Sound.cs, SoundGate.cs   alert sounds, throttles, sound sets (1.1.5 / 1.2.0)
  Skin.cs         1.2.0: Beacons / Tabs skin switch (settings.ini skin=); 1.2.2: per-beacon floating/tab (tabModes=)
  TabsSkin.cs     1.2.0: the Tabs skin: layered tab windows, the slide-out shelf, dragging and snapping, hover timing
  TabLayout.cs    1.2.0: pure maths: edge snapping per monitor working area, stacking, shelf placement, slide easing
  TabRender.cs    1.2.0: pure drawing: tabs (all four edges), the shelf, the snap outline
  TabCard.cs      1.2.2: a tab's conversation card (the bubble for beacons that are tabs)
  TabStore.cs     1.2.0: tabs.json (per-tab overrides, positions, user-added tabs), custom tab images
  TabEditForm.cs  1.2.0: the Edit tab popover (1.2.5: the New tab dialog is gone; bots create their own tabs)
  TabPictures.cs  1.2.6: the Tab pictures choice (tabPictures=profile|beacon|ask) and the "prefs" whitelist
  TabGif.cs       1.2.6: animated GIF custom tab pictures (frames, timing per animation mode and fps)
  ThemeMerge.cs   1.2.6: three-way merge of the user's beacons.json on a version bump; the beacons.user.json overlay
  NewBots.cs      1.2.5: "New bot X wants a tab. Add / Not now" for a signed message to an id with no beacon
  Avatars.cs      1.2.0: bot profile pictures received over the relay (chunked, checked, re-encoded)
  HitPad.cs       1.2.3: makes a floating beacon's whole body clickable in its layered window (1.2.4: solid silhouette, swept +/-12 deg, + 8 px)
  ClickLogic.cs   1.2.4: pure click rules for floating beacons (slop, click/drag/catch, double-click) and the opt-in click log
  ModalHold.cs    1.2.4: our dialogs hold the shelf and cards open; a hidden top-level owner for dialogs
  UpdateLogic.cs  1.2.3: pure update logic: version compare, GitHub release JSON, URL rules, checksum and zip verification
  Updater.cs      1.2.3: update checks, the download, and the helper that swaps the exe
art/            starter-pack PNGs + icon (embedded in the exe; overridable)
beacons.json    default beacons (embedded)
themes/starter/make_starter_art.py   draws art/
tools/          beaconctl.py (bot side), make_beacons.py, agents.json template, install.ps1, uninstall.ps1,
                test_message_size.py and *Harness.cs.txt (offline checks)
build.bat       builds dist\BotBeacons.exe
```

Per-user data lives in `%APPDATA%\BotBeacons\`:

| File | Contents |
|---|---|
| `settings.ini` | positions, `hidden=` / `paused=`, options, the encrypted link and wake hook |
| `beacons.json` | your beacons (editable) |
| `theme.json` | bubble style |
| `attention.txt` | open attention alerts |
| `seen.txt`, `relay-cursor.txt` | replay protection, relay position |
| `log.txt` | events only. Message text is never logged, only the beacon id and length. |
| `art\` | optional custom art |
| `tabs.json` | 1.2.0 Tabs skin: per-tab overrides (`name`, `image` beacon\|profile\|custom (1.2.3; older builds wrote `bot`, now read as beacon), `color`, `sound` ""\|custom\|classic, `monitor` device name, `edge`, `along` 0..1, `seam` 1 for a tab docked on an edge between monitors (1.2.3)) and the tabs you added (`added`). Overrides win over beacons.json |
| `tabs\<id>.png` | a tab's custom image (imported from your PNG/ICO, 128 px) |
| `avatars\<id>.png` | a bot's profile picture, sent with `beaconctl.py avatar` (re-encoded, 128 px) |

settings.ini keys added in 1.2.0: `skin=beacons|tabs` (default beacons) and `soundSet=custom|classic` (default custom). In 1.2.1: `tabSize=small|normal|large` (default normal) and `tabHover=tab|shelf` (default tab; replaced in 1.2.2). In 1.2.2: `tabModes=id:tab,id2:float` (each beacon's own choice when it differs from `skin=`) and `tabOpen=click|hover` (default click). In 1.2.3: `throwDock=1|0` (default 1), `updates=notify|off` (default notify) and `updateNotified=<tag>` (the release the one-time balloon was shown for).

Program files go in `%LOCALAPPDATA%\Programs\BotBeacons\` (from `tools/install.ps1`). Autostart is the per-user Run value `BotBeacons`. There is no admin rights, service or installer database.

## Security model

**Keys per install.** Pairing creates a fresh random 32-byte master key `K` and two fresh random relay topics (`bb-` + 32 hex characters): one for bot→PC and one for PC→bot. No two installs share anything. The link (server, topics, key) is stored in `settings.ini` encrypted with DPAPI (CurrentUser scope plus app entropy), so other Windows accounts and copied files can't read it. On the bot side, `beaconctl.py` keeps its copy in `secrets/` (folder 0700, files 0600) and never prints it.

**Every message:**

- `encKey = HMAC-SHA256(K, "bot-beacons enc v1")` and `macKey = HMAC-SHA256(K, "bot-beacons mac v1")`.
- The payload `{"id","ts","bot","text","title","from","re","kind"}` is encrypted with AES-256-CBC (random IV, PKCS7).
- `kind` from bots: empty (message), `attention`, or `probe` (1.1.3: a silent health check, log-only, with no bubble, glow, sound or wake). Anything else is treated as a normal message. From the app: empty (reply), `ping`, or `probe-ack` (the answer to a probe: `re` = the probe id and `ver` = the app version, at most one every 5 s). beaconctl and the router skip `probe-ack`, and it is never sent to the wake hook. 1.2.6: a probe may carry `prefs` (`"tabPictures=profile"`; only whitelisted keys and values, `;`-separated, at most 200 characters). The app applies it on the UI thread and its ack adds `applied` (`"tabPictures=profile"` or `"rejected: <reason>"`); an ack carrying `applied` skips the 5 s limit. Older apps ignore the field and ack as usual, which is how `beaconctl.py prefs` tells them apart (exit 2). `beaconctl.py probe --retry [N]` and the check after `pair` post a fresh probe id per try (15 s each) and accept an ack for any of them.
- `tag = HMAC-SHA256(macKey, "v1|" + d + "|" + iv + "|" + ct)` (encrypt-then-MAC). `d` is the direction: `b2p` (bot→PC) or `p2b` (PC→bot).
- The envelope is `{"v":1,"d","iv","ct","tag"}`.

**Message size** (1.1.6). ntfy.sh caps a message at 4096 bytes. A bigger body becomes a file attachment, which the receiver rejects as "too large for the relay (arrived as a file)", while the sender still gets HTTP 200. So both sides measure the final sealed envelope (title, JSON escaping, padding and base64 included) and refuse anything over 3900 bytes:

- **beaconctl** exits 1 with a clear "Not sent" message (and how many characters would fit) and posts nothing. It doesn't split, because one alert should be one pop-up; parts would arrive seconds apart (relay rate limit), could each be lost or reordered, and would each sound an alert. `echo` shortens its test answer to fit.
- **The app** checks replies before sending. The text stays in the box with "Too long to send: shorten it by about N%". The box no longer cuts text at 1500 characters.
- The payload JSON is UTF-8 as is (beaconctl 1.1.5 and older wrote `\uXXXX`, 6 bytes per accented letter and 12 per emoji; older apps read both forms). Roughly what fits: 2,700 plain English characters, 1,350 accented letters, 900 Chinese/Japanese characters or 680 emoji.
- Checks: `python3 tools/test_message_size.py` (offline) and `tools/SizeHarness.cs.txt` (Mono, including a round trip with the Python side; the header has the commands).

**Checks on receive**, in order. Failures are logged as "Rejected …" and never shown.

1. It is a v1 envelope.
2. The direction is right, so the app's own messages can't be reflected back.
3. The tag matches (constant-time compare).
4. It decrypts.
5. It is at most 15 minutes old and at most 2 minutes in the future.
6. Its id hasn't been seen. Seen ids persist, so replays fail even after a restart.

**Pairing** (ECDH P-256, authenticated by a one-time code):

1. The app shows a code like `K7QX2-MH9TD`: 10 characters from a 32-character alphabet, about 50 bits. It expires after 10 minutes and works once.
2. Both sides derive a rendezvous topic, `bbp-` + sha256("bot-beacons pair rv v1|" + code)[:40], and a code key, `ck = HMAC(code, "bot-beacons pair v1")`.
3. The app posts `hello {pk, mac=HMAC(ck,"hello|"+pk)}`.
4. The bot posts `join {pk, name, mac=HMAC(ck,"join|"+apppk+"|"+botpk)}`.
5. Both compute `z = SHA256(ECDH shared x)` and `K = HMAC(z, "bot-beacons pair key v1|"+code+"|"+apppk+"|"+botpk)`.
6. The app posts `done`: the server and both new topics, sealed with `K`.
7. The bot replies `ack`, sealed with `K`, which proves it has the same key. Only then does the app save the link.

The PC posts `hello` and then checks the rendezvous topic every 3 s until the code expires. Closing the code window doesn't stop it; only **Cancel pairing** or the 10-minute expiry does, and both are logged. Relay errors (`429`, 5xx, network) never end pairing on either side. Both sides back off and retry until the code expires, and every post (`hello`, `done`, `ack`) is retried the same way. When pairing completes, the relay stream reconnects at once on the new link. `beaconctl pair` waits until the code expires (or `--wait S`).

Someone on the relay who doesn't know the code can't produce a valid `join`. Even someone who does know it can't recover `K` from what was posted, because the key comes from ECDH. Re-pairing replaces the link and makes the old topics useless.

**Limits (honest list):**

- The relay can see *when* messages are sent and roughly how big they are, but not their content or which beacon they are for.
- There is one link per install, shared by all the user's bots on their shared computer. A beacon's id is inside the signed payload, so outsiders can't fake it, but the user's own bots trust each other.
- On the bot side the key sits in `secrets/link.json` (mode 0600) on that shared computer. Any of the user's bots, or a prompt-injected one with shell access, can read it and then send as any beacon or read replies. It can't make the app run anything: message text never opens links, and the agent id is URL-escaped into a fixed `grokbot://` link.
- Anyone using the user's unlocked Windows account can use the link, as with any desktop app.
- Message history is kept in memory only.
- The relay server is `https://ntfy.sh` unless `server=` is set in settings.ini before pairing (and `beaconctl.py pair --server` on the bot side). A self-hosted ntfy server must be version 2.22.0 or later: older versions are affected by CVE-2026-39087 (GHSA-pqhx-w72w-m393). Messages stay end-to-end encrypted and signed either way, but keep the server patched.

## Tabs skin and profile pictures (1.2.0)

**Windows.** Each tab, the shelf and the drag outline are `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE` windows painted with `UpdateLayeredWindow` (per-pixel alpha, so clicks go through transparent pixels). `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE` and `ShowWithoutActivation` is true, so nothing in the skin ever takes focus. The floating beacon windows stay hidden in this skin; their state (unread, attention, history, sounds) is untouched, and the tabs read it. A 40 ms timer drives pop-out depth, the attention pulse (still when Animations is Off), hover dwell (0.15 s) and the shelf's close delay (0.4 s; kept open while a menu, any of our dialogs (1.2.4 `ModalHold`: Edit tab, its image picker; reference-counted) or a bubble the pointer is over is up). Only one shelf is open at a time; it slides in 170 ms (out 130 ms) with an ease-out curve.

**Size and hover (1.2.1).** `TabLayout.Scale` is 1.1 / 1.4 / 1.7 for Small / Normal / Large. The tabs and the shelf are laid out in 1.2.0 base units and drawn with a scale transform. The Edit tab form is scaled by `UiScale` (1.0 / 1.3 / 1.55). With `tabHover=tab`, hovering a tab for 0.15 s slides out a single card for that tab (`ShelfModel.Card`). A grip window just past the end of each strip, and **Open shelf** on the tray and tab menus (1.2.3: Layout → **Open the whole shelf**), open the whole shelf. With `tabHover=shelf`, hovering opens the whole shelf as in 1.2.0. Either closes 0.4 s after the pointer leaves.

**Per-beacon layout, click to open, the conversation card (1.2.2).** `Skin.IsTab(b)` is `tabModes[b]` if set, else `skin=`. Floating beacons and tabs run side by side: `Visibility` shows a floating window only when the beacon isn't a tab, `TabsSkin.Sync` makes tabs only for beacons that are. While a floating beacon is dragged, `TabLayout.DocksFloating` (the pointer within `DockReach` = 24 px of the outer edge `Snap` picks, so seams never count) shows the dock outline; on release it saves monitor + edge + along and flips the beacon to a tab. A tab dropped more than `UndockReach` = 80 px from its snapped edge (`TabLayout.Undocks`) floats at `TabLayout.FloatAt` (centred on the pointer, clamped to that monitor). The picture sits at `TabLayout.ImageRect` (inset 10 base px from the tab's outer end, 30 px), and `Screens()` trims every `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` rect off the work areas (`TabLayout.TrimTaskbars`). `Theme.CreateBubble` returns a `TabCard` for tabs, so messages, attention, read state and the dev hooks go through the usual bubble path. It is a borderless top-most tool window shown without activation; `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE` except over the reply box and Send. It is placed beside its tab (`TabLayout.CardBeside`, past the alert depth) and slides a third of its size with a fade (150 ms out, 120 ms back). In click mode the 40 ms tick polls Esc (`GetAsyncKeyState`) and mouse presses outside the card, its tab, menus and the Edit window while the card hasn't focus. With focus, Esc and deactivation close it. Drafts are kept per beacon for the session.

**Docking.** `TabLayout.Snap` picks the monitor under the pointer (else the nearest) and the nearest edge of its `WorkingArea`. Since 1.2.1 it skips any edge that faces another monitor's work area: at least half of an 80 px strip beyond the edge overlaps another monitor. If every edge is a seam, all edges are allowed. `TabLayout.Outward` moves a tab saved on a seam to the outer edge on the same side. Dragging is a 15 ms poll of `Control.MouseButtons` and `Cursor.Position`, not mouse-capture events. On release, the tab is snapped to the monitor under the pointer, saved and laid out at once. A tab is saved as monitor device name + edge + `along` (0..1). Tabs on the same monitor edge are stacked in order without overlapping (`TabLayout.Stack`) and re-laid out when displays change. A monitor that is gone falls back to the primary one.

**Profile pictures.** `beaconctl.py avatar BEACON FILE` centre-crops to a square, shrinks to ≤ 96 px and compresses to ≤ 24 KB (Pillow), then sends `kind: "avatar"` messages, each sealed and signed like any other message, each with a fresh id and timestamp (so replay and staleness checks apply): `aid` (transfer id, hex), `seq`, `n` (≤ 32), `size` (≤ 32 KB), `sha256`, `data` (base64 of ≤ 1800 bytes, keeping the sealed message under ntfy's 4 KB inline limit). Since 1.2.2 every chunk is sealed with the 1.1.6 `seal_checked` (≤ 3,900 bytes) before anything is posted; if one is too large the chunk size drops by 200 bytes until all fit, and below 768 bytes nothing is sent. `clear: "true"` removes it. The app handles the kind before normal messages and stays silent (log line only). It keeps at most 6 transfers open, drops them after 10 minutes, checks size and SHA-256, decodes with image validation (8..1024 px), and re-draws it into a new 128 px PNG, so the saved file is never the bytes that were sent. Custom tab images go through the same re-draw (≤ 20 MB in, PNG or ICO, also JPG/BMP/GIF).

**Sound sets.** Custom = each beacon's own sounds (the 1.1.5 lookup); Classic = the Windows system sounds (Asterisk for messages, Exclamation for attention). The per-tab `sound` override picks a set for one beacon; per-tab Off is the 1.1.5 `soundMuted` list. Throttles and quiet hours are unchanged.

**Harnesses (Linux/mono).** `mcs -nowarn:649 -out:/tmp/th.exe -r:System.Drawing.dll -r:System.Core.dll src/TabLayout.cs src/TabRender.cs src/HitPad.cs tools/TabLayoutHarness.cs.txt && mono /tmp/th.exe [--render DIR]`: snapping, stacking, shelf placement and slide maths at all three sizes, on fixed cases, two and negative-coordinate monitors, the two-1920×1080 cross-monitor drops (each must land where its outline was), seams and random layouts; `--render` writes previews of the tabs on every edge and the shelf. 1.2.2 adds the dock (24 px) and undock (80 px) thresholds on the two-1920×1080 layout, with seams, the taskbar and a 4,000-point fuzz (never both), the picture inside the visible tab for every edge, size and depth (also checked on the rendered pixels), taskbar trimming, and the card beside its tab. 1.2.3 adds throw-to-dock impact points (outer edges only), seam dwell vs pass-through (hold still 0.5 s within 12 px to dock; moving on crosses), the settle wobble, beacon pictures and colour rings, and the click target pad. `tools/ClickHarness.cs.txt` (1.2.4; build lines at the top) checks the floating-beacon click rules, the solid hit pad on the real art, the dialog hold, the grouped shelf header and its tips, and, with `alerts` under `xvfb-run`, that every alert path sets the visual state. `tools/UpdateHarness.cs.txt` (fixtures from `tools/update_fixtures.py`) checks the updater's logic offline. `tools/AvatarHarness.cs.txt` feeds avatar chunks to `Avatars.Receive` (build line at the top of the file). `tools/ThemeHarness.cs.txt` (1.2.6) checks the theme merge and overlay rules, `BeaconConfig.Load` end to end in a temporary home, `beaconWord` and `trayIcon`. 1.2.6 adds to `ClickHarness`: middle-click vs click (`ClickRules.ForButton`, `MiddlePing`; on real beacons and tabs: a middle-click pings with the wiggle and throttle, never drags or opens the card, a left press cancels it), the Tab pictures rules, and GIF frame timing and import.

**Tab pictures and middle-click (1.2.6).** `tabs.json` entries may carry `imageBy: "user"` when the user picked the picture in Edit (written only when set; older apps ignore it). `TabPictures.OnAvatar` / `OnChoose` are the pure rules: `custom` is never changed; `profile` switches a tab to the bot's picture when one is saved (on arrival, `Avatars.Saved`, and for every tab when the setting is chosen); `beacon` keeps the beacon picture (a new bot's tab too); `ask` changes nothing (a new bot's tab still takes its picture, as in 1.2.5). Middle button: `ClickRules.ForButton` maps it to `PressKind.Ping`. A floating beacon captures the mouse and pings on release if the pointer stayed within the click slop and no left press came in between; tabs and shelf rows ping on release within 6 px, never while dragging. Both call `BeaconForm.PingClick`, the same as menu → Ping (30 s throttle, wiggle). Hover tooltips name the three clicks.

**Animated tab pictures (1.2.6).** `TabStore.ImportCustom` keeps a GIF with 2–150 frames (≤ 5 MB) as `tabs\<id>.gif` next to the usual 128 px `tabs\<id>.png` (its first frame); any other file removes the GIF. `TabGif` decodes the frames once at 96 px (delays from property 0x5100, read per frame when GDI+ gives only the active one; 0–1 cs = 100 ms) and caches them by file time. `FrameAt` is pure: Off = frame 0, Subtle = one loop then 8 s on frame 0, Full = loop; delays are clamped to `MinDelay(FrameClock.EffectiveFps)` (≥ 20 ms). `TabStore.FrameTag` is part of each tab's redraw signature, so the 40 ms tab timer redraws only on a frame change; while the session is locked it shows the first frame. A PNG newer than the GIF (an older version picked a still) wins. Avatars from bots stay PNG.

**Themes across versions (1.2.6).** On a `beacons.json` version bump `BeaconConfig.Load` backs up the user's file as before, then `ThemeMerge.Upgrade(user, base, shipped)` merges it three ways against `beacons.shipped.json` (the embedded list, written whenever its version matches). Top-level keys: user change wins, untouched takes the new value, deleted stays deleted, new keys are added, objects recurse. Beacons are matched by `id`; the look fields move as a group; new shipped beacons are added only if all base beacons are still there. No base: the user's beacons are left as they are. `"upgrade": "keep"` moves only the version. Unparseable user file: the 1.2.5 behaviour. `ThemeMerge.Overlay` then applies `beacons.user.json` in memory (never written). `BeaconConfig.Root` is the merged document (`Theme` reads the bubble style from it). `trayIcon` is a plain file name looked up like art (≤ 1 MB; .ico via `Icon`, falling back to the PNG-compressed entry; .png drawn at 32 px); `beaconWord` (1–24 printable characters) feeds the Pause/Test menu labels.

**Attention badge geometry (1.2.5).** Nothing is ever cut off. A tab window is `BaseWinDepth` × `BaseWinLength` = 86 × 70 base units (was 80 × 66). The drawn depth is clamped to `MaxDepthBase` = 76: `TabsSkin` caps `DepthNow` at `TabLayout.MaxDepth`, and the throw/settle wobble can no longer push the body past the window. The "!" badge (r 8.5 + 3 halo) sits 4 units in from the body's outer end and 5 in from its side (`BadgeCentreBase`), on the corner away from the screen edge, overlapping the frame. `TabLayout.BadgeRect` / `GlowRect` give its exact extent and the halo/wave extent. The harness checks both are inside the window (with a 1 px gap) for every size, DPI 1–3, edge and depth from the settle dip to past the maximum, and inside each monitor's work area after stacking. It also checks the rendered pixels. A floating beacon's window is the art plus a transparent `BeaconForm.Pad` = 32 px on every side. Drawing is translated by Pad (`PaintFrame`), so the badge (`BadgeBox`), the red aura (`AuraBox`) and ±12° tilts stay inside it. `KeepOnScreen` keeps `VisibleBox` (art, badge and aura) on the monitor. Positions are still saved as the art's top-left, so old `settings.ini` positions load unchanged.

**New bots (1.2.5).** There is no in-app tab maker (the "+ Add a tab" pane, the New tab dialog and Bots → Add a tab… are gone). `Relay.Process` sets `PetMessage.Verified` only after `Crypto.Open` (signature, decryption) and `CheckFresh` (age, replay) pass. Local dev-hook messages and user replies are never verified. `Program.Route` sends a message for an id with no beacon to `NewBots.Offer`. Its pure `Decide` rule asks only for a verified, non-user message with a `TabStore.SafeId` id that isn't declined or already pending, with at most 3 prompts at once. Anything else goes to `Program.RouteFallback` (the default beacon, title tagged `[id]`). While a prompt is up, more messages for that id are kept (up to 20). `NewBotPrompt` is non-modal, top-most, has no taskbar button and doesn't take focus (`ShowWithoutActivation`). Add is its `AcceptButton`. **Add** saves a `UserTab` (name from the optional payload `name`, else the id made readable; colour from the id), sets the image to the bot's profile picture if `Avatars` has one, calls `Program.AddBeacon` and delivers the kept messages. **Not now** or closing the prompt routes them to the fallback and declines the id until restart (as does Remove tab). Harness: `ClickHarness alerts` → `NewBotTests`.

## Updates (1.2.3)

With **Updates: Notify** (default), the app asks `https://api.github.com/repos/SnazzyGaz/bot-beacons/releases/latest` 30 s after start-up and then about every 12 hours, or when you click **Check for updates now**. These and the download below are its only network calls apart from the relay. It sends a `BotBeacons/<version>` user agent and nothing else. Offline, 404 (no releases) and rate limits (403/429) only update the status line, or show a balloon after a manual check. Tags are compared as semantic versions (a pre-release is older than its release; drafts, pre-releases and tags that aren't versions are never offered).

If the release is newer, the menu shows **Update available vX** and a balloon appears once per release. Nothing installs without a click. **Update now** asks first, then:

1. It picks the release's `*-win.zip` (a name without "test" first). The asset's URL must be `https://github.com/SnazzyGaz/bot-beacons/releases/download/…`, and redirects are followed by hand (at most 5) only to github.com or `*.githubusercontent.com` over https.
2. It downloads `<zip>.sha256`, `SHA256SUMS` or `checksums.txt` if there is one, then the zip (≤ 60 MB).
3. It checks the size against the API, the SHA-256 against the checksum, and that the zip is valid. Every entry is extracted with its CRC checked; zip64, encrypted entries and `..` paths are refused. It takes the shallowest `BotBeacons.exe` outside `source/`, which must be at least 64 KB and start with `MZ`.
4. It writes the new exe to `%TEMP%\BotBeacons-update\` with `apply-update.cmd`, starts the script hidden and exits. The script waits until the exe can be written (up to about a minute), renames the old one to `BotBeacons.exe.old`, copies the new one in (restoring the old one if that fails), starts it and deletes itself. The next start removes `.old`.

Only the exe is replaced. `%APPDATA%\BotBeacons` (settings.ini, the DPAPI-protected link, tabs.json, avatars) is never touched.

## Waking bots on reply

After a reply has been posted to the relay, the app POSTs a wake notice to the router routine's webhook. The notice carries no text. Replies within 10 seconds are combined, and a failed request is retried after 5 s, 20 s and 60 s on network errors, 5xx or 429. On 401/403 it stops and logs "check the key". The log shows only the host and HTTP status.

- Request: `POST`, `Content-Type: application/json`, `Authorization: Bearer <key>` and `X-Automation-Key: <key>` (the Grok Bot routine webhook format). Other sign-in styles can be chosen in the dialog: Bearer only, or an HMAC signature in `X-Beacon-Signature: sha256=<hex>`.
- Body: `{"type":"beacon-reply","v":1,"beacons":[…],"ids":[…],"count":N,"pings":[…],"first_ts":…,"last_ts":…,"sent_ts":…}`. The dialog's **Test** button sends `beacon-wake-test`.
- Setup: right-click → **Bots** → **Wake hook…**, then paste the routine's Webhook URL and Sender key (the key on its own, as `Bearer …`, or as the whole header line). Leave "Applies to" as *All beacons*, click **Test** ("Test OK (HTTP 200)"), then **Save**. Both values are stored encrypted with DPAPI (`wake=`).
- **No hook saved (1.2.6).** When the app is paired but `Wake.AnySet` is false (no default and no per-beacon hook), replies and pings still go to the relay but nothing is woken. The app says so without nagging: the tray tooltip ends in " - wake hook not set" (when there's room and the relay is connected), the Bots menu item reads **Wake hook… (not set)**, and after a reply the bubble, cloud or tab card status reads "Sent, but no wake hook: Bots → Wake hook…" (`Wake.SentStatus`; the tab card stays up 3 s instead of 0.7 s so it can be read). `Program.WakeNotSet` shows one tray balloon per run at most: right after pairing, or on the first reply or ping with no hook. Clicking it opens the Wake hook dialog. Setup treats a real ping as the test (SETUP.md, step 8, "Ping test").

See [ROUTER.md](ROUTER.md) for the routine.

## Relay connection

The app keeps one long-lived ntfy stream open (`GET <server>/<down topic>/json?since=<last message time>`) and handles each message as it arrives. ntfy sends a keepalive about every 45 s. If nothing arrives for 100 s, the app reopens the stream. On network errors it backs off 5, 10, 20, 40 and then 60 s, and reconnects at once when Windows reports the network is back. One stream costs ntfy.sh one request per reconnect. Polling every 5 s instead would hit ntfy.sh's per-IP limit of roughly one request per 5 s and get `429 Too Many Requests`, especially with a second ntfy app on the same PC. If a proxy breaks streaming, set `relayMode=poll` (and optionally `pollSeconds=15`, minimum 5) in `settings.ini` while the app is closed.

## Building

`build.bat` uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, which is on every Windows 10/11 PC, so no SDK is needed. It embeds every `art\*.png` and `beacons.json` and writes `dist\BotBeacons.exe` (about 200 KB).

Build steps on any Windows 10/11 PC:

1. Unpack the package (or `bot-beacons-src.zip`) into a folder.
2. Edit `src\*.cs`, `art\*.png` or the default `beacons.json`. The code is C# 5 for the built-in compiler: no `$"..."` interpolation, `?.`, `nameof` or expression-bodied members.
3. Run `build.bat`. Compiler errors are printed with file and line. The result is `dist\BotBeacons.exe`.
4. Install over the current copy, keeping settings and pairing: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1 -Source .` (or close the app and copy `dist\BotBeacons.exe` to `%LOCALAPPDATA%\Programs\BotBeacons\` yourself).

**Building a real exe on a Linux box (1.2.0 test builds).** `mcs` can compile against the genuine .NET Framework 4.8 reference assemblies (NuGet package `Microsoft.NETFramework.ReferenceAssemblies.net48`) instead of Mono's: `mcs -noconfig -nostdlib -langversion:5 -target:winexe -optimize+ -win32icon:art/icon.ico -r:<refs>/mscorlib.dll -r:<refs>/System.dll -r:<refs>/System.Windows.Forms.dll -r:<refs>/System.Drawing.dll -r:<refs>/System.Web.Extensions.dll -r:<refs>/System.Security.dll -r:<refs>/System.Core.dll -resource:art/X.png,X.png … -resource:beacons.json,beacons.json src/*.cs`. That exe uses the real CNG classes on Windows. `build.bat` on the PC is still the reference build.

To check that the code compiles on a Linux box, run `sh tools/mono-check.sh`. It runs Mono's `mcs -langversion:5` with the same references, plus `tools/MonoCngStub.cs.txt` standing in for the Windows-only CNG classes. It is only a syntax and type check. The real build is `build.bat` on Windows.

Command line: `BotBeacons.exe --startup` (used by the Run entry: no pairing window at login) and `BotBeacons.exe --autostart on|off` (sets Start with Windows and exits; used by the installer).

## Developer hooks (off by default)

**Click log (1.2.4).** `clickLog=1` in `settings.ini` (app closed) or `BOTBEACONS_CLICKLOG=1` in the environment writes `clicklog.txt` (capped at 256 KB) in the settings folder: each press on a floating beacon with its screen and window position, how far the pointer moved, the time held and the decision (Click, Drag, Catch, TooLong; then Open, Close or KeepOpen). Off by default; it contains no message text.

Developer hooks are for testing only and are **off** in every install. They turn on only when a developer adds `devHooks=1` to `settings.ini` while the app is closed. With them on, a single command written into `%APPDATA%\BotBeacons\control.txt` is run within a second. Commands older than 10 seconds at start-up are ignored. Never leave this on for everyday use: anyone who can write to your profile folder could then drive the app.

| Command | Effect |
|---|---|
| `open <id>` / `click <id>` / `ping <id>` | open the bubble without focus / act like a left click / ping |
| `test <id>` / `attention <id> <reason>` | local glow / local attention test (no relay) |
| `reply <id> <text>` / `type <id> <text>` / `key <id> esc\|enter\|dismiss` / `dismiss <id>` | drive the bubble |
| `hide <id>` / `show <id>` / `hideall` / `showall` / `pause on\|off` / `trayclick` / `trayicons` | visibility, as in the menus (`trayicons` saves the badge icons to `snaps\`) |
| `pair` | start pairing without taking focus and write `pair-code.txt` (one-time, 10 min) |
| `status` | write `status.txt` (paired yes/no, unread counts, positions; no secrets) |
| `snap` / `frames <id> <n> <ms> [action]` | save the beacons' own frames (never the desktop) |
| `bubble card\|cloud`, `anim full\|subtle\|off`, `act <id> <action>`, `fling <id> <vx> <vy>`, `autostart on\|off`, `showtest on\|off`, `netsim <s>` | settings and animation tests |
| `wake-dialog`, `wake-snap`, `wake-test`, `wake-clear`, `wake-set` | wake hook tests (`wake-set` reads and deletes `wake-set.txt`: url, key, [auth], [beacon id]) |
| `openapp <id>` | run Open in chat (also `key <id> openchat`) |
| `fps 30\|60\|monitor`, `clock legacy\|auto`, `tune <subSteps> <swayStep> <fastCopy>` | frame clock settings |
| `frametimes <s>` | frame interval and render stats for s seconds, written to `frametimes.txt` |
| `flingtrace <id> <vx> <vy> [classic]` | dry-run fling (nothing moves) logged frame by frame to `flingtrace_<id>_*.txt` |
| `flingcatch <id> [classic]` | dry-run catch test: six simulated catches of a sliding beacon, frames to `flingcatch_<id>_*.txt`, summary in the log |
| `skin beacons\|tabs`, `soundset custom\|classic` | 1.2.0 skin and sound set |
| `shelf <id>` / `edittab <id>` / `tabsnap` | 1.2.0: open the shelf for that tab's edge / open Edit tab / save the tab and shelf images the app draws to `snaps\` |
| `card <id>` / `openshelf` / `tabsize small\|normal\|large` / `tabhover tab\|shelf` | 1.2.1: slide out that tab's card / open the whole shelf / Tab size / Hover opens |
| `layout floating\|tabs` / `mode <id> tab\|float` / `tabopen click\|hover` | 1.2.2: all floating or all tabs / one beacon's layout / Open tabs on |
| `throwdock on\|off` / `update check` | 1.2.3: Throw to dock / run an update check now (result in the log and status.txt) |
| `exit` | quit cleanly |

## Frame rate and smoothness settings

Right-click a beacon → **Skin** → **Animations** → **Frame rate: 30 fps / 60 fps / match display (N Hz)**. These and a few tuning keys live in `settings.ini`:

| Setting | Values | Default |
|---|---|---|
| `fps` | `30`, `60`, `monitor` | `60` (Subtle and Off are capped at 30) |
| `frameClock` | `auto`, `legacy` (the old 25 fps timer, whole pixels) | `auto` |
| `subSteps` | sub-pixel steps per pixel, 1-64 | `4` |
| `swayStep` | degrees of sway before a redraw, 0.005-1 | `0.1` |
| `fastCopy` | `1` = cached sprite + bilinear sampler, `0` = GDI+ for every frame | `1` |
| `relayMode` / `pollSeconds` | `stream` or `poll`; poll interval (minimum 5) | `stream` / `15` |

If a slow PC struggles, choose 30 fps or set `frameClock=legacy`.

## Performance

Since 1.1.0 all beacons share one frame clock (`FrameClock.cs`) paced by the display compositor (DwmFlush), with a high-resolution waitable timer as the fallback, at 60 fps by default (30 fps on Subtle and Off). Motion is time-based, so speed doesn't depend on the frame rate. Each beacon's window sits on a whole pixel and the sprite is drawn at the sub-pixel remainder (snapped to 1/4 px) with a bilinear sampler (`FastBlit.cs`) from a cached, pre-composed sprite, so a slow bob glides instead of stepping. A fling decays smoothly all the way to rest, and the bob, sway and wander fade out while you drag or fling and fade back in afterwards, so nothing jumps. Each beacon keeps a single preallocated 32-bit DIB, alert frames pre-blended at 16 brightness levels, and skips frames that wouldn't change what you see. With full animations, a few beacons use about 3% of one core and roughly 60 MB of memory. Subtle and Off use close to nothing. Hidden beacons don't draw at all.
