#!/usr/bin/env python3
"""beaconctl: bot-side tool for the Bot Beacons secure relay (runs on the Grok Bot box).

  beaconctl.py pair CODE [--name NAME] [--wait S]  one-time pairing with the code shown on the PC (waits until it expires)
  beaconctl.py status                       is a link set up (never prints keys or topics)
  beaconctl.py probe [--wait S]             silent health check: the PC app (1.1.3+) answers with a signed ack,
                                           shows nothing on any beacon. Exit 0 = OK, 1 = no answer within S s (default 30)
  beaconctl.py send BEACON TEXT [--title T]  send an encrypted, signed message that pops up on beacon BEACON.
                                           TEXT "-" reads stdin. One message must fit in 3900 bytes once encrypted:
                                           about 2,700 plain English characters, about 900 for Chinese/Japanese, 680 emoji.
                                           Longer ones are refused (exit 1, nothing sent): send a summary instead.
                                           A new BEACON id (no tab yet) asks the user "New bot NAME wants a tab. Add / Not now";
                                           --name NAME sets the name shown (default: the id, e.g. news-bot -> News Bot)
  beaconctl.py send --as BEACON TEXT         same thing, other spelling ("notify" is an alias of "send")
  beaconctl.py send BEACON TEXT --re ID      ...as an answer to reply ID (or to a ping)
  beaconctl.py attention BEACON REASON       "needs attention" call to action: stronger alert + "!" badge on the beacon,
                                           the bubble shows REASON and an "Open in chat" link to the beacon's agent chat
  beaconctl.py avatar BEACON FILE [--size 96]  send the bot's profile picture for BEACON (1.2.0 Tabs skin): squared,
                                           shrunk to <= SIZE px and ~24 KB, sent in signed encrypted chunks. Silent on the PC
  beaconctl.py avatar BEACON --clear         remove it again (the tab goes back to initials / the user's custom image)
  beaconctl.py read [--since new|1h|all] [--bot BOT] [--json]
                                           verify + decrypt replies from the PC. "new" (default) = only
                                           unread ones, with full replay/staleness checks; 1h/all = history
  beaconctl.py read --unhandled [--bot BOT] [--json]
                                           every verified reply not yet marked handled (router use; safe to
                                           run repeatedly: replies stay listed until mark-handled)
  beaconctl.py mark-handled ID [ID ...] [--bot BOT]
                                           mark replies handled. With --bot, only that beacon's replies are touched;
                                           "all" needs --bot (an agent clears only its own replies)
  beaconctl.py route [--json]               which beacons have unhandled replies, the owning agent, and which ids
                                           have not had a wake yet ("to_notify")
  beaconctl.py mark-notified ID [ID ...] [--bot BOT]
                                           router: record that the owning agent was woken for these ids
  beaconctl.py agents                       the beacon -> agent registry (agents.json, incl. optional "ping" actions)
  beaconctl.py agent-set BEACON --agent-id ID --name NAME [--ping "what to do when pinged"]
                                           add or update a registry entry;  agent-remove BEACON  deletes one
  beaconctl.py pong BEACON [--text T]        answer every pending ping on BEACON with T (default "How can I help?")
                                           and mark those pings handled (test beacons, or agents with no ping action)
  beaconctl.py echo                         one-shot: answer each unhandled reply to the echo beacon
  beaconctl.py forge-test                   post deliberately bad messages (plain, forged, replayed, stale)
  beaconctl.py unpair                       forget the link

Data lives in $BEACONS_HOME (default: the folder this script is in): agents.json, and secrets/ (mode 0700/0600).
Nothing secret is ever printed.
Protocol: see docs/TECHNICAL.md ("Security model").
"""
import argparse, base64, hashlib, hmac, json, os, secrets, sys, time, urllib.error, urllib.request, urllib.parse
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import padding
from cryptography.hazmat.primitives.asymmetric import ec

HERE = os.environ.get("BEACONS_HOME") or os.path.dirname(os.path.abspath(__file__))
SECRETS = os.environ.get("BEACONS_SECRETS", os.path.join(HERE, "secrets"))
LINK = os.path.join(SECRETS, "link.json")
SEEN = os.path.join(SECRETS, "seen.json")
CURSOR = os.path.join(SECRETS, "cursor.txt")
INBOX = os.path.join(SECRETS, "inbox.json")     # verified replies + handled state (router idempotency)
LOCK = os.path.join(SECRETS, ".lock")
KEEP_MS = 7 * 24 * 3600 * 1000
# What the reply router does per beacon (see docs/ROUTER.md).
AGENTS_FILE = os.path.join(HERE, "agents.json")   # beacon id -> {agentId, name, ping}
TEST_ROUTES = {"echo": "echo"}   # test beacons the router answers itself
DEFAULT_PONG = "How can I help?"

def load_agents():
    try: return json.load(open(AGENTS_FILE))["agents"]
    except Exception: return {}

