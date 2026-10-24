using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BotBeacons
{
    // Sticky "needs attention" calls, kept in %APPDATA%\BotBeacons\attention.txt so they survive a restart.
    // One line per beacon: id <TAB> agentId <TAB> time ticks <TAB> base64(UTF-8 text). Only Open in chat or Dismiss removes one.
    static class AttentionStore
    {
        static string FilePath { get { return Path.Combine(Settings.Dir, "attention.txt"); } }

        public static Dictionary<string, PetMessage> Load()
        {
            var d = new Dictionary<string, PetMessage>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return d;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split('\t');
                    if (p.Length != 4 || p[0].Length == 0) continue;
                    long ticks; if (!long.TryParse(p[2], out ticks)) ticks = DateTime.Now.Ticks;
                    d[p[0]] = new PetMessage { Id = "restored", Bot = p[0], AgentId = p[1], Kind = "attention", Time = new DateTime(ticks), Text = Encoding.UTF8.GetString(Convert.FromBase64String(p[3])) };
                }
            }
            catch (Exception ex) { Log.Write("Could not read attention.txt: " + ex.Message); }
            return d;
        }

        public static void Set(string id, PetMessage m)
        {
            var d = Load();
            if (m == null) { if (!d.Remove(id)) return; } else d[id] = m;
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                var lines = new List<string>();
                foreach (var kv in d) lines.Add(kv.Key + "\t" + kv.Value.AgentId + "\t" + kv.Value.Time.Ticks + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value.Text)));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception ex) { Log.Write("Could not write attention.txt: " + ex.Message); }
        }
    }
}
