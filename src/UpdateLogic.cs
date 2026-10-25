using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BotBeacons
{
    // 1.2.3: the in-app updater's pure parts (no network, no UI), so tools/UpdateHarness.cs.txt can test them:
    // version compare, the GitHub "latest release" JSON, picking the -win.zip asset and its checksum, and checking a
    // downloaded zip (valid, every entry's CRC right, BotBeacons.exe inside) before anything is swapped.
    class ReleaseInfo
    {
        public string Tag = "", Name = "", Page = "";
        public string ZipName = "", ZipUrl = "", SumName = "", SumUrl = "";
        public long ZipSize = -1;
        public bool Draft, Pre;
    }

    static class UpdateLogic
    {
        public const string Repo = "SnazzyGaz/bot-beacons";
        public const string ApiLatest = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const long MaxZip = 60L * 1024 * 1024, MaxSum = 64 * 1024;

        // "v1.2.3", "1.2.3", "1.2", "v1.2.3-beta.1" -> numbers + pre-release tag (null if not a version)
        public static bool TryParse(string s, out int[] nums, out string pre)
        {
            nums = null; pre = "";
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim(); if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int plus = s.IndexOf('+'); if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-'); if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); }
            var parts = s.Split('.'); if (parts.Length < 1 || parts.Length > 4) return false;
            var n = new int[3];
            for (int i = 0; i < parts.Length; i++) { int v; if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v)) return false; if (i < 3) n[i] = v; }
            nums = n; return true;
        }
        // <0: a older than b, 0: same, >0: newer (semver: 1.2.3-beta < 1.2.3). Unparseable versions compare as oldest.
        public static int Compare(string a, string b)
        {
            int[] na, nb; string pa, pb; bool oa = TryParse(a, out na, out pa), ob = TryParse(b, out nb, out pb);
            if (!oa || !ob) return oa == ob ? 0 : oa ? 1 : -1;
            for (int i = 0; i < 3; i++) if (na[i] != nb[i]) return na[i] < nb[i] ? -1 : 1;
            if (pa.Length == 0 || pb.Length == 0) return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
            var xa = pa.Split('.'); var xb = pb.Split('.');
            for (int i = 0; i < Math.Min(xa.Length, xb.Length); i++)
            {
                int ia, ib; bool da = int.TryParse(xa[i], out ia), db = int.TryParse(xb[i], out ib);
                int c = da && db ? ia.CompareTo(ib) : da ? -1 : db ? 1 : string.CompareOrdinal(xa[i], xb[i]);
                if (c != 0) return c < 0 ? -1 : 1;
            }
            return xa.Length.CompareTo(xb.Length);
        }

        // the "latest release" JSON -> ReleaseInfo (null if it isn't one). Only assets on this repo's release download path are taken.
        public static ReleaseInfo ParseRelease(string json)
        {
            Dictionary<string, object> o;
            try { o = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>; } catch { return null; }
            if (o == null || !o.ContainsKey("tag_name")) return null;
            var r = new ReleaseInfo { Tag = S(o, "tag_name"), Name = S(o, "name"), Page = S(o, "html_url"), Draft = B(o, "draft"), Pre = B(o, "prerelease") };
            object a; var assets = o.TryGetValue("assets", out a) ? a as object[] : null;
            if (assets == null) { var al = a as System.Collections.ArrayList; if (al != null) assets = al.ToArray(); }
            var names = new List<KeyValuePair<string, Dictionary<string, object>>>();
            if (assets != null) foreach (var x in assets) { var d = x as Dictionary<string, object>; if (d != null) names.Add(new KeyValuePair<string, Dictionary<string, object>>(S(d, "name"), d)); }
            // the zip: name ends -win.zip (a plain release before a -tabs-test one if both are there)
            Dictionary<string, object> zip = null;
            foreach (var kv in names) if (kv.Key.EndsWith("-win.zip", StringComparison.OrdinalIgnoreCase) && AssetUrlOk(S(kv.Value, "browser_download_url")) && (zip == null || (S(zip, "name").IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 && kv.Key.IndexOf("test", StringComparison.OrdinalIgnoreCase) < 0))) zip = kv.Value;
            if (zip != null)
            {
                r.ZipName = S(zip, "name"); r.ZipUrl = S(zip, "browser_download_url");
                long sz; r.ZipSize = long.TryParse(S(zip, "size"), out sz) ? sz : -1;
                // its checksum: <zip>.sha256, else a SHA256SUMS / checksums file
                foreach (var kv in names)
                {
                    string n = kv.Key.ToLowerInvariant(); if (!AssetUrlOk(S(kv.Value, "browser_download_url"))) continue;
                    bool own = n == r.ZipName.ToLowerInvariant() + ".sha256", list = n == "sha256sums" || n == "sha256sums.txt" || n == "checksums.txt" || n.EndsWith(".sha256sums");
                    if (own || (list && r.SumName.Length == 0)) { r.SumName = kv.Key; r.SumUrl = S(kv.Value, "browser_download_url"); if (own) break; }
                }
            }
            return r;
        }
        static string S(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""; }
        static bool B(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }

        // downloads only ever go to this repo's release assets on github.com (which redirects to GitHub's asset CDN)
        public static bool AssetUrlOk(string url)
        {
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            return u.Scheme == "https" && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && u.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase);
        }
        public static bool RedirectOk(string url)
        {
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https") return false;
            string h = u.Host.ToLowerInvariant();
            return h == "github.com" || h == "objects.githubusercontent.com" || h == "release-assets.githubusercontent.com" || h.EndsWith(".githubusercontent.com");
        }

        // a checksum file -> the hex SHA-256 for zipName (a lone hash counts for a <zip>.sha256 file), else null
        public static string ParseChecksum(string text, string zipName)
        {
            if (text == null) return null;
            string lone = null; int hashes = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim(); if (line.Length < 64) continue;
                var parts = line.Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (!IsHex64(p)) continue;
                    hashes++; lone = p.ToLowerInvariant();
                    foreach (var q in parts) if (q.Equals(zipName, StringComparison.OrdinalIgnoreCase) || q.EndsWith("/" + zipName, StringComparison.OrdinalIgnoreCase)) return p.ToLowerInvariant();
                }
            }
            return hashes == 1 ? lone : null;
        }
        static bool IsHex64(string s) { if (s.Length != 64) return false; foreach (char c in s) if (!Uri.IsHexDigit(c)) return false; return true; }
        public static string Sha256(byte[] b) { using (var h = SHA256.Create()) { var d = h.ComputeHash(b); var sb = new StringBuilder(); foreach (var x in d) sb.Append(x.ToString("x2")); return sb.ToString(); } }

        // the checks before an update is applied; null = fine (exe = BotBeacons.exe's bytes from the zip)
        public static string Verify(byte[] zip, long expectSize, string expectSha, out byte[] exe)
        {
            exe = null;
            if (zip == null || zip.Length == 0) return "the download was empty";
            if (expectSize >= 0 && zip.Length != expectSize) return "the download is the wrong size (" + zip.Length + " bytes, expected " + expectSize + ")";
            if (!string.IsNullOrEmpty(expectSha) && Sha256(zip) != expectSha.ToLowerInvariant()) return "the download's SHA-256 doesn't match the release's checksum";
            List<ZipEntry> entries; string err = ZipMini.Read(zip, out entries); if (err != null) return "not a valid zip (" + err + ")";
            ZipEntry best = null;
            foreach (var e in entries)
            {
                string n = e.Name.Replace('\\', '/');
                if (!(n == "BotBeacons.exe" || n.EndsWith("/BotBeacons.exe", StringComparison.OrdinalIgnoreCase))) continue;
                if (n.StartsWith("source/", StringComparison.OrdinalIgnoreCase) || n.IndexOf("/source/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (best == null || n.Split('/').Length < best.Name.Split('/').Length) best = e;
            }
            if (best == null) return "the zip has no BotBeacons.exe";
            foreach (var e in entries) { byte[] data; string ee = ZipMini.Extract(zip, e, out data); if (ee != null) return "a file in the zip is damaged (" + e.Name + ": " + ee + ")"; if (e == best) exe = data; }
            if (exe.Length < 64 * 1024 || exe[0] != 'M' || exe[1] != 'Z') { exe = null; return "BotBeacons.exe in the zip isn't a Windows program"; }
            return null;
        }
    }

    class ZipEntry { public string Name; public int Method, Flags; public uint Crc; public long CSize, USize, Local; }

    // just enough of the zip format to check a release: central directory, stored/deflate entries, CRC-32 (no zip64, no encryption)
    static class ZipMini
    {
        static uint U32(byte[] b, long o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }
        static int U16(byte[] b, long o) { return b[o] | b[o + 1] << 8; }
        public static string Read(byte[] z, out List<ZipEntry> list)
        {
            list = new List<ZipEntry>();
            if (z.Length < 22) return "too short";
            long eocd = -1;
            for (long i = z.Length - 22; i >= Math.Max(0, z.Length - 22 - 65535); i--) if (U32(z, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) return "no end record";
            int count = U16(z, eocd + 10); long size = U32(z, eocd + 12), off = U32(z, eocd + 16);
            if (count == 0xFFFF || off == 0xFFFFFFFF) return "zip64 is not supported";
            if (off + size > eocd || off < 0) return "directory out of range";
            long p = off;
            for (int i = 0; i < count; i++)
            {
                if (p + 46 > eocd || U32(z, p) != 0x02014b50) return "bad directory entry " + i;
                var e = new ZipEntry { Flags = U16(z, p + 8), Method = U16(z, p + 10), Crc = U32(z, p + 16), CSize = U32(z, p + 20), USize = U32(z, p + 24), Local = U32(z, p + 42) };
                int nl = U16(z, p + 28), el = U16(z, p + 30), cl = U16(z, p + 32);
                if (p + 46 + nl > eocd) return "bad name " + i;
                e.Name = Encoding.UTF8.GetString(z, (int)p + 46, nl);
                if ((e.Flags & 1) != 0) return "encrypted entry " + e.Name;
                if (e.Name.Contains("..")) return "unsafe path " + e.Name;
                list.Add(e); p += 46 + nl + el + cl;
            }
            return null;
        }
        public static string Extract(byte[] z, ZipEntry e, out byte[] data)
        {
            data = null;
            if (e.Local + 30 > z.Length || U32(z, e.Local) != 0x04034b50) return "bad local header";
            long start = e.Local + 30 + U16(z, e.Local + 26) + U16(z, e.Local + 28);
            if (start + e.CSize > z.Length) return "data out of range";
            if (e.USize > UpdateLogic.MaxZip * 2) return "too big";
            if (e.Method == 0) { if (e.CSize != e.USize) return "size mismatch"; data = new byte[e.USize]; Array.Copy(z, start, data, 0, e.USize); }
            else if (e.Method == 8)
            {
                try
                {
                    using (var ms = new MemoryStream(z, (int)start, (int)e.CSize)) using (var ds = new DeflateStream(ms, CompressionMode.Decompress)) using (var outp = new MemoryStream())
                    {
                        var buf = new byte[65536]; int n; while ((n = ds.Read(buf, 0, buf.Length)) > 0) { outp.Write(buf, 0, n); if (outp.Length > e.USize) return "longer than recorded"; }
                        data = outp.ToArray();
                    }
                }
                catch (Exception ex) { return "inflate failed: " + ex.GetType().Name; }
                if (data.Length != e.USize) return "size mismatch";
            }
            else return "unsupported method " + e.Method;
            if (Crc32(data) != e.Crc) { data = null; return "CRC mismatch"; }
            return null;
        }
        static uint[] table;
        public static uint Crc32(byte[] d)
        {
            if (table == null) { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; } table = t; }
            uint crc = 0xFFFFFFFFu; foreach (var b in d) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8); return crc ^ 0xFFFFFFFFu;
        }
    }
}
