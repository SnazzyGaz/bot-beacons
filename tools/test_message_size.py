#!/usr/bin/env python3
"""Offline test of the 1.1.6 message size limit (no network, no real link; a temporary BEACONS_HOME).

Every message beaconctl accepts must seal to at most MAX_ENVELOPE (3900) bytes, under ntfy.sh's 4096-byte limit,
and must decrypt and verify. Anything longer must be refused with exit 1 and nothing posted.
Optional: --csharp FILE also opens envelopes sealed by the C# app (tools/SizeHarness.cs.txt writes them) and writes
envelopes for it to open (FILE + ".py").   Usage: python3 tools/test_message_size.py [--csharp /tmp/x/cs_envs.json]
"""
import base64, importlib.util, io, json, os, random, secrets, sys, tempfile, contextlib
HERE = os.path.dirname(os.path.abspath(__file__))
home = tempfile.mkdtemp(prefix="bb-size-"); os.environ["BEACONS_HOME"] = home
spec = importlib.util.spec_from_file_location("beaconctl", os.path.join(HERE, "beaconctl.py"))
bc = importlib.util.module_from_spec(spec); spec.loader.exec_module(bc)
LIMIT = 4096; fails = []
def check(cond, what):
    if not cond: fails.append(what); print("FAIL:", what)

key = secrets.token_bytes(32)
UNITS = {"ascii": "a", "emoji": "\U0001F600", "accented": "\u00e9", "quote+newline": "\"\n", "cjk": "\u6f22",
         "mixed": "Caf\u00e9 \u2615 \"ok\"\n\u65e5\u672c \U0001F680 <b>&amp;</b> \\ \t"}
def payload(text, title=None, bot="research", **extra):
    p = {"id": secrets.token_hex(12), "ts": bc.now_ms(), "from": "bot", "bot": bot, "text": text}
    if title is not None: p["title"] = title
    p.update(extra); return p

# 1. per kind: the largest accepted message, and one more character is refused
print(f"{'kind':14s} {'max chars':>9s} {'text bytes':>10s} {'envelope':>8s}")
for kind, unit in UNITS.items():
    base = payload(unit * 4000)
    k = bc.fit_chars(key, bc.B2P, base)
    p = dict(base, text=base["text"][:k]); env = bc.seal_checked(key, bc.B2P, p); n = len(env.encode())
    got, why = bc.open_env(key, bc.B2P, env)
    check(n <= bc.MAX_ENVELOPE < LIMIT and got == p, f"{kind}: largest accepted envelope {n} B / decrypts {why is None}")
    try: bc.seal_checked(key, bc.B2P, dict(base, text=base["text"][:k + 1])); check(False, f"{kind}: k+1 accepted")
    except bc.TooLong: pass
    print(f"{kind:14s} {k:9d} {len(p['text'].encode()):10d} {n:8d}")

