using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BotBeacons
{
    // What every bubble style offers the beacon: open/focus, refresh, placement next to the beacon, a frame dump for
    // the snap dev hook, and the typed-reply dev hook.
    abstract class BubbleBase : Form
    {
        public string CloseReason = "closed";
        // set once closing starts: a deactivate fired by the close itself must not close (and log) a second time
        protected bool ClosingNow;
        protected override void OnFormClosing(FormClosingEventArgs e) { base.OnFormClosing(e); if (!e.Cancel) ClosingNow = true; }
        public abstract void Refresh2();
        public abstract void FocusInput();
        public abstract void Place(Rectangle beacon);          // beacon = where the beacon is drawn on screen
        public virtual void Follow(Rectangle beacon) { Place(beacon); }   // 1.1.4: re-place while the beacon is dragged (must be cheap)
        public abstract void DevType(string text);            // dev hook: type into the reply field and press Enter
        public virtual void DevKey(string key) { if (key == "esc") { CloseReason = "Esc"; Close(); } }
        // a new attention call while open: show it in place and return true, or false to have the beacon reopen the bubble
        public virtual bool ShowAttention(PetMessage m) { return false; }
        public virtual string Style { get { return "card"; } }
        public virtual Rectangle ScreenBounds { get { return Bounds; } }
        // Never steal focus: a bubble only activates when the user opened it themselves (a beacon/tray/menu click).
        // Opens from dev hooks, messages or alerts show without activation; the user clicks the reply box to type.
        public virtual void ShowAndFocus(bool takeFocus) { Show(); if (takeFocus) { Native.SetForegroundWindow(Handle); Activate(); FocusInput(); } }
        // dev hook "snap": save what the bubble shows (default: its on-screen pixels; the card is an opaque window)
        public virtual void SaveFrame(string path)
        {
            var br = ScreenBounds;
            using (var b = new Bitmap(br.Width, br.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(br.Location, Point.Empty, br.Size); b.Save(path); }
        }
    }

    // Look-and-feel choices. Bubble style: "card" (dark card, the neutral default) or "cloud" (cream thought cloud).
    // Looked up in order: %APPDATA%\BotBeacons\theme.json {"bubble":"cloud"} (a personal choice that beacon-list
    // upgrades never touch), then a top-level "bubble" field in beacons.json, then "card".
    static class Theme
    {
        public static readonly string[] BubbleStyles = { "card", "cloud" };
        static string FilePath { get { return Path.Combine(Settings.Dir, "theme.json"); } }

        static Dictionary<string, object> Read(string path)
        {
            try { if (File.Exists(path)) return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>; } catch { }
            return null;
        }

        static string Valid(object v)
        {
            string s = v == null ? "" : v.ToString().Trim().ToLowerInvariant();
            return Array.IndexOf(BubbleStyles, s) >= 0 ? s : null;
        }

        public static string BubbleStyle()
        {
            object v; string s;
            var t = Read(FilePath);
            if (t != null && t.TryGetValue("bubble", out v) && (s = Valid(v)) != null) return s;
            var k = Read(BeaconConfig.FilePath);
            if (k != null && k.TryGetValue("bubble", out v) && (s = Valid(v)) != null) return s;
            return "card";
        }

        // writes theme.json, keeping any other keys in it
        public static void SetBubbleStyle(string style)
        {
            style = Valid(style); if (style == null) return;
            var t = Read(FilePath) ?? new Dictionary<string, object>();
            t["bubble"] = style;
            try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(t)); Log.Write("Bubble style set to '" + style + "' (theme.json); it applies to the next bubble opened."); }
            catch (Exception ex) { Log.Write("Could not save theme.json: " + ex.Message); }
        }

        public static BubbleBase CreateBubble(BeaconForm owner, PetMessage attention)
        {
            if (Skin.IsTab(owner)) return new TabCard(owner, attention);   // 1.2.2: a tab's card is its bubble
            if (BubbleStyle() == "cloud") return new CloudBubble(owner, attention);
            return new Bubble(owner, attention);
        }
    }
}
