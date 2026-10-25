using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;

namespace BotBeacons
{
    // 1.1.5: alert sounds. Never on the UI thread, never takes focus, never touches the Windows volume:
    // the WAV is read and pre-scaled for Quiet/Normal/Loud on a worker thread, then handed to PlaySound
    // (SND_MEMORY | SND_ASYNC | SND_NODEFAULT), which returns immediately.
    // Lookup: the beacon's "sound"/"attentionSound" from beacons.json, then the theme default (default.wav /
    // default_attention.wav), each in %APPDATA%\BotBeacons\sounds\ then next to the exe in sounds\;
    // then a Windows system sound. Settings (settings.ini): soundOn, soundVolume, soundQuietHours, soundMuted.
    // 1.2.0: sound sets. soundSet=custom (default: the beacon's own WAVs, the 1.1.5 behaviour) or classic (the Windows
    // system sounds: Asterisk for messages, Exclamation for attention). A tab can override the set for its beacon
    // (tabs.json "sound"); Off is the per-beacon mute.
    static class Sound
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern bool PlaySound(string name, IntPtr hmod, uint flags);
        [DllImport("winmm.dll", EntryPoint = "PlaySoundW")] static extern bool PlaySoundMem(IntPtr data, IntPtr hmod, uint flags);
        const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004, SND_FILENAME = 0x00020000;