def route_for(beacon):
    if beacon in TEST_ROUTES: return TEST_ROUTES[beacon]
    return "agent" if beacon in load_agents() else "unknown"
DEFAULT_SERVER = "https://ntfy.sh"
ALPHA = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
MAX_AGE_MS, MAX_FUTURE_MS = 15 * 60 * 1000, 2 * 60 * 1000
B2P, P2B = "b2p", "p2b"
UA = {"User-Agent": "beaconctl/1.0"}
# 1.1.6: ntfy.sh turns a body over 4096 bytes into a file attachment, which the receiver can't read as a message, while
# the sender still gets HTTP 200. So every envelope we post must fit, measured on the final sealed bytes (title, escaping
# and base64 included), with a margin.
MAX_ENVELOPE = 3900

def b64(b): return base64.b64encode(b).decode()
def ub64(s): return base64.b64decode(s)
def H(key, msg): return hmac.new(key, msg.encode(), hashlib.sha256).digest()
def now_ms(): return int(time.time() * 1000)

def _write_secret(path, data):
    os.makedirs(SECRETS, mode=0o700, exist_ok=True); os.chmod(SECRETS, 0o700)
    tmp = path + ".tmp"
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as f: f.write(data)
    os.replace(tmp, path)

def load_link():
    if not os.path.exists(LINK): sys.exit("Not paired. Run: beaconctl.py pair <CODE>")
    l = json.load(open(LINK)); l["key"] = ub64(l["key"]); return l

# ---------------------------------------------------------------- envelope (must match src/Crypto.cs)
def seal(key, direction, payload):
    ek, mk = H(key, "bot-beacons enc v1"), H(key, "bot-beacons mac v1")
    iv = secrets.token_bytes(16)
    # ensure_ascii=False: UTF-8 as is (1.1.5 and older wrote \uXXXX: 6 bytes per accented letter, 12 per emoji)
    raw = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    p = padding.PKCS7(128).padder(); data = p.update(raw) + p.finalize()
    e = Cipher(algorithms.AES(ek), modes.CBC(iv)).encryptor(); ct = e.update(data) + e.finalize()
    ivs, cts = b64(iv), b64(ct)
    tag = b64(H(mk, f"v1|{direction}|{ivs}|{cts}"))
    return json.dumps({"v": 1, "d": direction, "iv": ivs, "ct": cts, "tag": tag}, separators=(",", ":"))

class TooLong(Exception):
    def __init__(self, n): super().__init__(n); self.n = n

def utf8_clean(s):
    """replace anything that isn't valid UTF-8 (e.g. stray bytes from the shell) with U+FFFD, so sealing never fails"""
    return s.encode("utf-8", "replace").decode("utf-8") if isinstance(s, str) else s

def seal_checked(key, direction, payload):
    """seal and refuse (TooLong) if the final envelope would not fit in one relay message"""
    env = seal(key, direction, payload); n = len(env.encode("utf-8"))
    if n > MAX_ENVELOPE: raise TooLong(n)
    return env

def fit_chars(key, direction, payload, field="text"):
    """how many leading characters of payload[field] fit (binary search on the real sealed size)"""
    t = payload.get(field, ""); lo, hi = 0, len(t)
    while lo < hi:
        mid = (lo + hi + 1) // 2
        try: seal_checked(key, direction, dict(payload, **{field: t[:mid]})); lo = mid
        except TooLong: hi = mid - 1
    return lo

def too_long_message(key, payload, n):
    text = payload.get("text", ""); k = fit_chars(key, B2P, payload)
    if k == 0:
        return (f"Not sent: the title and other fields alone are too long for one beacon message ({n} bytes once encrypted, "
                f"limit {MAX_ENVELOPE}). Use a short title (a few words).")
    return (f"Not sent: the message is too long for one beacon pop-up ({n} bytes once encrypted, limit {MAX_ENVELOPE}). "
            f"About {k} of its {len(text)} characters would fit. Send a shorter summary (the user can open the chat for details).")

def open_env(key, direction, body):
    """returns (payload, None) or (None, reason)"""
    try: env = json.loads(body)
    except Exception: return None, "not an encrypted envelope"
    if not isinstance(env, dict) or "tag" not in env or "ct" not in env: return None, "not an encrypted envelope"
    if str(env.get("v")) != "1": return None, "unknown version"
    if env.get("d") != direction: return None, "wrong direction"
    ek, mk = H(key, "bot-beacons enc v1"), H(key, "bot-beacons mac v1")
    try:
        want = H(mk, f"v1|{env['d']}|{env['iv']}|{env['ct']}")
        if not hmac.compare_digest(want, ub64(env["tag"])): return None, "bad signature"
        d = Cipher(algorithms.AES(ek), modes.CBC(ub64(env["iv"]))).decryptor(); data = d.update(ub64(env["ct"])) + d.finalize()
        u = padding.PKCS7(128).unpadder(); return json.loads(u.update(data) + u.finalize()), None
    except Exception: return None, "malformed"

