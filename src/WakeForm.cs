using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BotBeacons
{
    // "Wake hook..." dialog: paste the routine's webhook URL and sender key. The key is masked and stored encrypted.
    class WakeForm : Form
    {
        readonly ComboBox scope, auth;
        readonly TextBox url, key;
        readonly Label status, keyHint;
        readonly List<string> scopeIds = new List<string>();
        static readonly string[] AuthIds = { "grokbot", "bearer", "hmac" };

        public WakeForm()
        {
            Text = "Wake hook"; FormBorderStyle = FormBorderStyle.FixedDialog; ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true; ClientSize = new Size(470, 318);
            BackColor = Color.FromArgb(24, 25, 28); ForeColor = Color.FromArgb(225, 228, 232); Font = new Font("Segoe UI", 9.75f);
            var dim = Color.FromArgb(140, 146, 155); var field = Color.FromArgb(36, 38, 43);

            var intro = new Label { Left = 16, Top = 12, Width = 440, Height = 36, ForeColor = dim, Text = "When you reply to a beacon, the app wakes your bot's webhook routine. Only the beacon name and time are sent; never the message." };
            var l1 = new Label { Left = 16, Top = 56, Width = 110, Text = "Applies to" };
            scope = new ComboBox { Left = 130, Top = 52, Width = 324, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = field, ForeColor = ForeColor, FlatStyle = FlatStyle.Flat };
            scope.Items.Add("All beacons (default)"); scopeIds.Add(null);
            foreach (var s in Program.Beacons) { scope.Items.Add(s.Def.Name + " only"); scopeIds.Add(s.Def.Id); }
            var l2 = new Label { Left = 16, Top = 92, Width = 110, Text = "Webhook URL" };
            url = new TextBox { Left = 130, Top = 88, Width = 324, BackColor = field, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            var l3 = new Label { Left = 16, Top = 128, Width = 110, Text = "Sender key" };
            key = new TextBox { Left = 130, Top = 124, Width = 324, UseSystemPasswordChar = true, BackColor = field, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            keyHint = new Label { Left = 130, Top = 150, Width = 324, Height = 18, ForeColor = dim, Font = new Font("Segoe UI", 8.25f) };
            var l4 = new Label { Left = 16, Top = 178, Width = 110, Text = "Sign-in style" };
            auth = new ComboBox { Left = 130, Top = 174, Width = 324, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = field, ForeColor = ForeColor, FlatStyle = FlatStyle.Flat };
            auth.Items.AddRange(new object[] { "Grok Bot routine (recommended)", "Bearer token only", "HMAC signature" });

            Button test = Btn("Test", 16), save = Btn("Save", 130), remove = Btn("Remove", 244), close = Btn("Close", 358);
            status = new Label { Left = 16, Top = 262, Width = 440, Height = 48, ForeColor = dim };
            Controls.AddRange(new Control[] { intro, l1, scope, l2, url, l3, key, keyHint, l4, auth, test, save, remove, close, status });

            Dark(scope); Dark(auth);
            scope.SelectedIndexChanged += (s, e) => LoadScope();
            test.Click += (s, e) => RunTest();
            save.Click += (s, e) => DoSave();
            remove.Click += (s, e) => { Wake.Set(Scope, null); LoadScope(); Say("Removed. Replies here won't wake anything.", dim); };
            close.Click += (s, e) => Close();
            AcceptButton = save; CancelButton = close;
            scope.SelectedIndex = 0;
        }

        static void Dark(ComboBox c)
        {
            c.DrawMode = DrawMode.OwnerDrawFixed; c.ItemHeight = 20;
            c.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var b = new SolidBrush(sel ? Color.FromArgb(58, 64, 76) : Color.FromArgb(36, 38, 43))) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, c.Items[e.Index].ToString(), c.Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), Color.FromArgb(225, 228, 232), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
        }

        Button Btn(string text, int left)
        {
            return new Button { Text = text, Left = left, Top = 216, Width = 98, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 38, 43) };
        }

        string Scope { get { return scopeIds[Math.Max(0, scope.SelectedIndex)]; } }

        void LoadScope()
        {
            var h = Wake.Get(Scope);
            url.Text = h.Url; key.Text = "";
            keyHint.Text = h.Key.Length > 0 ? "A key is saved. Leave blank to keep it." : "Paste the sender key from the routine panel.";
            int i = Array.IndexOf(AuthIds, h.Auth); auth.SelectedIndex = i < 0 ? 0 : i;
            Say(h.Valid ? "Set: wakes " + h.Host + "." : (Scope == null ? "Not set: replies are only relayed." : "Not set: uses the default."), Color.FromArgb(140, 146, 155));
        }

        WakeHook FromFields()
        {
            var old = Wake.Get(Scope);
            string k = WakeHook.CleanKey(key.Text);
            return new WakeHook { Url = url.Text.Trim(), Key = k.Length > 0 ? k : old.Key, Auth = AuthIds[Math.Max(0, auth.SelectedIndex)] };
        }

        void DoSave()
        {
            var h = FromFields();
            if (!h.Valid) { Say(h.Key.Length == 0 ? "Enter the sender key." : "Enter a full https:// URL.", Color.FromArgb(255, 120, 90)); return; }
            Wake.Set(Scope, h); key.Text = "";
            keyHint.Text = "A key is saved. Leave blank to keep it.";
            Say("Saved. Replies " + (Scope == null ? "to any beacon" : "to this beacon") + " will wake " + h.Host + ".", Color.FromArgb(120, 220, 120));
        }

        void RunTest()
        {
            var h = FromFields();
            if (!h.Valid) { Say(h.Key.Length == 0 ? "Enter the sender key." : "Enter a full https:// URL.", Color.FromArgb(255, 120, 90)); return; }
            Say("Testing\u2026", Color.FromArgb(140, 146, 155));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err; int code = Wake.Test(h, out err);
                string msg; Color c;
                if (code >= 200 && code < 300) { msg = "Test OK (HTTP " + code + "). The routine should wake with a test notice it ignores."; c = Color.FromArgb(120, 220, 120); }
                else if (code == 401 || code == 403) { msg = "Rejected (HTTP " + code + "): the sender key is wrong or was regenerated."; c = Color.FromArgb(255, 120, 90); }
                else if (code == 404) { msg = "Not found (HTTP 404): check the webhook URL."; c = Color.FromArgb(255, 120, 90); }
                else { msg = "Failed: " + (code < 0 ? err : "HTTP " + code) + "."; c = Color.FromArgb(255, 120, 90); }
                try { if (!IsDisposed) BeginInvoke((Action)(() => Say(msg, c))); } catch { }
            });
        }

        void Say(string text, Color c) { status.Text = text; status.ForeColor = c; }

        public void SnapTo(string path)
        {
            using (var b = new Bitmap(Width, Height)) { DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(path); }
        }
    }
}
