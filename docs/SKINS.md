# Skins: hot-swappable character packs (app 1.2.7+)

A **skin** is a folder of character art plus a `skin.json` that dresses the user's beacons. It changes how the beacons look (art, colours, glow, animations, sounds), never which beacons there are. The user switches skins live from right-click → **Skin** → **Theme**, with no restart: positions, unread messages, attention calls and history stay as they were.

The floating/tabs *layout* is a different thing (right-click → Skin → All floating / All tabs). A skin works in both.

## Bundled skins

The repo's `skins/` folder ships two skins, and `tools/install.ps1` copies them into `%APPDATA%\BotBeacons\skins\` (only skins that aren't there yet; your chosen theme is not changed):

- **Starter** (`skins/starter/`): the original Bot Beacons look, the orb, robot and cat starter beacons with their sounds, the same art as **Built-in**, packaged as a skin. Choose it to put any set of bots in the original look (it hands the orb, robot and cat looks out in that order) or as the starting point for your own skin.
- **Cute** (`skins/cute/`): an example skin with seven characters, to copy from when making a new one.

## Where skins live

```
%APPDATA%\BotBeacons\skins\
  cute\                 <- one skin = one folder (its name is the theme id)
    skin.json
    preview.png         <- optional: a picture of the whole set
    art\*.png           <- the art (PNG or GIF)
    sounds\*.wav        <- optional
  cute.zip              <- or drop a zip here: it unpacks itself
  _installed\           <- zips already unpacked, and the previous copy of a replaced skin
```

- **Drop a folder or a `.zip`** in `skins\`. The app watches the folder: a zip is unpacked a couple of seconds after it has finished copying, a balloon says "New skin … is installed", and it shows up in Skin → Theme. A zip that fails the checks is renamed `.zip.failed` and a balloon says why.
- **Editing the active skin's files** reloads it live. Deleting the active skin's folder goes back to the built-in look.
- The choice is saved as `theme=<folder>` in `settings.ini` (`theme=` or `theme=builtin` = the built-in look).
- **Built-in** is the normal `beacons.json` (and `beacons.user.json`), exactly as before. Skins never write to those files, so updates and the user's theme edits keep working.
- If a skin can't be loaded, the current look stays, the reason goes in the log and one balloon says why.

**Zip safety.** Only `.png .gif .ico .wav .json` files are unpacked (anything else is skipped). Paths must stay inside the skin (no `..`, absolute paths, drive letters or `:`), at most 4 folders deep, 400 files, 8 MB a file and 64 MB in all (counted while unpacking), stored or deflated, not encrypted, CRC checked. One top folder inside the zip is fine. The result must be a skin that loads, or nothing is installed.

## skin.json

```jsonc
{
  "name": "Cute crew",                       // shown in Skin > Theme
  "author": "…",
  "description": "Seven friendly characters…",
  "credits": "Art AI-generated",             // where the art came from: always say if it is AI-generated
  "bubble": "card",                          // optional, like beacons.json: bubble, beaconWord, trayIcon
  "anim": { "idleEvery": [18, 45], "twitchEvery": [14, 40] },   // defaults for every entry below
  "beacons": [                               // matched to the user's beacons by id
    { "id": "research", "name": "Research Bot", "color": "#FFB13B", "art": "art/research.png" }
  ],
  "looks": [                                 // generic looks, handed out to the other beacons
    { "look": "cloudy", "name": "Cloudy", "color": "#6EC6FF", "tint": "#6EC6FF",
      "base": "art/weather-halo.png", "overlays": ["art/weather-body.png", "art/weather-drop.png"],
      "eye": [78.9, 102.5], "eyeR": 0, "eyeGlow": 64, "lamp": [117.6, 150.5], "lampR": 0, "lampGlow": 14, "glowOver": 0, "eyeCore": 0,
      "anim": { "idle": [ … ], "twitches": [ … ] } }
  ]
}
```

Entries use the same keys as a `beacons.json` entry (see [THEMES.md](THEMES.md): `art`/`alertArt` or `base` + `overlays`, `color`, `tint`, `eye`/`eyeR`, `lamp`/`lampR`, the glow keys, `anim`, `sound`, `attentionSound`). File names are **relative to the skin folder** (`art/x.png`; a bare `x.png` is also looked for in `art/`). An entry in `beacons` may also set `name` and `gist`. A look never renames a bot.

**How beacons are matched**

1. An entry in `beacons` whose `id` is one of the user's beacons dresses that beacon.
2. Every other beacon gets a look from `looks` (or an entry marked `"generic": true`): in list order, least-used first, test beacons last. The choice is remembered per skin in `settings.ini` (`skinlooks.<folder>`), so adding or removing a bot never reshuffles the others.
3. The user can pick by hand: Skin → Theme → **Look for <bot>** or **Assign looks** (Automatic, any look, or Built-in look).
4. A beacon the skin doesn't cover keeps its built-in look.
5. An entry with `art` or `base` replaces the whole look: the built-in layers, anchors and animations are dropped, then the skin's `anim` and the entry's own `anim` apply.
6. The user's edits still win, in this order: beacons.json → skin → `beacons.user.json` → the tab's name and colour in `tabs.json`.

## Recipe: "make me a new skin of X" (for bots)

1. **Pick the cast.** A generic skin (`looks`) works for any user. Use `beacons` entries (by id) only for a skin made for this user's own bots (`beaconctl.py agents` lists their ids). Five to eight looks is plenty.
2. **Draw on one canvas.** As in `themes/starter/make_starter_art.py`: every layer of a character is a transparent PNG of the same size (the starter pack uses 150 × 190; anything up to 1024 px a side works; keep it around 150–200 px tall). Stack a `base` and `overlays` for parts that move (ears, a tail, steam), or use one flat `art` image. Note where the eyes and the chest light are and set `eye`, `lamp` (and `eyeR`/`lampR` 0 if the art has no white parts to tint) and the glow radii. Images you generate are fine: **say so in `credits`** ("Art AI-generated").
3. **Animate calmly.** `idleEvery` about `[18, 45]` and `twitchEvery` about `[14, 40]` seconds, hops of 6 px or less, small tilts (≤ 8°), no `flicker`. Busy beacons make dragging feel laggy and are tiring to look at. Layer steps must name one of the entry's overlays exactly (`"layer": "art/fox-ear-l.png"`).
4. **Add `preview.png`**: the whole cast side by side with their names, no more than 2048 px wide.
5. **Check and pack** on the box:
   ```
   python3 /workspace/beacons/beaconctl.py skin check <folder>      # errors and calm-animation warnings
   python3 /workspace/beacons/beaconctl.py skin pack <folder> [-o out.zip]
   ```
   `check` refuses private paths (a drive letter, `/workspace`, `%APPDATA%`, `\Users\`…) anywhere in `skin.json`, missing or oversized art and broken entries. `pack` writes a zip of only the skin files, with relative names, and prints its size and SHA-256.
6. **Put it on the PC** with the user's approval: in Grok Bot, `CopyFromBox` the zip to `%APPDATA%\BotBeacons\skins\` (`C:\Users\<user>\AppData\Roaming\BotBeacons\skins\`). If the copy tool can only write to the user's folder, ask the user to move the zip into `skins\` (right-click a beacon → Skin → Theme → **Open skins folder** opens it). The app unpacks it by itself; no restart.
7. **Tell the user** to choose it from right-click → **Skin** → **Theme**, and that **Assign looks** there changes who wears what. Mention the credits.

Don't send skins through the relay, and don't overwrite the user's `beacons.json` to install one.
