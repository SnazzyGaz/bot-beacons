using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BotBeacons
{
    // Speech bubble for one beacon: conversation history, reply box and Send. Closes on X, Esc, clicking elsewhere or
    // clicking the beacon again. Opened for an attention call it shows the reason. Every card has two quiet footer links,
    // "Open in chat" (Alt+O) and "Dismiss" (Alt+D); they never take focus or activate the card.
    class Bubble : BubbleBase
    {
        static readonly Color Bg = Color.FromArgb(24, 25, 28), Panel2 = Color.FromArgb(36, 38, 43), Fg = Color.FromArgb(225, 228, 232), Dim = Color.FromArgb(140, 146, 155);
        readonly Color Accent;
        readonly BeaconForm owner;
        readonly Label header, purpose, body, status;
        readonly Panel scroller;
        readonly TextBox input;
        readonly Button send, close;
        readonly FooterLink openChat, dismiss;
        readonly Label attReason;
        readonly PetMessage att;
        static readonly Color Warn = Color.FromArgb(255, 196, 64);

        public Bubble(BeaconForm owner, PetMessage attention)
        {
            att = attention;
            this.owner = owner; Accent = owner.Def.Color;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f); Width = 350; KeyPreview = true;

            header = new Label { AutoSize = false, Left = 14, Top = 9, Width = 290, Height = 20, ForeColor = Accent, Font = new Font("Segoe UI Semibold", 10f), Text = owner.Def.Name };
            purpose = new Label { AutoSize = false, Left = 14, Top = 29, Width = 300, Height = 17, ForeColor = Dim, Font = new Font("Segoe UI", 8.5f), Text = owner.Def.Purpose };
            close = new Button { Text = "\u2715", Left = Width - 34, Top = 6, Width = 26, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Dim, BackColor = Bg, TabStop = false };
            close.FlatAppearance.BorderSize = 0; close.Click += (s, e) => { CloseReason = "X button"; Close(); };
            int top = 52;
            if (att != null)
            {
                attReason = new Label { AutoSize = false, Left = 14, Top = top, Width = Width - 28, ForeColor = Warn, BackColor = Panel2, Padding = new Padding(6, 5, 6, 5), Font = new Font("Segoe UI Semibold", 9.5f),
                    Text = "\u26A0 Needs you in the Grok Bot app" + Environment.NewLine + att.Text };
                attReason.Height = TextRenderer.MeasureText(attReason.Text, attReason.Font, new Size(attReason.Width - 12, 10000), TextFormatFlags.WordBreak).Height + 12;
                Controls.Add(attReason);
                top = attReason.Bottom + 10;
            }
            scroller = new Panel { Left = 10, Top = top, Width = Width - 20, AutoScroll = true, BackColor = Bg };
            body = new Label { AutoSize = false, Left = 4, Top = 0, Width = scroller.Width - 24, ForeColor = Fg, BackColor = Bg };
            scroller.Controls.Add(body);
            input = new TextBox { Left = 14, Width = Width - 28 - 70, BackColor = Panel2, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f), MaxLength = 1500 };
            send = new Button { Text = "Send", Width = 62, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.Black, Font = new Font("Segoe UI Semibold", 9f) };
            send.FlatAppearance.BorderSize = 0; send.Click += (s, e) => Send();
            status = new Label { AutoSize = false, Left = 14, Width = Width - 28, Height = 18, ForeColor = Dim, Font = new Font("Segoe UI", 8.5f), AutoEllipsis = true };
            // footer links: small dim text, amber while an attention call is pending (they are what clears it)
            Color linkC = att != null || owner.Attention ? Warn : Dim;
            openChat = new FooterLink("Open in chat", "Alt+O: open " + owner.Def.Name + "'s chat in the Grok Bot app and mark this read", linkC, Fg);
            dismiss = new FooterLink("Dismiss", "Alt+D: mark this read and close", linkC, Fg);
            openChat.Click += (s, e) => OpenChat(); dismiss.Click += (s, e) => Dismiss();
            Controls.AddRange(new Control[] { header, purpose, close, scroller, input, send, status, openChat, dismiss });
            AcceptButton = send;
            Refresh2();
            status.Text = Settings.Link.Valid ? "End-to-end encrypted" : "Not paired: right-click a beacon \u2192 Pair with bots";
        }

        // Dismiss: mark read (ending an attention call too) and close
        void Dismiss()
        {
            if (ClosingNow || IsDisposed) return;
            owner.DismissFromBubble();
            CloseReason = "dismissed";
            BeginInvoke((Action)(() => { if (!IsDisposed) Close(); }));
        }
        // Open in chat: the bot's chat in the Grok Bot app (ends an attention call), then close
        void OpenChat()
        {
            if (ClosingNow || IsDisposed) return;
            string how = owner.OpenChatFromBubble(att);
            status.Text = "Opening the chat in Grok Bot (" + how + ")\u2026";
            CloseReason = "opened chat";
            BeginInvoke((Action)(() => { if (!IsDisposed) Close(); }));
        }

        // keyboard: Alt+O / Alt+D while the user is in the card (the links themselves are never focusable)
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.O)) { OpenChat(); return true; }
            if (keyData == (Keys.Alt | Keys.D)) { Dismiss(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public override void DevKey(string key)
        {
            if (key == "dismiss") Dismiss();
            else if (key == "openapp" || key == "openchat") OpenChat();
            else base.DevKey(key);
        }

        public override void Refresh2()
        {
            var h = owner.History; string text;
            if (h.Count == 0) text = "No messages yet. Type below to talk to " + owner.Def.Name + ".";
            else
            {
                var parts = new List<string>();
                for (int i = Math.Max(0, h.Count - 20); i < h.Count; i++)
                {
                    var m = h[i];
                    string who = m.FromUser ? "You" : owner.Def.Name;
                    string extra = (m.IsAttention ? "  needs attention" : "") + (m.Title.Length > 0 ? "  " + m.Title : "");
                    parts.Add(m.Time.ToString("HH:mm") + "  " + who + extra + Environment.NewLine + m.Text);
                }
                text = string.Join(Environment.NewLine + Environment.NewLine, parts.ToArray());
            }
            body.Text = text;
            var sz = TextRenderer.MeasureText(text, body.Font, new Size(body.Width, 100000), TextFormatFlags.WordBreak);
            body.Height = sz.Height + 6;
            scroller.Height = Math.Min(300, body.Height + 2);
            input.Top = scroller.Bottom + 8; send.Left = input.Right + 6; send.Top = input.Top - 1;
            status.Top = input.Bottom + 6;
            dismiss.Top = openChat.Top = status.Top + 1;
            dismiss.Left = Width - 14 - dismiss.Width; openChat.Left = dismiss.Left - 12 - openChat.Width;
            status.Width = openChat.Left - 8 - status.Left;
            Height = status.Bottom + 10;
            scroller.AutoScrollPosition = new Point(0, body.Height);
            Invalidate();
        }

        public override void FocusInput() { input.Focus(); }
        public override void DevType(string text) { input.Text = text; Send(); }

        // to the right of the beacon, or to its left near the right screen edge
        public override void Place(Rectangle beacon)
        {
            var scr = Screen.FromPoint(new Point(beacon.X + beacon.Width / 2, beacon.Y + beacon.Height / 2)).WorkingArea;
            int bx = beacon.Right - 18, by = beacon.Y + 10;
            if (bx + Width > scr.Right) bx = beacon.X - Width + 18;
            by = Math.Max(scr.Top, Math.Min(by, scr.Bottom - Height));
            bx = Math.Max(scr.Left, Math.Min(bx, scr.Right - Width));
            Location = new Point(bx, by);
        }

        void Send()
        {
            string text = input.Text.Trim(); if (text.Length == 0) return;
            input.Enabled = false; send.Enabled = false; status.Text = "Sending\u2026";
            owner.SendReplyText(text, (ok, err) =>
            {
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    input.Enabled = true; send.Enabled = true;
                    if (ok) { input.Text = ""; status.Text = "Sent (encrypted)."; }
                    else status.Text = "Send failed: " + err;
                    if (Form.ActiveForm == this) input.Focus();   // only if the user is still in the bubble
                }));
            });
        }

        readonly DateTime opened = DateTime.Now;
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (ClosingNow || IsDisposed) return;
            if ((DateTime.Now - opened).TotalMilliseconds < 600) return;   // a stray deactivate right after opening: ignore, never grab focus back
            CloseReason = "clicked away"; Close();
        }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) { CloseReason = "Esc"; Close(); } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8; cp.ClassStyle |= 0x20000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }   // shows as SW_SHOWNOACTIVATE; clicking it activates normally

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Accent, 1)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Accent)) e.Graphics.FillRectangle(b, 0, 0, 3, Height);
        }
    }

    // A small text link for bubble footers. It is a Label (never focusable, so Tab and new bubbles never land on it)
    // and answers WM_MOUSEACTIVATE with MA_NOACTIVATE, so clicking it doesn't activate the bubble or move focus.
    // Keyboard: the owning bubble maps Alt+<key>; screen readers see it as a link.
    class FooterLink : Label
    {
        readonly Color normal, hover;
        static readonly ToolTip tips = new ToolTip { InitialDelay = 600, ShowAlways = true };
        public FooterLink(string text, string tip, Color normal, Color hover)
        {
            this.normal = normal; this.hover = hover;
            Text = text; AutoSize = true; UseMnemonic = false; ForeColor = normal; BackColor = Color.Transparent; Cursor = Cursors.Hand;
            Font = F; Padding = new Padding(0); Margin = new Padding(0); TabStop = false;
            AccessibleRole = AccessibleRole.Link; AccessibleName = text; AccessibleDescription = tip;
            tips.SetToolTip(this, tip);
        }
        static readonly Font F = new Font("Segoe UI", 8.5f), FU = new Font("Segoe UI", 8.5f, FontStyle.Underline);
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); ForeColor = hover; Font = FU; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); ForeColor = normal; Font = F; }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }
    }
}
