using System;
using System.Collections.Generic;
using System.Linq;

namespace BotBeacons
{
    // 1.2.0: two looks over the same beacons, messages, alerts, sounds and relay.
    //   floating: the floating beacon art
    //   tab:      a bookmark tab docked to a screen edge, with a slide-out card / shelf (TabsSkin.cs)
    // 1.2.2: the look is PER BEACON. Drag a floating beacon to an outer screen edge and it becomes a tab; drag a tab well
    // into the screen and it floats again. Both kinds can be on screen at once.
    //   settings.ini skin=beacons|tabs   the layout for every beacon without its own choice (Layout > All floating / All tabs)
    //   settings.ini tabModes=id:tab,id2:float   each beacon's own choice (set by dragging; cleared by All floating / All tabs)
    static class Skin
    {
        public static bool DefaultTabs { get { return Settings.Get("skin", "beacons") == "tabs"; } }

        static Dictionary<string, bool> Modes()
        {
            var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in Settings.Get("tabModes", "").Split(','))
            {
                int i = part.LastIndexOf(':'); if (i <= 0) continue;
                string id = part.Substring(0, i).Trim(), m = part.Substring(i + 1).Trim();
                if (id.Length > 0 && (m == "tab" || m == "float")) d[id] = m == "tab";
            }
            return d;
        }
        static void SaveModes(Dictionary<string, bool> d)
        {
            Settings.Set("tabModes", string.Join(",", d.Select(kv => kv.Key + ":" + (kv.Value ? "tab" : "float"))));
            Settings.Save();
        }

        public static bool IsTab(BeaconForm b) { return b != null && IsTab(b.Def.Id); }
        public static bool IsTab(string id) { bool v; return Modes().TryGetValue(id ?? "", out v) ? v : DefaultTabs; }

        // beacons that count for the Layout menu (test beacons only while they are shown)
        static IEnumerable<BeaconForm> Counted() { return Program.Beacons.Where(b => !b.Def.Test || Program.ShowTest); }
        public static bool AnyTabs { get { return Program.Beacons.Any(b => IsTab(b)); } }

        // floating | tabs | mixed (status.txt, the log, the Layout menu)
        public static string Name
        {
            get
            {
                var l = Counted().ToList(); if (l.Count == 0) return DefaultTabs ? "tabs" : "floating";
                int t = l.Count(b => IsTab(b)); return t == 0 ? "floating" : t == l.Count ? "tabs" : "mixed";
            }
        }

        // Layout > All floating / All tabs: every beacon at once (also clears the per-beacon choices)
        public static void Set(string layout)
        {
            layout = (layout ?? "").Trim().ToLowerInvariant();
            bool tabs = layout == "tabs" || layout == "tab";
            if (!tabs && layout != "beacons" && layout != "floating" && layout != "float") return;
            if (Name == (tabs ? "tabs" : "floating") && Settings.Get("tabModes", "").Length == 0 && DefaultTabs == tabs) return;
            foreach (var b in Program.Beacons) CloseBubble(b);   // 1.2.2: a card/bubble of the old look must not linger
            Settings.Set("skin", tabs ? "tabs" : "beacons"); Settings.Set("tabModes", ""); Settings.Save();
            Log.Write("Layout: all " + (tabs ? "tabs" : "floating") + ".");
            Visibility.Apply();   // hides or shows the floating beacons, and syncs the tabs
        }

        static void CloseBubble(BeaconForm b) { var f = b.OpenBubbleForm; if (f != null) { f.CloseReason = "layout changed"; f.Close(); } }

        // one beacon: tab (true) or floating (false). The choice is dropped when it matches the default, so the file stays short
        public static void SetMode(BeaconForm b, bool tab)
        {
            if (b == null) return;
            CloseBubble(b);
            var d = Modes();
            if (tab == DefaultTabs) d.Remove(b.Def.Id); else d[b.Def.Id] = tab;
            SaveModes(d);
            Log.Write(b.Def.Id + ": now " + (tab ? "a tab" : "floating") + " (layout " + Name + ").");
            Visibility.Apply();
        }
    }
}
