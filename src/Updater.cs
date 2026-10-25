using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.3 in-app updater. Checks GitHub's latest release for SnazzyGaz/bot-beacons at start-up, every 12 hours and on
    // "Check for updates now"; if it is newer than this copy it says so (Updates menu + one balloon per version). Nothing
    // installs by itself: "Update now" asks, downloads the release's -win.zip, checks it (size, SHA-256 when the release
    // has a checksum file, a valid zip with BotBeacons.exe inside), and a small batch file swaps BotBeacons.exe once this
    // copy has exited and starts the new one. Pairing, settings.ini and avatars live in %APPDATA%\BotBeacons and are not
    // touched. The only network calls are api.github.com and the release asset download (github.com and its asset host).
    // Offline, rate-limited or any error: automatic checks stay silent (logged); a manual check says what happened.
    static class Updater
    {
        public const double EverySeconds = 12 * 3600;
        public static bool Notify { get { return Settings.Get("updates", "notify").ToLowerInvariant() != "off"; } }
        public static void SetNotify(bool on) { Settings.Set("updates", on ? "notify" : "off"); Settings.Save(); Log.Write("Updates: " + (on ? "notify" : "off") + "."); if (on) CheckNow(false); }
        public static ReleaseInfo Available;          // newer than this copy (null: none / not checked)
        public static string Status = "Not checked yet";
        public static bool Busy;
        static Control ui; static System.Windows.Forms.Timer timer; static DateTime lastCheck = DateTime.MinValue;

        public static void Start(Control host)
        {
            ui = host;
            timer = new System.Windows.Forms.Timer { Interval = 30 * 1000 };   // first check 30 s after start-up, then hourly ticks
            timer.Tick += (s, e) => { timer.Interval = 3600 * 1000; if (Notify && (DateTime.UtcNow - lastCheck).TotalSeconds >= EverySeconds - 60) CheckNow(false); };
            timer.Start();
        }

        public static void CheckNow(bool manual)
        {
            if (Busy) return;
            Busy = true; lastCheck = DateTime.UtcNow;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ReleaseInfo r = null; string err = null;
                try
                {
                    string json = Get(UpdateLogic.ApiLatest, "application/vnd.github+json", 1024 * 1024);
                    r = UpdateLogic.ParseRelease(json); if (r == null) err = "GitHub's answer wasn't a release";
                }
                catch (WebException ex)
                {
                    var hr = ex.Response as HttpWebResponse; int code = hr != null ? (int)hr.StatusCode : 0;
                    err = code == 404 ? "no releases published yet" : code == 403 || code == 429 ? "GitHub is limiting requests right now" : "couldn't reach GitHub (" + (code > 0 ? "HTTP " + code : ex.Status.ToString()) + ")";
                }
                catch (Exception ex) { err = "couldn't check (" + ex.GetType().Name + ")"; }
                Post(() => Done(r, err, manual));
            });
        }

        static void Done(ReleaseInfo r, string err, bool manual)
        {
            Busy = false;
            if (err != null)
            {
                Status = "Last check: " + err; Log.Write("Update check: " + err + ".");
                if (manual) Program.Balloon("Bot Beacons updates", "Couldn't check for updates: " + err + ".", ToolTipIcon.None);
                return;
            }
            int c = UpdateLogic.Compare(r.Tag, Program.Version);
            if (c > 0 && !r.Draft && !r.Pre && r.ZipUrl.Length > 0)
            {
                Available = r; Status = "Update available: " + r.Tag;
                Log.Write("Update check: " + r.Tag + " is available (this is v" + Program.Version + "; asset " + r.ZipName + (r.SumName.Length > 0 ? ", checksum " + r.SumName : ", no checksum file") + ").");
                if (manual || Settings.Get("updateNotified", "") != r.Tag)
                {
                    Settings.Set("updateNotified", r.Tag); Settings.Save();
                    Program.Balloon("Bot Beacons " + r.Tag + " is available", "Right-click a beacon > Updates > Update now (you have v" + Program.Version + ").", ToolTipIcon.Info);
                }
            }
            else
            {
                Available = null;
                Status = c == 0 ? "Up to date (v" + Program.Version + ")" : c < 0 ? "v" + Program.Version + " is newer than the latest release (" + r.Tag + ")" : "Latest release " + r.Tag + " has no Windows zip";
                Log.Write("Update check: " + Status + ".");
                if (manual) Program.Balloon("Bot Beacons updates", Status + ".", ToolTipIcon.None);
            }
        }

        // "Update now": ask, download, verify, swap
        public static void UpdateNow()
        {
            var r = Available; if (r == null || Busy) return;
            var ok = MessageBox.Show("Update Bot Beacons to " + r.Tag + "?\n\nIt downloads " + r.ZipName + " from GitHub, checks it, then closes and restarts Bot Beacons. Your pairing, settings and avatars are kept.",
                "Bot Beacons update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;
            string exe = Application.ExecutablePath, dir = Path.GetDirectoryName(exe);
            if (!CanWrite(dir)) { MessageBox.Show("Bot Beacons can't write to its folder (" + dir + "), so it can't update itself there.\n\nDownload " + r.ZipName + " from " + r.Page + " instead.", "Bot Beacons update", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Busy = true; Status = "Downloading " + r.Tag + "\u2026"; Log.Write("Update: downloading " + r.ZipName + ".");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = null, newExe = null;
                try
                {
                    string sha = null;
                    if (r.SumUrl.Length > 0)
                    {
                        sha = UpdateLogic.ParseChecksum(Encoding.UTF8.GetString(Download(r.SumUrl, UpdateLogic.MaxSum)), r.ZipName);
                        if (sha == null) err = "the release's checksum file doesn't list " + r.ZipName;
                    }
                    if (err == null)
                    {
                        var zip = Download(r.ZipUrl, UpdateLogic.MaxZip);
                        byte[] exeBytes; err = UpdateLogic.Verify(zip, r.ZipSize, sha, out exeBytes);
                        if (err == null)
                        {
                            var tmp = Path.Combine(Path.GetTempPath(), "BotBeacons-update"); Directory.CreateDirectory(tmp);
                            newExe = Path.Combine(tmp, "BotBeacons.exe"); File.WriteAllBytes(newExe, exeBytes);
                            Log.Write("Update: " + r.ZipName + " verified (" + zip.Length + " bytes" + (sha != null ? ", SHA-256 matches" : ", no checksum published") + ").");
                        }
                    }
                }
                catch (Exception ex) { err = "the download failed (" + ex.GetType().Name + ")"; }
                Post(() =>
                {
                    Busy = false;
                    if (err != null) { Status = "Update failed: " + err; Log.Write("Update: " + err + "."); MessageBox.Show("The update wasn't applied: " + err + ".\n\nNothing was changed.", "Bot Beacons update", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    try { Swap(newExe, exe); }
                    catch (Exception ex) { Status = "Update failed"; Log.Write("Update: couldn't start the swap (" + ex.Message + ")."); MessageBox.Show("The update couldn't be started. Nothing was changed.", "Bot Beacons update"); }
                });
            });
        }

        // the helper: waits until BotBeacons.exe is no longer in use (this copy has exited), keeps the old one as
        // BotBeacons.exe.old, copies the new one in (puts the old one back if that fails), starts it and deletes itself
        public static string HelperScript(string newExe, string exe)
        {
            var sb = new StringBuilder();
            sb.Append("@echo off\r\nsetlocal\r\n");
            sb.Append("set \"EXE=" + exe + "\"\r\nset \"NEW=" + newExe + "\"\r\nset N=0\r\n");
            sb.Append(":wait\r\n2>nul (>>\"%EXE%\" (call )) && goto swap\r\nset /a N+=1\r\nif %N% GEQ 60 goto run\r\nping -n 2 127.0.0.1 >nul\r\ngoto wait\r\n");
            sb.Append(":swap\r\ncopy /y \"%EXE%\" \"%EXE%.old\" >nul\r\ncopy /y \"%NEW%\" \"%EXE%\" >nul\r\nif errorlevel 1 copy /y \"%EXE%.old\" \"%EXE%\" >nul\r\n");
            sb.Append(":run\r\nstart \"\" \"%EXE%\"\r\ndel \"%NEW%\" >nul 2>&1\r\n(goto) 2>nul & del \"%~f0\"\r\n");
            return sb.ToString();
        }
        static void Swap(string newExe, string exe)
        {
            var bat = Path.Combine(Path.GetDirectoryName(newExe), "apply-update.cmd");
            File.WriteAllText(bat, HelperScript(newExe, exe), Encoding.Default);
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            Log.Write("Update: restarting into the new version.");
            Application.Exit();
        }
        // a BotBeacons.exe.old left by the last update
        public static void Cleanup() { try { var old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { } }

        static bool CanWrite(string dir) { try { var p = Path.Combine(dir, ".bb-write-test"); File.WriteAllText(p, ""); File.Delete(p); return true; } catch { return false; } }
        static void Post(Action a) { try { if (ui != null && ui.IsHandleCreated && !ui.IsDisposed) ui.BeginInvoke(a); } catch { } }

        static HttpWebRequest Req(string url, string accept)
        {
            var q = (HttpWebRequest)WebRequest.Create(url);
            q.UserAgent = "BotBeacons/" + Program.Version + " (+https://github.com/" + UpdateLogic.Repo + ")";
            q.Accept = accept; q.Timeout = 20000; q.ReadWriteTimeout = 30000; q.AllowAutoRedirect = false;
            q.Proxy = WebRequest.DefaultWebProxy;
            return q;
        }
        static string Get(string url, string accept, int max) { return Encoding.UTF8.GetString(Fetch(url, accept, max)); }
        static byte[] Download(string url, long max)
        {
            if (!UpdateLogic.AssetUrlOk(url)) throw new InvalidOperationException("not a release asset of " + UpdateLogic.Repo);
            return Fetch(url, "application/octet-stream", max);
        }
        // follows up to 5 redirects by hand, only to GitHub's own hosts
        static byte[] Fetch(string url, string accept, long max)
        {
            for (int hop = 0; hop < 6; hop++)
            {
                if (hop > 0 && !UpdateLogic.RedirectOk(url)) throw new InvalidOperationException("redirected off GitHub");
                using (var resp = (HttpWebResponse)Req(url, accept).GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code >= 300 && code < 400) { var loc = resp.Headers["Location"]; if (string.IsNullOrEmpty(loc)) throw new WebException("redirect without a location"); url = new Uri(new Uri(url), loc).AbsoluteUri; continue; }
                    if (resp.ContentLength > max) throw new InvalidOperationException("too big");
                    using (var s = resp.GetResponseStream()) using (var ms = new MemoryStream())
                    {
                        var buf = new byte[65536]; int n; while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > max) throw new InvalidOperationException("too big"); }
                        return ms.ToArray();
                    }
                }
            }
            throw new WebException("too many redirects");
        }
    }
}
