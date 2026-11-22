using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace DeskRewind
{
    internal static class LayoutDiagram
    {
        internal static void DrawWorkspace(Graphics graphics, Rectangle area, Workspace layout, WorkspaceWindow focused,
            float scale, Font labelFont)
        {
            var screens = layout.Screens.Select(screen => screen.Bounds.Rectangle).ToArray();
            int index = layout.Windows.IndexOf(focused);
            var occupied = layout.Windows.Where(window => window.State != WindowState.Minimized).Select(window => window.Frame.Rectangle);
            var selected = index < 0 ? new WindowTile[0] : new[] { new WindowTile(focused.Frame.Rectangle, Glass.InkFor(index),
                focused.State == WindowState.Minimized) };
            Paint(graphics, area, screens, occupied.ToArray(), selected, scale,
                layout.Screens.Select(screen => screen.Label + " · " + layout.Windows.Count(window => window.Device == screen.Device)).ToArray(), labelFont);
        }

        internal static void DrawHistory(Graphics graphics, Rectangle area, DesktopLayout layout, float scale)
        {
            var screens = layout.Displays.Select(display => display.Bounds).ToArray();
            var windows = layout.Windows.Where(window => window.State != WindowState.Minimized).ToArray();
            var foreground = windows.GroupBy(window => window.Display.Name).Select(group => group.First())
                .Select(window => new WindowTile(window.Frame, Glass.InkFor(window), false)).ToArray();
            Paint(graphics, area, screens, windows.Select(window => window.Frame).ToArray(), foreground, scale, null, null);
        }

        private static void Paint(Graphics graphics, Rectangle area, Rectangle[] screens, Rectangle[] occupied,
            WindowTile[] highlighted, float scale, string[] labels, Font labelFont)
        {
            if (screens.Length == 0 || area.Width <= 0 || area.Height <= 0) return;
            bool detailed = labels != null;
            var desktop = screens.Aggregate(Rectangle.Union);
            float zoom = Math.Min(area.Width / (float)desktop.Width, area.Height / (float)desktop.Height);
            float x = area.X + (area.Width - desktop.Width * zoom) / 2;
            float y = area.Y + (area.Height - desktop.Height * zoom) / 2;
            Func<Rectangle, Rectangle> project = value => Rectangle.Round(new RectangleF(x + (value.X - desktop.X) * zoom,
                y + (value.Y - desktop.Y) * zoom, value.Width * zoom, value.Height * zoom));

            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var clip = new Region())
            {
                clip.MakeEmpty();
                for (int i = 0; i < screens.Length; i++)
                {
                    var monitor = Rectangle.Inflate(project(screens[i]), detailed ? -3 : -1, detailed ? -3 : -1);
                    if (monitor.Width < 3 || monitor.Height < 3) continue;
                    float radius = (detailed ? 10 : 3) * scale;
                    Glass.Panel(graphics, monitor, Glass.Card, Glass.Rim, radius);
                    using (var path = Glass.Corners(Rectangle.Inflate(monitor, -1, -1), radius)) clip.Union(path);
                    if (!detailed) continue;
                    DrawGrid(graphics, monitor, scale);
                    var label = new Rectangle(monitor.X + 1, monitor.Y - (int)(24 * scale), monitor.Width, (int)(18 * scale));
                    Glass.TextAt(graphics, labels[i], labelFont, label, Glass.Muted);
                }

                graphics.SetClip(clip, CombineMode.Intersect);
                using (var coverage = new Region())
                using (var wash = new SolidBrush(Color.FromArgb(detailed ? 34 : 68, Glass.Muted)))
                {
                    coverage.MakeEmpty();
                    foreach (var window in occupied) coverage.Union(project(window));
                    graphics.FillRegion(wash, coverage);
                }
                foreach (var window in highlighted)
                {
                    var box = project(window.Bounds);
                    if (box.Width <= 0 || box.Height <= 0) continue;
                    Color ink = window.Ink;
                    using (var fill = new SolidBrush(Color.FromArgb(window.Minimized ? 30 : detailed ? 72 : 93, ink)))
                    using (var edge = new Pen(Color.FromArgb(235, ink), detailed ? 1.8f * scale : 1f))
                    {
                        if (window.Minimized) edge.DashStyle = DashStyle.Dash;
                        Glass.Fill(graphics, fill, box, (detailed ? 5 : 2) * scale);
                        Glass.Stroke(graphics, edge, box, (detailed ? 5 : 2) * scale);
                    }
                }
            }
            graphics.Restore(state);
        }

        private static void DrawGrid(Graphics graphics, Rectangle area, float scale)
        {
            var grid = Rectangle.Inflate(area, -(int)(9 * scale), -(int)(8 * scale));
            if (grid.Width <= 0 || grid.Height <= 0) return;
            using (var pen = new Pen(Color.FromArgb(108, Glass.Rim)))
            {
                for (int i = 1; i < 6; i++)
                {
                    int x = grid.Left + grid.Width * i / 6;
                    graphics.DrawLine(pen, x, grid.Top, x, grid.Bottom);
                }
                for (int i = 1; i < 4; i++)
                {
                    int y = grid.Top + grid.Height * i / 4;
                    graphics.DrawLine(pen, grid.Left, y, grid.Right, y);
                }
            }
        }

        private sealed class WindowTile
        {
            internal readonly Rectangle Bounds;
            internal readonly Color Ink;
            internal readonly bool Minimized;

            internal WindowTile(Rectangle bounds, Color ink, bool minimized)
            {
                Bounds = bounds;
                Ink = ink;
                Minimized = minimized;
            }
        }
    }
}
