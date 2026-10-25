using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.5: bots create their own tabs. Tabs are not made in the app: a bot (set up from Grok Bot) simply sends to its own
    // beacon id. When a message for an id with no beacon arrives AND it came over the paired relay (decrypted, signed and fresh:
    // PetMessage.Verified, set only by Relay.Process after Crypto.Open + CheckFresh), the user gets a small prompt:
    //     New bot <name> wants a tab.   [Add]  [Not now]
    // Add (the default button) creates the tab, uses the bot's picture if it sent one, and shows the message(s) on it.
    // Not now (or closing the prompt) keeps the old behaviour: the message goes to the fallback beacon tagged [id], and that id
    // is not asked about again until Bot Beacons restarts. Anything unverified (local dev commands, user pings, a bad id)
    // never prompts and just goes to the fallback as before.
    enum NewBotAct { Route, Prompt, Queue, Fallback }

    static class NewBots
    {
        public const int MaxQueued = 20, MaxOpenPrompts = 3;
        static readonly Dictionary<string, List<PetMessage>> pending = new Dictionary<string, List<PetMessage>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> declined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Form> prompts = new Dictionary<string, Form>(StringComparer.OrdinalIgnoreCase);
        public static Action<string, string> PromptHook = null;   // harness: (id, name) instead of showing a window

        // the pure rule (tested): what to do with a message for a beacon id
        public static NewBotAct Decide(PetMessage m, bool known, bool declinedId, bool pendingId, int openPrompts)
        {
            if (m == null) return NewBotAct.Fallback;
            if (known) return NewBotAct.Route;
            if (!m.Verified || m.FromUser || !TabStore.SafeId(m.Bot)) return NewBotAct.Fallback;   // signed relay messages only
            if (declinedId) return NewBotAct.Fallback;
            if (pendingId) return NewBotAct.Queue;
            if (openPrompts >= MaxOpenPrompts) return NewBotAct.Fallback;
            return NewBotAct.Prompt;
        }

        // the name shown: the bot's own "name" field if it sent one, else the id made readable ("news-bot" -> "News Bot")
        public static string NameFor(string id, string sent)
        {
            var n = (sent ?? "").Trim();
            if (n.Length > 0) { var sb = new System.Text.StringBuilder(); foreach (char c in n) if (!char.IsControl(c)) sb.Append(c); n = sb.ToString().Trim(); if (n.Length > 32) n = n.Substring(0, 32).Trim(); }
            if (n.Length > 0) return n;
            var parts = (id ?? "").Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return parts.Length > 0 ? string.Join(" ", parts) : id;
        }

        // Program.Route calls this for an id with no beacon. true = handled here (prompting or queued for a prompt)
        public static bool Offer(PetMessage m)
        {
            string id = m.Bot ?? "";
            var act = Decide(m, false, declined.Contains(id), pending.ContainsKey(id), pending.Count);
            if (act == NewBotAct.Queue) { var q = pending[id]; if (q.Count < MaxQueued) q.Add(m); if (m.BotName.Length > 0) names[id] = NameFor(id, m.BotName); Log.Write("New bot '" + id + "': message kept for its tab (" + q.Count + ")."); return true; }
            if (act != NewBotAct.Prompt) return false;
            pending[id] = new List<PetMessage> { m };
            string name = NameFor(id, m.BotName); names[id] = name;
            Log.Write("New bot '" + id + "' wants a tab: asking.");
            if (PromptHook != null) { PromptHook(id, name); return true; }
            ShowPrompt(id, name);
            return true;
        }

        public static bool IsPending(string id) { return pending.ContainsKey(id ?? ""); }
        public static bool IsDeclined(string id) { return declined.Contains(id ?? ""); }
        public static void Decline(string id) { if (!string.IsNullOrEmpty(id)) declined.Add(id); }   // Remove tab: don't ask again this run
        public static void ResetForTests() { pending.Clear(); names.Clear(); declined.Clear(); prompts.Clear(); }

        // the user's answer. Add: a tab of its own (with the bot's picture if it sent one) and its messages on it
        public static BeaconForm Resolve(string id, bool add)
        {
            List<PetMessage> q; if (!pending.TryGetValue(id, out q)) return null;
            pending.Remove(id);
            Form f; if (prompts.TryGetValue(id, out f)) { prompts.Remove(id); try { if (!f.IsDisposed) f.Close(); } catch { } }
            if (!add)
            {
                declined.Add(id);
                Log.Write("New bot '" + id + "': not now (" + q.Count + " message(s) to the fallback beacon).");
                foreach (var m in q) Program.RouteFallback(m);
                return null;
            }
            string name; if (!names.TryGetValue(id, out name)) name = NameFor(id, "");
            var u = new UserTab { Id = id, Name = name, Color = TabStore.Hex(ColorFor(id)), Purpose = "" };
            TabStore.Add(u);
            if (System.IO.File.Exists(Avatars.PathFor(id)) && TabPictures.Setting != "beacon") { TabStore.Get(id).Image = "profile"; TabStore.Save(); }   // the bot's own picture
            var b = Program.AddBeacon(TabStore.DefFor(u));
            Log.Write("New bot '" + id + "': tab added (" + q.Count + " message(s)).");
            foreach (var m in q) b.Notify(m);
            return b;
        }

        static readonly Color[] Palette = { Color.FromArgb(56, 189, 248), Color.FromArgb(123, 132, 255), Color.FromArgb(74, 222, 128), Color.FromArgb(251, 146, 60), Color.FromArgb(244, 114, 182), Color.FromArgb(250, 204, 21), Color.FromArgb(45, 212, 191) };
        public static Color ColorFor(string id) { int h = 17; foreach (char c in (id ?? "").ToLowerInvariant()) h = unchecked(h * 31 + c); return Palette[(h & 0x7fffffff) % Palette.Length]; }

        static void ShowPrompt(string id, string name)
        {
            var f = new NewBotPrompt(id, name);
            prompts[id] = f;
            f.Answered += add => Resolve(id, add);
            f.FormClosed += (s, e) => { prompts.Remove(id); if (pending.ContainsKey(id)) Resolve(id, false); };   // closed with X = Not now
            var wa = Screen.PrimaryScreen.WorkingArea;
            int k = prompts.Count - 1;
            f.Location = new Point(wa.Right - f.Width - 16, wa.Bottom - f.Height - 16 - k * (f.Height + 10));
            f.Show();   // non-modal and without taking focus (ShowWithoutActivation): typing elsewhere never answers it
        }
    }

    // the prompt itself: small, dark, top-most, no taskbar button, never steals focus. Add is the default button.
    class NewBotPrompt : Form
    {
        public event Action<bool> Answered;
        bool answered;
        protected override bool ShowWithoutActivation { get { return true; } }

        public NewBotPrompt(string id, string name)
        {
            Text = "Bot Beacons"; FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; MaximizeBox = MinimizeBox = false;
            BackColor = Color.FromArgb(30, 32, 38); ForeColor = Color.FromArgb(235, 236, 240);
            Font = new Font("Segoe UI", 9.5f); ClientSize = new Size(340, 118);
            var title = new Label { Text = "New bot " + name + " wants a tab.", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), AutoSize = false, Bounds = new Rectangle(14, 12, 312, 24), AutoEllipsis = true };
            var sub = new Label { Text = "It sends to beacon id \u201c" + id + "\u201d. Its message is kept for the new tab.", ForeColor = Color.FromArgb(160, 164, 176), AutoSize = false, Bounds = new Rectangle(14, 38, 312, 36) };
            var add = new Button { Text = "Add", Bounds = new Rectangle(152, 80, 84, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 110, 220), ForeColor = Color.White };
            var later = new Button { Text = "Not now", Bounds = new Rectangle(242, 80, 84, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 51, 60), ForeColor = ForeColor };
            add.FlatAppearance.BorderColor = Color.FromArgb(120, 150, 240); later.FlatAppearance.BorderColor = Color.FromArgb(80, 84, 96);
            add.Click += (s, e) => Answer(true); later.Click += (s, e) => Answer(false);
            AcceptButton = add; CancelButton = later;
            Controls.AddRange(new Control[] { title, sub, add, later });
            ActiveControl = add;
        }
        void Answer(bool add) { if (answered) return; answered = true; var h = Answered; if (h != null) h(add); if (!IsDisposed) Close(); }
    }
}
