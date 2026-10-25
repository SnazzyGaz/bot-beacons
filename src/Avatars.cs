using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;

namespace BotBeacons
{
    // 1.2.0: a bot's own profile picture, sent over the same encrypted, signed relay as messages ("beaconctl avatar BEACON
    // file.png"). ntfy turns anything over 4 KB into an attachment, so the bot side shrinks the picture (96 px, palette PNG,
    // at most 24 KB) and sends it as a few "avatar" chunks; each chunk is its own sealed envelope with the usual freshness and
    // replay checks. Here the chunks are put back together, checked against the SHA-256 the bot sent, decoded with size limits,
    // redrawn as a fresh 128 px PNG (nothing from the original file is kept) and saved to %APPDATA%\BotBeacons\avatars\<id>.png.
    // Silent: no bubble, glow, sound or wake. Unfinished transfers are dropped after 10 minutes.
    static class Avatars
    {
        public const int MaxBytes = 32 * 1024, MaxChunks = 32, MaxChunkChars = 4000, MaxSide = 1024, MaxPending = 6;
        public static string Dir { get { return Path.Combine(Settings.Dir, "avatars"); } }
        public static string PathFor(string id) { return Path.Combine(Dir, id + ".png"); }
        static volatile int version;
        public static int Version { get { return version; } }

        class Pending { public string Bot, Sha; public int N, Size; public string[] Parts; public DateTime Started; }
        static readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        static readonly object gate = new object();

        static string S(Dictionary<string, object> p, string k) { return Crypto.Str(p, k); }
        static int I(Dictionary<string, object> p, string k) { int v; return int.TryParse(S(p, k), out v) ? v : -1; }

        static bool Hex(string s, int maxLen) { if (s.Length == 0 || s.Length > maxLen) return false; foreach (char c in s) if (!Uri.IsHexDigit(c)) return false; return true; }

        // returns a short log line (never the picture itself)
        public static event Action<string> Saved;   // 1.2.6: raised on the relay thread with the bot id (TabPictures)
        public static string Receive(Dictionary<string, object> p)
        {
            string bot = S(p, "bot");
            if (!TabStore.SafeId(bot)) return "avatar ignored (bad beacon id)";
            if (S(p, "clear").ToLowerInvariant() == "true")
            {
                try { if (File.Exists(PathFor(bot))) File.Delete(PathFor(bot)); } catch { }
                version++; return "avatar cleared for '" + bot + "'";
            }
            string aid = S(p, "aid"), sha = S(p, "sha256").ToLowerInvariant(), data = S(p, "data");
            int seq = I(p, "seq"), n = I(p, "n"), size = I(p, "size");
            if (!Hex(aid, 32) || sha.Length != 64 || !Hex(sha, 64) || n < 1 || n > MaxChunks || seq < 0 || seq >= n || size < 1 || size > MaxBytes || data.Length == 0 || data.Length > MaxChunkChars)
                return "avatar chunk ignored (bad fields)";
            Pending pd; byte[] whole = null;
            lock (gate)
            {
                var now = DateTime.UtcNow;
                var old = new List<string>(); foreach (var kv in pending) if ((now - kv.Value.Started).TotalMinutes > 10) old.Add(kv.Key);
                foreach (var k in old) pending.Remove(k);
                if (!pending.TryGetValue(aid, out pd))
                {
                    if (pending.Count >= MaxPending) return "avatar chunk ignored (too many transfers at once)";
                    pd = new Pending { Bot = bot, Sha = sha, N = n, Size = size, Parts = new string[n], Started = now };
                    pending[aid] = pd;
                }
                if (pd.Bot != bot || pd.Sha != sha || pd.N != n || pd.Size != size) return "avatar chunk ignored (does not match its transfer)";
                pd.Parts[seq] = data;
                foreach (var part in pd.Parts) if (part == null) return "avatar chunk " + (seq + 1) + "/" + n + " for '" + bot + "'";
                pending.Remove(aid);
                try
                {
                    var ms = new MemoryStream();
                    foreach (var part in pd.Parts) { var b = Convert.FromBase64String(part); ms.Write(b, 0, b.Length); if (ms.Length > MaxBytes) return "avatar rejected (too big)"; }
                    whole = ms.ToArray();
                }
                catch { return "avatar rejected (bad base64)"; }
            }
            if (whole.Length != size) return "avatar rejected (size mismatch)";
            string got; using (var h = SHA256.Create()) got = BitConverter.ToString(h.ComputeHash(whole)).Replace("-", "").ToLowerInvariant();
            if (got != sha) return "avatar rejected (checksum mismatch)";
            string err = Store(bot, whole);
            if (err != null) return "avatar rejected (" + err + ")";
            version++;
            var sv = Saved; if (sv != null) try { sv(bot); } catch { }
            return "avatar saved for '" + bot + "' (" + whole.Length + " bytes)";
        }

        // decode with limits, redraw to a clean 128 px PNG, write atomically
        public static string Store(string bot, byte[] bytes)
        {
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var im = Image.FromStream(ms, false, true))
                {
                    if (im.Width < 8 || im.Height < 8 || im.Width > MaxSide || im.Height > MaxSide) return "picture must be 8 to " + MaxSide + " px";
                    using (var sq = TabStore.Square(im, 128))
                    {
                        Directory.CreateDirectory(Dir);
                        string f = PathFor(bot), tmp = f + ".tmp";
                        sq.Save(tmp, ImageFormat.Png);
                        if (File.Exists(f)) File.Delete(f);
                        File.Move(tmp, f);
                    }
                }
                return null;
            }
            catch (Exception ex) { return "not a picture: " + ex.GetType().Name; }
        }
    }
}
