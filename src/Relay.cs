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
    public class PetMessage
    {
        public string Id = "", Bot = "", Title = "", Text = "";
        public string Kind = "";      // "" = normal message, "attention" = the user must act in the Grok Bot app, "ping" = user ping
        public string AgentId = "";   // attention: which Grok Bot agent chat "Open in chat" should open
        public bool FromUser;
        public bool Verified;   // 1.2.5: came over the paired relay (decrypted, signed, fresh, not a replay). Only these can ask for a new tab
        public string BotName = "";   // 1.2.5: optional "name" a bot sends (shown in the new-tab prompt)
        public bool Silent;   // 1.1.5: shown as usual but never plays a sound (status/heartbeat/probe-ack kinds, or "silent": true)
        public bool IsAttention { get { return Kind == "attention"; } }
        public DateTime Time = DateTime.Now;
    }

    // Streams (or, with relayMode=poll, polls) <server>/<down>/json?since=<last message time>. Every body must be a valid
    // encrypted, signed, fresh, never-seen-before envelope; anything else is dropped and counted.
    // Replies are sealed and posted to <server>/<up>.
    class Relay
    {
        public event Action<PetMessage> MessageArrived;
        public int Rejected;
        Thread thread; volatile bool running;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        int failures;
        const long MaxAgeMs = 15 * 60 * 1000, MaxFutureMs = 2 * 60 * 1000;
        static readonly object seenGate = new object();
        static Dictionary<string, long> seen;   // message id -> ts, kept for replay protection

        public void Start()
        {
            running = true;
            if (ServicePointManager.DefaultConnectionLimit < 8) ServicePointManager.DefaultConnectionLimit = 8;   // the stream holds one connection; replies need another
            thread = new Thread(Loop) { IsBackground = true, Name = "relay-poll" };
            thread.Start();
        }
        public void Stop() { running = false; wake.Set(); var r = current; if (r != null) try { r.Abort(); } catch { } }

        // "connected", "reconnecting (n)", "connecting", "not paired" (shown in the tray tooltip and status.txt)
        public volatile string State = "connecting";
        public DateTime LastOk = DateTime.MinValue;
        public static DateTime SimOfflineUntil = DateTime.MinValue;   // dev hook "netsim <s>": polls fail as if offline
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        // network came back (or a dev hook): drop the current stream and reconnect now instead of waiting out the backoff
        public void Kick() { wake.Set(); var r = current; if (r != null) try { r.Abort(); } catch { } }

        // Default: one long-lived ntfy stream (<server>/<down>/json?since=...), so the relay costs about one request
        // per reconnect instead of one every few seconds. ntfy.sh limits each IP to roughly one request per 5 s, and
        // two apps polling every 5 s from one PC get "429 Too Many Requests". ntfy sends a keepalive about every 45 s;
        // if nothing (message or keepalive) arrives for 100 s the stream is treated as dead and reopened.
        // relayMode=poll in settings.ini falls back to polling every pollSeconds (default 15).
        // While the relay can't be reached (no network after a cold boot, Wi-Fi drop, sleep) it backs off
        // 5, 10, 20, 40, 60, 60... s, and reconnects at once when Windows reports the network is back.
        volatile HttpWebRequest current;
        void Loop()
        {
            bool stream = !string.Equals(Settings.Get("relayMode", "stream"), "poll", StringComparison.OrdinalIgnoreCase);
            int interval = Math.Max(5, Settings.GetInt("pollSeconds", 15)) * 1000;
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (s, e) => { if (e.IsAvailable) { Log.Write("Network available: reconnecting to the relay now."); Kick(); } };
            Log.Write("Relay mode: " + (stream ? "stream" : "poll every " + interval / 1000 + " s") + ".");
            while (running)
            {
                int wait = interval;
                if (Settings.Link.Valid)
                {
                    DateTime opened = DateTime.Now;
                    try
                    {
                        if (DateTime.Now < SimOfflineUntil) throw new System.Net.WebException("simulated offline (dev hook)");
                        if (stream) { StreamOnce(); wait = (DateTime.Now - opened).TotalSeconds < 30 ? 5000 : 1000; }
                        else { Process(Http.Poll(link0(), Settings.Link.Down, Since())); Ok(); }
                    }
                    catch (Exception ex)
                    {
                        if (!running) break;
                        bool hadStream = stream && LastOk > opened;   // the stream worked for a while, then dropped
                        if (hadStream) { Log.Write("Relay stream ended (" + ex.Message + "); reconnecting."); wait = (DateTime.Now - opened).TotalSeconds < 30 ? 5000 : 1000; }
                        else
                        {
                            failures++;
                            wait = Math.Min(60000, 5000 * (1 << Math.Min(4, failures - 1)));
                            State = "reconnecting";
                            if (failures <= 3 || failures % 10 == 0) Log.Write("Relay connection failed (" + failures + "): " + ex.Message + " - retrying in " + wait / 1000 + " s.");
                        }
                    }
                    finally { current = null; }
                }
                else State = "not paired";
                if (running) wake.WaitOne(wait);
            }
        }
        static string link0() { return Settings.Link.Server; }
        static string Since() { return Settings.Get("since", (Crypto.NowMs() / 1000 - 60).ToString()); }
        void Ok()
        {
            if (failures > 0) Log.Write("Relay reachable again after " + failures + " failed attempt" + (failures == 1 ? "" : "s") + ".");
            failures = 0; State = "connected"; LastOk = DateTime.Now;
        }

        void StreamOnce()
        {
            var link = Settings.Link;
            var req = (HttpWebRequest)WebRequest.Create(link.Server.TrimEnd('/') + "/" + link.Down + "/json?since=" + Uri.EscapeDataString(Since()));
            req.Timeout = 15000; req.ReadWriteTimeout = 100000; req.UserAgent = "BotBeacons/1.0"; req.KeepAlive = true;
            current = req;
            using (var resp = Http.GetResponse(req))
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                Ok();
                string line;
                while (running && (line = rd.ReadLine()) != null)
                {
                    if (DateTime.Now < SimOfflineUntil) throw new System.Net.WebException("simulated offline (dev hook)");
                    line = line.Trim(); if (line.Length == 0) continue;
                    Dictionary<string, object> o = null; try { o = json.DeserializeObject(line) as Dictionary<string, object>; } catch { }
                    if (o == null) continue;
                    LastOk = DateTime.Now; State = "connected";
                    if (Crypto.Str(o, "event") == "message") Process(new List<Dictionary<string, object>> { o });
                }
            }
            if (running) throw new System.Net.WebException("stream closed by the server");
        }

        // ntfy's since=<id> is not stable for several messages in the same second, so the cursor is the
        // last message time (inclusive) plus a set of relay ids already handled, which are skipped silently.
        static readonly Dictionary<string, long> handled = new Dictionary<string, long>();
        static bool handledLoaded;
        static string HandledPath { get { return Path.Combine(Settings.Dir, "relay-cursor.txt"); } }

        void Process(List<Dictionary<string, object>> events)
        {
            var link = Settings.Link;
            if (!handledLoaded)
            {
                handledLoaded = true;
                try { if (File.Exists(HandledPath)) foreach (var l in File.ReadAllLines(HandledPath)) { var a = l.Split(' '); long t; if (a.Length == 2 && long.TryParse(a[1], out t)) handled[a[0]] = t; } } catch { }
            }
            bool changed = false;
            foreach (var o in events)
            {
                string nid = Crypto.Str(o, "id"); long ntime; long.TryParse(Crypto.Str(o, "time"), out ntime);
                if (handled.ContainsKey(nid)) continue;
                handled[nid] = ntime; changed = true;
                long cur; if (ntime > 0 && (!long.TryParse(Settings.Get("since", "0"), out cur) || ntime > cur)) Settings.Set("since", ntime.ToString());
                string reason;
                Dictionary<string, object> p = null;
                if (o.ContainsKey("attachment")) reason = "too large for the relay (arrived as a file)";   // over 4096 bytes
                else
                {
                    p = Crypto.Open(link.Key, Crypto.BotToPet, Crypto.Str(o, "message"), out reason);
                    if (p != null) reason = CheckFresh(p);
                }
                if (reason != null) { Rejected++; Log.Write("Rejected relay message " + nid + ": " + reason); continue; }
                // 1.1.3: a silent health probe (beaconctl probe). Log-only: no bubble, glow, sound or wake; answered with a signed ack
                if (Crypto.Str(p, "kind") == "probe") { SendProbeAck(Crypto.Str(p, "id")); continue; }
                // 1.2.0: a bot's profile picture, in chunks (beaconctl avatar). Silent: stored for the Tabs skin, nothing shown or played
                if (Crypto.Str(p, "kind") == "avatar") { Log.Write("Relay: " + Avatars.Receive(p) + "."); continue; }
                var m = new PetMessage { Id = Crypto.Str(p, "id"), Bot = Crypto.Str(p, "bot"), Title = Crypto.Str(p, "title"), Text = Crypto.Str(p, "text"), Kind = Crypto.Str(p, "kind"), AgentId = Crypto.Str(p, "agent"), BotName = Crypto.Str(p, "name"), Verified = true };   // reason == null: Crypto.Open + CheckFresh passed
                string rawKind = m.Kind.ToLowerInvariant();
                m.Silent = rawKind == "probe-ack" || rawKind == "status" || rawKind == "heartbeat" || Crypto.Str(p, "silent").ToLowerInvariant() == "true";
                if (m.Kind != "attention") m.Kind = "";   // bots can only send normal messages or attention calls
                long ts; if (long.TryParse(Crypto.Str(p, "ts"), out ts)) m.Time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ts).ToLocalTime();
                Log.Write("Accepted " + (m.IsAttention ? "attention call" : "message") + " for '" + m.Bot + "' (" + m.Text.Length + " chars).");   // content is never logged
                var h = MessageArrived; if (h != null) h(m);
            }
            if (changed)
            {
                long cut; long.TryParse(Settings.Get("since", "0"), out cut); cut -= 120;
                var drop = new List<string>(); foreach (var kv in handled) if (kv.Value < cut) drop.Add(kv.Key);
                foreach (var k in drop) handled.Remove(k);
                try { var lines = new List<string>(); foreach (var kv in handled) lines.Add(kv.Key + " " + kv.Value); File.WriteAllLines(HandledPath, lines.ToArray()); } catch { }
                Settings.Save();
            }
        }

        // freshness + replay window
        public static string CheckFresh(Dictionary<string, object> p)
        {
            string id = Crypto.Str(p, "id"); long ts;
            if (id.Length < 8 || !long.TryParse(Crypto.Str(p, "ts"), out ts)) return "missing id/timestamp";
            long now = Crypto.NowMs();
            if (ts < now - MaxAgeMs) return "stale (older than 15 min)";
            if (ts > now + MaxFutureMs) return "timestamp in the future";
            lock (seenGate)
            {
                LoadSeen();
                if (seen.ContainsKey(id)) return "replay (already seen)";
                seen[id] = ts;
                var drop = new List<string>(); foreach (var kv in seen) if (kv.Value < now - 2 * MaxAgeMs) drop.Add(kv.Key);
                foreach (var k in drop) seen.Remove(k);
                SaveSeen();
            }
            return null;
        }
        static string SeenPath { get { return Path.Combine(Settings.Dir, "seen.txt"); } }
        static void LoadSeen()
        {
            if (seen != null) return; seen = new Dictionary<string, long>();
            try { if (File.Exists(SeenPath)) foreach (var l in File.ReadAllLines(SeenPath)) { var a = l.Split(' '); long t; if (a.Length == 2 && long.TryParse(a[1], out t)) seen[a[0]] = t; } } catch { }
        }
        static void SaveSeen()
        {
            try { var lines = new List<string>(); foreach (var kv in seen) lines.Add(kv.Key + " " + kv.Value); File.WriteAllLines(SeenPath, lines.ToArray()); } catch { }
        }

        public static void SendReply(string bot, string text, string inReplyTo, Action<bool, string> done) { Send(bot, text, inReplyTo, "", done); }

        // kind "ping": the user clicked Ping. The text is a fixed marker so older routers treat it as a short reply.
        public static void SendPing(string bot, Action<bool, string> done) { Send(bot, "(ping)", "", "ping", done); }

        // 1.1.3: answer a probe with a signed "probe-ack" on the reply channel. beaconctl and the router skip acks, and
        // nothing is queued for the wake hook. At most one ack per 5 s, so a flood of probes can't flood the relay.
        static DateTime lastAck = DateTime.MinValue;
        static void SendProbeAck(string probeId)
        {
            if ((DateTime.Now - lastAck).TotalSeconds < 5) { Log.Write("Probe received (silent); ack skipped (rate limit)."); return; }
            lastAck = DateTime.Now;
            Log.Write("Probe received (silent); sending ack.");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var link = Settings.Link; if (!link.Valid) return;
                    var payload = new Dictionary<string, object> {
                        { "id", Crypto.Hex(Crypto.Random(12)) }, { "ts", Crypto.NowMs() }, { "from", "app" },
                        { "bot", "" }, { "text", "" }, { "re", probeId ?? "" }, { "kind", "probe-ack" }, { "ver", Program.Version } };
                    Http.Post(link.Server, link.Up, Crypto.Seal(link.Key, Crypto.PetToBot, payload));
                    Log.Write("Probe ack sent.");
                }
                catch (Exception ex) { Log.Write("Probe ack failed: " + ex.Message); }
            });
        }

        static Dictionary<string, object> ReplyPayload(string id, long ts, string bot, string text, string inReplyTo, string kind)
        {
            var payload = new Dictionary<string, object> {
                { "id", id }, { "ts", ts }, { "from", "user" },
                { "bot", bot }, { "text", text }, { "re", inReplyTo ?? "" } };
            if (kind.Length > 0) payload["kind"] = kind;
            return payload;
        }

        // 1.1.6: the exact size the sealed reply would have (the size never depends on the key, the IV or the id's value)
        public static int ReplyEnvelopeBytes(string bot, string text, string inReplyTo)
        {
            var payload = ReplyPayload(Crypto.Hex(new byte[12]), Crypto.NowMs(), bot, text, inReplyTo, "");
            return Crypto.Utf8Bytes(Crypto.Seal(new byte[32], Crypto.PetToBot, payload));
        }

        // 1.1.6: 0 if the reply fits in one relay message, else roughly what share of the text (in %, 1..99) has to go
        public static int ReplyOverPercent(string bot, string text, string inReplyTo)
        {
            if (ReplyEnvelopeBytes(bot, text, inReplyTo) <= Crypto.MaxEnvelopeBytes) return 0;
            int lo = 0, hi = text.Length;   // longest prefix that fits
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (ReplyEnvelopeBytes(bot, text.Substring(0, mid), inReplyTo) <= Crypto.MaxEnvelopeBytes) lo = mid; else hi = mid - 1; }
            return Math.Max(1, Math.Min(99, (int)Math.Ceiling((text.Length - lo) * 100.0 / text.Length)));
        }
        public static string TooLongNotice(int pct) { return "Too long to send: shorten it by about " + pct + "%. Nothing was sent."; }

        static void Send(string bot, string text, string inReplyTo, string kind, Action<bool, string> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var link = Settings.Link; if (!link.Valid) throw new Exception("not paired yet");
                    string id = Crypto.Hex(Crypto.Random(12)); long ts = Crypto.NowMs();
                    string env = Crypto.Seal(link.Key, Crypto.PetToBot, ReplyPayload(id, ts, bot, text, inReplyTo, kind));
                    int size = Crypto.Utf8Bytes(env);
                    if (size > Crypto.MaxEnvelopeBytes)   // 1.1.6: never post what the relay would turn into an unreadable attachment
                    {
                        Log.Write("Reply not sent to '" + bot + "': too long (" + size + " bytes sealed, limit " + Crypto.MaxEnvelopeBytes + ").");
                        done(false, "too long to send; shorten it"); return;
                    }
                    Http.Post(link.Server, link.Up, env);
                    Log.Write((kind == "ping" ? "Ping" : "Reply") + " sent to '" + bot + "' (" + text.Length + " chars).");
                    done(true, null);
                    try { Wake.Queue(bot, id, ts, kind == "ping"); } catch (Exception wex) { Log.Write("Wake queue error: " + wex.GetType().Name); }
                }
                catch (Exception ex) { Log.Write("Reply failed: " + ex.Message); done(false, ex.Message); }
            });
        }
    }

    static class Http
    {
        static readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public static List<Dictionary<string, object>> Poll(string server, string topic, string since)
        {
            var list = new List<Dictionary<string, object>>();
            var req = (HttpWebRequest)WebRequest.Create(server.TrimEnd('/') + "/" + topic + "/json?poll=1&since=" + Uri.EscapeDataString(since));
            req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.UserAgent = "BotBeacons/1.0";
            using (var resp = GetResponse(req))
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = rd.ReadLine()) != null)
                {
                    line = line.Trim(); if (line.Length == 0) continue;
                    Dictionary<string, object> o = null; try { o = json.DeserializeObject(line) as Dictionary<string, object>; } catch { }
                    if (o != null && Crypto.Str(o, "event") == "message") list.Add(o);
                }
            }
            return list;
        }
        // GetResponse that closes the error response of a failed request (otherwise, for example, every 429 would hold
        // on to a connection to the relay until the garbage collector runs)
        public static HttpWebResponse GetResponse(HttpWebRequest req)
        {
            try { return (HttpWebResponse)req.GetResponse(); }
            catch (WebException ex) { if (ex.Response != null) try { ex.Response.Close(); } catch { } throw; }
        }
        public static void Post(string server, string topic, string body)
        {
            var req = (HttpWebRequest)WebRequest.Create(server.TrimEnd('/') + "/" + topic);
            req.Method = "POST"; req.Timeout = 20000; req.ReadWriteTimeout = 20000; req.UserAgent = "BotBeacons/1.0";
            req.ContentType = "text/plain; charset=utf-8";
            byte[] data = Encoding.UTF8.GetBytes(body); req.ContentLength = data.Length;
            using (var rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
            using (var resp = GetResponse(req)) { }
        }
    }

    // One-time pairing with a bot host using a short code (see docs\TECHNICAL.md, "Security model" > "Pairing").
    class Pairing
    {
        public string Code { get; private set; }
        public string Status = "Waiting for the bot\u2026";
        public bool Done, Failed;
        public event Action Changed;
        readonly string server;
        volatile bool cancelled;

        public Pairing(string server) { this.server = server; Code = Crypto.NewPairCode(); }
        public bool Active { get { return !Done && !Failed && !cancelled; } }
        public void Cancel() { if (Active) { cancelled = true; Log.Write("Pairing cancelled by the user."); } }
        public void Start() { new Thread(Run) { IsBackground = true, Name = "pairing" }.Start(); }
        public static event Action Paired;   // the app reconnects the relay with the new link

        // ntfy.sh allows roughly one request per 5 s per IP, shared with anything else on the PC that uses it, so a
        // 429 (or a network blip) must never end pairing: wait and try again until the code expires.
        static int BackoffMs(int fails) { return Math.Min(30000, 3000 * (1 << Math.Min(4, fails))); }
        void PostRetry(string rv, string body, DateTime until, string what)
        {
            int fails = 0;
            while (true)
            {
                try { Http.Post(server, rv, body); if (fails > 0) Log.Write("Pairing: " + what + " sent after " + fails + " retr" + (fails == 1 ? "y" : "ies") + "."); return; }
                catch (Exception ex)
                {
                    if (cancelled || DateTime.UtcNow >= until || !(ex is WebException || ex is IOException)) throw;
                    int w = BackoffMs(fails); fails++;
                    if (fails <= 3 || fails % 10 == 0) Log.Write("Pairing: sending " + what + " failed (" + ex.Message + "), retrying in " + w / 1000 + " s.");
                    Thread.Sleep(w);
                }
            }
        }
        void Set(string s, bool done, bool failed) { Status = s; Done = done; Failed = failed; var h = Changed; if (h != null) h(); }

        void Run()
        {
            try
            {
                string rv = Crypto.RendezvousTopic(Code); byte[] ck = Crypto.CodeKey(Code);
                using (var ec = new ECDiffieHellmanCng(256))
                {
                    string pk = Crypto.PubXY(ec);
                    string since = (Crypto.NowMs() / 1000 - 5).ToString();
                    DateTime until = DateTime.UtcNow.AddMinutes(10);
                    PostRetry(rv, Ser(new Dictionary<string, object> { { "t", "hello" }, { "pk", pk }, { "mac", B64(Crypto.Hmac(ck, "hello|" + pk)) } }), until, "hello");
                    Log.Write("Pairing started (code shown to the user, valid 10 minutes; closing the window does not cancel it).");
                    int pollFails = 0;
                    byte[] key = null; string peer = ""; string down = null, up = null;
                    while (!cancelled && DateTime.UtcNow < until)
                    {
                        List<Dictionary<string, object>> got;
                        try { got = Http.Poll(server, rv, since); if (pollFails > 0) Log.Write("Pairing: relay reachable again."); pollFails = 0; }
                        catch (Exception ex)
                        {
                            if (!(ex is WebException || ex is IOException)) throw;
                            int w = BackoffMs(pollFails); pollFails++;
                            if (pollFails <= 3 || pollFails % 10 == 0) Log.Write("Pairing: checking for the bot failed (" + ex.Message + "), retrying in " + w / 1000 + " s.");
                            for (int i = 0; i < w / 250 && !cancelled; i++) Thread.Sleep(250);
                            continue;
                        }
                        foreach (var o in got)
                        {
                            since = Crypto.Str(o, "id");
                            Dictionary<string, object> m = null; try { m = new JavaScriptSerializer().DeserializeObject(Crypto.Str(o, "message")) as Dictionary<string, object>; } catch { }
                            if (m == null) continue;
                            string t = Crypto.Str(m, "t");
                            if (t == "join" && key == null)
                            {
                                string bpk = Crypto.Str(m, "pk");
                                byte[] mac; try { mac = Convert.FromBase64String(Crypto.Str(m, "mac")); } catch { continue; }
                                if (!Crypto.FixedEquals(mac, Crypto.Hmac(ck, "join|" + pk + "|" + bpk))) { Log.Write("Pairing: ignored a join with a wrong code."); continue; }
                                byte[] z = Crypto.Agree(ec, bpk);
                                key = Crypto.Hmac(z, "bot-beacons pair key v1|" + Crypto.NormCode(Code) + "|" + pk + "|" + bpk);
                                peer = Crypto.Str(m, "name"); if (peer.Length == 0) peer = "bot host";
                                down = "bb-" + Crypto.Hex(Crypto.Random(16)); up = "bb-" + Crypto.Hex(Crypto.Random(16));
                                var cfg = new Dictionary<string, object> { { "id", Crypto.Hex(Crypto.Random(12)) }, { "ts", Crypto.NowMs() }, { "server", server }, { "down", down }, { "up", up } };
                                Log.Write("Pairing: bot \"" + peer + "\" joined; confirming.");
                                Set("Bot found, confirming\u2026", false, false);
                                PostRetry(rv, Ser(new Dictionary<string, object> { { "t", "done" }, { "env", Crypto.Seal(key, "pair", cfg) } }), until, "confirmation");
                            }
                            else if (t == "ack" && key != null)
                            {
                                string why; var p = Crypto.Open(key, "pair-ack", Crypto.Str(m, "env"), out why);
                                if (p == null || Crypto.Str(p, "down") != down) { Log.Write("Pairing: bad ack (" + why + ")."); continue; }
                                Settings.SaveLink(new Link { Server = server, Down = down, Up = up, Peer = peer, Key = key });
                                Log.Write("Paired with " + peer + ".");
                                Set("Paired with " + peer + ". You can close this.", true, false);
                                var ph = Paired; if (ph != null) try { ph(); } catch { }
                                return;
                            }
                        }
                        for (int i = 0; i < 12 && !cancelled; i++) Thread.Sleep(250);   // every 3 s: gentle on ntfy's per-IP limit
                    }
                    if (!cancelled) { Log.Write("Pairing timed out."); Set("The code expired. Start pairing again.", false, true); }
                }
            }
            catch (Exception ex) { Log.Write("Pairing failed: " + ex.Message); Set("Pairing failed: " + ex.Message, false, true); }
        }
        static string Ser(object o) { return new JavaScriptSerializer().Serialize(o); }
        static string B64(byte[] b) { return Convert.ToBase64String(b); }
    }
}
