# Bot Beacons themes

A theme is a `beacons.json` file plus some PNG art. The app reads `%APPDATA%\BotBeacons\beacons.json`. It is created from the built-in starter pack on first run, and a setup bot usually writes a personal one with `tools/make_beacons.py`. Edit it, then right-click a beacon → **Exit** and start Bot Beacons again.

## Make a custom beacon (walkthrough)

1. **Start from a starter look.** On the box, `python3 tools/make_beacons.py --beacon "id=chef; name=Chef Bot; look=cat; color=#FF7A59; agent=<agent id>"` writes a full entry you can edit. Or copy an existing entry in `%APPDATA%\BotBeacons\beacons.json` and give it a new `id`.
2. **Recolour.** Change `color` (bubble accent) and `tint` (eyes and chest light). Any `#RRGGBB` works because the base's eyes are drawn in neutral white.
3. **Mix accessories.** `overlays` is just a list of PNGs drawn on top of the base. A robot antenna on the cat is `["cat-ears.png", "cat-whiskers.png", "cat-tail.png", "robot-antenna.png"]`.
4. **Add your own art.** Draw PNGs on a 150 × 190 transparent canvas (copy `art/beacon-base.png` as a guide for where the body, eyes and chest light are). Save them in `%APPDATA%\BotBeacons\art\` and list them in `overlays`. You can also replace the whole body with your own `base`, as long as the eyes and chest light are white or light grey and you set `eye`/`eyeR` and `lamp`/`lampR` to where they are.
5. **Animate it.** Give the new layer moves in the `anim` block (below), for example a hat that bobs with `"layer": "hat.png"`.
6. **Reload.** Right-click a beacon → **Exit**, start Bot Beacons again, and check the beacon idle and glowing: right-click it → **Test glow (local)** (nothing is sent anywhere; click the beacon to clear it). A bot testing on its own install can also capture frames with the developer hooks (`test <id>`, `frames <id>`, see TECHNICAL.md), but never leaves them on.
7. **Keep it or share it.** To make it the built-in default, copy the PNGs into the package's `art/`, merge the entry into the package `beacons.json`, and rebuild with `build.bat` (see TECHNICAL.md, "Building"). Only share art you made or are allowed to share.

## Where art is looked up

For each file name, the app checks in order:

1. `%APPDATA%\BotBeacons\art\<name>`: custom art, no rebuild needed
2. `art\<name>` next to `BotBeacons.exe`
3. the art built into the exe (everything in the package's `art/` at build time)

## Layers

Every starter beacon is a **base** plus **accent layers**, all PNGs on the same 150 × 190 canvas with a transparent background:

| File | What it is |
|---|---|
| `beacon-base.png` | the shared body: pearl sphere, dark visor with two eyes and a smile, chest light |
| `orb-aura.png`, `orb-crest.png`, `orb-sparkles.png` | the orb: soft halo, little crest wisp, floating sparkles |
| `robot-ears.png`, `robot-panel.png`, `robot-antenna.png` | the robot: side ear-bolts, chest panel, antenna |
| `cat-ears.png`, `cat-whiskers.png`, `cat-tail.png` | the cat: ears, whiskers, tail |

Overlays are drawn in the listed order on top of the base. The animation can move each one on its own (wag a tail, wobble an antenna) because they are separate layers.

**Tinting.** The colour lives in the base, not in separate art. `tint` recolours the *neutral* (white or grey, bright, low-saturation) pixels of the base inside two circles: the eyes (`eye` centre, `eyeR` radius) and the chest light (`lamp`, `lampR`). Draw those parts in white or light grey and any colour works. Saturated pixels are never tinted.

**Alert frame.** When a message arrives, the app builds the glowing frame itself: a soft radial glow around the eyes (`eyeGlow` radius, default `eyeR × 2.6`) and the chest light (`lampGlow`, default `lampR × 2.8`), plus a white core. You don't draw alert art. The eye glow is drawn twice: once behind the art (so a large `eyeGlow` shows as a halo around the body) and once over it. Tune the over-the-art part with `glowOver` (radius; -1 = same as `eyeGlow`, 0 = none), `glowOverAlpha` (0-255, default 100) and `eyeCore` (white core radius, default 5, 0 = none). For a dark visor with two eyes, keep the over-glow small and faint and the core off, or the eyes turn muddy. The starter beacons use `"eyeGlow": 68, "glowOver": 24, "glowOverAlpha": 55, "eyeCore": 0`.

**Single-image beacons.** Instead of `base` and `overlays` you can give `"art": "mine.png"` (and optionally `"alertArt"`) for one flat image. Layers and tinting are then not used.

## A beacon entry

```jsonc
{
  "id": "research", "name": "Research Bot", "purpose": "Research and reports",
  "gist": "Task finished: your report is ready in chat.",
  "agentId": "<Grok Bot agent id>",
  "color": "#FFB13B", "tint": "#FFB13B",
  "base": "beacon-base.png", "overlays": ["robot-ears.png", "robot-panel.png", "robot-antenna.png"],
  "eye": [75, 101], "eyeR": 20, "eyeGlow": 68, "glowOver": 24, "glowOverAlpha": 55, "eyeCore": 0,
  "lamp": [75, 141], "lampR": 6, "lampGlow": 16,
  "anim": { "...": "see below" }
}
```

| Key | Meaning |
|---|---|
| `id` | short id that bots send to (`beaconctl.py send research "…"`) |
| `name`, `purpose` | the bubble's title and subtitle |
| `gist` | sample line for right-click → **Test glow (local)** |
| `agentId` | the Grok Bot agent behind it (used by the bubble's **Open in chat** link) |
| `color` / `tint` | glow colour / colour of the eyes and chest light |
| `fallback: true` | receives messages for unknown ids (one beacon) |
| `test: true` | hidden unless tray → **Test beacons → Show test beacons** is on (the `echo` beacon) |

Leave the file's `"version"` alone. When an update ships a higher version, the app saves your file as `beacons.v<N>.bak.json` and installs the new default.

## The `anim` block

The top-level `"anim"` holds the defaults for every beacon. A beacon's own `"anim"` overrides those defaults key by key: giving `idle` replaces the theme's idle list for that beacon.

```jsonc
"anim": {
  "idleEvery": [20, 60],      // seconds between idle actions (random in range)
  "twitchEvery": [12, 35],    // seconds between small twitches
  "antenna": [75, 27],        // the point named "antenna" in steps
  "idle":     [ { "name": "tail-swish", "dur": 1.6, "steps": [ ... ] } ],
  "twitches": [ { "name": "blink-glow", "dur": 0.5, "steps": [ ... ] } ]
}
```

An action has a `name`, a duration `dur` in seconds (0.1–5) and `steps`. Each step runs during part of the action: `at` (start, 0–1) and `len` (length, 0–1). A step's kind depends on its main key:

| Step | Example | Does |
|---|---|---|
| layer | `{"layer":"cat-tail.png","rotate":12,"pivot":[118,150],"wave":3}` | moves one overlay: `rotate` (degrees, about `pivot`), `dx`/`dy` (px), `scale` (+fraction), `alpha` |
| glint | `{"glint":"eye","size":5}` | a four-point sparkle at a point |
| glow | `{"glow":"lamp","r":12,"alpha":220,"flicker":"true"}` | a soft glow |
| sparks | `{"sparks":"antenna","n":7,"color":"#FFD27A"}` | a little burst of sparks |
| tilt | `{"tilt":3}` | tilts the whole beacon (degrees) |
| hop | `{"hop":4}` | a small hop (px) |

- Points are `[x, y]` on the canvas, or `"eye"`, `"lamp"` or `"antenna"`.
- `wave: n` makes a decaying back-and-forth with n cycles. `0` (the default) is one smooth out-and-back.
- `color` defaults to the beacon's colour.

The user's **Animations** setting scales all of this. **Full** plays everything. **Subtle** plays idle actions half as often and smaller, with no twitches or sway. **Off** plays no idle motion: alerts still glow and pings still wiggle.

## Accessories

An accessory is just another overlay: draw it on the 150 × 190 canvas, put the PNG in `%APPDATA%\BotBeacons\art\`, and add it to a beacon's `overlays` (later entries are drawn on top). A hat, scarf or badge works on any starter look because they all share the base. To animate it, add a `layer` step that names the file, for example a bobbing hat:

```jsonc
{"name":"hat-bob","dur":1.2,"steps":[{"layer":"hat.png","dy":-3,"wave":2}]}
```

## Making new art

`themes/starter/make_starter_art.py` (Python 3 + Pillow) draws the whole starter pack: `python3 themes/starter/make_starter_art.py` rewrites `art/`. The anchor points (body centre, eyes, chest light, antenna) are at the top of the script. Copy it as a starting point for your own pack, keep the same canvas size, and rebuild with `build.bat` to embed new defaults.
