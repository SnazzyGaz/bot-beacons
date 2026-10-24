using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BotBeacons
{
    // v0.10: one shared frame clock for all beacons, paced by the display instead of 8 WinForms timers.
    // A WinForms Timer only ticks on the 15.6 ms system clock, so "40 ms" really meant 31 or 47 ms, and 25 ticks a
    // second on a 60 Hz screen show for 2 or 3 refreshes unevenly: a slight stutter. Now:
    //   - a background thread waits for each display refresh (DwmFlush) and posts one frame to the UI thread;
    //     at 60 fps on a 120/144 Hz screen it uses every 2nd refresh. If DWM is idle (returns at once) or missing,
    //     it falls back to a high-resolution waitable timer (no timeBeginPeriod needed), or Sleep with
    //     timeBeginPeriod(1) held only while something is animating.
    //   - nothing fast to do (animations off, all hidden, session locked): the thread waits ~10 times a second and the
    //     slow timers (250/500 ms) fire from that, so idle cost stays near zero.
    //   - a post is never queued twice: if the UI thread is busy (a menu, a drag), frames are skipped, not bunched up.
    //     Motion is driven by elapsed time, so a skipped frame never shows as a jump.
    // settings.ini: fps=60|30|monitor (default 60; Subtle uses at most 30), frameClock=auto|legacy (legacy = the
    // v0.9 40 ms WinForms timer with whole-pixel motion, kept for comparisons).
    class FrameTimer
    {
        int interval = 40; bool enabled;
        public int Interval { get { return interval; } set { bool kick = value <= 40 && interval > 40; interval = value; if (kick) FrameClock.Kick(); } }
        public bool Enabled { get { return enabled; } set { enabled = value; if (value) FrameClock.Kick(); } }
        public event EventHandler Tick;
        internal double NextDue;
        internal bool Fast { get { return enabled && interval <= 40; } }
        public FrameTimer() { FrameClock.Add(this); }
        internal void Fire() { var h = Tick; if (h != null) h(this, EventArgs.Empty); }
    }

    static class FrameClock
    {
        static readonly List<FrameTimer> timers = new List<FrameTimer>();
        static readonly Stopwatch sw = Stopwatch.StartNew();
        static readonly AutoResetEvent kick = new AutoResetEvent(false);
        static Control marshal; static Thread thread; static System.Windows.Forms.Timer legacyTimer;
        static volatile bool pending, active, legacy;
        static volatile int targetFps = 60, divisor = 1;
        public static double RefreshHz = 60;
        public static volatile string Backend = "starting";   // dwm | hires-timer | sleep | legacy
        public static int Missed;                              // frames skipped because the UI thread was still busy
        public static bool Smooth { get { return !legacy; } }   // sub-pixel motion + vsync clock
        public static double Now { get { return sw.Elapsed.TotalSeconds; } }
        public static long Ticks { get { return sw.ElapsedTicks; } }
        // redraw quantization (settings subSteps=4, swayStep=0.1, fastCopy=1); tunable for measurements
        public static float SubSteps = 4, SwayStep = 0.1f; public static bool FastCopy = true;
        public static void LoadTuning()
        {
            float v;
            if (float.TryParse(Settings.Get("subSteps", "4"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) SubSteps = Math.Max(1, Math.Min(64, v));
            if (float.TryParse(Settings.Get("swayStep", "0.1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) SwayStep = Math.Max(0.005f, Math.Min(1, v));
            FastCopy = Settings.Get("fastCopy", "1") != "0";
        }
        public static void Tune(string sub, string sway, string fast)
        {
            if (sub != null) Settings.Set("subSteps", sub); if (sway != null) Settings.Set("swayStep", sway); if (fast != null) Settings.Set("fastCopy", fast);
            Settings.Save(); LoadTuning();
            Log.Write("Redraw tuning: subSteps=" + SubSteps + " swayStep=" + SwayStep + " fastCopy=" + FastCopy + ".");
        }
        static readonly Action dispatch = Dispatch;

        public static void Add(FrameTimer t) { lock (timers) timers.Add(t); }
        public static void Kick() { active = true; kick.Set(); }

        public static string FpsSetting { get { var s = Settings.Get("fps", "60").Trim().ToLowerInvariant(); return s == "30" || s == "monitor" ? s : "60"; } }
        public static int EffectiveFps { get { return (int)Math.Round(RefreshHz / divisor); } }

        public static void Start()
        {
            marshal = new Control(); var h = marshal.Handle;
            RefreshHz = Native2.RefreshRate(); LoadTuning();
            legacy = Settings.Get("frameClock", "auto").Trim().ToLowerInvariant() == "legacy";
            Retarget();
            legacyTimer = new System.Windows.Forms.Timer { Interval = 40 };
            legacyTimer.Tick += (s, e) => Dispatch();
            if (legacy) { Backend = "legacy"; legacyTimer.Start(); }
            thread = new Thread(Loop) { IsBackground = true, Name = "frames", Priority = ThreadPriority.AboveNormal };
            thread.Start();
            Log.Write("Frame clock: " + (legacy ? "legacy 40 ms timer" : "display-paced, " + EffectiveFps + " fps target") + " (display " + RefreshHz.ToString("0.#") + " Hz).");
        }

        // fps cap from the setting and the animation mode; the display refresh is divided by a whole number so every
        // frame shows for the same number of refreshes
        public static void Retarget()
        {
            int cap = FpsSetting == "30" ? 30 : FpsSetting == "monitor" ? (int)Math.Round(RefreshHz) : 60;
            if (AnimSettings.Mode != AnimMode.Full) cap = Math.Min(cap, 30);
            targetFps = Math.Max(10, cap);
            divisor = Math.Max(1, (int)Math.Round(RefreshHz / targetFps));
            Kick();
        }

        public static void SetFps(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant(); if (v != "30" && v != "monitor") v = "60";
            Settings.Set("fps", v); Settings.Save(); Retarget();
            Log.Write("Frame rate set to " + v + " (" + EffectiveFps + " fps on this " + RefreshHz.ToString("0.#") + " Hz display).");
        }

        public static void SetLegacy(bool on)
        {
            Settings.Set("frameClock", on ? "legacy" : "auto"); Settings.Save();
            legacy = on;
            if (on) { Backend = "legacy"; legacyTimer.Start(); } else { legacyTimer.Stop(); Backend = "starting"; Kick(); }
            Log.Write("Frame clock set to " + (on ? "legacy (v0.9 timer)" : "display-paced") + ".");
        }

        // ---------------------------------------------------------------- thread
        static void Loop()
        {
            IntPtr hiTimer = Native2.CreateWaitableTimerExW(IntPtr.Zero, null, 0x2, 0x1F0003);   // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION (Win10 1803+)
            bool dwmOk = true, periodHeld = false; long vsyncs = 0; double lastFlush = Now, lastPost = 0;
            while (true)
            {
                try
                {
                    if (legacy || !active)
                    {
                        if (periodHeld) { Native2.timeEndPeriod(1); periodHeld = false; }
                        kick.WaitOne(legacy ? 500 : 100);
                        if (!legacy) Post();
                        continue;
                    }
                    double period = 1.0 / RefreshHz;
                    bool waited = false;
                    if (dwmOk)
                    {
                        double t0 = Now;
                        int hr = Native2.DwmFlush();
                        double t1 = Now;
                        if (hr != 0) dwmOk = false;
                        else if (t1 - lastFlush >= period * 0.6) { waited = true; lastFlush = t1; vsyncs++; Backend = "dwm"; }
                        else lastFlush = t1;   // DWM had nothing to compose and returned at once: time it ourselves below
                    }
                    if (!waited)
                    {
                        double due = lastPost + 1.0 / targetFps, wait = due - Now;
                        if (wait > 0.0005)
                        {
                            if (hiTimer != IntPtr.Zero) { long d = -(long)(wait * 1e7); Native2.SetWaitableTimer(hiTimer, ref d, 0, IntPtr.Zero, IntPtr.Zero, false); Native2.WaitForSingleObject(hiTimer, 100); if (!dwmOk) Backend = "hires-timer"; }
                            else { if (!periodHeld) { Native2.timeBeginPeriod(1); periodHeld = true; } Thread.Sleep(Math.Max(1, (int)(wait * 1000))); if (!dwmOk) Backend = "sleep"; }
                        }
                        Post(); lastPost = Now; continue;
                    }
                    if (vsyncs % divisor != 0) continue;
                    Post(); lastPost = Now;
                }
                catch (Exception ex) { Log.Write("Frame clock: " + ex.Message); Thread.Sleep(50); }
            }
        }

        static void Post()
        {
            if (pending) { if (active) Missed++; return; }
            pending = true;
            try { marshal.BeginInvoke(dispatch); } catch { pending = false; }
        }

        // ---------------------------------------------------------------- UI thread
        static double lastDispatch = -1;
        static void Dispatch()
        {
            pending = false;
            double t = Now;
            if (measureUntil > 0) Measure(t);
            lastDispatch = t;
            bool anyFast = false;
            FrameTimer[] list; lock (timers) list = timers.ToArray();
            foreach (var ft in list)
            {
                if (!ft.Enabled) continue;
                if (ft.Fast) { anyFast = true; long k0 = sw.ElapsedTicks; ft.Fire(); if (measureUntil > 0) tickTicks += sw.ElapsedTicks - k0; }
                else if (t >= ft.NextDue) { ft.NextDue = t + ft.Interval / 1000.0; ft.Fire(); }
                if (ft.Fast) anyFast = true;
            }
            active = anyFast;
        }

        // ---------------------------------------------------------------- dev hook "frametimes <s>"
        static double measureUntil; static readonly List<double> intervals = new List<double>();
        static readonly List<double> posErr = new List<double>(); static int renders, missedAtStart; static string measureFile;
        static long drawTicks, ulwTicks, tickTicks;
        public static void StartMeasure(double seconds, string file)
        {
            intervals.Clear(); posErr.Clear(); renders = 0; drawTicks = ulwTicks = tickTicks = 0; missedAtStart = Missed; measureFile = file;
            measureUntil = Now + Math.Max(2, Math.Min(60, seconds));
        }
        // the beacons report how far the drawn position is from the exact (unrounded) one, in px
        public static void ReportPos(double err) { if (measureUntil > 0) posErr.Add(Math.Abs(err)); }
        public static void ReportRender(long draw, long ulw) { if (measureUntil > 0) { renders++; drawTicks += draw; ulwTicks += ulw; } }
        static double Ms(long t) { return t * 1000.0 / Stopwatch.Frequency; }
        static void Measure(double t)
        {
            if (lastDispatch > 0 && active) intervals.Add((t - lastDispatch) * 1000);
            if (t < measureUntil) return;
            measureUntil = 0;
            var iv = intervals.OrderBy(x => x).ToList(); var pe = posErr.OrderBy(x => x).ToList();
            Func<List<double>, double, double> pct = (l, p) => l.Count == 0 ? 0 : l[Math.Min(l.Count - 1, (int)(p * l.Count))];
            double mean = iv.Count > 0 ? iv.Average() : 0, sd = iv.Count > 1 ? Math.Sqrt(iv.Sum(x => (x - mean) * (x - mean)) / (iv.Count - 1)) : 0;
            double target = legacy ? 40 : 1000.0 / EffectiveFps; int late = iv.Count(x => x > target * 1.5);
            double secs = iv.Sum() / 1000.0;
            var lines = new[] {
                "backend=" + Backend + " display=" + RefreshHz.ToString("0.##") + "Hz target=" + (legacy ? "40ms timer" : EffectiveFps + "fps") + " smooth=" + Smooth,
                "frames=" + iv.Count + " over " + secs.ToString("0.0") + "s (" + (secs > 0 ? iv.Count / secs : 0).ToString("0.0") + " fps)",
                "interval_ms mean=" + mean.ToString("0.00") + " sd=" + sd.ToString("0.00") + " p50=" + pct(iv, 0.5).ToString("0.00") + " p95=" + pct(iv, 0.95).ToString("0.00") + " p99=" + pct(iv, 0.99).ToString("0.00") + " max=" + (iv.Count > 0 ? iv[iv.Count - 1] : 0).ToString("0.00"),
                "late(>1.5x target)=" + late + " missed=" + (Missed - missedAtStart),
                "pos_error_px mean=" + (pe.Count > 0 ? pe.Average() : 0).ToString("0.000") + " p95=" + pct(pe, 0.95).ToString("0.000") + " max=" + (pe.Count > 0 ? pe[pe.Count - 1] : 0).ToString("0.000") + " samples=" + pe.Count,
                "sprite_redraws=" + renders + " (" + (secs > 0 ? renders / secs : 0).ToString("0") + "/s, all beacons) draw_ms=" + (renders > 0 ? Ms(drawTicks) / renders : 0).ToString("0.000") + " ulw_ms=" + (renders > 0 ? Ms(ulwTicks) / renders : 0).ToString("0.000") + " ui_ms_per_s=" + (secs > 0 ? Ms(tickTicks) / secs : 0).ToString("0.0"),
                "tuning subSteps=" + SubSteps + " swayStep=" + SwayStep + " fastCopy=" + FastCopy };
            try { System.IO.File.WriteAllLines(measureFile, lines); } catch { }
            Log.Write("Frame times measured: " + lines[1] + ", " + lines[2]);
        }
    }

    static class Native2
    {
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint p);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWaitableTimerExW(IntPtr attr, string name, uint flags, uint access);
        [DllImport("kernel32.dll")] public static extern bool SetWaitableTimer(IntPtr h, ref long due, int period, IntPtr cb, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);

        // the primary display's refresh rate (DWM paces composition by it); 60 if unknown
        public static double RefreshRate()
        {
            try
            {
                var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(Screen.PrimaryScreen.DeviceName, -1, ref dm) && dm.dmDisplayFrequency >= 24 && dm.dmDisplayFrequency <= 500) return dm.dmDisplayFrequency;
            }
            catch { }
            return 60;
        }
    }
}