def check_fresh(p, seen):
    mid, ts = str(p.get("id", "")), p.get("ts")
    if len(mid) < 8 or not isinstance(ts, (int, float)): return "missing id/timestamp"
    n = now_ms()
    if ts < n - MAX_AGE_MS: return "stale"
    if ts > n + MAX_FUTURE_MS: return "timestamp in the future"
    if mid in seen: return "replay"
    seen[mid] = ts
    return None

# ---------------------------------------------------------------- ntfy
def _retry(fn, what, until=None, tries=6):
    """ntfy.sh limits each IP to roughly one request per 5 s: on 429, 5xx or a network error wait and try again
    (until `until` if given, else `tries` times). Other errors are raised at once."""
    n = 0
    while True:
        try: return fn()
        except urllib.error.HTTPError as e:
            if e.code != 429 and e.code < 500: raise
            err = f"HTTP {e.code}"
        except (urllib.error.URLError, TimeoutError, ConnectionError, OSError) as e:
            err = type(e).__name__
        n += 1
        wait = min(30, 3 * 2 ** min(n - 1, 4))
        if (until is not None and time.time() + wait > until) or (until is None and n >= tries):
            raise SystemExit(f"The relay kept failing while {what} ({err}). Try again in a minute.")
        print(f"(relay busy while {what}: {err}; retrying in {wait} s)", file=sys.stderr)
        time.sleep(wait)

def _post_once(server, topic, body):
    req = urllib.request.Request(f"{server}/{topic}", data=body.encode(), method="POST", headers=UA)
    with urllib.request.urlopen(req, timeout=20) as r: return r.status

def _poll_once(server, topic, since):
    url = f"{server}/{topic}/json?poll=1&since={urllib.parse.quote(str(since))}"
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=20) as r:
        out = []
        for line in r.read().decode().splitlines():
            if line.strip():
                o = json.loads(line)
                if o.get("event") == "message": out.append(o)
        return out

def post(server, topic, body, until=None):
    return _retry(lambda: _post_once(server, topic, body), "sending", until)

def poll(server, topic, since, until=None):
    return _retry(lambda: _poll_once(server, topic, since), "checking the relay", until)

# ---------------------------------------------------------------- pairing
def norm(code): return "".join(c for c in code.upper() if c in ALPHA)
def rv_topic(code): return "bbp-" + hashlib.sha256(("bot-beacons pair rv v1|" + norm(code)).encode()).hexdigest()[:40]
def code_key(code): return H(norm(code).encode(), "bot-beacons pair v1")

def cmd_pair(a):
    code = norm(a.code)
    if len(code) != 10: sys.exit("That doesn't look like a pairing code (expected 10 letters/digits like ABCDE-FGH23).")
    server = a.server.rstrip("/"); rv = rv_topic(code); ck = code_key(code)
    hello = None; hello_time = 0
    for o in poll(server, rv, "15m"):
        try: m = json.loads(o["message"])
        except Exception: continue
        if m.get("t") == "hello" and hmac.compare_digest(ub64(m.get("mac", "")), H(ck, "hello|" + m.get("pk", ""))): hello = m; hello_time = o.get("time", 0)
    if not hello: sys.exit("No pairing request found for that code. Check the code, or start pairing again on the PC.")
    pet_pk = hello["pk"]; xy = ub64(pet_pk)
    peer = ec.EllipticCurvePublicNumbers(int.from_bytes(xy[:32], "big"), int.from_bytes(xy[32:], "big"), ec.SECP256R1()).public_key()
    priv = ec.generate_private_key(ec.SECP256R1()); pn = priv.public_key().public_numbers()
    my_pk = b64(pn.x.to_bytes(32, "big") + pn.y.to_bytes(32, "big"))
    z = hashlib.sha256(priv.exchange(ec.ECDH(), peer)).digest()
    key = H(z, f"bot-beacons pair key v1|{code}|{pet_pk}|{my_pk}")
    # the PC keeps waiting until its code expires (10 minutes after it was shown), so wait that long, at least 2 minutes
    deadline = max(time.time() + a.wait if a.wait else hello_time + 600, time.time() + 120)
    since = str(int(time.time()) - 2)
    post(server, rv, json.dumps({"t": "join", "pk": my_pk, "name": a.name, "mac": b64(H(ck, f"join|{pet_pk}|{my_pk}"))}), deadline)
    print(f"Joined; waiting up to {int(deadline - time.time())} s for the PC to confirm...", file=sys.stderr)
    while time.time() < deadline:
        for o in poll(server, rv, since, deadline):
            since = o["id"]
            try: m = json.loads(o["message"])
            except Exception: continue
            if m.get("t") != "done": continue
            cfg, why = open_env(key, "pair", m.get("env", ""))
            if not cfg or check_fresh(cfg, {}): continue
            link = {"server": cfg["server"], "down": cfg["down"], "up": cfg["up"], "key": b64(key), "paired": now_ms()}
            ack = seal(key, "pair-ack", {"id": secrets.token_hex(12), "ts": now_ms(), "down": cfg["down"]})
            post(server, rv, json.dumps({"t": "ack", "env": ack}), time.time() + 120)
            _write_secret(LINK, json.dumps(link)); _write_secret(SEEN, "{}"); _write_secret(CURSOR, json.dumps({"since": int(time.time()), "handled": {}}))
            print("Paired. Secure link saved (keys stay in the secrets folder).")
            return
        time.sleep(5)   # gentle on ntfy's per-IP limit
    sys.exit("The PC didn't confirm before the code expired. On the PC, right-click a beacon > Re-pair with bots... for a new code "
             "(with Bot Beacons older than 1.0.1, keep the pairing window open until it says Paired).")

