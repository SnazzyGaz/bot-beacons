using System;
using System.Drawing;
using System.Windows.Forms;

namespace BotBeacons
{
    // Shows the one-time pairing code. The user gives it to their bot ("pair Bot Beacons: ABCDE-FGHJK").
    class PairForm : Form
    {
        public readonly Pairing Session;
        readonly Label status;
        readonly bool quiet;
        protected override bool ShowWithoutActivation { get { return quiet; } }
        // quiet (dev hook): topmost via the window style instead of the TopMost property, which would activate it
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; if (quiet) cp.ExStyle |= 0x00000008; return cp; } }

        public PairForm(string server, bool quiet = false, Pairing existing = null)
        {
            this.quiet = quiet;
            bool resume = existing != null && existing.Active;
            Session = resume ? existing : new Pairing(server);
            Text = "Pair Bot Beacons with your bots"; FormBorderStyle = FormBorderStyle.FixedDialog; ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = !quiet; ClientSize = new Size(440, 230);
            BackColor = Color.FromArgb(24, 25, 28); ForeColor = Color.FromArgb(225, 228, 232); Font = new Font("Segoe UI", 10f);
            var intro = new Label { Left = 20, Top = 16, Width = 400, Height = 44, Text = "Give this one-time code to your bot. It works once and expires in 10 minutes. You can close this window: pairing keeps waiting for your bot." };
            var code = new Label { Left = 20, Top = 66, Width = 400, Height = 54, Text = Session.Code, Font = new Font("Consolas", 26f, FontStyle.Bold), ForeColor = Color.FromArgb(255, 110, 40), TextAlign = ContentAlignment.MiddleCenter };
            var copy = new Button { Text = "Copy code", Left = 20, Top = 130, Width = 110, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 38, 43) };
            copy.Click += (s, e) => { try { Clipboard.SetText(Session.Code); status.Text = "Copied."; } catch { } };
            var close = new Button { Text = "Close", Left = 310, Top = 130, Width = 110, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 38, 43) };
            close.Click += (s, e) => Close();
            var cancel = new Button { Text = "Cancel pairing", Left = 165, Top = 130, Width = 130, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 38, 43) };
            cancel.Click += (s, e) => { Session.Cancel(); Close(); };
            status = new Label { Left = 20, Top = 176, Width = 400, Height = 40, Text = Session.Status, ForeColor = Color.FromArgb(140, 146, 155) };
            Controls.AddRange(new Control[] { intro, code, copy, cancel, close, status });
            Session.Changed += () => { if (!IsDisposed) BeginInvoke((Action)(() => { status.Text = Session.Status; if (Session.Done) { status.ForeColor = Color.FromArgb(120, 220, 120); var t = new Timer { Interval = 2500 }; t.Tick += (s2, e2) => { t.Stop(); Close(); }; t.Start(); } })); };
            // closing only hides the code: the session keeps waiting for the bot until the code expires (or Cancel pairing)
            FormClosed += (s, e) => { if (Session.Active) Log.Write("Pairing window closed; still waiting for the bot."); };
            if (!resume) Session.Start();
        }
    }
}
