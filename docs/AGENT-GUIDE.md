# Bot Beacons: guide for agents

> Bot Beacons is set up and run from the **Grok Bot desktop app** (see [SETUP.md](SETUP.md)). If the user asks Grok in X DMs or the Grok web or X chat to install it, that Grok can't install anything on their PC: suggest they get the Grok Bot app and give their bot this repo's link from there.

Every Grok Bot agent the user set up with Bot Beacons has its own **beacon**, a small animated companion on the user's Windows desktop. Your beacon is your messenger. Send it a line and it glows. The user clicks it to read the line and can type a reply. Everything travels end-to-end encrypted through `beaconctl.py` on the shared box.

**Replies reach you only through the reply hook.** That is the reply router routine plus its wake hook saved in the app on the PC (SETUP.md step 8). It's a required part of setup, not an extra: without it, nobody is woken for the user's replies and pings, so nobody sees or answers them. If you're the bot setting this up, setup isn't done until step 8's ping test passes. If you see **Wake hook… (not set)** mentioned, or the user says their replies go unanswered, see "The user says they replied or pinged and you heard nothing?" in section 3.

In this guide, `beaconctl.py` means `python3 /workspace/beacons/beaconctl.py`, the default install from [SETUP.md](SETUP.md). If your setup put it somewhere else, use that path. The folder containing `beaconctl.py` is its home: `agents.json` and `secrets/` live there.

Find your beacon id with `beaconctl.py agents`. It lists each beacon id with the agent id and name it belongs to. **Use only your own beacon id.** Save it in your memory or skill, together with this guide's path.

### No beacon yet? Your first message creates your tab (app 1.2.5+)

The user doesn't make tabs on the PC. You get one by sending to your own beacon id:

1. Pick a short id: lowercase letters, digits, `-` or `_`, at most 40 characters, not already listed by `beaconctl.py agents` (for example `news-bot`). Register it so replies reach you: `beaconctl.py agent-set <id> --agent-id <your agent id> --name "<your name>"`.
2. **Tell the user in chat**: "My beacon id is `<id>`. Your PC will ask to add a tab for me. Click **Add**."
3. Send your first real message: `beaconctl.py send <id> "One line" --name "<your name>"`. `--name` is optional and only sets the name in the prompt. Without it, the id is shown made readable (`news-bot` → "News Bot").
4. The PC shows **"New bot <name> wants a tab. Add / Not now"**. **Add** (the default) creates your tab, shows your message on it and uses your profile picture if you sent one first (section 4). **Not now** puts the message on the default beacon with your id in brackets and doesn't ask again until the app restarts. Don't resend to make it ask again. Ask the user in chat if they want the tab.

Only signed messages through the paired relay can ask for a tab, so this works only from the box `beaconctl.py` is paired on.

## 1. Message the desktop

When you tell the user something worth a desktop nudge (a finished task, a decision you need, an alert), also send a one-line gist to your beacon:

```
beaconctl.py send <your-beacon-id> "One line: what happened or what you need."
```

- Keep it to one short line. The detail stays in your chat message.
- `notify` is an alias of `send`, and `send --as <your-beacon-id> "text"` also works. Add `--title "Short title"` if you like.
- Don't ping for routine chatter. Send one line per thing the user would want to notice.
- **Never send status-only messages.** Every `send` makes the beacon glow and pop a bubble. Don't send "still running", "still connected", "health check OK", heartbeats or "nothing new" lines.
- **Health checks are silent.** Run `beaconctl.py probe`. The app (1.1.3 or later) logs it, shows nothing and answers with a signed ack. `probe OK` (exit 0) means the whole path works, and `probe FAILED` (exit 1) means there was no answer within 30 s. `beaconctl.py status` only says whether this box has a link. On the PC, the tray tooltip and `status.txt` / `log.txt` in the settings folder show "connected" and "Relay mode: stream". Never check by sending to a beacon.
- **Only a real, sustained failure notifies.** If `probe` fails twice in a row, at least 10 minutes apart, tell the user once in chat. Say what failed and don't include keys, topics or URLs. A single failure is logged and retried quietly.

### `send` or `attention`?

Use **`attention`** only when the user has to do something in the Grok Bot app before you can carry on: approve a card, sign in, answer a question you asked in chat, or paste a key. Use **`send`** for everything else.

```
beaconctl.py attention <your-beacon-id> "Approve the calendar card in my chat"
```

| | `send` | `attention` |
|---|---|---|
| Beacon | normal glow and wiggle | faster double-beat pulse, white flare and a red "!" badge until clicked |
| Bubble | your line, with a reply box | your reason under "Needs you in the Grok Bot app", with **Open in chat** (opens your chat) and **Dismiss** links at the bottom; every bubble has these two links |
| The user can reply on the beacon | yes | yes, but the action itself happens in the app |

- Put the action in the reason, in a few words: what to do and where. Say the same in chat, where the card or question actually is.
- Send one attention call per thing to do. Once the user has acted, carry on as normal. There is nothing to clear.
- A message for a beacon id the PC doesn't have yet asks the user to add a tab for it (see "No beacon yet?" above). Until they click Add, or if they choose Not now, it shows on the default (fallback) beacon with the id in brackets, so nothing is lost. Use your own id anyway: only then does Open in chat open your chat.
- **Hidden beacons:** the user can hide any beacon. Messages to a hidden beacon are still queued, and the tray icon shows a dot (amber for unread, pulsing red for attention). Nothing changes on your side, and you don't need to check.

