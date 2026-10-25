#!/usr/bin/env python3
"""Offline tests for 1.2.7 `beaconctl.py skin check` and `skin pack` (no network; a temporary BEACONS_HOME).
Usage: python3 tools/test_skins.py [OUT.zip]   (OUT.zip: also write a packed fixture skin for SkinHarness to install)
"""
import contextlib, hashlib, importlib.util, io, json, os, shutil, sys, tempfile, zipfile
HERE = os.path.dirname(os.path.abspath(__file__))
home = tempfile.mkdtemp(prefix="bb-skins-"); os.environ["BEACONS_HOME"] = home; os.environ.pop("BEACONS_SECRETS", None)
spec = importlib.util.spec_from_file_location("beaconctl", os.path.join(HERE, "beaconctl.py"))
bc = importlib.util.module_from_spec(spec); spec.loader.exec_module(bc)
spec2 = importlib.util.spec_from_file_location("fx", os.path.join(HERE, "make_skin_fixtures.py"))
fx = importlib.util.module_from_spec(spec2); spec2.loader.exec_module(fx)
fails = []
def check(cond, what):
    print(("ok   " if cond else "FAIL ") + what)
    if not cond: fails.append(what)
def run(*argv):
    out = io.StringIO(); code = 0; old = sys.argv; sys.argv = ["beaconctl.py"] + list(argv)
    try:
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out): bc.main()
    except SystemExit as e: code = e.code if isinstance(e.code, int) else (0 if e.code is None else 1)
    finally: sys.argv = old
    return code, out.getvalue()

def make(name, doc, extra=None):
    d = os.path.join(home, name); shutil.rmtree(d, ignore_errors=True); os.makedirs(os.path.join(d, "art"))
    for n, data in fx.GOOD.items():
        if n != "skin.json": open(os.path.join(d, n), "wb").write(data)
    for n, data in (extra or {}).items():
        os.makedirs(os.path.dirname(os.path.join(d, n)) or d, exist_ok=True); open(os.path.join(d, n), "wb").write(data)
    json.dump(doc, open(os.path.join(d, "skin.json"), "w")); return d

good = make("good-skin", fx.SKIN, {"notes.txt": b"x"})
code, out = run("skin", "check", good)
check(code == 0 and "skin OK: Fixture (1 beacon entry, 2 looks" in out and "notes.txt is not a skin file type" in out, "check: a good skin passes (txt warned)")
z1 = os.path.join(home, "a.zip"); z2 = os.path.join(home, "b.zip")
code, out = run("skin", "pack", good, "-o", z1); run("skin", "pack", good, "-o", z2)
names = zipfile.ZipFile(z1).namelist()
check(code == 0 and "skin.json" in names and "art/a.png" in names and "notes.txt" not in names and all(not n.startswith("/") and ".." not in n for n in names), "pack: relative names, only skin files")
check(hashlib.sha256(open(z1, "rb").read()).digest() == hashlib.sha256(open(z2, "rb").read()).digest(), "pack: the same folder packs to the same bytes")
code, out = run("skin", "pack", good, "-o", os.path.join(good, "x.zip")); check(code == 1 and not os.path.exists(os.path.join(good, "x.zip")), "pack: refuses to write inside the skin folder")
code, out = run("skin", "pack", good); check(code == 0 and os.path.isfile(good + ".zip"), "pack: default output is DIR.zip")

def bad(label, doc, expect, extra=None, warn=False):
    d = make("bad", doc, extra); code, out = run("skin", "check", d)
    check((code == 0 if warn else code == 1) and expect in out, label + " (" + (out.strip().splitlines() or [""])[-1 if not warn else 0][:90] + ")")
S = lambda **k: dict(fx.SKIN, **k)
bad("private path refused", S(credits="from C:\\Users\\me\\art"), "private path")
bad("workspace path refused", S(looks=[{"look": "a", "art": "/workspace/x.png"}]), "private path")
bad("missing art refused", S(looks=[{"look": "a", "art": "art/nope.png"}]), "not in the skin folder")
bad("art outside the folder refused", S(looks=[{"look": "a", "art": "../good-skin/art/a.png"}]), "not in the skin folder")
bad("non-image art refused", S(looks=[{"look": "a", "art": "art/a.jpg"}]), "must be a .png or .gif")
bad("overlays need a base", S(looks=[{"look": "a", "art": "art/a.png", "overlays": ["art/b.png"]}]), "overlays need a base")
bad("duplicate look refused", S(looks=[{"look": "a", "art": "art/a.png"}, {"look": "a", "art": "art/b.png"}]), "listed twice")
bad("bad look id refused", S(looks=[{"look": "a b", "art": "art/a.png"}]), "may only use")
bad("huge art refused", S(), "at most 1024", {"art/a.png": fx.png(1500, 10)})
bad("no entries refused", {"name": "x", "credits": "y"}, "no beacons or looks")
bad("busy idle warned", S(anim={"idleEvery": [3, 7]}), "idleEvery starts at 3", warn=True)
bad("big hop warned", S(looks=[{"look": "a", "art": "art/a.png", "anim": {"idle": [{"name": "boing", "steps": [{"hop": 20}]}]}}]), "hops 20 px", warn=True)
bad("flicker warned", S(looks=[{"look": "a", "art": "art/a.png", "anim": {"idle": [{"name": "fl", "steps": [{"flicker": "true"}]}]}}]), "flickers", warn=True)
bad("no credits warned", {k: v for k, v in fx.SKIN.items() if k != "credits"}, 'no "credits"', warn=True)
d = make("bad name!", fx.SKIN); code, out = run("skin", "check", d); check(code == 1 and "folder name" in out, "folder name checked")

if len(sys.argv) > 1: shutil.copy(z1, sys.argv[1]); print("packed fixture written to " + sys.argv[1])
print("ALL OK" if not fails else f"{len(fails)} FAILED"); sys.exit(1 if fails else 0)
