using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BotBeacons
{
    // "cloud" bubble style: a per-pixel-alpha layered window (drawn by CloudRender) that never takes focus, plus a small
    // owned borderless window over the reply field that holds a real TextBox (a layered window can't show child
    // controls). The TextBox window is the one with focus: Enter sends, Esc closes, and when it loses focus to anything
    // else the whole bubble closes (as the card does). Clicks on the cloud hit-test the send button and the footer links (Open in chat, Dismiss).
    class CloudBubble : BubbleBase
    {
        readonly BeaconForm owner;
        PetMessage att;
        readonly CloudRender r = new CloudRender();
        readonly CloudModel m = new CloudModel();
        readonly InputHost host;
        Rectangle lastBeacon; bool placed;
        int wx, wy;
        readonly DateTime opened = DateTime.Now;
        bool closing;

        public CloudBubble(BeaconForm owner, PetMessage attention)
        {
            this.owner = owner; att = attention;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; Text = owner.Def.Name;
            m.Name = owner.Def.Name; m.Accent = owner.Def.Color; m.Cue = "Reply to " + owner.Def.Name + "\u2026";
            if (att != null) { m.Attention = true; m.AttentionText = att.Text; }
            m.Status = DefaultStatus;
            host = new InputHost(r.FField);
            host.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Send(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseReason = "Esc"; Close(); }
                else if (e.Alt && e.KeyCode == Keys.O) { e.SuppressKeyPress = true; OpenApp(); }    // footer link "Open in chat"
                else if (e.Alt && e.KeyCode == Keys.D) { e.SuppressKeyPress = true; Dismiss(); }    // footer link "Dismiss"
            };
            host.Box.MouseWheel += (s, e) => ScrollBy(e.Delta);
            host.Box.TextChanged += (s, e) => LengthHint();
            host.Deactivate += (s, e) => HostDeactivated();
            host.FormClosed += (s, e) => { if (!closing) { CloseReason = "input closed"; Close(); } };
            Build();
        }

        static string DefaultStatus { get { return Settings.Link.Valid ? "End-to-end encrypted \u00b7 Esc closes" : "Not paired: right-click a beacon \u2192 Bots \u2192 Pair with bots"; } }
        public override string Style { get { return "cloud"; } }
        public override Rectangle ScreenBounds { get { return new Rectangle(wx, wy, r.W, r.H); } }

        // short speaker name for history lines ("Research Helper" -> "Helper"; "Echo Beacon" stays)
        string ShortName
        {
            get
            {
                var w = owner.Def.Name.Split(' ');
                return w.Length > 1 && !w[w.Length - 1].Equals("Beacon", StringComparison.OrdinalIgnoreCase) ? w[w.Length - 1] : owner.Def.Name;
            }
        }

        void Build()
        {
            m.Items.Clear();
            var h = owner.History; PetMessage lastIn = null;
            for (int i = Math.Max(0, h.Count - 30); i < h.Count; i++)
            {
                var x = h[i];
                if (!x.FromUser) lastIn = x;
                m.Items.Add(new CloudItem { Time = x.Time.ToString("HH:mm"), Who = x.FromUser ? "You" : ShortName, FromUser = x.FromUser, Attention = x.IsAttention, Text = x.Text + (x.Title.Length > 0 ? "  (" + x.Title + ")" : "") });
            }
            // gist: the latest message's title if it has one, else what this beacon is for
            m.Gist = lastIn != null && lastIn.Title.Length > 0 && !lastIn.Title.StartsWith("[") ? lastIn.Title : owner.Def.Purpose;
            m.Scroll = int.MaxValue;   // newest at the bottom; Layout clamps
            r.Layout(m);
        }

        public override void Refresh2()
        {
            if (IsDisposed) return;
            Build();
            if (placed) Place(lastBeacon); else Render();
        }

        public override bool ShowAttention(PetMessage a)
        {
            if (IsDisposed) return true;
            att = a; m.Attention = true; m.AttentionText = a.Text;
            Refresh2(); return true;
        }

        // Dismiss (footer link): mark read, end an attention call, close
        void Dismiss()
        {
            if (closing || IsDisposed) return;
            closing = true;
            if (!host.IsDisposed) host.Hide();
            owner.DismissFromBubble();
            CloseReason = "dismissed";
            BeginInvoke((Action)(() => { if (!IsDisposed) Close(); }));
        }

        public override void FocusInput() { if (!host.IsDisposed) { host.Activate(); host.Box.Focus(); } }
        public override void DevType(string text) { host.Box.Text = text; Send(); }
        public override void DevKey(string key)
        {
            if (key == "enter") Send();
            else if (key == "esc") { CloseReason = "Esc"; Close(); }
            else if (key == "dismiss") Dismiss();
            else if (key == "openapp" || key == "openchat") OpenApp();
        }

        // 1.1.4: the cloud goes to the beacon's left and above it by default (beacons usually live near the bottom
        // right), and flips right and/or below when that side would cross an edge of the beacon's monitor (taskbar
        // excluded). The trail is mirrored to match, so its smallest circle still lands on the beacon.
        public override void Place(Rectangle beacon) { PlaceCore(beacon, true); }
        // while the beacon is dragged: same choice, but only move the window unless the side changed (no redraw per move)
        public override void Follow(Rectangle beacon) { PlaceCore(beacon, false); }
        void PlaceCore(Rectangle beacon, bool redraw)
        {
            lastBeacon = beacon; placed = true;
            var p = Placement.Cloud(beacon, r.BodyH, Placement.WorkArea(beacon));
            bool trailRight = !p.Right, flipped = trailRight != m.TrailRight || p.Down != m.TrailBelow;
            if (flipped) { m.TrailRight = trailRight; m.TrailBelow = p.Down; r.Layout(m); }
            wx = p.Window.X; wy = p.Window.Y;
            if (redraw || flipped || !Visible) { Render(); return; }
            Native.SetWindowPos(Handle, IntPtr.Zero, wx, wy, 0, 0, 0x1 | 0x4 | 0x10);   // NOSIZE | NOZORDER | NOACTIVATE
            if (!host.IsDisposed) { var tb = r.TextBoxRect; host.Location = new Point(wx + tb.X, wy + tb.Y); }
        }

        // ------------------------------------------------------------ drawing (one premultiplied DIB, resized as needed)
        Bitmap frame; Graphics fg; IntPtr memDC, dib, oldObj; int fw, fh;
        void Surface(int w, int h)
        {
            if (frame != null && fw == w && fh == h) return;
            FreeSurface();
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr bits;
            memDC = Native.CreateCompatibleDC(IntPtr.Zero);
            dib = Native.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldObj = Native.SelectObject(memDC, dib);
            frame = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            fg = Graphics.FromImage(frame); fw = w; fh = h;
        }
        void FreeSurface()
        {
            if (fg != null) { fg.Dispose(); fg = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            if (memDC != IntPtr.Zero) { Native.SelectObject(memDC, oldObj); Native.DeleteObject(dib); Native.DeleteDC(memDC); memDC = IntPtr.Zero; }
        }

        void Render()
        {
            if (IsDisposed) return;
            var hh = Handle;   // make sure the window exists
            Surface(r.W, r.H);
            fg.ResetClip(); fg.Clear(Color.Transparent);
            r.Draw(fg, m);
            fg.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            var dst = new Native.POINT { x = wx, y = wy }; var sz = new Native.SIZE { cx = r.W, cy = r.H }; var s0 = new Native.POINT();
            var bf = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(hh, IntPtr.Zero, ref dst, ref sz, memDC, ref s0, 0, ref bf, 2);
            if (!host.IsDisposed)
            {
                var tb = r.TextBoxRect;
                host.Bounds = new Rectangle(wx + tb.X, wy + tb.Y, tb.Width, tb.Height);
            }
        }

        public override void ShowAndFocus(bool takeFocus)
        {
            Render();
            Show();
            host.Show(this);
            host.Box.SetCue(m.Cue);
            Render();
            if (takeFocus) { Native.SetForegroundWindow(host.Handle); FocusInput(); }
        }

        public override void SaveFrame(string path)
        {
            if (frame == null) return;
            using (var b = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent); g.DrawImageUnscaled(frame, 0, 0);
                    m.FieldText = host.IsDisposed ? "" : host.Box.Text;
                    r.DrawFieldText(g, m);
                }
                b.Save(path, ImageFormat.Png);
            }
        }

        // ------------------------------------------------------------ input
        // 1.1.6: say so as soon as the text gets too long for one message (only when that changes, to keep typing light)
        bool tooLong;
        void LengthHint()
        {
            if (m.Sending || host.IsDisposed) return;
            bool now = owner.ReplyTooLong(host.Box.Text.Trim());
            if (now == tooLong) return;
            tooLong = now;
            m.Status = now ? "Too long to send: please shorten it" : DefaultStatus; Render();
        }

        void Send()
        {
            if (m.Sending) return;
            string text = host.Box.Text.Trim(); if (text.Length == 0) return;
            int over = owner.ReplyOverPercent(text);
            if (over > 0) { tooLong = true; m.Status = Relay.TooLongNotice(over); Render(); return; }   // the text stays in the box
            m.Sending = true; host.Box.ReadOnly = true; m.Status = "Sending\u2026"; Render();
            owner.SendReplyText(text, (ok, err) =>
            {
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed) return;
                    m.Sending = false; host.Box.ReadOnly = false;
                    if (ok) { host.Box.Text = ""; m.Status = Wake.SentStatus(owner.Def.Id, "Sent (encrypted)"); }   // 1.2.6: or "no wake hook"
                    else m.Status = "Send failed: " + err;
                    Render(); if (Form.ActiveForm == host) host.Box.Focus();
                }));
            });
        }

        void OpenApp()
        {
            if (closing || IsDisposed) return;
            closing = true;   // the app taking focus must not count as "clicked away"
            if (!host.IsDisposed) host.Hide();
            string how = owner.OpenChatFromBubble(att);   // footer link "Open in chat": same deep link as Open in app
            m.Status = "Opening the chat in Grok Bot (" + how + ")\u2026"; Render();
            CloseReason = "opened chat";
            BeginInvoke((Action)(() => { if (!IsDisposed) Close(); }));
        }

        void ScrollBy(int delta)
        {
            int old = m.Scroll; m.Scroll = Math.Max(0, Math.Min(r.MaxScroll, m.Scroll - delta * 36 / 120));
            if (m.Scroll != old) Render();
        }

        void HostDeactivated()
        {
            if (closing || ClosingNow || IsDisposed) return;
            if ((DateTime.Now - opened).TotalMilliseconds < 600) return;   // ignore a stray deactivate right after opening; never grab focus back
            if (ModalHold.Held || ModalHold.OwnDialogActive()) return;   // 1.2.4: focus went to one of our dialogs
            CloseReason = "clicked away"; Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hs = r.SendRect.Contains(e.Location), ho = r.OpenRect.Contains(e.Location), hd = r.DismissRect.Contains(e.Location);
            Cursor = hs || ho || hd ? Cursors.Hand : Cursors.Default;
            if (hs != m.HoverSend || ho != m.HoverOpen || hd != m.HoverDismiss) { m.HoverSend = hs; m.HoverOpen = ho; m.HoverDismiss = hd; Render(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (m.HoverSend || m.HoverOpen || m.HoverDismiss) { m.HoverSend = m.HoverOpen = m.HoverDismiss = false; Render(); } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (r.SendRect.Contains(e.Location)) Send();
            else if (r.OpenRect.Contains(e.Location)) OpenApp();
            else if (r.DismissRect.Contains(e.Location)) Dismiss();
            else FocusInput();
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollBy(e.Delta); }

        // ------------------------------------------------------------ window plumbing
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x8000000; return cp; } }   // layered | toolwindow | topmost | noactivate
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == 0x21) { msg.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE: the TextBox keeps focus
            base.WndProc(ref msg);
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; base.OnFormClosing(e); }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true;
            if (!host.IsDisposed) host.Close();
            base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing) { FreeSurface(); if (disposing) { r.Dispose(); if (!host.IsDisposed) host.Dispose(); } base.Dispose(disposing); }
    }

    // the borderless window over the cloud's reply field
    class InputHost : Form
    {
        public readonly CueTextBox Box;
        public InputHost(Font f)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = CloudRender.FieldColor; KeyPreview = false;
            Box = new CueTextBox { BorderStyle = BorderStyle.None, BackColor = CloudRender.FieldColor, ForeColor = CloudRender.TextColor, Font = f, MaxLength = 20000, Dock = DockStyle.Fill };   // 1.1.6: the real cap is the sealed size (see Send)
            Controls.Add(Box);
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8; return cp; } }   // toolwindow | topmost
        protected override bool ShowWithoutActivation { get { return true; } }   // never activates on show; a click in the box does
    }

    // single-line TextBox with a grey placeholder (EM_SETCUEBANNER, shown even while focused)
    class CueTextBox : TextBox
    {
        string cue = "";
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        public void SetCue(string text) { cue = text; if (IsHandleCreated) SendMessage(Handle, 0x1501, (IntPtr)1, cue); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (cue.Length > 0) SendMessage(Handle, 0x1501, (IntPtr)1, cue); }
    }
}
