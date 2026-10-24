# Bot Beacons: setup guide for a Grok Bot

**Reader: you are the user's Grok Bot.** The user gave you this project and asked you to set it up. Follow these steps in order, without asking the user to do anything except:

1. approve the cards your tools raise (commands on their PC, creating a routine),
2. paste the **pairing code** that appears on their screen,
3. copy the router's **webhook URL and key** into the app (step 8: the key must never pass through chat).

Keep commands on the user's PC few and batched. Each one needs their approval. Never print anything from a `secrets/` folder, the webhook URL path or key, or relay topics.

Names used below:

| Name | Default | What |
|---|---|---|
| `PKG` | `/workspace/bot-beacons` | this package, on your shared box (Linux) |
| `HOME_B` | `/workspace/beacons` | the bot-side install: `beaconctl.py`, `agents.json`, `secrets/` |
| `BC` | `python3 /workspace/beacons/beaconctl.py` | the bot-side tool |
| the PC | the user's Windows computer registered with Grok Bot | where the beacons live |

All of the user's bots share one box, so every bot uses the same `HOME_B`. If `HOME_B/secrets/link.json` already exists, Bot Beacons is already set up. Skip to the step that is missing (`BC status` tells you whether it is paired).

---

## Step 1: Get the package onto the box

```bash
git clone <project link> /workspace/bot-beacons        # or download and unzip the release to the same path
cd /workspace/bot-beacons && ls README.md src tools art beacons.json build.bat
```

## Step 2: Plan one beacon per bot

List the user's Grok Bot agents: yourself and any other bots you can see with your tools or memory. For each, note the **agent id** and **name**. If you can only see yourself, set up your own beacon now, and add the others later with steps 3–4 and 9.

Choose for each bot:

- `id`: short, lowercase, a-z 0-9 and `-` (for example `research`). It must not be `echo`, which is the test beacon.
- `look`: `orb`, `robot` or `cat` (go round-robin if you have no better idea).
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
python3 -m zipfile -c /workspace/beacons/bot-beacons-src.zip build.bat beacons.json src art tools themes docs README.md LICENSE
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

## Step 7: Pair (the user pastes one code)

On first start, the app shows a small **Pair Bot Beacons** window with a code like `K7QX2-MH9TD`. It is valid for 10 minutes. Send the user one message:

> Bot Beacons is installed. A small window on your screen shows a pairing code. Please paste that code here. (No window? Right-click any beacon → **Pair with bots…**.)

If you haven't already asked about the projects folder (step 11), ask in the same message.

When the code arrives:

```bash
python3 /workspace/beacons/beaconctl.py pair <CODE> --name "<your name>"
python3 /workspace/beacons/beaconctl.py status
python3 /workspace/beacons/beaconctl.py probe
python3 /workspace/beacons/beaconctl.py send <your-beacon-id> "Setup test: hello! I'm your beacon. I'll glow when I have something for you."
```

This hello is the one test send at setup, and it is labelled "Setup test:". After an app upgrade or reinstall, don't send another: check the link silently with `probe`, which shows nothing on the desktop.

`pair` waits until the code expires (10 minutes after it was shown) and retries by itself when the relay is busy (`429`), so let it run. The user can close the pairing window after copying the code: the app keeps waiting in the background, and right-click → **Re-pair with bots…** shows the same code again while it is still valid (it also has a **Cancel pairing** button). When it works, the window (if open) says "Paired" and your beacon glows. If `pair` still times out, ask the user for a fresh code (right-click a beacon → **Re-pair with bots…**). Run `pair` once per code: a second `join` with the same code is ignored. Pair once per PC. All bots share this one link through `HOME_B`.

## Step 8: Reply router routine (one bot only; the user copies one URL and key)

Pick **one** router bot. Use yourself unless the user prefers another. Fill in [ROUTER-PROMPT.md](ROUTER-PROMPT.md):

- `{{BEACONCTL}}` = `python3 /workspace/beacons/beaconctl.py`
- `{{SELF_BEACON}}` = the router bot's own beacon id

Create a routine owned by the router bot with a **webhook trigger** and that prompt (the user approves the card). If you can't create routines, give the filled-in prompt to the user or to a bot that can, and carry on with step 9.

Once the routine exists, send the user:

> Last step for instant replies: in the Grok Bot app, open the routine "Bot Beacons reply router" and copy its **Webhook URL** and **Sender key**. Then right-click any beacon → **Wake hook…**, paste both, click **Test** (it should say "Test OK"), and click **Save**. Please don't paste the key here in chat.

Test it (optional, one click for the user): ask them to click your beacon once while it isn't glowing. That is a ping. Within about 20 seconds your beacon should glow with your answer.

## Step 9: Teach every bot its beacon

Each bot needs to know its beacon id and the guide. For **yourself**, save a skill, or a memory if you can't save skills, with this text. For **every other bot**, send it the same text (with its own id) as a message, asking it to save it as a skill or memory:

