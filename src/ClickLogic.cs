using System;
using System.Drawing;
using System.IO;

namespace BotBeacons
{
    // 1.2.4: "clicking the body is hit and miss". The press/release rules for a floating beacon, pulled out of BeaconForm
    // so the harness can drive them (tools/ClickHarness.cs.txt). Everything is measured in screen pixels from where the
    // cursor went down, so the window bobbing, wandering or hopping under a still cursor never counts as a drag.
    public enum ClickVerdict { Click, Drag, Catch, TooLong, NoPress }
    public enum ClickAct { Open, Close, KeepOpen }
    public enum PressKind { None, Left, Ping, Menu }   // 1.2.6: what each mouse button does on a beacon, tab or shelf row

    public static class ClickRules
    {
        public const int BaseSlopPx = 7;          // at 96 dpi; Windows' own DragSize (4 px) is too tight for a bobbing target
        public const double MaxSeconds = 1.5;     // a press that wandered a little must end within this
        public const int StillPx = 2;             // moved no more than this: no time limit at all (a slow, deliberate press)

        // how far the cursor may move before a press becomes a drag: the larger of Windows' drag size and 7 px x dpi scale
        public static int Slop(Size dragSize, float dpiScale)
        {
            int own = (int)Math.Round(BaseSlopPx * Math.Max(1f, dpiScale));
            return Math.Max(own, Math.Max(dragSize.Width, dragSize.Height));
        }

        public static bool PastSlop(Point down, Point now, int slop)
        {
            long dx = now.X - down.X, dy = now.Y - down.Y;
            return dx * dx + dy * dy > (long)slop * slop;
        }

        // what a finished press was. maxDist: the furthest the cursor got from the press point (screen px).
        // caughtFling: the press grabbed a beacon that was sliding after a throw (stopping it is not a click).
        public static ClickVerdict Classify(double heldSeconds, double maxDist, bool caughtFling, int slop)
        {
            if (maxDist > slop) return ClickVerdict.Drag;
            if (caughtFling) return ClickVerdict.Catch;
            if (maxDist > StillPx && heldSeconds > MaxSeconds) return ClickVerdict.TooLong;
            return ClickVerdict.Click;
        }

        // what a click does. A click that lands while the bubble is open closes it, except the second click of a
        // double-click on a beacon whose first click just opened it (that used to open then close at once).
        public static ClickAct Act(bool bubbleOpenAtDown, double secondsSinceOpenClick, double doubleClickSeconds)
        {
            if (!bubbleOpenAtDown) return ClickAct.Open;
            if (secondsSinceOpenClick >= 0 && secondsSinceOpenClick <= doubleClickSeconds) return ClickAct.KeepOpen;
            return ClickAct.Close;
        }

        // 1.2.6: left = click (open/close the card) or drag, as before; middle = ping the bot (same as the menu's Ping:
        // throttle and wiggle included); right = the menu. Any other button does nothing.
        public static PressKind ForButton(System.Windows.Forms.MouseButtons b)
        {
            if (b == System.Windows.Forms.MouseButtons.Left) return PressKind.Left;
            if (b == System.Windows.Forms.MouseButtons.Middle) return PressKind.Ping;
            if (b == System.Windows.Forms.MouseButtons.Right) return PressKind.Menu;
            return PressKind.None;
        }

        // 1.2.6: does a middle-button release ping? Only if the press started on this window, the left button isn't held
        // (no drag is going on) and the cursor stayed within the slop. A middle press never starts a drag or opens a card.
        // 1.2.6: the hover hint on a floating beacon or a tab (shown after a pause; not while its card or bubble is open)
        public static string HoverHint(string name) { return name + "\nClick: open   Middle-click: ping   Right-click: menu"; }

        public static bool MiddlePing(bool pressedHere, bool leftHeld, double maxDist, int slop)
        {
            return pressedHere && !leftHeld && maxDist <= slop;
        }
    }

    // opt-in click diagnostics: settings.ini clickLog=1 or the environment variable BOTBEACONS_CLICKLOG=1. Writes
    // clicklog.txt in the settings folder (down / move / up / decision). Off by default; capped at 256 KB.
    public static class ClickLog
    {
        static int on = -1;
        public static bool On
        {
            get
            {
                if (on < 0)
                {
                    string env = Environment.GetEnvironmentVariable("BOTBEACONS_CLICKLOG") ?? "";
                    on = (env == "1" || Settings.Get("clickLog", "0") == "1") ? 1 : 0;
                }
                return on == 1;
            }
        }
        public static void Reset() { on = -1; }
        public static void Write(string line)
        {
            if (!On) return;
            try
            {
                string path = Path.Combine(Settings.Dir, "clicklog.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch { }
        }
    }
}
