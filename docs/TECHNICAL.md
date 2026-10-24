# Bot Beacons: technical notes

## Using it

- **Drag** a beacon to move it. Each one remembers its own position.
- **Glow** means unread messages. **Click** a glowing beacon to open its bubble. The bubble shows the beacon's name and purpose, the latest messages (up to 50 per session), a reply box (Enter to send) and "End-to-end encrypted" once paired.
- **Click away**, press Esc, or click the beacon again to close the bubble. Opening it clears the glow of normal messages.
- **Footer links:** every bubble (messages, ping answers, the reply view and attention calls, in both bubble styles) has two small links on its status line. **Open in chat** (Alt+O) opens that bot's chat in the Grok Bot app, marks the beacon read and closes the bubble. **Dismiss** (Alt+D) marks it read and closes the bubble without opening the app. The links are never focusable and clicking them doesn't activate the bubble or move focus. They turn amber while an attention call is pending.
- **Focus:** the app never takes keyboard focus on its own. Incoming messages and alerts only glow and never open a bubble. A bubble takes focus only when you open it yourself or click into its reply box.
- **Ping:** clicking a beacon that isn't glowing pings its bot. The beacon wiggles, the ping goes through the relay and wakes the router, and the bot answers with "How can I help?" or its own ping action. You can ping each beacon at most once every 30 seconds. To open a quiet beacon's bubble and type, right-click it and choose **Open**.
- **Needs attention:** a fast double-beat pulse with a white flare and a red **!** badge means a bot needs you to act in the Grok Bot app. The bubble shows the reason. Its **Open in chat** link opens the bot's chat through `grokbot://app/v1/agent?id=<agentId>` and brings the Grok Bot window to the front (or only brings it forward if the `grokbot:` link isn't registered). Attention is **sticky**: it survives clicks, closing the bubble and restarts (`attention.txt`). Only the **Open in chat** or **Dismiss** footer link ends it; Esc, clicking away, X and clicking the beacon leave it pulsing.
- **Hide / show:** right-click a beacon → **Hide this beacon**. The tray menu has:
  - a **Show beacons** checklist (ticked = shown, with unread or attention notes), plus **Show all** / **Hide all**
  - **Pause beacons (hide all)**, also toggled by double-clicking the tray icon. Un-pausing restores exactly the previous set.
  - a **Test beacons** submenu: "Show test beacons" plus a one-by-one checklist

  These are saved in `settings.ini` as `hidden=id1,id2` and `paused=0|1`. Hidden beacons keep running underneath. Messages and attention calls still arrive and queue (unread count, glow state), and the tray icon shows them: an amber dot for unread, a pulsing red "!" dot for attention. The tooltip says, for example, "Research Bot: 2 unread (hidden)". There are no balloons, sounds or windows, so nothing takes focus. A single click on the tray icon opens the first visible beacon with news, or brings back a hidden one that has news. Showing a beacon restores its glow, and its bubble if it was open (without focus).
- **Right-click menu** (1.1.3: built once per beacon and reused, owned by the beacon, and refreshed when it opens): open/ping/hide this beacon, the Beacons list (open one), Show beacons, Pause beacons, Test beacons, Bubble style, Animations, Start with Windows, Pair / Re-pair, Wake hook…, Test glow (local), Open settings folder, Exit.

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
art/            starter-pack PNGs + icon (embedded in the exe; overridable)
beacons.json    default beacons (embedded)
themes/starter/make_starter_art.py   draws art/
tools/          beaconctl.py (bot side), make_beacons.py, agents.json template, install.ps1, uninstall.ps1
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

Program files go in `%LOCALAPPDATA%\Programs\BotBeacons\` (from `tools/install.ps1`). Autostart is the per-user Run value `BotBeacons`. There is no admin rights, service or installer database.

## Security model

**Keys per install.** Pairing creates a fresh random 32-byte master key `K` and two fresh random relay topics (`bb-` + 32 hex characters): one for bot→PC and one for PC→bot. No two installs share anything. The link (server, topics, key) is stored in `settings.ini` encrypted with DPAPI (CurrentUser scope plus app entropy), so other Windows accounts and copied files can't read it. On the bot side, `beaconctl.py` keeps its copy in `secrets/` (folder 0700, files 0600) and never prints it.

**Every message:**

- `encKey = HMAC-SHA256(K, "bot-beacons enc v1")` and `macKey = HMAC-SHA256(K, "bot-beacons mac v1")`.
- The payload `{"id","ts","bot","text","title","from","re","kind"}` is encrypted with AES-256-CBC (random IV, PKCS7).
- `kind` from bots: empty (message), `attention`, or `probe` (1.1.3: a silent health check, log-only, with no bubble, glow, sound or wake). Anything else is treated as a normal message. From the app: empty (reply), `ping`, or `probe-ack` (the answer to a probe: `re` = the probe id and `ver` = the app version, at most one every 5 s). beaconctl and the router skip `probe-ack`, and it is never sent to the wake hook.
- `tag = HMAC-SHA256(macKey, "v1|" + d + "|" + iv + "|" + ct)` (encrypt-then-MAC). `d` is the direction: `b2p` (bot→PC) or `p2b` (PC→bot).
- The envelope is `{"v":1,"d","iv","ct","tag"}`.

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
- Anyone using the user's unlocked Windows account can use the link, as with any desktop app.
- Message history is kept in memory only.

## Waking bots on reply

After a reply has been posted to the relay, the app POSTs a wake notice to the router routine's webhook. The notice carries no text. Replies within 10 seconds are combined, and a failed request is retried after 5 s, 20 s and 60 s on network errors, 5xx or 429. On 401/403 it stops and logs "check the key". The log shows only the host and HTTP status.

- Request: `POST`, `Content-Type: application/json`, `Authorization: Bearer <key>` and `X-Automation-Key: <key>` (the Grok Bot routine webhook format). Other sign-in styles can be chosen in the dialog: Bearer only, or an HMAC signature in `X-Beacon-Signature: sha256=<hex>`.
- Body: `{"type":"beacon-reply","v":1,"beacons":[…],"ids":[…],"count":N,"pings":[…],"first_ts":…,"last_ts":…,"sent_ts":…}`. The dialog's **Test** button sends `beacon-wake-test`.
- Setup: right-click → **Wake hook…**, then paste the routine's Webhook URL and Sender key (the key on its own, as `Bearer …`, or as the whole header line). Leave "Applies to" as *All beacons*, click **Test** ("Test OK (HTTP 200)"), then **Save**. Both values are stored encrypted with DPAPI (`wake=`).

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

To check that the code compiles on a Linux box, run `sh tools/mono-check.sh`. It runs Mono's `mcs -langversion:5` with the same references, plus `tools/MonoCngStub.cs.txt` standing in for the Windows-only CNG classes. It is only a syntax and type check. The real build is `build.bat` on Windows.

Command line: `BotBeacons.exe --startup` (used by the Run entry: no pairing window at login) and `BotBeacons.exe --autostart on|off` (sets Start with Windows and exits; used by the installer).

## Developer hooks (off by default)

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
| `exit` | quit cleanly |

## Frame rate and smoothness settings

Right-click a beacon → **Animations** → **Frame rate: 30 fps / 60 fps / match display (N Hz)**. These and a few tuning keys live in `settings.ini`:

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
