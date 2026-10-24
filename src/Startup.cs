using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BotBeacons
{
    // "Start with Windows": a per-user Run entry (HKCU, no admin) that launches the dist exe with --startup.
    // settings.ini startWithWindows=1|0 is the user's choice; the Run value is kept in step with it (and re-pointed
    // if the exe has moved) every time the pet starts.
    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "BotBeacons";
        public static string Command { get { return "\"" + Application.ExecutablePath + "\" --startup"; } }

        public static string Current()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k == null ? null : k.GetValue(ValueName) as string; } catch { return null; }
        }
        public static bool IsOn { get { return string.Equals(Current(), Command, StringComparison.OrdinalIgnoreCase); } }
        public static bool Wanted { get { return Settings.Get("startWithWindows", "0") == "1"; } }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(ValueName, Command, RegistryValueKind.String);
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                }
                Settings.Set("startWithWindows", on ? "1" : "0"); Settings.Save();
                Log.Write("Start with Windows " + (on ? "on (" + Command + ")." : "off."));
            }
            catch (Exception ex) { Log.Write("Start with Windows: could not update the Run entry (" + ex.GetType().Name + ")."); }
        }

        // at every start: if the user wants it, make sure the Run entry points at this exe; if not, leave nothing behind
        public static void Sync()
        {
            string cur = Current();
            if (Wanted && !IsOn) { Set(true); if (cur != null) Log.Write("Start with Windows: re-pointed the Run entry at this exe."); }
            else if (!Wanted && cur != null && Settings.Get("startWithWindows", "") == "0") Set(false);
        }
    }
}
