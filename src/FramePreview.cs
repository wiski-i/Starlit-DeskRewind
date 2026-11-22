using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace DeskRewind
{
    internal sealed class FramePreview : IDisposable
    {
        private readonly List<PreviewSurface> surfaces = new List<PreviewSurface>();

        internal void Show(DesktopLayout layout, DesktopLayout current, Rectangle controls)
        {
            var displays = current.Displays;
            var frames = new List<PreviewFrame>();
            for (int i = layout.Windows.Length - 1; i >= 0; i--)
            {
                var window = layout.Windows[i];
                if (!current.ByIdentity.ContainsKey(window.Identity) || !Native.IsWindow(window.Handle)) continue;
                Rectangle frame = window.TargetFrame(displays);
                if (!frame.IsEmpty) frames.Add(new PreviewFrame(window, frame, window.TargetDisplay(displays).Name));
            }

            while (surfaces.Count < displays.Length) surfaces.Add(new PreviewSurface());
            for (int i = 0; i < displays.Length; i++) surfaces[i].Draw(displays[i], frames, controls);
            for (int i = displays.Length; i < surfaces.Count; i++) surfaces[i].Hide();
        }

        public void Dispose()
        {
            foreach (var surface in surfaces) surface.Dispose();
            surfaces.Clear();
        }

        internal static RectangleF FindLabel(RectangleF area, float width, float height, float gap, IList<RectangleF> occupied)
        {
            if (width > area.Width || height > area.Height) return RectangleF.Empty;
            int rows = 1 + (int)((area.Height - height) / (height + gap));
            for (int row = 0; row < rows; row++)
            {
                int offset = row / 2;
                float y = row % 2 == 0 ? area.Top + offset * (height + gap) : area.Bottom - height - offset * (height + gap);
                var left = new RectangleF(area.Left, y, width, height);
                if (LabelFits(left, gap, occupied)) return left;
                var right = new RectangleF(area.Right - width, y, width, height);
                if (LabelFits(right, gap, occupied)) return right;
            }
            return RectangleF.Empty;
        }

        private static bool LabelFits(RectangleF label, float gap, IList<RectangleF> occupied)
        {
            var padded = RectangleF.Inflate(label, gap, gap);
            foreach (var other in occupied) if (other.IntersectsWith(padded)) return false;
            return true;
        }

        private sealed class PreviewFrame
        {
            internal readonly WindowLayout Window;
            internal readonly Rectangle Bounds;
            internal readonly string LabelDisplay;

            internal PreviewFrame(WindowLayout window, Rectangle bounds, string labelDisplay)
            { Window = window; Bounds = bounds; LabelDisplay = labelDisplay; }
        }

        private sealed class PreviewSurface : NativeWindow, IDisposable
        {
            private IntPtr dc, dib, previous;
            private Bitmap bitmap;

            internal PreviewSurface()
            {
                CreateHandle(new CreateParams
                {
                    Caption = "DeskRewind Preview", Style = Native.Popup,
                    ExStyle = Native.Layered | Native.Transparent | Native.NoActivate | Native.ToolWindow
                });
            }

            internal void Draw(Display display, List<PreviewFrame> frames, Rectangle controls)
            {
                Rectangle area = Rectangle.Empty;
                foreach (var frame in frames)
                {
                    Rectangle part = Rectangle.Intersect(frame.Bounds, display.Bounds);
                    if (!part.IsEmpty) area = area.IsEmpty ? part : Rectangle.Union(area, part);
                }
                if (area.Width <= 0 || area.Height <= 0 || !PrepareBuffer(area.Size)) { Hide(); return; }

                float scale = display.Scale;
                using (var graphics = Graphics.FromImage(bitmap))
                using (var font = new Font("Segoe UI Semibold", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap,
                    LineAlignment = StringAlignment.Center })
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    var painted = new HashSet<Rectangle>();
                    for (int i = frames.Count - 1; i >= 0; i--)
                    {
                        var item = frames[i];
                        var bounds = item.Bounds;
                        if (!bounds.IntersectsWith(display.Bounds) || !painted.Add(bounds)) continue;
                        bounds.Offset(-area.X, -area.Y);
                        var outline = new RectangleF(bounds.X + 2 * scale, bounds.Y + 2 * scale,
                            bounds.Width - 4 * scale, bounds.Height - 4 * scale);
                        Color ink = Glass.InkFor(item.Window);
                        using (var halo = new Pen(Color.FromArgb(177, Glass.Surface), 4 * scale))
                        using (var edge = new Pen(Color.FromArgb(208, ink), 1.75f * scale))
                        {
                            Glass.Stroke(graphics, halo, outline, 8 * scale);
                            Glass.Stroke(graphics, edge, outline, 8 * scale);
                        }
                    }

                    // Lay out labels after the outlines so no window edge cuts through a caption.
                    var occupied = new List<RectangleF>();
                    if (!controls.IsEmpty && controls.IntersectsWith(display.Bounds))
                    {
                        controls.Offset(-area.X, -area.Y);
                        occupied.Add(controls);
                    }
                    for (int i = frames.Count - 1; i >= 0; i--)
                    {
                        var item = frames[i];
                        if (item.LabelDisplay != display.Name) continue;
                        RectangleF space = Rectangle.Intersect(item.Bounds, display.Bounds);
                        space.Offset(-area.X, -area.Y);
                        space.Inflate(-8 * scale, -8 * scale);
                        if (space.Width < 52 * scale) continue;

                        float width = Math.Min(space.Width, Math.Min(300 * scale, graphics.MeasureString(item.Window.Title, font).Width + 27 * scale));
                        var label = FindLabel(space, width, 23 * scale, 4 * scale, occupied);
                        if (label.IsEmpty) continue;
                        occupied.Add(label);
                        using (var fill = new SolidBrush(Color.FromArgb(241, Glass.Surface)))
                        using (var text = new SolidBrush(Glass.Text))
                        using (var marker = new SolidBrush(Glass.InkFor(item.Window)))
                        {
                            Glass.Fill(graphics, fill, label, 5 * scale);
                            graphics.FillEllipse(marker, label.X + 8 * scale, label.Y + 8 * scale, 7 * scale, 7 * scale);
                            var title = new RectangleF(label.X + 19 * scale, label.Y, label.Width - 24 * scale, label.Height);
                            graphics.DrawString(item.Window.Title, font, text, title, format);
                        }
                    }
                }

                var position = new Native.Point(area.X, area.Y);
                var size = new Native.Size(area.Width, area.Height);
                var source = new Native.Point(0, 0);
                var blend = new Native.Blend { Opacity = 255, AlphaFormat = 1 };
                if (!Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref position, ref size, dc, ref source, 0, ref blend, 2))
                {
                    Hide();
                    return;
                }
                Native.SetWindowPos(Handle, Native.Topmost, 0, 0, 0, 0,
                    Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);
                Native.ShowWindow(Handle, Native.SwShowNoActivate);
            }

            private bool PrepareBuffer(Size size)
            {
                if (bitmap != null && bitmap.Size == size) return true;
                ReleaseBuffer();
                IntPtr pixels;
                var info = new Native.BitmapInfo
                {
                    Size = 40, Width = size.Width, Height = -size.Height, Planes = 1, BitCount = 32
                };
                dc = Native.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                dib = Native.CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) { ReleaseBuffer(); return false; }
                previous = Native.SelectObject(dc, dib);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                {
                    previous = IntPtr.Zero;
                    ReleaseBuffer();
                    return false;
                }
                bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, pixels);
                return true;
            }

            internal void Hide() { Native.ShowWindow(Handle, Native.SwHide); }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == Native.WmNcHitTest) { message.Result = new IntPtr(Native.HtTransparent); return; }
                base.WndProc(ref message);
            }

            private void ReleaseBuffer()
            {
                if (bitmap != null) { bitmap.Dispose(); bitmap = null; }
                if (previous != IntPtr.Zero) { Native.SelectObject(dc, previous); previous = IntPtr.Zero; }
                if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
                if (dc != IntPtr.Zero) { Native.DeleteDC(dc); dc = IntPtr.Zero; }
            }

            public void Dispose() { DestroyHandle(); ReleaseBuffer(); }
        }
    }
}