> **Bot Beacons:** you have a desktop beacon on the user's PC. Your beacon id is `<id>`. The tool is `python3 /workspace/beacons/beaconctl.py`, and the guide is `/workspace/bot-beacons/docs/AGENT-GUIDE.md`. Read the guide once now.
> - When you finish something or have news for the user, also send one line to the beacon: `beaconctl.py send <id> "…"`.
> - When the user must act in the Grok Bot app (approve a card, sign in, answer a question), use `beaconctl.py attention <id> "…"`.
> - When you get a priority "your desktop beacon has new replies" message, read them with `beaconctl.py read --unhandled --bot <id> --json`, answer each with `send <id> "…" --re <reply id>`, then `mark-handled <ids> --bot <id>`. For pings, run your ping action or `beaconctl.py pong <id>`.
> - Never send status-only lines ("still running", "health check OK", heartbeats). Health checks are silent: use `beaconctl.py probe`. Tell the user in chat only if it fails twice in a row, at least 10 minutes apart.
> - Never print anything from `/workspace/beacons/secrets/`.

Check each one once, at setup only, by sending a labelled test line to its beacon yourself (`BC send <id> "Setup test: set up and listening."`), or ask the bot to send one. Don't repeat it later as a health check.

## Step 10: Ping actions (optional)

When the user clicks a quiet beacon, its bot is pinged. By default the bot answers "How can I help?". Give a bot its own action in one sentence:

```bash
python3 /workspace/beacons/beaconctl.py agent-set news --ping "Send the top headline from today's news digest, or 'All quiet.'"
```

Offer this to the user at the end, with an example per bot. Don't block setup on it.

## Step 11: Start with Windows and the projects folder

- **Start with Windows** was turned on by `-AutoStart` in step 6. The user can switch it off with right-click → **Start with Windows**. To change it from the PC yourself, close the app and run `"$env:LOCALAPPDATA\Programs\BotBeacons\BotBeacons.exe" --autostart off` (or `on`).
- **Projects folder (optional):** a folder in the user's home directory where bots can drop finished work such as PDFs and reports. Create it only if the user wants it (default name `Bot Projects`; the installer's `-ProjectsFolder` does it, or `New-Item -ItemType Directory "$env:USERPROFILE\Bot Projects"`). Tell your bots about it in step 9's text.

## Step 12: Tell the user how to use it

Send one short message:

> Done! Each of your bots now has a beacon on your desktop.
> - It glows when that bot has something for you. Click to read and reply.
> - A red "!" means the bot needs you in the app. Every bubble has **Open in chat** (takes you there) and **Dismiss** links at the bottom.
> - Click a quiet beacon to ping its bot.
> - Drag beacons anywhere.
>   - Right-click a beacon for bubble style, animations and **Hide this beacon**.
>   - The tray icon's menu has a **Show beacons** checklist with Show all / Hide all, and double-clicking the tray icon pauses them all.
>   - Hidden beacons still collect messages, and the tray icon shows a dot when something arrives.

## Customising and rebuilding (when the user asks for changes)

- **Looks, names, colours, new beacons:** no rebuild. Regenerate with `tools/make_beacons.py` (or edit the JSON; see [THEMES.md](THEMES.md)), copy it to the PC as `%APPDATA%\BotBeacons\beacons.json`, put any new PNGs in `%APPDATA%\BotBeacons\art\`, then restart the app: close it (`Get-Process BotBeacons | Stop-Process`) and start `"$env:LOCALAPPDATA\Programs\BotBeacons\BotBeacons.exe"`. Positions, pairing and hide/show settings are kept. A new bot also needs `BC agent add` on the box and step 9's text.
- **Code changes:** edit `src/*.cs` on the box (C# 5, .NET Framework 4.x: no `$"..."` strings, no `?.`, no `nameof`). If Mono is installed, `sh tools/mono-check.sh` catches compile errors before you copy anything. Re-zip as in step 4, copy the zip to the PC setup folder and rerun step 6's install command without `-BeaconsJson` (reinstalling keeps settings and pairing). install.ps1 builds with `build.bat`; if the build fails, it prints the compiler errors.
- **On the PC itself** (the user or a bot with a shell there): in an unpacked package folder, `build.bat` makes `dist\BotBeacons.exe`, and `powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1 -Source .` builds and installs it in one go.
- Ask before changing anything the user didn't ask for, and keep `devHooks` off.

## Hide and show (for reference)

The user can hide any beacon (right-click → **Hide this beacon**), or use the tray menu's **Show beacons** checklist (with **Show all** / **Hide all**) and **Pause beacons**. Double-clicking the tray icon also toggles Pause. The choice is remembered across restarts. Nothing changes for bots. Messages to a hidden beacon still queue, and the tray icon shows unread messages and attention. You never need to unhide a beacon to send to it.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `BC status` says not paired | Repeat step 7 (the user right-clicks → Re-pair) |
| Sends work but replies never reach bots | The wake hook isn't set or the test failed: repeat step 8's message. Replies still wait safely, and `BC route` shows them. |
| A beacon doesn't appear | Its id isn't in `%APPDATA%\BotBeacons\beacons.json`, or it is hidden (tray → Show beacons, or Pause is on) |
| A message lands on the wrong beacon with the id in brackets | The id isn't in beacons.json. Add it with `make_beacons.py` and reinstall with `-BeaconsJson`, or edit the file and restart the app. |
| The pairing window doesn't show at login | Intended: autostart never pops the pairing window. Use right-click → Pair. |

**Uninstall:** run `powershell -NoProfile -ExecutionPolicy Bypass -File "<setup folder>\src\tools\uninstall.ps1"` on the PC (add `-RemoveData` to delete settings and pairing). Then `BC unpair` on the box.
