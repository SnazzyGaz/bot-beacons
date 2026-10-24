using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace BotBeacons
{
    // The secure link to the bots: relay server, the two private topics and the shared key.
    // Created by pairing. Stored only DPAPI-encrypted (bound to this Windows account) in settings.ini.
    class Link
    {
        public string Server = "https://ntfy.sh", Down = "", Up = "", Peer = "";
        public byte[] Key;
        public bool Valid { get { return Key != null && Key.Length == 32 && Down.Length >= 20 && Up.Length >= 20; } }
    }

    // %APPDATA%\BotBeacons\settings.ini : simple key=value lines (no secrets in clear text)
    static class Settings
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BotBeacons");
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }
        static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();
        public static Link Link = new Link();

        public static bool DevHooks { get { return Get("devHooks", "0") == "1"; } }

        public static void Load()
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(FilePath))
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            LoadLink();
            Save();
        }

        static void LoadLink()
        {
            string enc = Get("link", "");
            if (enc.Length == 0) return;
            try
            {
                var o = new JavaScriptSerializer().DeserializeObject(Crypto.Unprotect(enc)) as Dictionary<string, object>;
                var l = new Link { Server = Crypto.Str(o, "server"), Down = Crypto.Str(o, "down"), Up = Crypto.Str(o, "up"), Peer = Crypto.Str(o, "peer"), Key = Convert.FromBase64String(Crypto.Str(o, "key")) };
                if (l.Valid) Link = l; else Log.Write("Stored link is incomplete; pairing needed.");
            }
            catch (Exception ex) { Log.Write("Could not unlock the stored link (" + ex.GetType().Name + "); pairing needed."); }
        }

        public static void SaveLink(Link l)
        {
            var o = new Dictionary<string, object> { { "server", l.Server }, { "down", l.Down }, { "up", l.Up }, { "peer", l.Peer }, { "key", Convert.ToBase64String(l.Key) } };
            Set("link", Crypto.Protect(new JavaScriptSerializer().Serialize(o)));
            Set("since", (Crypto.NowMs() / 1000).ToString()); try { File.Delete(Path.Combine(Dir, "relay-cursor.txt")); } catch { }
            Link = l; Save();
        }
        public static void ClearLink() { Set("link", ""); Link = new Link(); Save(); }

        public static string Get(string key, string def)
        {
            lock (gate) { string v; return values.TryGetValue(key, out v) && v.Length > 0 ? v : def; }
        }
        public static int GetInt(string key, int def) { int v; return int.TryParse(Get(key, ""), out v) ? v : def; }
        public static void Set(string key, string value) { lock (gate) values[key] = value ?? ""; }

        public static void Save()
        {
            try
            {
                lock (gate)
                {
                    var lines = new List<string> { "# Bot Beacons settings. 'link' is encrypted to this Windows account (DPAPI).", "[pet]" };
                    foreach (var kv in values) if (kv.Value.Length > 0) lines.Add(kv.Key + "=" + kv.Value);
                    File.WriteAllLines(FilePath, lines.ToArray());
                }
            }
            catch (Exception ex) { Log.Write("Settings save failed: " + ex.Message); }
        }
    }
}
