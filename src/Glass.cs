using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskRewind
{
    internal static class Glass
    {
        internal static readonly Color Surface = Color.FromArgb(15, 19, 26);
        internal static readonly Color Card = Color.FromArgb(24, 31, 42);
        internal static readonly Color Film = Color.FromArgb(18, 24, 33);
        internal static readonly Color Tint = Color.FromArgb(32, 46, 60);
        internal static readonly Color Rim = Color.FromArgb(43, 55, 72);
        internal static readonly Color Text = Color.FromArgb(240, 245, 253);
        internal static readonly Color Muted = Color.FromArgb(158, 174, 196);
        internal static readonly Color Accent = Color.FromArgb(179, 224, 247);
        internal static readonly Color Highlight = Color.FromArgb(117, 194, 237);
        internal static readonly Color Danger = Color.FromArgb(255, 172, 179);
        private static readonly Color[] WindowColors =
        {
            Accent, Color.FromArgb(202, 186, 255), Color.FromArgb(141, 227, 207), Color.FromArgb(247, 204, 161)
        };

        internal static Color InkFor(WindowLayout window) { return InkFor(window.Identity - 1); }
        internal static Color InkFor(long index) { return WindowColors[(int)(index % WindowColors.Length)]; }

        /// <summary>Tooltip in the app palette; the system one is a light box.</summary>
        internal static ToolTip Tip(int initialDelay = 500, int reshowDelay = 100, int autoPopDelay = 5000)
        {
            var tip = new ToolTip
            {
                OwnerDraw = true, BackColor = Card, ForeColor = Text,
                InitialDelay = initialDelay, ReshowDelay = reshowDelay, AutoPopDelay = autoPopDelay
            };
            tip.Draw += (sender, e) =>
            {
                e.Graphics.Clear(Card);
                using (var rim = new Pen(Rim)) e.Graphics.DrawRectangle(rim, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
            return tip;
        }

        internal static Font Font(float size, bool strong)
        {
            return new Font(strong ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        internal static GraphicsPath Corners(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(2 * radius, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void Fill(Graphics graphics, Brush brush, RectangleF bounds, float radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = Corners(bounds, radius)) graphics.FillPath(brush, path);
        }

        internal static void Stroke(Graphics graphics, Pen pen, RectangleF bounds, float radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = Corners(bounds, radius)) graphics.DrawPath(pen, path);
        }

        internal static void Panel(Graphics graphics, RectangleF box, Color fill, Color rim, float radius)
        {
            using (var brush = new SolidBrush(fill)) Fill(graphics, brush, box, radius);
            using (var pen = new Pen(rim)) Stroke(graphics, pen, box, radius);
        }

        internal static void TextAt(Graphics graphics, string text, Font font, Rectangle area, Color color,
            TextFormatFlags extra = 0)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            TextRenderer.DrawText(graphics, text, font, area, color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | extra);
        }

        internal static void DrawMark(Graphics graphics, RectangleF bounds)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 40, bounds.Height / 40);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new LinearGradientBrush(new Rectangle(0, 0, 40, 40), Color.FromArgb(32, 50, 69), Surface, 45f))
            using (var border = new Pen(Color.FromArgb(59, 88, 112), 1f))
            using (var previous = new Pen(Color.FromArgb(204, InkFor(1)), 1.65f))
            using (var foreground = new Pen(Accent, 1.8f))
            using (var glyph = new Pen(Accent, 1.6f))
            {
                var tile = new RectangleF(1, 1, 38, 38);
                var window = new RectangleF(8, 16, 20, 16);
                Fill(graphics, background, tile, 10);
                Stroke(graphics, border, tile, 10);
                Stroke(graphics, previous, new RectangleF(15.5f, 9, 16, 15), 3.2f);
                using (var face = new SolidBrush(Color.FromArgb(24, 38, 51))) Fill(graphics, face, window, 3.2f);
                Stroke(graphics, foreground, window, 3.2f);
                glyph.StartCap = glyph.EndCap = LineCap.Round;
                glyph.LineJoin = LineJoin.Round;
                graphics.DrawLine(glyph, 8.8f, 20.5f, 27.2f, 20.5f);
                graphics.DrawLines(glyph, new[] { new PointF(17, 23), new PointF(14, 26), new PointF(17, 29) });
                graphics.DrawLine(glyph, 14, 26, 22, 26);
            }
            graphics.Restore(state);
        }

        internal static void Icon(Graphics graphics, string name, float x, float y, float scale, Color ink)
        {
            var saved = graphics.Save();
            graphics.TranslateTransform(x, y);
            graphics.ScaleTransform(scale, scale);
            using (var pen = new Pen(ink, 1.55f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "close":
                        graphics.DrawLine(pen, -4, -4, 4, 4);
                        graphics.DrawLine(pen, -4, 4, 4, -4);
                        break;
                    case "minimize": graphics.DrawLine(pen, -4.5f, 2.5f, 4.5f, 2.5f); break;
                    case "maximize": Stroke(graphics, pen, new RectangleF(-4.5f, -4.5f, 9, 9), 1.2f); break;
                    case "unmaximize":
                        graphics.DrawLines(pen, new[] { new PointF(-2, -5), new PointF(5, -5), new PointF(5, 2) });
                        Stroke(graphics, pen, new RectangleF(-5, -2, 7, 7), 1);
                        break;
                    case "older": graphics.DrawLines(pen, new[] { new PointF(2.5f, -4.5f), new PointF(-2, 0), new PointF(2.5f, 4.5f) }); break;
                    case "newer": graphics.DrawLines(pen, new[] { new PointF(-2.5f, -4.5f), new PointF(2, 0), new PointF(-2.5f, 4.5f) }); break;
                    case "restore":
                        graphics.DrawArc(pen, -5.5f, -5.5f, 11, 11, 225, 284);
                        graphics.DrawLines(pen, new[] { new PointF(-6, -4), new PointF(-5.7f, 0), new PointF(-2, -1.1f) });
                        break;
                    case "capture":
                        graphics.DrawLine(pen, -4.8f, 0, 4.8f, 0);
                        graphics.DrawLine(pen, 0, -4.8f, 0, 4.8f);
                        break;
                    case "history":
                        graphics.DrawEllipse(pen, -5.6f, -5.6f, 11.2f, 11.2f);
                        graphics.DrawLines(pen, new[] { new PointF(0, -3.3f), new PointF(0, 0), new PointF(2.7f, 1.9f) });
                        break;
                    case "desktop":
                        Stroke(graphics, pen, new RectangleF(-6, -5, 12, 8.5f), 1.5f);
                        graphics.DrawLine(pen, 0, 3.5f, 0, 6);
                        graphics.DrawLine(pen, -2.5f, 6, 2.5f, 6);
                        break;
                    case "more":
                        using (var brush = new SolidBrush(ink))
                            for (int i = -1; i <= 1; i++) graphics.FillEllipse(brush, i * 4 - 1, -1, 2, 2);
                        break;
                }
            }
            graphics.Restore(saved);
        }
    }

    internal sealed class GlassButton : Button
    {
        private bool hovered, pressed;
        internal bool Primary;
        internal bool Quiet;
        internal bool Destructive;
        internal string Glyph;
        internal bool IconOnly;
        internal float DisplayScale = 1;

        internal GlassButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            CausesValidation = false;
            BackColor = Glass.Surface;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
            float scale = DisplayScale;
            var fill = Quiet ? BackColor : Glass.Card;
            Color ink = Enabled ? Glass.Text : Glass.Muted;
            if (Primary && Enabled)
            {
                fill = pressed ? Glass.Highlight : hovered ? Color.FromArgb(212, 241, 255) : Glass.Accent;
                ink = Glass.Surface;
            }
            else if (Enabled && (hovered || pressed)) fill = Glass.Tint;
            if (Enabled && Destructive) ink = Glass.Danger;
            var rim = Focused ? Glass.Highlight : Quiet || (Primary && Enabled) ? fill : Glass.Rim;
            Glass.Panel(graphics, bounds, fill, rim, 9 * scale);

            if (IconOnly)
            {
                Glass.Icon(graphics, Glyph, Width / 2f, Height / 2f, scale, ink);
                return;
            }

            var area = Rectangle.Inflate(ClientRectangle, -(int)(10 * scale), 0);
            if (Glyph != null)
            {
                Glass.Icon(graphics, Glyph, 16 * scale, Height / 2f, scale, ink);
                area.X += (int)(20 * scale);
                area.Width -= (int)(20 * scale);
            }
            Glass.TextAt(graphics, Text, Font, area, ink, Glyph == null ? TextFormatFlags.HorizontalCenter : 0);
        }
    }

    internal sealed class GlassToggle : CheckBox
    {
        private readonly float scale;
        private bool hovered;

        internal GlassToggle(float scale)
        {
            this.scale = scale;
            FlatStyle = FlatStyle.Flat;
            AutoSize = false;
            Cursor = Cursors.Hand;
            BackColor = Glass.Surface;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(scale, (Height - 20 * scale) / 2, 35 * scale, 20 * scale);
            Glass.Panel(graphics, track, Checked && Enabled ? Glass.Accent : Glass.Card,
                Focused || hovered ? Glass.Highlight : Glass.Rim, 10 * scale);
            using (var thumb = new SolidBrush(Checked && Enabled ? Glass.Surface : Glass.Muted))
                graphics.FillEllipse(thumb, track.X + (Checked ? 18 : 4) * scale, track.Y + 4 * scale, 12 * scale, 12 * scale);
            int left = (int)(46 * scale);
            Glass.TextAt(graphics, Text, Font, new Rectangle(left, 0, Width - left, Height), Enabled ? Glass.Text : Glass.Muted);
        }
    }

    internal sealed class GlassMenu : ContextMenuStrip
    {
        internal GlassMenu()
        {
            ShowImageMargin = false;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Glass.Card;
            ForeColor = Glass.Text;
            Padding = new Padding(5);
            Renderer = new DarkRenderer();
        }

        private sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            internal DarkRenderer() { RoundedEdges = false; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                e.Graphics.Clear(Glass.Card);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Glass.Rim)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var box = new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Glass.Tint : Glass.Card))
                    Glass.Fill(e.Graphics, fill, box, 5);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Glass.Text : Glass.Muted;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Glass.Muted; base.OnRenderArrow(e); }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (var pen = new Pen(Glass.Rim)) e.Graphics.DrawLine(pen, 7, e.Item.Height / 2, e.Item.Width - 7, e.Item.Height / 2);
            }
        }
    }
}