# ---------------------------------------------------------------- commands
def cmd_status(a):
    if not os.path.exists(LINK): print("Not paired."); return
    l = json.load(open(LINK))
    print(f"Paired since {time.strftime('%Y-%m-%d %H:%M %Z', time.localtime(l['paired']/1000))}; relay {l['server']}.")

def cmd_probe(a):
    """silent health check: send kind "probe" (log-only on the PC), wait for the app's signed ack.
    Real replies fetched while waiting go into the inbox as usual, so the router and bots still see them."""
    l = load_link()
    pid = secrets.token_hex(12)
    p = {"id": pid, "ts": now_ms(), "from": "bot", "bot": "", "kind": "probe", "text": ""}
    deadline = time.time() + max(5, a.wait)
    post(l["server"], l["down"], seal(l["key"], B2P, p), deadline)
    t0 = time.time()
    while time.time() < deadline:
        time.sleep(3)
        with _Lock():
            b, bad = sync_inbox()
        for k in PROBE_ACKS:
            if k.get("re") == pid:
                print(f"probe OK: app v{k.get('ver', '?')} answered in {time.time() - t0:.0f} s (nothing shown on the desktop)")
                return
    print(f"probe FAILED: no answer within {a.wait} s (app closed, PC asleep or offline, relay down, or app older than 1.1.3)")
    sys.exit(1)

def cmd_send(a):
    bot = getattr(a, "as_", None) or a.bot
    if a.as_ and a.bot and a.as_ != a.bot: sys.exit("Give the beacon once: either send BEACON TEXT or send --as BEACON TEXT.")
    if not bot: sys.exit("Which beacon? Use: send BEACON TEXT  or  send --as BEACON TEXT")
    if bot not in load_agents() and bot not in TEST_ROUTES:
        print(f"warning: '{bot}' is not in agents.json; the app shows unknown ids on its fallback beacon.", file=sys.stderr)
    a.bot = bot
    l = load_link()
    text = a.text if a.text != "-" else sys.stdin.read()
    text = utf8_clean(text)
    p = {"id": secrets.token_hex(12), "ts": now_ms(), "from": "bot", "bot": a.bot, "text": text}
    if getattr(a, "attention", False):
        p["kind"] = "attention"
        ag = load_agents().get(a.bot)
        if ag: p["agent"] = ag["agentId"]   # which chat "Open in chat" opens
    if a.title: p["title"] = utf8_clean(a.title)
    if getattr(a, "name", None): p["name"] = utf8_clean(a.name)[:32]   # 1.2.5: shown in "New bot NAME wants a tab" (new ids only)
    if getattr(a, "re", None): p["re"] = a.re
    # 1.1.6: refuse loudly (exit 1, nothing posted) rather than split: one alert should be one short pop-up, and parts
    # would arrive seconds apart (relay rate limit), could be lost or reordered one by one, and would each sound an alert
    try: env = seal_checked(l["key"], B2P, p)
    except TooLong as e: sys.exit(too_long_message(l["key"], p, e.n))
    st = post(l["server"], l["down"], env)
    print(f"sent {'attention call ' if p.get('kind') == 'attention' else ''}to {a.bot}: HTTP {st}, id {p['id']}")

AVATAR_MAX = 24 * 1024     # the PC accepts up to 32 KB; stay well under
AVATAR_CHUNK = 1800        # raw bytes per message: seals to ~3,630-3,720 bytes (bot id 3-64 chars), under MAX_ENVELOPE
AVATAR_MIN_CHUNK = 768     # 24 KB / 768 = 32 chunks, the most the PC accepts (Avatars.MaxChunks)

