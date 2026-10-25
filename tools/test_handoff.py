#!/usr/bin/env python3
"""Offline tests for 1.2.7 handoff sparks (no network, no real link; a temporary BEACONS_HOME).

  - beaconctl handoff: argument checks, --dry-run, the version gate (probe and relay post stubbed), the message itself
Usage: python3 tools/test_handoff.py
"""
import base64, contextlib, importlib.util, io, json, os, secrets, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
home = tempfile.mkdtemp(prefix="bb-handoff-"); os.environ["BEACONS_HOME"] = home; os.environ.pop("BEACONS_SECRETS", None)
spec = importlib.util.spec_from_file_location("beaconctl", os.path.join(HERE, "beaconctl.py"))
bc = importlib.util.module_from_spec(spec); spec.loader.exec_module(bc)
assert bc.SECRETS.startswith(home), "test must never touch real secrets"
fails = []
def check(cond, what):
    print(("ok   " if cond else "FAIL ") + what)
    if not cond: fails.append(what)

def run(*argv):
    """beaconctl main() with argv; returns (exit code, stdout, stderr)"""
    out, err = io.StringIO(), io.StringIO(); code = 0
    old = sys.argv; sys.argv = ["beaconctl.py"] + list(argv)
    try:
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err): bc.main()
    except SystemExit as e:
        code = e.code if isinstance(e.code, int) else (0 if e.code is None else 1)
        if isinstance(e.code, str): err.write(e.code)
    finally: sys.argv = old
    return code, out.getvalue(), err.getvalue()

# ---- versions
check(bc._ver("1.2.6") == (1, 2, 6) and bc._ver("1.2.7-test") == (1, 2, 7) and bc._ver("garbage") == (0,), "version parsing (pre-release tag ignored)")

# ---- dry run: no link needed, nothing posted
posted = []; probes = []
bc.post = lambda server, topic, body, until=None: posted.append(body) or 200
code, out, err = run("handoff", "research", "writer", "drafting the summary", "--dry-run")
p = json.loads(out.split("payload: ", 1)[1].splitlines()[0]) if "payload: " in out else {}
check(code == 0 and "nothing sent" in out and not posted, "dry run: exit 0, nothing posted")
check(p.get("kind") == "handoff" and p.get("bot") == "research" and p.get("to") == "writer" and p.get("label") == "drafting the summary"
      and p.get("text") == "" and p.get("silent") == "true" and p.get("from") == "bot", "dry run: the handoff payload")
check("sealed size: " in out and int(out.split("sealed size: ")[1].split()[0]) <= bc.MAX_ENVELOPE, "dry run: sealed size under the limit")
code, out, err = run("handoff", "a", "a", "--dry-run"); check(code == 1 and not posted, "same beacon twice: refused")
code, out, err = run("handoff", "a", "b c", "--dry-run"); check(code == 1, "id with a space: refused")
code, out, err = run("handoff", "a", "x" * 65, "--dry-run"); check(code == 1, "id over 64 chars: refused")
code, out, err = run("handoff", "a", "b", "\U0001F680" * 60 + "\nsecond line", "--dry-run")
lp = json.loads(out.split("payload: ", 1)[1].splitlines()[0])
check(len(lp["label"]) <= 40 and "\n" not in lp["label"] and lp["label"].endswith("\u2026"), "label: one line, cut to 40 chars")
big = bc.handoff_payload("b" * 64, "c" * 64, "\U0001F680" * 40)
check(len(bc.seal(secrets.token_bytes(32), bc.B2P, big).encode()) <= bc.MAX_ENVELOPE, "largest handoff fits one relay message")
check("label" not in bc.handoff_payload("a", "b", "   "), "blank label left out")

# ---- the version gate (fake link, probe + post stubbed)
key = secrets.token_bytes(32)
os.makedirs(bc.SECRETS, mode=0o700, exist_ok=True)
bc._write_secret(bc.LINK, json.dumps({"server": "https://relay.invalid", "down": "d", "up": "u", "key": base64.b64encode(key).decode(), "paired": 0}))
answer = {"ver": None}
def fake_probe(prefs=None, tries=1, per=30):
    probes.append(1)
    if answer["ver"] is None: return None, 15, 1
    bc._note_ver(answer["ver"]); return {"kind": "probe-ack", "ver": answer["ver"]}, 2, 1
bc._probe = fake_probe
def gate(label, expect_sent, expect_probe, *extra):
    posted.clear(); probes.clear()
    code, out, err = run("handoff", "research", "writer", *extra)
    sent = len(posted) == 1
    check(code == 0 and sent == expect_sent and bool(probes) == expect_probe, f"{label}: {'sent' if sent else 'skipped'}{', probed' if probes else ''} ({out.strip()})")
    return out
def set_cache(ver, age_s):
    if ver is None:
        if os.path.exists(bc.APPVER): os.remove(bc.APPVER)
    else: bc._write_secret(bc.APPVER, json.dumps({"ver": ver, "at": bc.now_ms() - int(age_s * 1000)}))
set_cache(None, 0); answer["ver"] = None;         gate("unknown version, no answer", False, True)
set_cache(None, 0); answer["ver"] = "1.2.6";      gate("unknown version, app 1.2.6", False, True)
check(bc._known_ver()[0] == "1.2.6", "probe ack version remembered")
answer["ver"] = "1.2.7-test";                     gate("app 1.2.6 seen 1 min ago: no new probe", False, False)
set_cache("1.2.6", 7 * 3600);                     gate("app 1.2.6 seen 7 h ago: probes again, now 1.2.7-test", True, True)
set_cache("1.2.7-test", 60);                      gate("app 1.2.7-test seen 1 min ago: sent at once", True, False)
set_cache("1.2.7", 25 * 3600); answer["ver"] = "1.2.7"; gate("app 1.2.7 seen 25 h ago: checks again", True, True)
set_cache(None, 0); answer["ver"] = None;         gate("--force: no version check", True, False, "--force")
env = posted[0]; got, why = bc.open_env(key, bc.B2P, env)
check(why is None and got["kind"] == "handoff" and got["bot"] == "research" and got["to"] == "writer" and got["text"] == "", "posted handoff opens with the link key")
check(bc.open_env(key, bc.P2B, env)[0] is None, "a handoff is bot->PC only (does not open as a PC->bot reply)")

print("ALL OK" if not fails else f"{len(fails)} FAILED"); sys.exit(1 if fails else 0)
