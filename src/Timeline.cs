using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DeskRewind
{
    internal sealed class Timeline : Form
    {
        private readonly DesktopLayout[] layouts;
        private readonly Label title, status, keys;
        private readonly GlassButton older, newer, close, restore, workspaces;
        private readonly FilmCard[] cards;
        private readonly List<Font> fonts = new List<Font>();
        private readonly float scale;
        private int selected, first;
        private bool closing;

        internal event Action<DesktopLayout> SelectionChanged;
        internal event Action<DesktopLayout> RestoreRequested;
        internal event Action WorkspacesRequested;

        internal Timeline(DesktopLayout[] layouts, Display display)
        {
            this.layouts = layouts;
            scale = display.Scale;
            Text = "DeskRewind";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Glass.Surface;
            ForeColor = Glass.Text;

            Rectangle work = display.Work;
            int width = Math.Min(Px(988), work.Width - Px(24));
            int height = Math.Min(Px(270), work.Height - Px(16));
            Bounds = new Rectangle(work.X + (work.Width - width) / 2,
                work.Bottom - height - Px(22), width, height);

            title = AddLabel("Window history", "Segoe UI Semibold", 22, Glass.Text);
            keys = AddLabel("← →  Browse     Enter  Restore     Esc  Close", "Segoe UI", 12, Glass.Muted);
            status = AddLabel("", "Segoe UI", 14, Glass.Text);
            older = AddButton("Older layout", "older", () => Select(selected + 1, false));
            newer = AddButton("Newer layout", "newer", () => Select(selected - 1, false));
            close = AddButton("Close history", "close", Close);
            workspaces = AddButton("Workspaces", null, () => { if (WorkspacesRequested != null) WorkspacesRequested(); });
            older.IconOnly = newer.IconOnly = close.IconOnly = true;
            close.Quiet = close.Destructive = true;
            restore = AddButton("Restore", "restore", Apply);
            restore.AccessibleName = "Restore selected layout";
            restore.Primary = true;

            int count = Math.Min(layouts.Length, Math.Max(1, Math.Min(5, (width - Px(48)) / Px(145))));
            cards = new FilmCard[count];
            for (int i = 0; i < cards.Length; i++)
            {
                var card = new FilmCard(scale, UseFont("Segoe UI Semibold", 13), UseFont("Consolas", 11));
                card.Click += (sender, args) => Select(card.Index, true);
                card.MouseMove += (sender, args) => Select(card.Index, false);
                card.MouseWheel += OnWheel;
                Controls.Add(card);
                cards[i] = card;
            }
            MouseWheel += OnWheel;
            selected = layouts.Length > 1 ? 1 : 0;
            PlaceControls();
            RefreshCards();
            ActiveControl = cards.First(card => card.Index == selected);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int rounded = 2;
            int dark = 1;
            int border = ColorTranslator.ToWin32(Glass.Rim);
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            Native.DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
            Native.DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
        }

        private int Px(int value) { return (int)Math.Round(value * scale); }

        private Font UseFont(string face, int size)
        {
            var font = new Font(face, Px(size), FontStyle.Regular, GraphicsUnit.Pixel);
            fonts.Add(font);
            return font;
        }

        private Label AddLabel(string text, string face, int size, Color color)
        {
            var label = new Label { Text = text, Font = UseFont(face, size), ForeColor = color,
                BackColor = Glass.Surface, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(label);
            return label;
        }

        private GlassButton AddButton(string text, string glyph, Action clicked)
        {
            var button = new GlassButton { Text = text, Glyph = glyph, AccessibleName = text, DisplayScale = scale,
                Font = UseFont("Segoe UI Semibold", 13) };
            button.Click += (sender, args) => clicked();
            Controls.Add(button);
            return button;
        }

        private void PlaceControls()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            title.SetBounds(Px(69), Px(17), Math.Max(Px(100), width - Px(355)), Px(36));
            workspaces.SetBounds(width - Px(263), Px(19), Px(105), Px(32));
            older.SetBounds(width - Px(145), Px(19), Px(31), Px(32));
            newer.SetBounds(width - Px(109), Px(19), Px(31), Px(32));
            close.SetBounds(width - Px(62), Px(19), Px(35), Px(32));
            restore.SetBounds(width - Px(156), height - Px(51), Px(131), Px(38));
            keys.SetBounds(Px(25), height - Px(30), Math.Max(0, width - Px(195)), Px(18));
            status.SetBounds(Px(25), height - Px(51), Math.Max(0, width - Px(195)), Px(21));

            int gap = Px(10);
            int available = Math.Min(width - Px(48) - gap * (cards.Length - 1), Px(226) * cards.Length);
            int start = (width - available - gap * (cards.Length - 1)) / 2;
            for (int i = 0; i < cards.Length; i++)
            {
                int x = start + available * i / cards.Length + gap * i;
                int cardWidth = available * (i + 1) / cards.Length - available * i / cards.Length;
                cards[i].SetBounds(x, Px(70), cardWidth, Math.Max(Px(65), height - Px(140)));
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Glass.Surface);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var border = new Pen(Glass.Rim))
            {
                Glass.Stroke(e.Graphics, border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Px(14));
                e.Graphics.DrawLine(border, Px(24), Height - Px(62), Width - Px(24), Height - Px(62));
            }
            Glass.DrawMark(e.Graphics, new Rectangle(Px(25), Px(18), Px(34), Px(34)));
        }

        private void RefreshCards()
        {
            first = Math.Max(selected - cards.Length + 1, Math.Min(first, selected));
            for (int i = 0; i < cards.Length; i++)
            {
                int index = first + cards.Length - 1 - i;
                cards[i].UpdateLayout(layouts[index], index, selected == index);
            }
            older.Enabled = selected < layouts.Length - 1;
            newer.Enabled = selected > 0;
        }

        internal void UpdateStatus(DesktopLayout current)
        {
            var layout = layouts[selected];
            int available = layout.Windows.Count(window => current.ByIdentity.ContainsKey(window.Identity));
            string time = selected == 0 ? "Current layout" : layout.Time.ToString("HH:mm:ss");
            status.Text = layouts.Length == 1 ? "Move or resize a window to build history."
                : string.Format("{0}   ·   {1} of {2} {3} still open", time, available,
                    layout.Windows.Length, layout.Windows.Length == 1 ? "window" : "windows");
            restore.Enabled = selected != 0 && available != 0;
        }

        internal void RaiseSelection()
        {
            if (closing || IsDisposed) return;
            if (SelectionChanged != null) SelectionChanged(layouts[selected]);
            if (closing || IsDisposed) return;
            Native.SetWindowPos(Handle, Native.Topmost, 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (closing || IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(() => { if (!closing && !IsDisposed && !ContainsFocus) Close(); }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            base.OnFormClosing(e);
            if (e.Cancel) closing = false;
        }

        private void Select(int index, bool focus)
        {
            if (index < 0 || index >= layouts.Length) return;
            if (index != selected)
            {
                selected = index;
                RefreshCards();
                RaiseSelection();
            }
            if (focus) ActiveControl = cards.First(card => card.Index == index);
        }

        private void Apply()
        {
            if (restore.Enabled && RestoreRequested != null) RestoreRequested(layouts[selected]);
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            if (e.Delta != 0) Select(selected + Math.Sign(e.Delta), false);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys key)
        {
            switch (key)
            {
                case Keys.Left: Select(selected + 1, true); return true;
                case Keys.Right: Select(selected - 1, true); return true;
                case Keys.Home: Select(layouts.Length - 1, true); return true;
                case Keys.End: Select(0, true); return true;
                case Keys.Enter: Apply(); return true;
                case Keys.Escape: Close(); return true;
            }
            return base.ProcessCmdKey(ref message, key);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var font in fonts) font.Dispose();
        }

        private sealed class FilmCard : Button
        {
            private readonly float scale;
            private readonly Font heading, detail;
            private DesktopLayout layout;
            private bool selected;
            internal int Index { get; private set; }

            internal FilmCard(float scale, Font heading, Font detail)
            {
                this.scale = scale;
                this.heading = heading;
                this.detail = detail;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                BackColor = Glass.Surface;
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            }

            internal void UpdateLayout(DesktopLayout value, int index, bool active)
            {
                layout = value;
                Index = index;
                selected = active;
                AccessibleName = string.Format("{0}, {1} windows{2}", Clock, layout.Windows.Length, selected ? ", selected" : "");
                Invalidate();
            }

            private string Clock { get { return Index == 0 ? "Now" : layout.Time.ToString("HH:mm:ss"); } }
            private int Px(int value) { return (int)Math.Round(value * scale); }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (layout == null) return;
                Graphics graphics = e.Graphics;
                graphics.Clear(Glass.Surface);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
                Glass.Panel(graphics, bounds, selected ? Glass.Tint : Glass.Film,
                    selected || Focused ? Glass.Highlight : Glass.Rim, Px(11));
                var stage = new Rectangle(Px(8), Px(8), Width - Px(16), Height - Px(44));
                using (var ink = new SolidBrush(Glass.Surface)) Glass.Fill(graphics, ink, stage, Px(7));
                LayoutDiagram.DrawHistory(graphics, Rectangle.Inflate(stage, -Px(12), -Px(8)), layout, scale);

                int left = Px(12);
                if (selected)
                {
                    using (var spot = new SolidBrush(Glass.Accent)) graphics.FillEllipse(spot, Px(12), Height - Px(22), Px(5), Px(5));
                    left += Px(11);
                }
                var text = new Rectangle(left, Height - Px(31), Width - left - Px(12), Px(23));
                Glass.TextAt(graphics, Clock, heading, text, selected ? Glass.Accent : Glass.Text);
                Glass.TextAt(graphics, layout.Windows.Length + (layout.Windows.Length == 1 ? " window" : " windows"), detail,
                    text, Glass.Muted, TextFormatFlags.Right);
            }
        }
    }
}