def avatar_png(path, size=96):
    """Square-crop, shrink and compress an image to a small PNG (bytes). Needs Pillow."""
    try:
        from PIL import Image
    except ImportError:
        sys.exit("avatar needs Pillow: pip install pillow")
    import io
    size = max(32, min(256, int(size)))
    try:
        im = Image.open(path); im.load()
    except Exception as e:
        sys.exit(f"Could not read {path}: {e.__class__.__name__}")
    im = im.convert("RGBA")
    w, h = im.size; s = min(w, h)
    im = im.crop(((w - s) // 2, (h - s) // 2, (w - s) // 2 + s, (h - s) // 2 + s))
    for side in (size, 80, 64, 48, 32):
        if side > size: continue
        sm = im.resize((side, side), Image.LANCZOS)
        for quant in (False, True):
            buf = io.BytesIO()
            (sm.quantize(colors=128, method=Image.Quantize.FASTOCTREE) if quant else sm).save(buf, "PNG", optimize=True)
            if buf.tell() <= AVATAR_MAX: return buf.getvalue(), side
    sys.exit("Could not get the picture under 24 KB. Try a simpler image.")

def avatar_messages(bot, png, chunk=None):
    """The chunk payloads for one avatar transfer (not sealed yet)."""
    chunk = chunk or AVATAR_CHUNK
    aid = secrets.token_hex(8); sha = hashlib.sha256(png).hexdigest()
    parts = [png[i:i + chunk] for i in range(0, len(png), chunk)]
    return [{"id": secrets.token_hex(12), "ts": now_ms(), "from": "bot", "bot": bot, "kind": "avatar", "aid": aid,
             "seq": i, "n": len(parts), "size": len(png), "sha256": sha, "data": b64(c)} for i, c in enumerate(parts)]

def avatar_envelopes(key, bot, png):
    """Seal every chunk with the 1.1.6 size check before anything is posted. If a chunk would not fit (a very long
    beacon id), shrink the chunk size and try again; give up (TooLong) only below AVATAR_MIN_CHUNK."""
    chunk = AVATAR_CHUNK
    while True:
        try: return [seal_checked(key, B2P, p) for p in avatar_messages(bot, png, chunk)]
        except TooLong as e:
            if chunk <= AVATAR_MIN_CHUNK: raise
            chunk = max(AVATAR_MIN_CHUNK, chunk - 200)

def cmd_avatar(a):
    l = load_link()
    if a.clear:
        p = {"id": secrets.token_hex(12), "ts": now_ms(), "from": "bot", "bot": a.bot, "kind": "avatar", "clear": "true"}
        try: env = seal_checked(l["key"], B2P, p)
        except TooLong as e: sys.exit(f"Not sent: the beacon id is too long for one message ({e.n} bytes once encrypted, limit {MAX_ENVELOPE}).")
        st = post(l["server"], l["down"], env)
        print(f"avatar cleared for {a.bot}: HTTP {st}"); return
    if not a.file: sys.exit("Give the picture: avatar BEACON FILE  (or --clear)")
    png, side = avatar_png(a.file, a.size)
    # 1.2.2: every sealed chunk must fit in one relay message (1.1.6 rule). All are sealed first, so nothing is posted
    # unless the whole picture fits; the timestamps are a few ms apart, far inside the 15-minute freshness window
    try: envs = avatar_envelopes(l["key"], a.bot, png)
    except TooLong as e: sys.exit(f"Not sent: an avatar chunk would be {e.n} bytes once encrypted (limit {MAX_ENVELOPE}). Use a shorter beacon id.")
    for env in envs:
        post(l["server"], l["down"], env)
    print(f"avatar sent for {a.bot}: {side}x{side} PNG, {len(png)} bytes in {len(envs)} chunk(s)")

def load_cursor():
    try:
        c = json.load(open(CURSOR))
        if isinstance(c, dict): return c
    except Exception: pass
    return {"since": int(time.time()) - 3600, "handled": {}}

PROBE_ACKS = []   # probe acks seen by read_replies (1.1.3); every reader skips them

def read_replies(since=None, strict=True):
    """strict (new replies, the ones a bot would act on): signature + freshness + never-seen-before,
    and a time cursor + handled relay ids (ntfy's since=<id> is unreliable within one second).
    history view (strict=False, since=1h/all): signature check and in-batch duplicate check only."""
    l = load_link()
    seen = json.load(open(SEEN)) if os.path.exists(SEEN) else {}
    cur = load_cursor() if strict else None
    batch = {}
    good, bad = [], []
    for o in poll(l["server"], l["up"], cur["since"] if strict else since):
        if strict:
            if o["id"] in cur["handled"]: continue
            cur["handled"][o["id"]] = o.get("time", 0); cur["since"] = max(cur["since"], o.get("time", 0))
        if o.get("attachment"):   # over 4096 bytes: the relay stored it as a file (the 1.1.6 app never sends one)
            bad.append((o["id"], "too large for the relay (arrived as a file)")); continue
        p, why = open_env(l["key"], P2B, o.get("message", ""))
        if p is not None:
            if strict: why = check_fresh(p, seen)
            elif str(p.get("id")) in batch: why = "replay"
            else: batch[str(p.get("id"))] = 1
        if why: bad.append((o["id"], why)); continue
        if p.get("kind") == "probe-ack": PROBE_ACKS.append(p); continue   # silent health-check answers, never replies
        good.append(p)
    if strict:
        cut = now_ms() - 2 * MAX_AGE_MS
        _write_secret(SEEN, json.dumps({k: v for k, v in seen.items() if v > cut}))
        cur["handled"] = {k: v for k, v in cur["handled"].items() if v >= cur["since"] - 120}
        _write_secret(CURSOR, json.dumps(cur))
    return good, bad

# ---------------------------------------------------------------- inbox (handled state)
class _Lock:
    def __enter__(self):
        import fcntl
        os.makedirs(SECRETS, mode=0o700, exist_ok=True)
        self.f = os.fdopen(os.open(LOCK, os.O_RDWR | os.O_CREAT, 0o600), "r+"); fcntl.flock(self.f, fcntl.LOCK_EX); return self
    def __exit__(self, *e):
        import fcntl
        fcntl.flock(self.f, fcntl.LOCK_UN); self.f.close()

def load_inbox():
    try:
        b = json.load(open(INBOX))
        if isinstance(b, dict) and isinstance(b.get("items"), list): return b
    except Exception: pass
    return {"items": []}

def save_inbox(b):
    cut = now_ms() - KEEP_MS
    b["items"] = [i for i in b["items"] if i.get("ts", 0) > cut]
    _write_secret(INBOX, json.dumps(b))

def ingest(b, good):
    """add newly verified replies to the inbox as unhandled (ids are unique; duplicates are ignored)"""
    have = {i["id"] for i in b["items"]}
    for g in good:
        if str(g.get("id")) in have: continue
        b["items"].append({"id": str(g["id"]), "ts": g.get("ts"), "bot": g.get("bot", ""), "text": g.get("text", ""),
                           "re": g.get("re", ""), "from": g.get("from", ""), "kind": "ping" if g.get("kind") == "ping" else "reply",
                           "handled": False})
        have.add(str(g["id"]))

def sync_inbox():
    """fetch + strictly verify new replies, store them, return (inbox, rejected)"""
    good, bad = read_replies()
    b = load_inbox(); ingest(b, good); save_inbox(b)
    return b, bad

def pending(b, bot=None):
    return [i for i in b["items"] if not i["handled"] and (not bot or i["bot"] == bot)]

def _select(b, ids, bot, field):
    """inbox items matching ids (or "all") that are not yet <field>; with bot set, only that beacon's items"""
    want = set(ids)
    if "all" in want and not bot: sys.exit('"all" needs --bot BEACON, so you only touch your own beacon\'s replies.')
    out, skipped = [], 0
    for i in b["items"]:
        if i.get(field): continue
        hit = "all" in want or i["id"] in want
        if not hit: continue
        if bot and i["bot"] != bot:
            if i["id"] in want: skipped += 1   # named someone else's id
            continue
        out.append(i)
    return out, skipped

def cmd_mark(a):
    with _Lock():
        b = load_inbox(); sel, skipped = _select(b, a.ids, a.bot, "handled")
        for i in sel: i["handled"] = True; i["handled_at"] = now_ms()
        save_inbox(b)
    print(f"marked {len(sel)} handled" + (f" (skipped {skipped} belonging to other beacons)" if skipped else ""))

def cmd_mark_notified(a):
    with _Lock():
        b = load_inbox(); sel, skipped = _select(b, a.ids, a.bot, "notified")
        for i in sel: i["notified"] = now_ms()
        save_inbox(b)
    print(f"marked {len(sel)} notified" + (f" (skipped {skipped} belonging to other beacons)" if skipped else ""))

def cmd_agents(a):
    print(json.dumps(load_agents(), indent=1))

def _save_agents(agents):
    try: root = json.load(open(AGENTS_FILE))
    except Exception: root = {"version": 1}
    root["agents"] = agents
    tmp = AGENTS_FILE + ".tmp"
    with open(tmp, "w") as f: json.dump(root, f, indent=2); f.write("\n")
    os.replace(tmp, AGENTS_FILE)

def cmd_agent_set(a):
    """add or update one beacon -> agent entry in agents.json"""
    if a.bot in TEST_ROUTES: sys.exit(f"'{a.bot}' is a test beacon id; pick another id.")
    ag = load_agents(); e = ag.get(a.bot, {})
    if a.agent_id: e["agentId"] = a.agent_id
    if a.name: e["name"] = a.name
    if a.ping is not None:
        if a.ping: e["ping"] = a.ping
        else: e.pop("ping", None)
    if not e.get("agentId") or not e.get("name"): sys.exit("A new entry needs --agent-id and --name.")
    ag[a.bot] = e; _save_agents(ag)
    print(f"agents.json: {a.bot} -> {e['name']}" + (" (with ping action)" if e.get("ping") else ""))

def cmd_agent_remove(a):
    ag = load_agents()
    if ag.pop(a.bot, None) is None: sys.exit(f"'{a.bot}' is not in agents.json.")
    _save_agents(ag); print(f"agents.json: removed {a.bot}")

def cmd_route(a):
    with _Lock():
        b, bad = sync_inbox()
    per = {}
    for i in pending(b): per.setdefault(i["bot"], []).append(i)
    ag = load_agents(); plan = []
    for k, v in sorted(per.items()):
        x = {"beacon": k, "pending": len(v), "ids": [i["id"] for i in v], "action": route_for(k),
             "pings": [i["id"] for i in v if i.get("kind") == "ping"]}
        if x["action"] == "agent":
            x["agentId"] = ag[k]["agentId"]; x["agentName"] = ag[k]["name"]
            if ag[k].get("ping"): x["ping_action"] = ag[k]["ping"]
            x["to_notify"] = [i["id"] for i in v if not i.get("notified")]   # wake the agent only for these
        plan.append(x)
    if a.json: print(json.dumps({"beacons": plan, "rejected": len(bad)}, indent=1)); return
    if not plan: print("Nothing pending." + (f" ({len(bad)} rejected)" if bad else "")); return
    for x in plan:
        extra = f" ({x['agentName']}, {len(x['to_notify'])} not yet notified)" if x["action"] == "agent" else ""
        pg = f", {len(x['pings'])} ping{'s' if len(x['pings']) != 1 else ''}" if x["pings"] else ""
        print(f"{x['beacon']}: {x['pending']} pending{pg} -> {x['action']}{extra}")
    if bad: print(f"({len(bad)} rejected: " + ", ".join(sorted({r for _, r in bad})) + ")")

def cmd_read(a):
    if a.unhandled:
        with _Lock():
            b, bad = sync_inbox()
        items = pending(b, a.bot)
        if a.json: print(json.dumps({"replies": items, "rejected": [{"ntfy_id": i, "reason": r} for i, r in bad]}, indent=1)); return
        for g in items:
            t = time.strftime("%H:%M:%S", time.localtime(g["ts"] / 1000))
            print(f"{t}  [{g['bot']}]  {g['id']}  " + ("PING" if g.get("kind") == "ping" else g["text"]))
        if not items: print("No unhandled replies.")
        return
    strict = a.since == "new"
    if strict:
        with _Lock():
            good, bad = read_replies()
            b = load_inbox(); ingest(b, good); save_inbox(b)    # never lose a reply: it stays unhandled for the router
    else:
        good, bad = read_replies(a.since, False)
    good = [g for g in good if not a.bot or g.get("bot") == a.bot]
    if a.json: print(json.dumps({"replies": good, "rejected": [{"ntfy_id": i, "reason": r} for i, r in bad]}, indent=1)); return
    for g in good:
        t = time.strftime("%H:%M:%S", time.localtime(g["ts"] / 1000))
        print(f"{t}  [{g.get('bot')}]  {g.get('text')}")
    if bad: print(f"({len(bad)} rejected: " + ", ".join(sorted({r for _, r in bad})) + ")")
    if not good and not bad: print("No new replies." if strict else "No replies.")

def cmd_echo(a):
    l = load_link(); n = 0
    with _Lock():
        b, bad = sync_inbox()
        for g in pending(b, "echo"):
            if g.get("kind") == "ping": continue   # pings are answered with: pong echo
            p = {"id": secrets.token_hex(12), "ts": now_ms(), "from": "bot", "bot": "echo", "text": "Echo: " + g["text"], "re": g["id"]}
            try: env = seal_checked(l["key"], B2P, p)
            except TooLong:   # test beacon: shorten the echo to fit rather than fail
                k = fit_chars(l["key"], B2P, dict(p, text=p["text"] + "\u2026")) - 1
                env = seal_checked(l["key"], B2P, dict(p, text=p["text"][:max(0, k)] + "\u2026"))
            post(l["server"], l["down"], env); n += 1
            g["handled"] = True; g["handled_at"] = now_ms(); save_inbox(b)
    print(f"echoed {n} repl{'y' if n == 1 else 'ies'}" + (f" ({len(bad)} rejected)" if bad else ""))

def cmd_pong(a):
    """answer pending pings on one beacon with a short greeting, then mark them handled"""
    l = load_link(); n = 0
    with _Lock():
        b, bad = sync_inbox()
        for g in pending(b, a.bot):
            if g.get("kind") != "ping": continue
            p = {"id": secrets.token_hex(12), "ts": now_ms(), "from": "bot", "bot": a.bot, "text": utf8_clean(a.text), "re": g["id"]}
            try: env = seal_checked(l["key"], B2P, p)
            except TooLong as e: sys.exit(too_long_message(l["key"], p, e.n))
            post(l["server"], l["down"], env); n += 1
            g["handled"] = True; g["handled_at"] = now_ms(); save_inbox(b)
    print(f"answered {n} ping{'s' if n != 1 else ''} on {a.bot}" + (f" ({len(bad)} rejected)" if bad else ""))

def cmd_forge(a):
    l = load_link(); s, d = l["server"], l["down"]
    good = {"id": secrets.token_hex(12), "ts": now_ms(), "bot": "echo", "text": "FORGE TEST: you should never see this"}
    tests = []
    tests.append(("plain JSON", json.dumps({"bot": "echo", "text": "FORGE TEST plain"})))
    tests.append(("wrong key", seal(secrets.token_bytes(32), B2P, good)))
    env = json.loads(seal(l["key"], B2P, dict(good, id=secrets.token_hex(12))))
    env["ct"] = b64(bytes([ub64(env["ct"])[0] ^ 1]) + ub64(env["ct"])[1:])
    tests.append(("tampered ciphertext", json.dumps(env)))
    tests.append(("wrong direction", seal(l["key"], P2B, dict(good, id=secrets.token_hex(12)))))
    tests.append(("stale (1h old)", seal(l["key"], B2P, dict(good, id=secrets.token_hex(12), ts=now_ms() - 3600_000))))
    rid = secrets.token_hex(12)
    replay = seal(l["key"], B2P, {"id": rid, "ts": now_ms(), "bot": "echo", "text": "Replay test: this one is genuine and should appear ONCE."})
    tests.append(("genuine (should be accepted once)", replay))
    tests.append(("replay of the genuine one", replay))
    for name, body in tests:
        st = post(s, d, body); print(f"posted {name}: HTTP {st}"); time.sleep(0.4)

def cmd_unpair(a):
    for p in (LINK, SEEN, CURSOR, INBOX):
        if os.path.exists(p): os.remove(p)
    print("Link forgotten.")

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sp = ap.add_subparsers(dest="cmd", required=True)
    p = sp.add_parser("pair"); p.add_argument("code"); p.add_argument("--name", default="Grok Bot"); p.add_argument("--wait", type=int, default=0, help="seconds to wait for the PC (default: until the code expires)"); p.add_argument("--server", default=DEFAULT_SERVER); p.set_defaults(f=cmd_pair)
    sp.add_parser("status").set_defaults(f=cmd_status)
    p = sp.add_parser("probe"); p.add_argument("--wait", type=int, default=30); p.set_defaults(f=cmd_probe)
    for name in ("send", "notify"):
        p = sp.add_parser(name); p.add_argument("bot", nargs="?"); p.add_argument("text"); p.add_argument("--as", dest="as_", metavar="BEACON")
        p.add_argument("--title"); p.add_argument("--re"); p.add_argument("--name", help="your bot's name, for the new-tab prompt"); p.set_defaults(f=cmd_send)
    p = sp.add_parser("attention"); p.add_argument("bot"); p.add_argument("text"); p.add_argument("--title"); p.add_argument("--name")
    p.set_defaults(f=cmd_send, as_=None, re=None, attention=True)
    p = sp.add_parser("avatar"); p.add_argument("bot", metavar="BEACON"); p.add_argument("file", nargs="?"); p.add_argument("--size", type=int, default=96); p.add_argument("--clear", action="store_true"); p.set_defaults(f=cmd_avatar)
    p = sp.add_parser("pong"); p.add_argument("bot"); p.add_argument("--text", default=DEFAULT_PONG); p.set_defaults(f=cmd_pong)
    p = sp.add_parser("read"); p.add_argument("--since", default="new"); p.add_argument("--bot"); p.add_argument("--json", action="store_true"); p.add_argument("--unhandled", action="store_true"); p.set_defaults(f=cmd_read)
    p = sp.add_parser("mark-handled"); p.add_argument("ids", nargs="+"); p.add_argument("--bot"); p.set_defaults(f=cmd_mark)
    p = sp.add_parser("mark-notified"); p.add_argument("ids", nargs="+"); p.add_argument("--bot"); p.set_defaults(f=cmd_mark_notified)
    sp.add_parser("agents").set_defaults(f=cmd_agents)
    p = sp.add_parser("agent-set"); p.add_argument("bot", metavar="BEACON"); p.add_argument("--agent-id"); p.add_argument("--name"); p.add_argument("--ping", help='what the bot does when pinged, in one sentence ("" removes it)'); p.set_defaults(f=cmd_agent_set)
    p = sp.add_parser("agent-remove"); p.add_argument("bot", metavar="BEACON"); p.set_defaults(f=cmd_agent_remove)
    p = sp.add_parser("route"); p.add_argument("--json", action="store_true"); p.set_defaults(f=cmd_route)
    sp.add_parser("echo").set_defaults(f=cmd_echo)
    sp.add_parser("forge-test").set_defaults(f=cmd_forge)
    sp.add_parser("unpair").set_defaults(f=cmd_unpair)
    a = ap.parse_args(); a.f(a)

if __name__ == "__main__": main()