        public static readonly string[] Volumes = { "quiet", "normal", "loud" };
        static readonly SoundGate gate = new SoundGate();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly object playLock = new object();
        // pre-scaled WAVs in unmanaged memory (PlaySound reads them while playing, so they must not move or be freed)
        static readonly Dictionary<string, IntPtr> cache = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        public static bool On { get { return Settings.Get("soundOn", "1") != "0"; } }
        public static string Volume { get { var v = Settings.Get("soundVolume", "normal").ToLowerInvariant(); return Array.IndexOf(Volumes, v) >= 0 ? v : "normal"; } }
        public static readonly string[] Sets = { "custom", "classic" };
        public static string Set { get { return Settings.Get("soundSet", "custom").ToLowerInvariant() == "classic" ? "classic" : "custom"; } }
        public static void SetSet(string v) { v = v == "classic" ? "classic" : "custom"; Settings.Set("soundSet", v); Settings.Save(); Log.Write("Sound set: " + v + "."); }
        static string SetFor(string id) { string t = TabStore.SoundFor(id); return t == "custom" || t == "classic" ? t : Set; }
        public static bool QuietHours { get { return Settings.Get("soundQuietHours", "0") == "1"; } }
        static List<string> MutedList() { var l = new List<string>(); foreach (var s in Settings.Get("soundMuted", "").Split(',')) if (s.Trim().Length > 0) l.Add(s.Trim()); return l; }
        public static bool IsMuted(string id) { return MutedList().Exists(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)); }

        public static void SetOn(bool on) { Settings.Set("soundOn", on ? "1" : "0"); Settings.Save(); Log.Write("Sound " + (on ? "on" : "off") + "."); }
        public static void SetVolume(string v) { Settings.Set("soundVolume", v); Settings.Save(); Log.Write("Sound volume: " + v + "."); }
        public static void SetQuietHours(bool on) { Settings.Set("soundQuietHours", on ? "1" : "0"); Settings.Save(); Log.Write("Quiet hours 23:00-07:00 " + (on ? "on" : "off") + "."); }
        public static void SetMuted(string id, bool mute)
        {
            var l = MutedList(); l.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)); if (mute) l.Add(id);
            Settings.Set("soundMuted", string.Join(",", l.ToArray())); Settings.Save(); Log.Write(id + ": sound " + (mute ? "muted" : "unmuted") + ".");
        }
        static float Gain(string v) { return v == "quiet" ? 0.35f : v == "loud" ? 1.0f : 0.65f; }

        // an incoming message or attention call reached this beacon (hidden and paused beacons too: muting is per beacon)
        public static void Alert(BeaconDef d, bool attention)
        {
            string why = gate.Check(d.Id, attention, clock.Elapsed.TotalSeconds, DateTime.Now, On, QuietHours, IsMuted(d.Id));
            if (why != null) { Log.Write(d.Id + ": no sound (" + why + ")."); return; }
            Play(d, attention, false, SetFor(d.Id));
        }

        // menu "Preview ..." items: the user asked for it, so it ignores mute, quiet hours and the throttle
        public static void Preview(BeaconDef d, bool attention) { Play(d, attention, true, SetFor(d.Id)); }
        public static void Preview(BeaconDef d, bool attention, string set) { Play(d, attention, true, set); }

        static void Play(BeaconDef d, bool attention, bool preview, string set)
        {
            bool classic = set == "classic";
            string vol = Volume, id = d.Id, named = attention ? d.AttentionSound : d.Sound;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string file = classic ? null : Find(named) ?? Find(attention ? "default_attention.wav" : "default.wav");
                    string how;
                    lock (playLock)
                    {
                        if (file == null) { (attention ? SystemSounds.Exclamation : SystemSounds.Asterisk).Play(); how = classic ? "Windows sound (classic set)" : "Windows sound"; }
                        else
                        {
                            IntPtr mem = Scaled(file, vol);
                            if (mem != IntPtr.Zero) { PlaySoundMem(mem, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); how = Path.GetFileName(file); }
                            else { PlaySound(file, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT); how = Path.GetFileName(file) + " (unscaled)"; }
                        }
                    }
                    Log.Write(id + ": " + (preview ? "preview " : "") + (attention ? "attention" : "message") + " sound " + how + " (" + vol + ").");
                }
                catch (Exception ex) { Log.Write(id + ": sound failed: " + ex.Message); }
            });
        }

        static string Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
                foreach (var dir in new[] { Path.Combine(Settings.Dir, "sounds"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds") })
                {
                    var p = Path.Combine(dir, name); if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        // 16-bit PCM WAV -> copy with samples scaled by the volume gain, cached in unmanaged memory. Other formats: Zero (played unscaled).
        static IntPtr Scaled(string file, string vol)
        {
            string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks + "|" + vol;
            IntPtr mem;
            if (cache.TryGetValue(key, out mem)) return mem;
            byte[] b = File.ReadAllBytes(file);
            if (b.Length > 8 * 1024 * 1024 || !ScaleInPlace(b, Gain(vol))) return IntPtr.Zero;
            if (cache.Count >= 96) return IntPtr.Zero;   // bounded; never freed while the app runs
            mem = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, mem, b.Length);
            cache[key] = mem; return mem;
        }

        public static bool ScaleInPlace(byte[] b, float gain)
        {
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E') return false;
            int pos = 12, bits = 0, fmt = 0;
            while (pos + 8 <= b.Length)
            {
                string tag = "" + (char)b[pos] + (char)b[pos + 1] + (char)b[pos + 2] + (char)b[pos + 3];
                int len = BitConverter.ToInt32(b, pos + 4); int body = pos + 8;
                if (len < 0 || body + len > b.Length) len = b.Length - body;
                if (tag == "fmt " && len >= 16) { fmt = BitConverter.ToInt16(b, body); bits = BitConverter.ToInt16(b, body + 14); }
                else if (tag == "data")
                {
                    if (fmt != 1 || bits != 16) return false;
                    for (int i = body; i + 1 < body + len; i += 2)
                    {
                        int s = (int)Math.Round((short)(b[i] | (b[i + 1] << 8)) * gain);
                        if (s > 32767) s = 32767; else if (s < -32768) s = -32768;
                        b[i] = (byte)(s & 0xFF); b[i + 1] = (byte)((s >> 8) & 0xFF);
                    }
                    return true;
                }
                pos = body + len + (len & 1);
            }
            return false;
        }
    }
}
