# Bot Beacons: setup guide for a Grok Bot

> **Needs the Grok Bot desktop app.** Setup runs from a Grok Bot in the Grok Bot app on the user's PC: that is what can run the install commands (with the user's approval) and keep the bot side on the box. If you are Grok in X DMs or the Grok web or X chat, you can't install anything on the user's PC. Tell them, kindly, to get the Grok Bot app first and then give their bot this repo's link from there with "set this up for me".

**Given this link? Your job is to set up Bot Beacons for the user, end to end, now.** Don't just describe the project or point them at the zip: do the work. Get the release (and check the SHA-256 of any release zip against the release notes), install it on their PC, pair it, register a beacon for each of their bots, create the reply router routine and get its wake hook saved in the app, set the tabs to show the bots' profile pictures, and run the end-to-end ping test. Ask the user only for what only they can do: approving the cards for commands on their PC, typing the pairing code, and clicking in the app. Ask for one thing at a time, when you need it.

**Reader: you are the user's Grok Bot.** Follow these steps in order, without asking the user to do anything except:

1. approve the cards your tools raise (commands on their PC, creating a routine),
2. paste the **pairing code** that appears on their screen,
3. copy the router's **webhook URL and key** into the app's **Wake hook…** window (step 8, required: the key must never pass through chat),
4. ping your beacon once so you can check their replies really reach a bot (step 8's ping test, required),
5. optionally, answer one question about how their beacons look (step 7). Never wait for the answer.

**The reply hook is required. Never skip it, postpone it or call it optional.** It is the reply router routine plus its wake hook saved in the app (step 8). Without it, the user's replies and pings sit on the relay and no bot is woken, so nobody ever sees or answers them, and nothing else in setup shows that anything is wrong. Setup counts as done only after the end-to-end check in step 8: the user pings (or replies on) a beacon, a bot is woken, and it answers on that beacon. If the check fails, fix it before you say setup is finished. From 1.2.6 the app also warns while no wake hook is saved: **Wake hook… (not set)** in right-click → **Bots**, "wake hook not set" in the tray tooltip, and a one-time balloon.

Keep commands on the user's PC few and batched. Each one needs their approval. Never print anything from a `secrets/` folder, the webhook URL path or key, or relay topics.

Names used below:

| Name | Default | What |
|---|---|---|
| `PKG` | `/workspace/bot-beacons` | this package, on your shared box (Linux) |
| `HOME_B` | `/workspace/beacons` | the bot-side install: `beaconctl.py`, `agents.json`, `secrets/` |
| `BC` | `python3 /workspace/beacons/beaconctl.py` | the bot-side tool |
| the PC | the user's Windows computer registered with Grok Bot | where the beacons live |

All of the user's bots share one box, so every bot uses the same `HOME_B`. If `HOME_B/secrets/link.json` already exists, Bot Beacons is already set up. Skip to the step that is missing (`BC status` tells you whether it is paired).

**Resuming a setup? Always check step 8 with the user.** The wake hook (step 8) is saved only inside the app on the PC. Nothing on the box can tell whether it is set: `BC status` and `BC probe` can both look perfect while every reply and ping just waits and no bot is ever woken. So even when everything else is done, ask the user whether they pasted the webhook URL and key into **Wake hook…** and clicked **Save** (app 1.2.6+ shows **Wake hook… (not set)** in the right-click → **Bots** menu while it's missing). Then run step 8's ping test. Setup isn't finished until that test passes.

---

## Step 1: Get the package onto the box

```bash
git clone <project link> /workspace/bot-beacons        # or download and unzip the release to the same path
cd /workspace/bot-beacons && ls README.md src tools art beacons.json build.bat
```

If you download a release zip (for example the ready-made `BotBeacons-<version>-win.zip` for step 6), check its SHA-256 against the one in the release notes first (`sha256sum <file>`). If it doesn't match, don't use it: download it again, or clone the repository instead.

## Step 2: Plan one beacon per bot

List the user's Grok Bot agents: yourself and any other bots you can see with your tools or memory. For each, note the **agent id** and **name**. If you can only see yourself, set up your own beacon now, and add the others later with steps 3–4 and 9.

Choose for each bot:

- `id`: short, lowercase, a-z 0-9 and `-` (for example `research`). It must not be `echo`, which is the test beacon.
- `look`: `orb`, `robot` or `cat`. If the user already said which they want, use that. Otherwise go round-robin (orb, robot, cat, orb, …) and don't ask yet: step 7 asks once, in the message you send anyway, when the beacons are already on screen.
- `color`: optional, as `#RRGGBB`.
- `purpose`: one line under the name.

## Step 3: Install the bot-side tool on the box

```bash
mkdir -p /workspace/beacons
cp /workspace/bot-beacons/tools/beaconctl.py /workspace/beacons/
[ -f /workspace/beacons/agents.json ] || cp /workspace/bot-beacons/tools/agents.json /workspace/beacons/
python3 -c "import cryptography" 2>/dev/null || pip install --user cryptography
# one line per bot from step 2 (--ping is optional, see step 10):
python3 /workspace/beacons/beaconctl.py agent-set research --agent-id <agent id> --name "Research Bot"
python3 /workspace/beacons/beaconctl.py agents
```

## Step 4: Build the user's beacons.json and the PC bundle

```bash
cd /workspace/bot-beacons
python3 tools/make_beacons.py -o /workspace/beacons/beacons.json \
  --beacon "id=research; name=Research Bot; look=robot; agent=<agent id>; purpose=Research and reports"
  # add one --beacon per bot
rm -f /workspace/beacons/bot-beacons-src.zip
python3 -m zipfile -c /workspace/beacons/bot-beacons-src.zip build.bat beacons.json src art sounds tools themes docs README.md LICENSE
```

## Step 5: Check the PC (approval 1)

Run this one PowerShell command on the PC. It changes nothing.

```powershell
"USERPROFILE=$env:USERPROFILE"; "csc=" + (Test-Path "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"); "installed=" + (Test-Path "$env:LOCALAPPDATA\Programs\BotBeacons\BotBeacons.exe"); "running=" + [bool](Get-Process BotBeacons -ErrorAction SilentlyContinue); [Environment]::OSVersion.VersionString
```

- `csc=False` means .NET Framework 4.x is missing (very rare on Windows 10 and 11). Tell the user and stop.
- `installed=True` means this is an upgrade. Carry on: the installer keeps pairing and settings.

## Step 6: Copy and install (approvals 2–3)

1. Copy both files from the box to the PC (one file at a time, with your copy-to-computer tool), into `<USERPROFILE>\Downloads\BotBeacons-setup\`:
   - `/workspace/beacons/bot-beacons-src.zip`
   - `/workspace/beacons/beacons.json`
2. Run the installer in one PowerShell command. Leave out `-ProjectsFolder` unless the user wants a projects folder (see step 11).

```powershell
$d = "$env:USERPROFILE\Downloads\BotBeacons-setup"; Expand-Archive "$d\bot-beacons-src.zip" "$d\src" -Force; powershell -NoProfile -ExecutionPolicy Bypass -File "$d\src\tools\install.ps1" -Bundle "$d\bot-beacons-src.zip" -BeaconsJson "$d\beacons.json" -AutoStart -ProjectsFolder "Bot Projects"
```

The installer:

1. builds `BotBeacons.exe` with Windows' own C# compiler,
2. installs it to `%LOCALAPPDATA%\Programs\BotBeacons\`,
3. puts the beacons in `%APPDATA%\BotBeacons\beacons.json`,
4. turns on **Start with Windows** (per-user Run value `BotBeacons`) and adds a Start menu shortcut,
5. starts the app.

It needs no admin rights and touches nothing else. If the build fails, show the user the last lines of output and stop.

**Ready-made exe instead (optional).** Building on the PC is the default. If the user prefers the release's prebuilt exe (README → Download), or already downloaded it, run its `install.ps1` instead, adding `-BeaconsJson "$d\beacons.json" -AutoStart` (nothing is compiled). Before they run it, or before you do, send:

> Heads-up: the ready-made BotBeacons.exe isn't code-signed, so Windows SmartScreen may show "Windows protected your PC" the first time it runs. Choose **More info**, then **Run anyway**. If you'd rather not, I can build it from the source on your PC instead.

An exe built on the PC by the command above normally doesn't trigger SmartScreen.

## Step 7: Pair (the user pastes one code)

On first start, the app shows a small **Pair Bot Beacons** window with a code like `K7QX2-MH9TD`. It is valid for 10 minutes. Send the user one message:

> Bot Beacons is installed. A small window on your screen shows a pairing code. Please paste that code here. (No window? Right-click any beacon → **Bots** → **Pair with bots…**.)
>
> Optional: each beacon can be an orb, a robot or a cat. I picked: Research Bot = robot, News Bot = cat. Tell me any changes, or ignore this and I'll keep these.

Fill in the real names and looks from step 2. Leave out the looks paragraph if the user already chose. If you haven't already asked about the projects folder (step 11), ask in the same message. Don't wait for answers to these questions: carry on as soon as the code arrives. If the user asks for different looks, apply them after pairing without a rebuild (see "Customising and rebuilding": regenerate beacons.json, copy it to the PC, restart the app).

When the code arrives:

```bash
python3 /workspace/beacons/beaconctl.py pair <CODE> --name "<your name>"
python3 /workspace/beacons/beaconctl.py status
python3 /workspace/beacons/beaconctl.py send <your-beacon-id> "Setup test: hello! I'm your beacon. I'll glow when I have something for you."
```

From 1.2.6, `pair` checks the new link by itself: a few seconds after "Paired." it probes up to 3 times over about 45 seconds (the app can take a moment to switch to the new link) and prints `probe OK` or `probe FAILED after pairing`. If it failed, run `BC probe --retry` (the same 3 tries); if that fails too, see Troubleshooting. `pair --no-probe` skips the check. With an older `beaconctl.py`, run `BC probe` after `pair` instead.

This hello is the one test send at setup, and it is labelled "Setup test:". After an app upgrade or reinstall, don't send another: check the link silently with `probe`, which shows nothing on the desktop.

`pair` waits until the code expires (10 minutes after it was shown) and retries by itself when the relay is busy (`429`), so let it run. The user can close the pairing window after copying the code: the app keeps waiting in the background, and right-click → **Bots** → **Re-pair with bots…** shows the same code again while it is still valid (it also has a **Cancel pairing** button). When it works, the window (if open) says "Paired" and your beacon glows. If `pair` still times out, ask the user for a fresh code (right-click a beacon → **Bots** → **Re-pair with bots…**). Run `pair` once per code: a second `join` with the same code is ignored. Pair once per PC. All bots share this one link through `HOME_B`.

## Step 8: Reply hook and router routine (required; one bot only; the user copies one URL and key)

Pick **one** router bot. Use yourself unless the user prefers another. Fill in [ROUTER-PROMPT.md](ROUTER-PROMPT.md):

- `{{BEACONCTL}}` = `python3 /workspace/beacons/beaconctl.py`
- `{{SELF_BEACON}}` = the router bot's own beacon id

This step is required, and you can't skip it, leave it for later or offer it as an extra: without it, nobody sees or answers the user's replies and pings.

Create a routine owned by the router bot with a **webhook trigger** and that prompt (the user approves the card). If you can't create routines yourself, get it created now: ask another of the user's bots that can, or give the user the filled-in prompt and walk them through creating it. You may carry on with step 9 while that happens. Setup isn't finished until the routine exists, its URL and key are saved in the app, and the ping test below passes.

Name the routine **Bot Beacons reply router**. Once it exists, send the user this message (fill in nothing else; the key must never pass through chat):

> Last step, so your replies and pings reach your bots within seconds:
> 1. In the Grok Bot app, open the routine named **Bot Beacons reply router**. Its panel shows a **Webhook URL** and a **Sender key**.
> 2. Right-click any beacon on your desktop and choose **Bots** → **Wake hook…**.
> 3. Copy the **Webhook URL** from the routine into the **Webhook URL** box (the whole address, starting with https://).
> 4. Copy the **Sender key** from the routine into the **Sender key** box. Leave **Applies to** on "All beacons (default)" and **Sign-in style** on "Grok Bot routine (recommended)".
> 5. Click **Test**. Green text starting with **Test OK** means it works.
> 6. Click **Save**, then **Close**.
>
> If Test shows red text instead:
> - "Rejected (HTTP 401/403)": the key is wrong or was regenerated. Copy the Sender key again and test again.
> - "Not found (HTTP 404)": the URL is incomplete. Copy the whole Webhook URL again.
> - "Enter a full https:// URL" or "Enter the sender key": a box is empty or cut short.
> - Anything else: check your internet connection and try once more. If it still fails, tell me the red text (it never contains the key).
>
> Please don't paste the key here in chat.

If the user reports a 401/403 that persists, the key may have been regenerated after copying: ask them to copy the key currently shown on the routine. If the URL was never shown, the routine may not have a webhook trigger: check it and fix it yourself.

### Ping test (required: setup isn't done until this passes)

Without a saved wake hook, the user's replies and pings reach the box but no bot is ever woken, and nothing else in setup would show it. So always do this test right after step 8, including when you resumed a partial setup or the user says step 8 was done long ago.

1. Send the user:

   > One quick check so I know your replies reach me: right-click my beacon (**<your beacon name>**) and choose **Ping <your beacon name>**. That's all. I should answer on that beacon within about a minute.

   (Before app 1.2.3, a ping was a click on a beacon that isn't glowing.)
2. Wait about a minute: the app waits 10 seconds before it wakes the router, and the routine takes a moment to start. Don't answer the ping yourself in the meantime. Then look, without changing anything:

   ```bash
   python3 /workspace/beacons/beaconctl.py route --json
   ```

3. **Passed** when the ping was picked up and answered:
   - If you are the router, your beacon is no longer listed (the router answered and cleared the ping) and the user sees the answer on your beacon.
   - If another bot is the router, your beacon's `to_notify` is empty (the router passed the ping on) and you get its "your desktop beacon has new replies" message. Answer the ping as usual (`beaconctl.py pong <your-beacon-id>`, see AGENT-GUIDE.md).
   - If you're unsure, ask the user whether an answer appeared on the beacon.
4. **Failed** when, after a minute or more, the ping id is still in `to_notify` (or your beacon is still listed and nothing was answered). That means no bot was woken. Send step 8's message again, and add:

   > Your ping reached me, but it didn't wake anyone, so the wake hook isn't working yet. Please check:
   > - In **Wake hook…**, did you click **Save** after **Test OK**? Right-click a beacon → **Bots**: if it says **Wake hook… (not set)**, nothing is saved yet.
   > - **Applies to** is "All beacons (default)".
   > - The routine **Bot Beacons reply router** still exists and is switched on.
   >
   > Then ping my beacon once more (wait 30 seconds between pings).

   If the routine has no webhook trigger or is switched off, fix that yourself. Don't wake the bot by hand or mark the test ping handled just to make the test pass. Once the wake hook works, the next wake picks up the waiting ping too.
5. **The ping never shows up at all** (your beacon isn't listed and the user saw nothing): the ping didn't reach the box. Run `BC probe`. If it fails, repeat step 7.

Only when the test has passed may you send step 12's "Done!" message. You can do steps 9–11 while you wait.

## Step 9: Teach every bot its beacon

**Tab pictures first: set the bots' profile pictures.** Set it on the PC from the box (signed and silent, with no command on their PC), and tell the user in any message that their tabs will show each bot's profile picture, and that they can switch to the beacon's picture any time:

```bash
python3 /workspace/beacons/beaconctl.py prefs tab-pictures profile   # if the user asks: beacon, or ask (choose per tab)
```

Exit code 0 means the app applied it (`prefs OK: … applied`). 2 means the app is older than 1.2.6: leave it, the user can pick per tab later with the pencil. 1 means no answer: check `BC probe --retry` and try once more. The user can change it any time with right-click → Layout → **Tab pictures**. A tab with a custom image is never changed.

Each bot needs to know its beacon id and the guide. For **yourself**, save a skill, or a memory if you can't save skills, with this text. For **every other bot**, send it the same text (with its own id) as a message, asking it to save it as a skill or memory:

> **Bot Beacons:** you have a desktop beacon on the user's PC. Your beacon id is `<id>`. The tool is `python3 /workspace/beacons/beaconctl.py`, and the guide is `/workspace/bot-beacons/docs/AGENT-GUIDE.md`. Read the guide once now.
> - When you finish something or have news for the user, also send one line to the beacon: `beaconctl.py send <id> "…"`.
> - When the user must act in the Grok Bot app (approve a card, sign in, answer a question), use `beaconctl.py attention <id> "…"`.
> - When you get a priority "your desktop beacon has new replies" message, read them with `beaconctl.py read --unhandled --bot <id> --json`, answer each with `send <id> "…" --re <reply id>`, then `mark-handled <ids> --bot <id>`. For pings, run your ping action or `beaconctl.py pong <id>`.
> - Never send status-only lines ("still running", "health check OK", heartbeats). Health checks are silent: use `beaconctl.py probe`. Tell the user in chat only if it fails twice in a row, at least 10 minutes apart.
> - The user may use the **Tabs** skin. Send your profile picture once with `beaconctl.py avatar <id> /path/to/picture.png` (silent); the user's **Tab pictures** setting decides whether their tab shows it. See the guide, section 4.
> - Never print anything from `/workspace/beacons/secrets/`.

Check each one once, at setup only, by sending a labelled test line to its beacon yourself (`BC send <id> "Setup test: set up and listening."`), or ask the bot to send one. Don't repeat it later as a health check.

## Step 10: Ping actions (optional)

When the user pings a beacon (right-click → **Ping <name>**; before 1.2.3 a click on a quiet beacon), its bot is pinged. By default the bot answers "How can I help?". Give a bot its own action in one sentence:

```bash
python3 /workspace/beacons/beaconctl.py agent-set news --ping "Send the top headline from today's news digest, or 'All quiet.'"
```

Offer this in step 13's closing message. Don't block setup on it.

## Step 11: Start with Windows and the projects folder

- **Start with Windows** was turned on by `-AutoStart` in step 6. The user can switch it off with right-click → **Start with Windows**. To change it from the PC yourself, close the app and run `"$env:LOCALAPPDATA\Programs\BotBeacons\BotBeacons.exe" --autostart off` (or `on`).
- **Projects folder (optional):** a folder in the user's home directory where bots can drop finished work such as PDFs and reports. Create it only if the user wants it (default name `Bot Projects`; the installer's `-ProjectsFolder` does it, or `New-Item -ItemType Directory "$env:USERPROFILE\Bot Projects"`). Tell your bots about it in step 9's text.

## Step 12: Tell the user how to use it

Send this only after step 8's ping test has passed. If it hasn't, go back to step 8 instead: a "Done!" while replies can't wake anyone is exactly what to avoid. If the app still shows **Wake hook… (not set)** in right-click → **Bots**, setup isn't done. Then send one short message:

> Done! Each of your bots now has a beacon on your desktop.
> - It glows when that bot has something for you. Click to read and reply.
> - A red "!" means the bot needs you in the app. Every bubble has **Open in chat** (takes you there) and **Dismiss** links at the bottom.
> - Middle-click a beacon or tab (or right-click → **Ping**) to say hello to its bot. It answers on the beacon. Hover over one for a reminder of the clicks.
> - Drag beacons anywhere.
>   - Right-click a beacon for bubble style and animations (**Skin**), sounds, and **Bots** → **Hide**.
>   - Right-click → **Bots** → **Show or hide** is a checklist with Show all / Hide all, and double-clicking the tray icon pauses them all.
>   - Hidden beacons still collect messages, and the tray icon shows a dot when something arrives.
> - Prefer them tucked away? Drag or throw a beacon to the outer edge of your screen (or right-click → **Skin** → **All tabs**) and it becomes a tab. Click a tab for its card, click the grip for the whole shelf, drag a tab to any edge, and use the pencil to rename it or pick its picture (an animated GIF works too), colour and sound.

## Step 13: Tell the user it's all theirs to change

Send this right after step 12's message (or as its last part, to save a message), so also only after the ping test passed. Adjust the examples to the user's real bots:

> One more thing: Bot Beacons is completely open to you. The full source is included, so I can rebuild and reinstall it whenever you like.
> - **Looks:** any beacon can become anything at all: a different character, your own art, new animations.
> - **Beacons:** add one for a new bot, remove one, rename them.
> - **How it works:** behaviour, sounds, alerts, what a click or a ping does, or a whole new feature.
>
> Just ask in plain words, for example:
> - "Turn my research beacon into a small owl that blinks and ruffles its feathers when it has news."
> - "When I ping my news bot, send me today's top headline."
> - "Use a softer sound for alerts and add a beacon for my new travel bot."

When the user asks, follow "Customising and rebuilding" below.

## Step 14: Show them what's possible

Right after step 13 (it can be the same message), add one short friendly paragraph. Pick 2 or 3 ideas that suit the user and only offer what works today. For example:

> This setup is just a starting point. A beacon doesn't have to be a bot: I can run a small script that puts the morning weather on one, lights another up when your build passes, or nudges you to drink water every couple of hours. I can also restyle any beacon to look like whatever you like. Want me to set one up or give one a new look?

If they say yes, a script-driven beacon is just a scheduled script or routine on the box that runs `beaconctl.py send <id> "..."` (see [AGENT-GUIDE.md](AGENT-GUIDE.md); a new id asks the user to add its tab). For looks, follow "Customising and rebuilding" below. Anything that needs data from the PC itself, like CPU stats, needs an app change, so say so.

## Customising and rebuilding (when the user asks for changes)

- **Looks, names, colours, new beacons:** no rebuild. Regenerate with `tools/make_beacons.py` (or edit the JSON; see [THEMES.md](THEMES.md)), copy it to the PC as `%APPDATA%\BotBeacons\beacons.json`, put any new PNGs in `%APPDATA%\BotBeacons\art\`, then restart the app: close it (`Get-Process BotBeacons | Stop-Process`) and start `"$env:LOCALAPPDATA\Programs\BotBeacons\BotBeacons.exe"`. Positions, pairing and hide/show settings are kept. A new bot also needs `beaconctl.py agent-set <id> --agent-id <agent id> --name "<name>"` on the box and step 9's text. With app 1.2.5+, beacons.json is optional for a new bot: its first signed message asks the user "New bot <name> wants a tab. Add / Not now", and Add creates the tab. The bot should tell the user its id first.
- **Code changes:** edit `src/*.cs` on the box (C# 5, .NET Framework 4.x: no `$"..."` strings, no `?.`, no `nameof`). If Mono is installed, `sh tools/mono-check.sh` catches compile errors before you copy anything. Re-zip as in step 4, copy the zip to the PC setup folder and rerun step 6's install command without `-BeaconsJson` (reinstalling keeps settings and pairing). install.ps1 builds with `build.bat`; if the build fails, it prints the compiler errors.
- **On the PC itself** (the user or a bot with a shell there): in an unpacked package folder, `build.bat` makes `dist\BotBeacons.exe`, and `powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1 -Source .` builds and installs it in one go.
- Ask before changing anything the user didn't ask for, and keep `devHooks` off.

## Tabs skin (for reference, 1.2.0; 1.2.1 adds Tab size and Hover opens)

Right-click → **Skin** switches live between all floating and all tabs (each beacon can also be dragged to an edge or off it). Nothing changes for bots: the same ids, messages, attention calls, pings and replies. The only new thing a bot can do is send its profile picture for its tab (`beaconctl.py avatar <id> FILE`, silent); offer the user the choice between that and their own image (AGENT-GUIDE.md, section 4). From 1.2.5, a bot with no beacon gets one by sending to its own id: the PC asks "New bot <name> wants a tab. Add / Not now" (AGENT-GUIDE.md, "No beacon yet?"). The bot tells the user its id first.

## Hide and show (for reference)

The user can hide any beacon (right-click → **Bots** → **Hide**), or use right-click → **Bots** → **Show or hide** (with **Show all** / **Hide all**) and **Pause beacons**. Double-clicking the tray icon also toggles Pause. The choice is remembered across restarts. Nothing changes for bots. Messages to a hidden beacon still queue, and the tray icon shows unread messages and attention. You never need to unhide a beacon to send to it.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `BC status` says not paired | Repeat step 7 (the user right-clicks → Bots → Re-pair) |
| Sends work but replies and pings never reach bots | The wake hook isn't set, wasn't saved, or the routine is off: repeat step 8's message and its ping test. App 1.2.6+ shows **Wake hook… (not set)** in right-click → Bots, "wake hook not set" in the tray tooltip, and "Sent, but no wake hook" after a reply. Replies still wait safely, and `BC route` shows them (ids in `to_notify` were never passed on). |
| A beacon doesn't appear | Its id isn't in `%APPDATA%\BotBeacons\beacons.json`, or it is hidden (right-click → Bots → Show or hide, or Pause is on) |
| A message lands on the wrong beacon with the id in brackets | The id has no tab. With 1.2.5+, the user answered **Not now** to "New bot … wants a tab" (or the message wasn't signed through the relay). Restart the app and the bot's next message asks again. Or add the id to beacons.json with `make_beacons.py` and reinstall with `-BeaconsJson`. |
| `probe FAILED after pairing` or `BC probe --retry` fails | Is the app running on the PC (tray icon)? Is the PC online? Wait a minute and run `BC probe --retry` again. Still failing: the app may be paired with an older code; ask the user to right-click → Bots → **Re-pair with bots…** and repeat step 7. |
| `BC prefs` exits 2 | The app is older than 1.2.6 and ignores preferences (harmless). Update the app, or the user sets each tab's picture with the pencil. |
| The pairing window doesn't show at login | Intended: autostart never pops the pairing window. Use right-click → Bots → Pair. |

**Uninstall:** run `powershell -NoProfile -ExecutionPolicy Bypass -File "<setup folder>\src\tools\uninstall.ps1"` on the PC (add `-RemoveData` to delete settings and pairing). Then `BC unpair` on the box.
