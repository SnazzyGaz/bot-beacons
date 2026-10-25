using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.2: for a beacon that is a tab, the slid-out card IS its conversation bubble (no separate dialog).
    //   Passive (shown by a tab click, or a hover in "Open tabs on: Hover"): picture, name, role, time and the full latest
    //   message (scrolls if long, links clickable). It never takes focus.
    //   Expanded (click the card): the recent conversation, a reply box, Send, and the Open in chat / Dismiss links.
    //   Focus is taken only when the user clicks into the reply box.
    // It collapses on Esc, after a reply is sent, when its tab is clicked again or when the user clicks elsewhere.
    // While it is expanded it never collapses because the mouse left it. TabsSkin places it beside the tab and polls
    // Esc / clicks elsewhere while it doesn't have focus.
    class TabCard : BubbleBase
    {
        static readonly Color Bg = Color.FromArgb(28, 29, 34), Panel2 = Color.FromArgb(38, 40, 46), Fg = Color.FromArgb(232, 234, 238), Dim = Color.FromArgb(146, 151, 160),
            Line = Color.FromArgb(58, 61, 70), Warn = Color.FromArgb(255, 196, 64);
        readonly BeaconForm owner; readonly PetMessage att;
        readonly float f; readonly int pad, imgSz, headH;
        readonly RichTextBox msg; readonly TextBox input; readonly Button send;
        readonly Label status, attReason, edit, close; readonly FooterLink openChat, dismiss;
        public bool Expanded { get; private set; }
        public BeaconForm Beacon { get { return owner; } }
        public TabEdge Edge = TabEdge.Right;
        Rectangle target; float k; int dir; readonly Timer anim = new Timer { Interval = 15 }; double animFrom; bool reallyClose;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        // drafts survive a collapse (clicking elsewhere must not throw away what was typed)
        static readonly Dictionary<string, string> drafts = new Dictionary<string, string>();

        public TabCard(BeaconForm owner, PetMessage attention)
        {
            this.owner = owner; att = attention;
            f = UiScale.ForForms; pad = S(14); imgSz = S(40); headH = S(64);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 10f * f); Width = S(380); KeyPreview = true; DoubleBuffered = true;

            close = new Label { Text = "\u2715", AutoSize = true, ForeColor = Dim, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10f * f), BackColor = Bg };
            close.Click += (s, e) => { CloseReason = "X button"; Close(); };
            edit = new Label { Text = "\u270E", AutoSize = true, ForeColor = Dim, Cursor = Cursors.Hand, Font = new Font("Segoe UI Symbol", 10.5f * f), BackColor = Bg };
            edit.Click += (s, e) => TabsSkin.Edit(owner, Bounds);
            new ToolTip().SetToolTip(edit, "Edit tab");
            if (att != null)
            {
                attReason = new Label { AutoSize = false, ForeColor = Warn, BackColor = Panel2, Padding = new Padding(S(6), S(5), S(6), S(5)), Font = new Font("Segoe UI Semibold", 9.5f * f),
                    Text = "\u26A0 Needs you in the Grok Bot app" + Environment.NewLine + att.Text };
                Controls.Add(attReason);
            }
            msg = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Bg, ForeColor = Fg, DetectUrls = true, ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = new Font("Segoe UI", 10f * f), TabStop = false, ShortcutsEnabled = true };
            // 1.2.3: no Cursor here. RichTextBox answers WM_SETCURSOR by letting the rich edit control set its own cursor
            // (I-beam over text, hand over links) and then, unless it was a link, applying the Cursor property as well, so
            // Cursor = Arrow made the pointer flip between I-beam and arrow on every mouse move over the message.
            // Left alone: I-beam over the (selectable) message, hand over links, arrow over the scroll bar.
            msg.LinkClicked += (s, e) => OpenLink(e.LinkText);
            msg.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left && msg.SelectionLength == 0) Expand(); };
            input = new TextBox { BackColor = Panel2, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f * f), MaxLength = 20000 };   // 1.1.6: the real cap is the sealed size
            input.TextChanged += (s, e) => { drafts[owner.Def.Id] = input.Text; LengthHint(); };
            send = new Button { Text = "Send", FlatStyle = FlatStyle.Flat, BackColor = owner.Def.Color, ForeColor = Color.Black, Font = new Font("Segoe UI Semibold", 9.5f * f) };
            send.FlatAppearance.BorderSize = 0; send.Click += (s, e) => Send();
            status = new Label { AutoSize = false, ForeColor = Dim, Font = new Font("Segoe UI", 8.5f * f), AutoEllipsis = true, BackColor = Bg };
            Color linkC = att != null || owner.Attention ? Warn : Dim;
            openChat = new FooterLink("Open in chat", "Alt+O: open " + owner.Def.Name + "'s chat in the Grok Bot app and mark this read", linkC, Fg);
            dismiss = new FooterLink("Dismiss", "Alt+D: mark this read and close", linkC, Fg);
            openChat.Click += (s, e) => OpenChat(); dismiss.Click += (s, e) => Dismiss();
            Controls.AddRange(new Control[] { close, edit, msg, input, send, status, openChat, dismiss });
            AcceptButton = send;
            string dr; if (drafts.TryGetValue(owner.Def.Id, out dr) && dr.Length > 0) { input.Text = dr; Expanded = true; }
            status.Text = Settings.Link.Valid ? "End-to-end encrypted" : "Not paired: right-click a tab \u2192 Bots \u2192 Pair with bots";
            anim.Tick += (s, e) => Step();
            Refresh2();
        }
        int S(float v) { return (int)Math.Round(v * f); }
        public override string Style { get { return "tabcard"; } }

        static void OpenLink(string url)
        {
            // only web links; anything else (file:, javascript:, custom schemes) is ignored
            if (url == null) return; url = url.Trim();
            if (!(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Log.Write("Link not opened: " + ex.GetType().Name); }
        }

        PetMessage Latest() { var h = owner.History; for (int i = h.Count - 1; i >= 0; i--) if (!h[i].FromUser) return h[i]; return h.Count > 0 ? h[h.Count - 1] : null; }

        public override void Refresh2()
        {
            int w = Width, x = pad, iw = w - 2 * pad, y = headH;
            close.Location = new Point(w - pad - close.PreferredWidth + S(4), S(10));
            edit.Location = new Point(close.Left - edit.PreferredWidth - S(4), S(9));
            if (attReason != null)
            {
                attReason.SetBounds(x, y, iw, TextRenderer.MeasureText(attReason.Text, attReason.Font, new Size(iw - S(12), 10000), TextFormatFlags.WordBreak).Height + S(12));
                y = attReason.Bottom + S(8);
            }
            string text;
            var h = owner.History;
            if (h.Count == 0) text = "No messages yet." + (Expanded ? " Type below to talk to " + owner.Def.Name + "." : " Click to reply.");
            else if (!Expanded) { var m = Latest(); text = (m.Title.Length > 0 ? m.Title + Environment.NewLine : "") + m.Text; }
            else
            {
                var parts = new List<string>();
                for (int i = Math.Max(0, h.Count - 20); i < h.Count; i++)
                {
                    var m = h[i];
                    parts.Add(m.Time.ToString("HH:mm") + "  " + (m.FromUser ? "You" : owner.Def.Name) + (m.IsAttention ? "  needs attention" : "") + (m.Title.Length > 0 ? "  " + m.Title : "") + Environment.NewLine + m.Text);
                }
                text = string.Join(Environment.NewLine + Environment.NewLine, parts.ToArray());
            }
            if (msg.Text != text) msg.Text = text;
            int need = TextRenderer.MeasureText(text, msg.Font, new Size(iw - S(18), 100000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + S(8);
            int maxH = Expanded ? S(300) : S(220);
            msg.SetBounds(x, y, iw, Math.Max(S(22), Math.Min(maxH, need)));
            msg.ScrollBars = need > maxH ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
            if (Expanded) { msg.SelectionStart = msg.TextLength; msg.ScrollToCaret(); }
            y = msg.Bottom + S(10);
            input.Visible = send.Visible = Expanded;
            if (Expanded)
            {
                send.Size = new Size(S(66), S(28));
                input.SetBounds(x, y, iw - send.Width - S(6), S(28));
                send.Location = new Point(input.Right + S(6), input.Top + (input.Height - send.Height) / 2);
                y = Math.Max(input.Bottom, send.Bottom) + S(8);
            }
            bool foot = Expanded || owner.Attention || att != null;   // (not status.Visible: that reads false until the card is shown)
            openChat.Visible = dismiss.Visible = foot; status.Visible = true;
            if (foot)
            {
                dismiss.Top = openChat.Top = y + 1;
                dismiss.Left = w - pad - dismiss.Width; openChat.Left = dismiss.Left - S(12) - openChat.Width;
                status.SetBounds(x, y, Math.Max(10, openChat.Left - S(8) - x), S(20));
                if (!Expanded) status.Text = "Click to reply";
                else if (status.Text == "Click to reply") status.Text = Settings.Link.Valid ? "End-to-end encrypted" : "Not paired: right-click a tab \u2192 Bots \u2192 Pair with bots";
                y += S(22);
            }
            else
            {
                status.Text = "Click to reply"; status.SetBounds(x, y, iw, S(20)); y += S(22);
            }
            int hgt = y + S(8);
            if (Height != hgt) { Height = hgt; if (Visible) Place(Rectangle.Empty); }
            Invalidate();
        }

        // click the card (not a link or a button): open the reply box in place. No focus yet: that comes with a click in the box
        public void Expand()
        {
            if (Expanded || ClosingNow) return;
            Expanded = true; Refresh2();
            Log.Write(owner.Def.Id + ": tab card expanded.");
        }
        public bool Busy { get { return Expanded && (input.Focused || ContainsFocus || input.Text.Trim().Length > 0); } }   // TabsSkin: never collapse on mouse leave

        public override void FocusInput() { if (!Expanded) Expand(); input.Focus(); }
        public override void DevType(string text) { if (!Expanded) Expand(); input.Text = text; Send(); }
        public override void DevKey(string key)
        {
            if (key == "dismiss") Dismiss();
            else if (key == "openapp" || key == "openchat") OpenChat();
            else if (key == "expand") Expand();
            else base.DevKey(key);
        }
        public override bool ShowAttention(PetMessage m) { return false; }   // reopened by the beacon with the reason shown

        void Dismiss()
        {
            if (ClosingNow || IsDisposed) return;
            owner.DismissFromBubble(); CloseReason = "dismissed";
            BeginInvoke((Action)(() => { if (!IsDisposed) Close(); }));
        }
        void OpenChat()
        {
            if (ClosingNow || IsDisposed) return;
            string how = owner.OpenChatFromBubble(att);
            status.Text = "Opening the chat in Grok Bot (" + how + ")\u2026"; CloseReason = "opened chat";
            BeginInvoke((Action)(() => { if (!IsDisposed) Close(); }));
        }
        protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.O)) { OpenChat(); return true; }
            if (keyData == (Keys.Alt | Keys.D)) { Dismiss(); return true; }
            if (keyData == Keys.Escape) { CloseReason = "Esc"; Close(); return true; }
            return base.ProcessCmdKey(ref m, keyData);
        }

        bool tooLong;
        void LengthHint()
        {
            bool now = owner.ReplyTooLong(input.Text.Trim());
            if (now == tooLong) return;
            tooLong = now; status.Text = now ? "Too long to send: please shorten it." : (Settings.Link.Valid ? "End-to-end encrypted" : status.Text);
        }
        void Send()
        {
            string text = input.Text.Trim(); if (text.Length == 0) return;
            int over = owner.ReplyOverPercent(text);
            if (over > 0) { tooLong = true; status.Text = Relay.TooLongNotice(over); return; }   // 1.1.6: the text stays in the box
            input.Enabled = false; send.Enabled = false; status.Text = "Sending\u2026";
            owner.SendReplyText(text, (ok, err) =>
            {
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed) return;
                    input.Enabled = true; send.Enabled = true;
                    if (ok)
                    {
                        input.Text = ""; drafts.Remove(owner.Def.Id); status.Text = Wake.SentStatus(owner.Def.Id, "Sent (encrypted).");
                        bool noWake = status.Text == Wake.NotSetShort;   // 1.2.6: keep the "no wake hook" line up long enough to read
                        var t = new Timer { Interval = noWake ? 3000 : 700 };   // let "Sent" show for a moment, then slide back in
                        t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); if (!IsDisposed && !ClosingNow) { CloseReason = "sent"; Close(); } };
                        t.Start();
                    }
                    else status.Text = "Send failed: " + err;
                }));
            });
        }

        // ---- placement and the slide (TabsSkin knows where this beacon's tab is)
        public override void Place(Rectangle anchor)
        {
            TabEdge e; var r = TabsSkin.CardRect(owner, Size, out e);
            if (r.IsEmpty) { var p = Placement.Card(anchor, Size, Placement.WorkArea(anchor)); r = new Rectangle(p.Window, Size); e = TabEdge.Right; }
            Edge = e; target = r; Apply();
        }
        void Apply()
        {
            var off = TabLayout.SlideOffset(Edge, target.Size, k);
            var at = new Point(target.X + off.X / 3, target.Y + off.Y / 3);   // a short slide (a third of its size) plus a fade
            if (Location != at) Location = at;
            double op = Math.Max(0.0, Math.Min(1.0, k));
            if (Math.Abs(Opacity - op) > 0.01) Opacity = op;
        }
        public override void ShowAndFocus(bool takeFocus)
        {
            k = AnimSettings.Mode == AnimMode.Off ? 1f : 0f; Opacity = k; Apply();
            Show();
            if (k < 1f) { dir = +1; animFrom = clock.Elapsed.TotalSeconds; anim.Start(); }
            if (takeFocus) { Expand(); Native.SetForegroundWindow(Handle); Activate(); input.Focus(); }
        }
        void Step()
        {
            double dt = clock.Elapsed.TotalSeconds - animFrom; animFrom = clock.Elapsed.TotalSeconds;
            k += (float)(dir > 0 ? dt / 0.15 : -dt / 0.12);
            if (dir > 0 && k >= 1f) { k = 1f; anim.Stop(); }
            if (dir < 0 && k <= 0f) { k = 0f; anim.Stop(); reallyClose = true; Apply(); Close(); return; }
            Apply();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyClose && e.CloseReason == System.Windows.Forms.CloseReason.UserClosing && AnimSettings.Mode != AnimMode.Off && Visible && k > 0f && !IsDisposed)
            {
                e.Cancel = true;                          // slide back in first; Step closes for real
                if (dir >= 0) { dir = -1; animFrom = clock.Elapsed.TotalSeconds; anim.Start(); }
                return;
            }
            anim.Stop();
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

        // clicking the card activates nothing, except a click in the reply box (the user is about to type)
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21)   // WM_MOUSEACTIVATE
            {
                var c = PointToClient(Cursor.Position);
                bool box = Expanded && (input.Bounds.Contains(c) || send.Bounds.Contains(c));
                if (!box && !Expanded && !close.Bounds.Contains(c) && !edit.Bounds.Contains(c) && !openChat.Bounds.Contains(c) && !dismiss.Bounds.Contains(c)) BeginInvoke((Action)Expand);
                m.Result = (IntPtr)(box ? 1 : 3); return;   // MA_ACTIVATE : MA_NOACTIVATE
            }
            base.WndProc(ref m);
        }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (ClosingNow || IsDisposed) return;
            // the user was typing and clicked somewhere else: collapse (the draft is kept for next time).
            // 1.2.4: not when focus went to one of our dialogs (Edit tab, its image picker)
            if (ModalHold.DeactivateCloses(ModalHold.Held, ModalHold.OwnDialogActive(), Bounds.Contains(Cursor.Position), 1e9)) { CloseReason = "clicked away"; Close(); }
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8; cp.ClassStyle |= 0x20000; return cp; } }   // tool window, topmost, shadow
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var p = new Pen(Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(owner.Def.Color)) g.FillRectangle(b, 0, 0, Math.Max(3, S(3)), Height);
            var d = owner.Def;
            TabRender.Avatar(g, TabStore.ImageFor(d), new RectangleF(pad, S(12), imgSz, imgSz), owner.Attention ? Warn : TabStore.ColorFor(d), 2f);
            float tx = pad + imgSz + S(10), right = edit.Left - S(6);
            var m = Latest(); string time = m != null ? m.Time.ToString("HH:mm") : "";
            using (var fn = new Font("Segoe UI Semibold", 11.5f * f)) using (var fr = new Font("Segoe UI", 9f * f))
            using (var bn = new SolidBrush(Fg)) using (var bd = new SolidBrush(Dim))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                float tw = g.MeasureString(time, fr).Width;
                g.DrawString(d.Name, fn, bn, new RectangleF(tx, S(12), Math.Max(10, right - tx - tw - S(6)), S(24)), sf);
                g.DrawString(time, fr, bd, right - tw, S(15));
                g.DrawString(d.Purpose ?? "", fr, bd, new RectangleF(tx, S(36), Math.Max(10, right - tx), S(20)), sf);
            }
            int n = owner.Unread.Count;
            if (n > 0 && !Expanded) TabRender.CountBadge(g, new PointF(pad + imgSz - S(2), S(14)), n, f);
        }
    }
}