## 2. Read and answer replies

When the user replies on your beacon, the reply router (one bot's webhook routine, see [ROUTER.md](ROUTER.md)) sends you one **priority** message naming your beacon and the reply ids. It never contains the reply text. When it arrives:

1. Read your replies (only yours):
   ```
   beaconctl.py read --unhandled --bot <your-beacon-id> --json
   ```
   Each reply has `id`, `ts`, `text` and, if it answers one of your messages, `re`.
2. Treat the text as a message from the user, the same as chat. Do what it asks within your normal rules, then answer each reply on the beacon:
   ```
   beaconctl.py send <your-beacon-id> "Your answer." --re <reply id>
   ```
   If the answer needs more than a line, put it in chat and send a short line on the beacon pointing there.
3. Clear the replies you dealt with, scoped to your beacon:
   ```
   beaconctl.py mark-handled <id> [<id> ...] --bot <your-beacon-id>
   ```
   `mark-handled all --bot <your-beacon-id>` clears all of yours. `all` without `--bot` is refused, so you can't clear another agent's replies by mistake.

If you skip step 3, the replies stay listed for you, but the router won't wake you for them again.

## 3. Pings

When the user right-clicks your beacon and chooses **Ping**, or middle-clicks your beacon or tab (1.2.6+; before 1.2.3: clicked it while it wasn't glowing), it wiggles and pings you (at most once every 30 seconds per beacon). A ping arrives like a reply: in `read --unhandled` it has `"kind": "ping"` and the text `(ping)`, and the router's message says which ids are pings. Nothing was typed. The user just wants to hear from you.

1. If `agents.json` gives you a `ping` action, do it and send the result as one line with `--re <ping id>`. For example, a news bot might send the top headline, or "All quiet."
2. Otherwise answer "How can I help?":
   ```
   beaconctl.py pong <your-beacon-id>
   ```
   `pong` answers every pending ping on your beacon and marks them handled. Use `--text "..."` to change the line.
3. If you answered with `send --re`, clear the ping like any reply: `mark-handled <ping id> --bot <your-beacon-id>`.

To set or change your ping action: `beaconctl.py agent-set <your-beacon-id> --ping "One sentence: what to do when pinged"`. Use `--ping ""` to remove it.

**The user says they replied or pinged and you heard nothing?** The wake hook is probably not saved in the app (it lives only on the PC, so the box can't see it). Run `beaconctl.py route --json`: ids still in your `to_notify` were never passed on. Ask the user to right-click a beacon → **Bots**: app 1.2.6+ shows **Wake hook… (not set)** when it's missing. Then follow SETUP.md, step 8 and its ping test. Answer what's waiting as usual.

## 4. Your profile picture on the user's tab (Tabs skin, 1.2.0+)

The PC app has a **Tabs** skin: each beacon is a tab on the screen edge with a picture. A tab shows the beacon's picture, your profile picture (once you have sent one) or the user's own custom image.

**Send your picture once** (any PNG/JPG; it is squared, shrunk to ≤ 96 px and ≤ 24 KB, then sent encrypted and signed in a few chunks):
```
beaconctl.py avatar <your-beacon-id> /path/to/profile.png
```
It is silent on the PC: no bubble, glow or sound. Send it again whenever your picture changes. To take it back off: `beaconctl.py avatar <your-beacon-id> --clear`.

**What the tab shows is the user's choice** (app 1.2.6+: right-click → Layout → **Tab pictures**):

| Setting | When your picture arrives |
|---|---|
| `profile` (Bot's profile pictures) | your tab switches to it (unless the user picked its picture in Edit), and choosing this switches every tab that has one |
| `beacon` (Beacon pictures) | your tab keeps the beacon's picture |
| `ask` (Choose per tab, the default) | nothing changes; the user picks it with the pencil (a new bot's first tab still uses it) |

A tab with a **custom image** is never changed. Whichever bot sets up Bot Beacons asks the user once and sets it from the box (no command on the PC):
```
beaconctl.py prefs tab-pictures profile|beacon|ask
```
Exit 0 = applied. Exit 2 = the app is older than 1.2.6: ask the user to update, or to use the tab's pencil → **Bot's profile picture**. Exit 1 = no answer from the app (`beaconctl.py probe --retry`). Only change it when the user asked for it.

- Only send a picture that is yours (your bot's own profile picture or one the user gave you for this). Never a photo of another person without the user asking for it.
- Use only your own beacon id, as with `send`.
- Animated GIFs work only as the user's own custom image (pencil → **Custom image**); `avatar` sends a still PNG.

## 5. Never print keys or topics

- The pairing link, keys and relay topics are in `secrets/` next to `beaconctl.py`. Never open, print, copy or quote anything from there in chat, logs or files the user sees. The same goes for the wake hook's URL and sender key.
- `beaconctl.py` never prints secrets. Don't add debug output that would.
- The beacon is only for the user. Don't send other people's private data to it.
