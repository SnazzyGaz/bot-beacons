using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.0: the Edit tab popover (pencil on a shelf row, or Edit tab... on a tab's menu). Every edit is a local override in
    // tabs.json and wins over beacons.json. Opened by the user's own click, so it takes focus (they type the name).
    class TabEditForm : Form
    {
        static readonly Color Bg = Color.FromArgb(28, 29, 34), Field = Color.FromArgb(38, 40, 46), Fg = Color.FromArgb(232, 234, 238), Dim = Color.FromArgb(146, 151, 160),
            Blue = Color.FromArgb(77, 132, 255), Line = Color.FromArgb(58, 61, 70), RedT = Color.FromArgb(255, 110, 100);
        public static readonly Color[] Swatches = { Color.FromArgb(56, 189, 248), Color.FromArgb(123, 132, 255), Color.FromArgb(74, 222, 128), Color.FromArgb(251, 146, 60), Color.FromArgb(244, 85, 120), Color.FromArgb(192, 112, 255), Color.FromArgb(200, 200, 205) };

        readonly BeaconForm b;
        readonly TextBox name, hex;
        readonly ChoiceRow beaconPic, botPic, customPic;
        readonly Button choose, remove;
        readonly Panel swatches;
        readonly Seg sound;
        readonly Toggle show;
        Color color; string pickedFile; string imageMode; bool removeArmed;
        readonly Label note;

        public TabEditForm(BeaconForm beacon)
        {
            b = beacon; var d = b.Def; var p = TabStore.Get(d.Id);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; KeyPreview = true;
            BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9f); ClientSize = new Size(300, 470); Text = "Edit tab";
            int x = 16, w = 268, y = 14;
            var title = new Label { Text = "Edit tab", Left = x, Top = y, AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Fg };
            var close = new Label { Text = "\u2715", Left = 270, Top = y, AutoSize = true, ForeColor = Dim, Cursor = Cursors.Hand };
            close.Click += (s, e) => Close();
            Controls.Add(title); Controls.Add(close);
            y += 36;
            Controls.Add(Cap("NAME", x, y)); y += 18;
            name = new TextBox { Left = x, Top = y, Width = w, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f), Text = d.Name, MaxLength = 40 };
            Controls.Add(name); y += 34;

            Controls.Add(Cap("TAB IMAGE", x, y)); y += 18;
            // 1.2.3: three choices; Beacon picture is the default, Bot's profile picture is greyed out until the bot sends one
            bool hasAvatar = System.IO.File.Exists(Avatars.PathFor(d.Id));
            imageMode = p.Image == "custom" && System.IO.File.Exists(TabStore.CustomPath(d.Id)) ? "custom" : p.Image == "profile" && hasAvatar ? "profile" : "beacon";
            beaconPic = new ChoiceRow("Beacon picture", "default \u2014 the beacon's own art", TabStore.BeaconPicture(d)) { Left = x, Top = y, Width = w, Height = 44 };
            y += 48;
            botPic = new ChoiceRow("Bot's profile picture", hasAvatar ? "sent by the bot" : "the bot hasn't sent one", hasAvatar ? TabStore.LoadFile(Avatars.PathFor(d.Id)) : null) { Left = x, Top = y, Width = w, Height = 44, Enabled = hasAvatar };
            y += 48;
            customPic = new ChoiceRow("Custom image", "PNG / ICO", System.IO.File.Exists(TabStore.CustomPath(d.Id)) ? TabStore.LoadFile(TabStore.CustomPath(d.Id)) : null) { Left = x, Top = y, Width = w - 84, Height = 44 };
            choose = Btn("Choose\u2026", x + w - 78, y + 9, 78, 26, Field, Fg);
            choose.Click += (s, e) => Choose();
            beaconPic.Click2 += () => SetImage("beacon"); botPic.Click2 += () => SetImage("profile"); customPic.Click2 += () => { if (pickedFile == null && !System.IO.File.Exists(TabStore.CustomPath(b.Def.Id))) Choose(); else SetImage("custom"); };
            Controls.Add(beaconPic); Controls.Add(botPic); Controls.Add(customPic); Controls.Add(choose);
            y += 52;

            Controls.Add(Cap("COLOUR", x, y)); y += 18;
            color = TabStore.ColorFor(d);
            swatches = new Panel { Left = x, Top = y, Width = 186, Height = 26 };
            swatches.Paint += (s, e) => PaintSwatches(e.Graphics);
            swatches.MouseClick += (s, e) => { int i = (int)(e.X / (26f * swatches.Height / 26f)); if (i >= 0 && i < Swatches.Length) { color = Swatches[i]; hex.Text = TabStore.Hex(color); swatches.Invalidate(); } };
            swatches.Cursor = Cursors.Hand;
            hex = new TextBox { Left = x + 192, Top = y + 1, Width = 76, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Text = TabStore.Hex(color), MaxLength = 7 };
            hex.TextChanged += (s, e) => { var c = TabStore.ParseColor(hex.Text); if (c != null) { color = c.Value; swatches.Invalidate(); } };
            Controls.Add(swatches); Controls.Add(hex);
            y += 38;

            Controls.Add(Cap("SOUND", x, y)); y += 18;
            string cur = Sound.IsMuted(d.Id) ? "off" : p.Sound.Length > 0 ? p.Sound : Sound.Set;
            sound = new Seg(new[] { "Custom", "Classic", "Off" }, cur == "off" ? 2 : cur == "classic" ? 1 : 0) { Left = x, Top = y, Width = 200, Height = 28 };
            var preview = Btn("\uD83D\uDD0A", x + w - 40, y, 40, 28, Field, Fg);
            preview.Font = new Font("Segoe UI Symbol", 10f);
            preview.Click += (s, e) => { if (sound.Index != 2) Sound.Preview(b.Def, false, sound.Index == 1 ? "classic" : "custom"); };
            var tip = new ToolTip(); tip.SetToolTip(preview, "Preview the message sound");
            Controls.Add(sound); Controls.Add(preview);
            y += 42;

            var showL = new Label { Text = "Show on shelf", Left = x, Top = y + 3, AutoSize = true, ForeColor = Fg };
            show = new Toggle { Left = x + w - 40, Top = y, On = !Visibility.IsHidden(d.Id) };
            Controls.Add(showL); Controls.Add(show);
            y += 34;
            note = new Label { Left = x, Top = y, Width = w, Height = 30, ForeColor = Dim, Font = new Font("Segoe UI", 8f), Text = TabStore.IsAdded(d.Id) ? "Your own tab. Bots send to it as \u201C" + d.Id + "\u201D." : "Edits stay on this PC and win over the bot's settings." };
            Controls.Add(note);
            y += 36;
            remove = Btn("Remove tab", x, y, 96, 30, Bg, RedT);
            remove.FlatAppearance.BorderColor = Color.FromArgb(120, 60, 56); remove.FlatAppearance.BorderSize = 1;
            remove.Click += (s, e) =>
            {
                if (!removeArmed) { removeArmed = true; remove.Text = "Click to confirm"; remove.Width = 120; return; }
                Close(); TabsSkin.Remove(b);
            };
            var cancel = Btn("Cancel", x + w - 152, y, 70, 30, Field, Fg);
            cancel.Click += (s, e) => Close();
            var save = Btn("Save", x + w - 76, y, 76, 30, Blue, Color.White);
            save.Click += (s, e) => Save();
            Controls.Add(remove); Controls.Add(cancel); Controls.Add(save);
            AcceptButton = save; CancelButton = cancel;
            ClientSize = new Size(300, y + 46);
            SetImage(imageMode);
            UiScale.Apply(this, UiScale.ForForms);   // 1.2.1: Size setting (Normal = 1.3x the 1.2.0 popover)
        }

        static Label Cap(string t, int x, int y) { return new Label { Text = t, Left = x, Top = y, AutoSize = true, ForeColor = Dim, Font = new Font("Segoe UI Semibold", 7.5f) }; }
        static Button Btn(string t, int x, int y, int w, int h, Color bg, Color fg)
        {
            var bt = new Button { Text = t, Left = x, Top = y, Width = w, Height = h, FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg, Cursor = Cursors.Hand };
            bt.FlatAppearance.BorderSize = 0; return bt;
        }

        void SetImage(string mode) { imageMode = mode; beaconPic.Selected = mode == "beacon"; botPic.Selected = mode == "profile"; customPic.Selected = mode == "custom"; }

        void Choose()
        {
            using (var dlg = new OpenFileDialog { Title = "Tab image", Filter = "Images (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" })
            {
                DialogResult dr; using (ModalHold.Hold("tab image picker")) dr = dlg.ShowDialog(this);
                if (dr != DialogResult.OK) return;
                Bitmap prev = null;
                try
                {
                    if (dlg.FileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) using (var ic = new Icon(dlg.FileName, 256, 256)) using (var bm = ic.ToBitmap()) prev = TabStore.Square(bm, 96);
                    else prev = TabStore.LoadFile(dlg.FileName);
                }
                catch { prev = null; }
                if (prev == null) { note.ForeColor = RedT; note.Text = "Could not read that image. Try a PNG or ICO."; return; }
                pickedFile = dlg.FileName; customPic.SetImage(prev); customPic.Sub = System.IO.Path.GetFileName(dlg.FileName);
                SetImage("custom");
            }
        }

        void PaintSwatches(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; float k = swatches.Height / 26f; g.ScaleTransform(k, k);
            for (int i = 0; i < Swatches.Length; i++)
            {
                var r = new Rectangle(i * 26 + 3, 3, 20, 20);
                using (var br = new SolidBrush(Swatches[i])) g.FillEllipse(br, r);
                if (Swatches[i].ToArgb() == color.ToArgb()) using (var pen = new Pen(Color.White, 2f)) g.DrawEllipse(pen, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            }
        }

        void Save()
        {
            var d = b.Def; var p = TabStore.Get(d.Id);
            string n = name.Text.Trim();
            p.Name = n.Length == 0 || n == TabStore.OriginalName(d) ? "" : n;
            if (pickedFile != null)
            {
                string err = TabStore.ImportCustom(d.Id, pickedFile);
                if (err != null) { note.ForeColor = RedT; note.Text = err; return; }
            }
            p.Image = imageMode;
            p.Color = color.ToArgb() == d.Color.ToArgb() ? "" : TabStore.Hex(color);
            if (sound.Index == 2) Sound.SetMuted(d.Id, true);
            else
            {
                if (Sound.IsMuted(d.Id)) Sound.SetMuted(d.Id, false);
                string want = sound.Index == 1 ? "classic" : "custom";
                p.Sound = want == Sound.Set && p.Sound.Length == 0 ? "" : want;
            }
            TabStore.ApplyName(d);
            Log.Write(d.Id + ": tab edited.");
            bool hide = !show.On;
            Close();
            TabsSkin.Changed();
            if (hide != Visibility.IsHidden(d.Id)) Visibility.SetHidden(b, hide);
        }

        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) Close(); }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; cp.ClassStyle |= 0x20000; return cp; } }   // tool window (no taskbar button), drop shadow
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        // drag the popover by its empty space
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84 && (int)m.Result == 1) m.Result = (IntPtr)2;   // WM_NCHITTEST: client -> caption
        }

        // a radio row: picture + title + small line, selected = blue outline
        class ChoiceRow : Control
        {
            Bitmap img; public string Title, Sub; bool sel;
            public event Action Click2;
            public bool Selected { get { return sel; } set { sel = value; Invalidate(); } }
            public ChoiceRow(string title, string sub, Bitmap image) { Title = title; Sub = sub; img = image; DoubleBuffered = true; Cursor = Cursors.Hand; BackColor = Bg; }
            public void SetImage(Bitmap b) { img = b; Invalidate(); }
            protected override void OnClick(EventArgs e) { base.OnClick(e); var h = Click2; if (h != null && Enabled) h(); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Bg);
                float k = this.Height / 44f; g.ScaleTransform(k, k);   // drawn at the 1.2.0 size, scaled with the row (1.2.1 Size)
                int W = (int)(this.Width / k), H = 44;
                var r = new RectangleF(0.5f, 0.5f, W - 1, H - 1);
                using (var path = TabRender.Round(r, 7))
                {
                    if (sel) using (var br = new SolidBrush(Color.FromArgb(34, 40, 58))) g.FillPath(br, path);
                    using (var pen = new Pen(sel ? Blue : Line)) g.DrawPath(pen, path);
                }
                var rc = new Rectangle(10, H / 2 - 7, 14, 14);
                Color fg = Enabled ? Fg : Line, dim = Enabled ? Dim : Line;   // 1.2.3: greyed out when not available
                using (var pen = new Pen(sel ? Blue : dim, 1.5f)) g.DrawEllipse(pen, rc);
                if (sel) using (var br = new SolidBrush(Blue)) g.FillEllipse(br, rc.X + 3.5f, rc.Y + 3.5f, 7, 7);
                int tx = 34;
                if (img != null) { TabRender.Avatar(g, img, new RectangleF(32, H / 2 - 13, 26, 26), Color.FromArgb(70, 255, 255, 255)); tx = 66; }
                else if (!Enabled) { using (var pen = new Pen(Line, 1.2f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(pen, 32, H / 2 - 13, 26, 26); tx = 66; }
                using (var f = new Font("Segoe UI Semibold", 9f)) using (var br = new SolidBrush(fg)) g.DrawString(Title, f, br, tx, 6);
                using (var f = new Font("Segoe UI", 7.5f)) using (var br = new SolidBrush(dim)) g.DrawString(Sub ?? "", f, br, tx, 24);
            }
        }

        // segmented control
        class Seg : Control
        {
            readonly string[] items; int index;
            public int Index { get { return index; } }
            public Seg(string[] items, int index) { this.items = items; this.index = index; DoubleBuffered = true; Cursor = Cursors.Hand; BackColor = Bg; }
            protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); index = Math.Max(0, Math.Min(items.Length - 1, e.X * items.Length / Width)); Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Bg);
                using (var path = TabRender.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6)) { using (var br = new SolidBrush(Field)) g.FillPath(br, path); using (var pen = new Pen(Line)) g.DrawPath(pen, path); }
                float w = Width / (float)items.Length;
                for (int i = 0; i < items.Length; i++)
                {
                    var r = new RectangleF(i * w + 2, 2, w - 4, Height - 4);
                    if (i == index) using (var path = TabRender.Round(r, 4.5f)) using (var br = new SolidBrush(Color.FromArgb(64, 68, 80))) g.FillPath(br, path);
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var br = new SolidBrush(i == index ? Fg : Dim)) g.DrawString(items[i], Font, br, r, sf);
                }
            }
        }

        // on/off switch
        class Toggle : Control
        {
            bool on; public bool On { get { return on; } set { on = value; Invalidate(); } }
            public Toggle() { Width = 40; Height = 22; DoubleBuffered = true; Cursor = Cursors.Hand; BackColor = Bg; }
            protected override void OnClick(EventArgs e) { base.OnClick(e); On = !On; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Bg);
                using (var path = TabRender.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), (Height - 1) / 2f)) using (var br = new SolidBrush(on ? Blue : Line)) g.FillPath(br, path);
                float d = Height - 6; float x = on ? Width - d - 3 : 3;
                using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, x, 3, d, d);
            }
        }
    }

    // 1.2.1: scale a laid-out form (positions, sizes, and the fonts it sets) for the Tabs Size setting
    static class UiScale
    {
        public static float ForForms { get { switch (TabsSkin.SizeSetting) { case "small": return 1f; case "large": return 1.55f; default: return 1.3f; } } }

        public static void Apply(Form root, float f)
        {
            if (Math.Abs(f - 1f) < 0.01f) return;
            var own = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Control, Font>>();
            Collect(root, own);   // before the form's font changes: children that inherit it keep inheriting
            var cs = root.ClientSize;
            root.SuspendLayout();
            root.Font = Bigger(root.Font, f);
            foreach (var kv in own) kv.Key.Font = Bigger(kv.Value, f);
            ScaleKids(root, f);
            root.ClientSize = new Size((int)Math.Round(cs.Width * f), (int)Math.Round(cs.Height * f));
            root.ResumeLayout(true);
        }
        static Font Bigger(Font x, float f) { return new Font(x.FontFamily, x.SizeInPoints * f, x.Style, GraphicsUnit.Point); }
        static void Collect(Control c, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Control, Font>> own)
        {
            foreach (Control k in c.Controls) { if (!ReferenceEquals(k.Font, c.Font)) own.Add(new System.Collections.Generic.KeyValuePair<Control, Font>(k, k.Font)); Collect(k, own); }
        }
        static void ScaleKids(Control c, float f)
        {
            foreach (Control k in c.Controls)
            {
                var b = k.Bounds;
                k.SetBounds((int)Math.Round(b.X * f), (int)Math.Round(b.Y * f), (int)Math.Round(b.Width * f), (int)Math.Round(b.Height * f));
                ScaleKids(k, f);
            }
        }
    }
}
