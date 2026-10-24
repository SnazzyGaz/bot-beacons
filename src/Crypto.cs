using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BotBeacons
{
    // Message security (protocol "v1"):
    //   master key K (32 random bytes, agreed at pairing) -> encKey = HMAC(K,"bot-beacons enc v1"), macKey = HMAC(K,"bot-beacons mac v1")
    //   envelope = {"v":1,"d":<direction>,"iv":b64,"ct":b64,"tag":b64}
    //   ct  = AES-256-CBC(encKey, iv, utf8(json payload)), PKCS7
    //   tag = HMAC-SHA256(macKey, "v1|" + d + "|" + iv + "|" + ct)   (encrypt-then-MAC, checked in constant time before decrypting)
    //   payload carries "id" (random) and "ts" (unix ms) so the receiver can reject replays and stale messages.
    static class Crypto
    {
        public const string BotToPet = "b2p", PetToBot = "p2b";
        static readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public static byte[] Random(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
        public static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
        public static byte[] Hmac(byte[] key, string msg) { using (var h = new HMACSHA256(key)) return h.ComputeHash(Encoding.UTF8.GetBytes(msg)); }
        public static byte[] Sha256(string s) { using (var h = SHA256.Create()) return h.ComputeHash(Encoding.UTF8.GetBytes(s)); }
        public static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        public static bool FixedEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0;
        }

        public static string Seal(byte[] key, string direction, Dictionary<string, object> payload)
        {
            byte[] encKey = Hmac(key, "bot-beacons enc v1"), macKey = Hmac(key, "bot-beacons mac v1");
            byte[] iv = Random(16), plain = Encoding.UTF8.GetBytes(json.Serialize(payload)), ct;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var enc = aes.CreateEncryptor()) ct = enc.TransformFinalBlock(plain, 0, plain.Length);
            }
            string ivs = Convert.ToBase64String(iv), cts = Convert.ToBase64String(ct);
            string tag = Convert.ToBase64String(Hmac(macKey, "v1|" + direction + "|" + ivs + "|" + cts));
            return json.Serialize(new Dictionary<string, object> { { "v", 1 }, { "d", direction }, { "iv", ivs }, { "ct", cts }, { "tag", tag } });
        }

        // Returns the payload, or null with a reason. Never throws on hostile input.
        public static Dictionary<string, object> Open(byte[] key, string expectDirection, string body, out string reason)
        {
            reason = null;
            try
            {
                var env = json.DeserializeObject(body ?? "") as Dictionary<string, object>;
                if (env == null || !env.ContainsKey("tag") || !env.ContainsKey("ct")) { reason = "not an encrypted envelope"; return null; }
                if (Str(env, "v") != "1") { reason = "unknown version"; return null; }
                string d = Str(env, "d"), ivs = Str(env, "iv"), cts = Str(env, "ct");
                if (d != expectDirection) { reason = "wrong direction"; return null; }
                byte[] encKey = Hmac(key, "bot-beacons enc v1"), macKey = Hmac(key, "bot-beacons mac v1");
                byte[] want = Hmac(macKey, "v1|" + d + "|" + ivs + "|" + cts), got = Convert.FromBase64String(Str(env, "tag"));
                if (!FixedEquals(want, got)) { reason = "bad signature"; return null; }
                byte[] iv = Convert.FromBase64String(ivs), ct = Convert.FromBase64String(cts), plain;
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var dec = aes.CreateDecryptor()) plain = dec.TransformFinalBlock(ct, 0, ct.Length);
                }
                var p = json.DeserializeObject(Encoding.UTF8.GetString(plain)) as Dictionary<string, object>;
                if (p == null) { reason = "bad payload"; return null; }
                return p;
            }
            catch { reason = "malformed"; return null; }
        }

        public static string Str(Dictionary<string, object> o, string k)
        {
            object v; return o != null && o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        // ---- DPAPI: secrets at rest are bound to this Windows user account
        static readonly byte[] entropy = Encoding.UTF8.GetBytes("BotBeacons secrets v1");
        public static string Protect(string s) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), entropy, DataProtectionScope.CurrentUser)); }
        public static string Unprotect(string s) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), entropy, DataProtectionScope.CurrentUser)); }

        // ---- pairing helpers (ECDH P-256)
        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O/1/I
        public static string NewPairCode()
        {
            var b = Random(10); var sb = new StringBuilder();
            for (int i = 0; i < 10; i++) { if (i == 5) sb.Append('-'); sb.Append(CodeAlphabet[b[i] & 31]); }
            return sb.ToString();
        }
        public static string NormCode(string c)
        {
            var sb = new StringBuilder(); foreach (char ch in (c ?? "").ToUpperInvariant()) if (CodeAlphabet.IndexOf(ch) >= 0) sb.Append(ch); return sb.ToString();
        }
        public static string RendezvousTopic(string code) { return "bbp-" + Hex(Sha256("bot-beacons pair rv v1|" + NormCode(code))).Substring(0, 40); }
        public static byte[] CodeKey(string code) { return Hmac(Encoding.UTF8.GetBytes(NormCode(code)), "bot-beacons pair v1"); }

        // CNG public blob = "ECK1" + len(32) + X + Y ; on the wire we send X||Y (64 bytes)
        public static string PubXY(ECDiffieHellmanCng ec)
        {
            byte[] blob = ec.PublicKey.ToByteArray(), xy = new byte[64];
            Buffer.BlockCopy(blob, 8, xy, 0, 64); return Convert.ToBase64String(xy);
        }
        public static byte[] Agree(ECDiffieHellmanCng ec, string peerXYb64)
        {
            byte[] xy = Convert.FromBase64String(peerXYb64); if (xy.Length != 64) throw new Exception("bad key");
            var blob = new byte[72];
            blob[0] = 0x45; blob[1] = 0x43; blob[2] = 0x4B; blob[3] = 0x31; blob[4] = 32;   // ECK1, 32
            Buffer.BlockCopy(xy, 0, blob, 8, 64);
            using (var peer = ECDiffieHellmanCngPublicKey.FromByteArray(blob, CngKeyBlobFormat.EccPublicBlob))
            {
                ec.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash; ec.HashAlgorithm = CngAlgorithm.Sha256;
                ec.SecretPrepend = null; ec.SecretAppend = null;
                return ec.DeriveKeyMaterial(peer);   // SHA256(shared x coordinate)
            }
        }
    }
}
