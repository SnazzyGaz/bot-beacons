using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace BotBeacons
{
    // Where to send a "wake up" when the user replies to a beacon: a Grok Bot webhook routine (or any HTTPS endpoint).
    // Auth styles:
    //   grokbot  (default)  Authorization: Bearer <key>  +  X-Automation-Key: <key>   (Grok Bot / Cursor routine webhooks)
    //   bearer              Authorization: Bearer <key>
    //   hmac                X-Beacon-Signature: sha256=<hex HMAC-SHA256(key, raw body)>
    //   header:<Name>       <Name>: <key>
    class WakeHook
    {
        public string Url = "", Key = "", Auth = "grokbot";
        public bool Valid
        {
            get
            {
                Uri u;
                return Key.Length > 0 && Uri.TryCreate(Url, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));
            }
        }
        public string Host { get { Uri u; return Uri.TryCreate(Url, UriKind.Absolute, out u) ? u.Host : "?"; } }

        // People paste the whole "Authorization: Bearer crsr_..." line from the routine panel; accept that too.
        public static string CleanKey(string k)
        {
            k = (k ?? "").Trim();
            if (k.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) k = k.Substring(14).Trim();
            if (k.StartsWith("X-Automation-Key:", StringComparison.OrdinalIgnoreCase)) k = k.Substring(17).Trim();
            if (k.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) k = k.Substring(7).Trim();
            return k;
        }
    }

    // The notice never contains message text: only which beacon(s), message ids and times. The routine then fetches
    // and verifies the encrypted replies itself (beaconctl.py read --unhandled). Replies within 10 s become one wake.
    static class Wake
    {
        public const int DebounceMs = 10000;
        static readonly int[] RetryMs = { 5000, 20000, 60000 };
        static readonly object gate = new object();
        static readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public static WakeHook Default = new WakeHook();
        public static readonly Dictionary<string, WakeHook> PerBeacon = new Dictionary<string, WakeHook>(StringComparer.OrdinalIgnoreCase);
        public static int Sent, Failed;

        class Pending { public WakeHook Hook; public List<string> Beacons = new List<string>(); public List<string> Ids = new List<string>(); public List<string> Pings = new List<string>(); public long First, Last; }
        static readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        static readonly HashSet<Timer> timers = new HashSet<Timer>();

        // ------------------------------------------------------------------ storage (DPAPI, like the relay link)
        public static void Load()
        {
            string enc = Settings.Get("wake", "");
            if (enc.Length == 0) return;
            try
            {
                var o = json.DeserializeObject(Crypto.Unprotect(enc)) as Dictionary<string, object>;
                Default = FromDict(o.ContainsKey("default") ? o["default"] as Dictionary<string, object> : null);
                if (o.ContainsKey("beacons") && o["beacons"] is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)o["beacons"]) { var h = FromDict(kv.Value as Dictionary<string, object>); if (h.Valid) PerBeacon[kv.Key] = h; }
            }
            catch (Exception ex) { Log.Write("Could not unlock the wake hook settings (" + ex.GetType().Name + "); wake is off until set again."); }
        }
        static WakeHook FromDict(Dictionary<string, object> d)
        {
            if (d == null) return new WakeHook();
            var h = new WakeHook { Url = Crypto.Str(d, "url"), Key = Crypto.Str(d, "key"), Auth = Crypto.Str(d, "auth") };
            if (h.Auth.Length == 0) h.Auth = "grokbot";
            return h;
        }
        static Dictionary<string, object> ToDict(WakeHook h) { return new Dictionary<string, object> { { "url", h.Url }, { "key", h.Key }, { "auth", h.Auth } }; }

        public static void Save()
        {
            lock (gate)
            {
                var beacons = new Dictionary<string, object>();
                foreach (var kv in PerBeacon) if (kv.Value.Valid) beacons[kv.Key] = ToDict(kv.Value);
                if (!Default.Valid && beacons.Count == 0) Settings.Set("wake", "");
                else Settings.Set("wake", Crypto.Protect(json.Serialize(new Dictionary<string, object> { { "default", ToDict(Default) }, { "beacons", beacons } })));
            }
            Settings.Save();
        }

        public static WakeHook Get(string scope) { lock (gate) { if (scope == null) return Default; WakeHook h; return PerBeacon.TryGetValue(scope, out h) ? h : new WakeHook(); } }
        public static void Set(string scope, WakeHook h)
        {
            lock (gate) { if (scope == null) Default = h ?? new WakeHook(); else if (h == null || !h.Valid) PerBeacon.Remove(scope); else PerBeacon[scope] = h; }
            Save();
            Log.Write("Wake hook " + (h != null && h.Valid ? "set" : "removed") + " for " + (scope ?? "all beacons") + (h != null && h.Valid ? " (" + h.Host + ", " + h.Auth + ")" : "") + ".");
        }
        public static void ClearAll() { lock (gate) { Default = new WakeHook(); PerBeacon.Clear(); } Save(); Log.Write("All wake hooks removed."); }

        public static WakeHook For(string beacon)
        {
            lock (gate) { WakeHook h; if (PerBeacon.TryGetValue(beacon, out h) && h.Valid) return h; return Default.Valid ? Default : null; }
        }
        public static string StatusLine()
        {
            lock (gate)
            {
                var ids = new List<string>(PerBeacon.Keys);
                return "wake default=" + (Default.Valid ? "set(" + Default.Auth + ")" : "none") + " perBeacon=" + (ids.Count == 0 ? "none" : string.Join(",", ids.ToArray())) + " sent=" + Sent + " failed=" + Failed + " pending=" + pending.Count;
            }
        }

        // ------------------------------------------------------------------ queue, debounce, send
        // Called after the encrypted reply was posted to the relay.
        public static void Queue(string beacon, string msgId, long ts, bool ping = false)
        {
            var hook = For(beacon);
            if (hook == null) return;
            lock (gate)
            {
                Pending p;
                if (!pending.TryGetValue(hook.Url, out p))
                {
                    p = new Pending { Hook = hook, First = ts };
                    pending[hook.Url] = p;
                    string url = hook.Url;
                    After(DebounceMs, () => Fire(url));
                    Log.Write("Wake queued for '" + beacon + "' (sends in " + DebounceMs / 1000 + " s).");
                }
                if (!p.Beacons.Contains(beacon)) p.Beacons.Add(beacon);
                p.Ids.Add(msgId); if (ping) p.Pings.Add(msgId); p.Last = ts;
            }
        }

        static void Fire(string url)
        {
            Pending p;
            lock (gate) { if (!pending.TryGetValue(url, out p)) return; pending.Remove(url); }
            var body = json.Serialize(new Dictionary<string, object> {
                { "type", "beacon-reply" }, { "v", 1 }, { "beacons", p.Beacons.ToArray() }, { "ids", p.Ids.ToArray() },
                { "count", p.Ids.Count }, { "pings", p.Pings.ToArray() }, { "first_ts", p.First }, { "last_ts", p.Last }, { "sent_ts", Crypto.NowMs() } });
            Attempt(p.Hook, body, string.Join(",", p.Beacons.ToArray()) + ", " + p.Ids.Count + " repl" + (p.Ids.Count == 1 ? "y" : "ies"), 0);
        }

        static void Attempt(WakeHook hook, string body, string what, int n)
        {
            string err; int code = Post(hook, body, out err);
            if (code >= 200 && code < 300) { Sent++; Log.Write("Wake delivered (" + what + ") to " + hook.Host + ": HTTP " + code + (n > 0 ? " after " + (n + 1) + " tries" : "") + "."); return; }
            bool retry = code < 0 || code == 429 || code >= 500;
            if (retry && n < RetryMs.Length)
            {
                Log.Write("Wake to " + hook.Host + " failed (" + (code < 0 ? err : "HTTP " + code) + "); retrying in " + RetryMs[n] / 1000 + " s.");
                After(RetryMs[n], () => Attempt(hook, body, what, n + 1));
                return;
            }
            Failed++;
            Log.Write("Wake to " + hook.Host + " gave up (" + (code < 0 ? err : "HTTP " + code) + ")" + (code == 401 || code == 403 ? ": check the sender key" : code == 404 ? ": check the webhook URL" : "") + ". The reply itself is safe on the relay and will be picked up on the next wake.");
        }

        public static int Test(WakeHook hook, out string err)
        {
            var body = json.Serialize(new Dictionary<string, object> { { "type", "beacon-wake-test" }, { "v", 1 }, { "sent_ts", Crypto.NowMs() } });
            int code = Post(hook, body, out err);
            Log.Write("Wake test to " + hook.Host + ": " + (code < 0 ? err : "HTTP " + code) + ".");
            return code;
        }

        static int Post(WakeHook hook, string body, out string err)
        {
            err = null;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                var req = (HttpWebRequest)WebRequest.Create(hook.Url);
                req.Method = "POST"; req.ContentType = "application/json"; req.UserAgent = "BotBeacons/1.0";
                req.Timeout = 8000; req.ReadWriteTimeout = 8000; req.AllowAutoRedirect = false;
                string a = hook.Auth ?? "grokbot";
                if (a == "grokbot") { req.Headers["Authorization"] = "Bearer " + hook.Key; req.Headers["X-Automation-Key"] = hook.Key; }
                else if (a == "bearer") req.Headers["Authorization"] = "Bearer " + hook.Key;
                else if (a == "hmac") using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(hook.Key))) req.Headers["X-Beacon-Signature"] = "sha256=" + Crypto.Hex(h.ComputeHash(bytes));
                else if (a.StartsWith("header:") && a.Length > 7) req.Headers[a.Substring(7)] = hook.Key;
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = (HttpWebResponse)req.GetResponse()) return (int)resp.StatusCode;
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null) { int c = (int)r.StatusCode; r.Close(); return c; }
                err = ex.Status.ToString(); return -1;
            }
            catch (Exception ex) { err = ex.GetType().Name; return -1; }
        }

        static void After(int ms, Action a)
        {
            Timer t = null;
            t = new Timer(_ =>
            {
                lock (timers) timers.Remove(t);
                t.Dispose();
                try { a(); } catch (Exception ex) { Log.Write("Wake error: " + ex.GetType().Name); }
            }, null, Timeout.Infinite, Timeout.Infinite);
            lock (timers) timers.Add(t);
            t.Change(ms, Timeout.Infinite);
        }
    }
}
