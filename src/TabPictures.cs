using System;
using System.Collections.Generic;

namespace BotBeacons
{
    // 1.2.6: which picture tabs use, for all tabs at once. settings.ini tabPictures = profile | beacon | ask.
    //   ask (default, the 1.2.5 behaviour): each tab keeps what the user picked with the pencil.
    //   profile: a tab switches to the bot's profile picture as soon as one arrives over the relay.
    //   beacon: tabs use the beacon art.
    // A tab set to Custom is never overridden. A tab the user changed with the pencil after the choice was made keeps
    // the user's pick when a later picture arrives (TabPrefs.ImageBy = "user"); choosing again from the menu or from the
    // bot (beaconctl prefs) applies to every non-custom tab again.
    static class TabPictures
    {
        public static event Action Changed;

        public static string Norm(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            return v == "profile" || v == "beacon" ? v : "ask";
        }

        public static string Setting { get { return Norm(Settings.Get("tabPictures", "ask")); } }

        // pure rules (ClickHarness checks them)
        // a profile picture arrived for a tab showing `current`
        public static string OnAvatar(string setting, string current, bool userSet)
        {
            if (Norm(setting) != "profile" || current == "custom" || userSet) return current;
            return "profile";
        }
        // the user (or the bot) chose `setting` for all tabs
        public static string OnChoose(string setting, string current, bool hasAvatar)
        {
            setting = Norm(setting);
            if (current == "custom") return current;
            if (setting == "beacon") return "beacon";
            if (setting == "profile") return hasAvatar ? "profile" : current;   // the rest switch when their picture arrives
            return current;
        }

        // the setting itself; applies it to every non-custom tab. Call on the UI thread.
        public static void Choose(string setting, string by)
        {
            setting = Norm(setting);
            Settings.Set("tabPictures", setting); Settings.Save();
            int n = 0;
            if (setting != "ask")
                foreach (var b in Program.Beacons)
                {
                    var p = TabStore.Get(b.Def.Id);
                    if (p.Image == "custom") continue;
                    p.ImageBy = "";
                    string want = OnChoose(setting, p.Image, System.IO.File.Exists(Avatars.PathFor(b.Def.Id)));
                    if (want != p.Image) { p.Image = want; n++; }
                }
            TabStore.Save();
            Log.Write("Tab pictures: " + setting + " (" + by + "; " + n + " tab(s) changed).");
            var h = Changed; if (h != null) h();
        }

        // a profile picture was saved (Avatars.Saved, marshalled to the UI thread)
        public static void AvatarArrived(string id)
        {
            if (Program.Find(id) == null) return;   // NewBots offers new ids; it uses the picture when the tab is added
            var p = TabStore.Get(id);
            string want = OnAvatar(Setting, p.Image, p.ImageBy == "user");
            if (want != p.Image) { p.Image = want; Log.Write(id + ": tab picture switched to the bot's profile picture (tabPictures=profile)."); }
            TabStore.Save();   // bumps TabStore.Version, so the tab redraws with the new picture either way
            var h = Changed; if (h != null) h();
        }

        // 1.2.6: preferences a bot can set over the relay, in a probe's "prefs" field: "key=value" pairs separated by
        // ';'. Only whitelisted keys and values; anything else rejects the whole field. Returns null if all are fine.
        public static string ParsePrefs(string s, Dictionary<string, string> into)
        {
            if (s == null || s.Trim().Length == 0) return "empty";
            if (s.Length > 200) return "too long";
            foreach (var part in s.Split(';'))
            {
                if (part.Trim().Length == 0) continue;
                int eq = part.IndexOf('=');
                if (eq <= 0) return "not key=value";
                string k = part.Substring(0, eq).Trim(), v = part.Substring(eq + 1).Trim().ToLowerInvariant();
                if (k == "tabPictures") { if (v != "profile" && v != "beacon" && v != "ask") return "bad value for tabPictures"; }
                else return "unknown preference";
                into[k] = v;
            }
            return into.Count == 0 ? "empty" : null;
        }
    }
}