# 2. random sweep: every accepted envelope is < 4096 and round-trips; every refused one really was over
random.seed(7); acc = ref = 0; biggest = 0
for i in range(1500):
    kind = random.choice(list(UNITS)); n_chars = random.randint(0, 3200)
    text = (UNITS[kind] * (n_chars // len(UNITS[kind]) + 1))[:n_chars]
    title = random.choice([None, "", "Build finished", "T" * random.randint(50, 1500), "\U0001F525" * random.randint(1, 400)])
    extra = random.choice([{}, {"kind": "attention", "agent": secrets.token_hex(16)}, {"re": secrets.token_hex(12)}])
    p = payload(text, title, **extra)
    try:
        env = bc.seal_checked(key, bc.B2P, p); size = len(env.encode()); biggest = max(biggest, size); acc += 1
        got, why = bc.open_env(key, bc.B2P, env)
        check(size < LIMIT and got == p and why is None, f"sweep {i}: accepted {size} B, decrypt {why}")
        check(bc.open_env(secrets.token_bytes(32), bc.B2P, env)[1] == "bad signature", f"sweep {i}: wrong key not rejected")
    except bc.TooLong as e:
        ref += 1; check(e.n > bc.MAX_ENVELOPE, f"sweep {i}: refused at {e.n} B")
print(f"sweep: {acc} accepted (largest {biggest} B), {ref} refused")

# 3. the send command end to end, relay post stubbed: too long -> exit 1, nothing posted; long title counted
os.makedirs(os.path.join(home, "secrets"), mode=0o700)
bc._write_secret(bc.LINK, json.dumps({"server": "https://relay.invalid", "down": "d", "up": "u", "key": base64.b64encode(key).decode(), "paired": 0}))
posted = []; bc.post = lambda server, topic, body, until=None: posted.append(body) or 200
class A: pass
def send(text, title=None, attention=False):
    a = A(); a.bot = "research"; a.as_ = None; a.text = text; a.title = title; a.re = None; a.attention = attention
    out, err = io.StringIO(), io.StringIO()
    try:
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err): bc.cmd_send(a)
        return 0, out.getvalue()
    except SystemExit as e: return (1 if isinstance(e.code, str) else e.code), str(e.code)
cases = [("2400 ASCII", "a" * 2400, None), ("600 emoji", "\U0001F600" * 600, None), ("1200 accented", "\u00e9" * 1200, None),
         ("1200 quote+newline pairs", "\"\n" * 1200, None), ("800 CJK", "\u6f22" * 800, None),
         ("short text, 3000-char title", "hi", "T" * 3000), ("1000 ASCII, 1500-char title", "a" * 1000, "T" * 1500),
         ("600 CJK", "\u6f22" * 600, None), ("600 emoji (attention)", "\U0001F600" * 600, None)]
print(f"\n{'send':30s} {'result':8s} envelope / message")
for name, text, title in cases:
    before = len(posted); code, msg = send(text, title, attention="attention" in name)
    if code == 0:
        body = posted[-1]; size = len(body.encode()); got, why = bc.open_env(key, bc.B2P, body)
        check(len(posted) == before + 1 and size < LIMIT and why is None and got["text"] == text, f"send {name}")
        print(f"{name:30s} {'sent':8s} {size} B, decrypts and verifies")
    else:
        check(len(posted) == before and code == 1 and "Not sent" in msg, f"send {name}: refused but posted / no clear error")
        print(f"{name:30s} {'refused':8s} exit {code}: {msg[:150]}...")
# invalid UTF-8 from the shell (surrogateescape) must not crash
code, _ = send("ok \udcff bytes"); check(code == 0, "send with stray byte")

# 3b. 1.2.2 (Tabs skin): avatar chunks follow the same rule. Every sealed chunk <= MAX_ENVELOPE, at most 32 chunks
# (the PC's Avatars.MaxChunks), and they reassemble to the exact bytes; a chunk size that would not fit is shrunk
import hashlib
def reassemble(envs):
    ps = [bc.open_env(key, bc.B2P, e) for e in envs]
    check(all(w is None for _, w in ps), "avatar: a chunk did not verify")
    ps = sorted((p for p, _ in ps), key=lambda p: p["seq"])
    return b"".join(base64.b64decode(p["data"]) for p in ps), ps
worst = 0
for bot in ["a", "orb", "research", "x" * 40, "x" * 64, "\u00e9" * 40]:
    for size in (1, 1799, 1800, 1801, 12000, 24 * 1024):
        png = os.urandom(size)   # random bytes: incompressible, the worst case for the base64 length
        envs = bc.avatar_envelopes(key, bot, png); sizes = [len(e.encode()) for e in envs]; worst = max(worst, max(sizes))
        data, ps = reassemble(envs)
        check(max(sizes) <= bc.MAX_ENVELOPE and len(envs) <= 32 and data == png and ps[0]["sha256"] == hashlib.sha256(png).hexdigest()
              and all(p["n"] == len(envs) and p["size"] == size for p in ps), f"avatar {len(bot)}-char id, {size} B: {max(sizes)} B x {len(envs)}")
print(f"\navatar: largest sealed chunk {worst} B (limit {bc.MAX_ENVELOPE}) at the default {bc.AVATAR_CHUNK}-byte chunk")
saved = bc.AVATAR_CHUNK; bc.AVATAR_CHUNK = 2600   # too big on purpose: must shrink, not post an oversize chunk
envs = bc.avatar_envelopes(key, "research", os.urandom(24 * 1024)); bc.AVATAR_CHUNK = saved
check(max(len(e.encode()) for e in envs) <= bc.MAX_ENVELOPE and len(envs) <= 32, "avatar: oversize chunk setting not shrunk")
print(f"avatar: a 2600-byte chunk setting shrinks to fit ({len(envs)} chunks, largest {max(len(e.encode()) for e in envs)} B)")
try: bc.avatar_envelopes(key, "x" * 4000, b"png"); check(False, "avatar: hopeless id accepted")
except bc.TooLong: pass
try:
    from PIL import Image
    img = os.path.join(home, "pic.png"); Image.effect_noise((400, 300), 90).convert("RGB").save(img)   # noisy = hard to compress
    a = A(); a.bot = "research"; a.file = img; a.size = 96; a.clear = False
    before = len(posted); out = io.StringIO()
    with contextlib.redirect_stdout(out): bc.cmd_avatar(a)
    envs = posted[before:]; data, ps = reassemble(envs)
    check(len(envs) >= 1 and max(len(e.encode()) for e in envs) <= bc.MAX_ENVELOPE and data[:8] == b"\x89PNG\r\n\x1a\n" and len(data) <= 24 * 1024,
          "avatar command end to end")
    print(f"avatar command: {out.getvalue().strip()}; largest posted {max(len(e.encode()) for e in envs)} B")
    a.clear = True; before = len(posted)
    with contextlib.redirect_stdout(io.StringIO()): bc.cmd_avatar(a)
    check(len(posted) == before + 1 and bc.open_env(key, bc.B2P, posted[-1])[0].get("clear") == "true", "avatar --clear")
except ImportError:
    print("avatar command: Pillow not installed, end-to-end step skipped")

# 4. optional cross-check with the C# app's Crypto.Seal/Open (envelopes from tools/SizeHarness.cs.txt)
if "--csharp" in sys.argv:
    f = sys.argv[sys.argv.index("--csharp") + 1]; d = json.load(open(f)); k2 = base64.b64decode(d["key"])
    ok = 0
    for e in d["envs"]:
        got, why = bc.open_env(k2, bc.P2B, e["env"])
        check(why is None and got["text"] == e["text"] and len(e["env"].encode()) == e["bytes"], f"C# envelope {e['name']}: {why}")
        ok += why is None
    print(f"\nC# -> Python: {ok}/{len(d['envs'])} C#-sealed replies verified and decrypted here")
    out = []
    for kind, unit in UNITS.items():
        p = payload(unit * 4000, "Title \u00e9\U0001F600"); kk = bc.fit_chars(key, bc.B2P, p); p["text"] = p["text"][:kk]
        p["ts"] = bc.now_ms(); out.append({"name": kind, "text": p["text"], "title": p["title"], "env": bc.seal_checked(k2, bc.B2P, p)})
    json.dump({"envs": out}, open(f + ".py", "w"), ensure_ascii=False)
    print(f"Python -> C#: wrote {len(out)} max-size envelopes to {os.path.basename(f)}.py")

print("\nALL PASSED" if not fails else f"\n{len(fails)} FAILED"); sys.exit(1 if fails else 0)
