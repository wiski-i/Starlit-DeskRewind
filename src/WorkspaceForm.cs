using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DeskRewind
{
    internal sealed class WorkspaceForm : DesktopChrome
    {
        private readonly ListBox layouts;
        private readonly WorkspacePreview preview;
        private const string LiveDesktop = "Live desktop";
        private DesktopLayout currentDesktop;
        private Workspace liveLayout;
        private WorkspaceApps.OpenWindow[] openWindows;
        private readonly GlassButton capture, restore, history, actions, minimize, maximize, close;
        private readonly GlassToggle launch;
        private readonly Label feedback;
        private readonly GlassMenu menu = new GlassMenu();
        private readonly ToolTip feedbackTip = new ToolTip();
        private readonly List<Font> fonts = new List<Font>();
        private readonly Font captionFont, headingFont, rowFont, smallFont;
        private readonly string captureShortcut;
        private Rectangle libraryBounds;
        private bool busy, updatingLaunchPreference;

        internal WorkspaceForm(DesktopLayout desktop, string shortcut, Action captureLayout, Action<Workspace, bool> restoreLayout,
            Func<Workspace, string, bool> renameLayout, Action<Workspace> deleteLayout, Action showHistory,
            Func<Workspace, bool, bool> saveLaunchPreference)
            : this(desktop, Display.Nearest(desktop.Displays, new Rectangle(Cursor.Position, Size.Empty)), shortcut,
                captureLayout, restoreLayout, renameLayout, deleteLayout, showHistory, saveLaunchPreference) { }

        private WorkspaceForm(DesktopLayout desktop, Display display, string shortcut, Action captureLayout,
            Action<Workspace, bool> restoreLayout, Func<Workspace, string, bool> renameLayout, Action<Workspace> deleteLayout,
            Action showHistory, Func<Workspace, bool, bool> saveLaunchPreference) : base(display.Scale, true)
        {
            Text = "DeskRewind Workspaces";
            StartPosition = FormStartPosition.Manual;
            captureShortcut = shortcut;
            captionFont = UseFont(14, true);
            headingFont = UseFont(32, true);
            rowFont = UseFont(14, true);
            smallFont = UseFont(12, false);

            capture = Button("Capture layout", "capture", captureLayout);
            capture.Primary = true;
            restore = Button("Restore", "restore", () => { if (Selected != null) restoreLayout(Selected, launch.Checked); });
            restore.Primary = true;
            AcceptButton = restore;
            history = Button("Window history", "history", showHistory);
            history.Quiet = true;
            history.BackColor = Glass.Film;
            actions = Button("Layout options", "more", () => menu.Show(actions, new Point(actions.Width - menu.Width, actions.Height)));
            actions.IconOnly = actions.Quiet = true;
            actions.BackColor = Glass.Film;
            menu.Items.Add("Rename layout…", null, (sender, args) => Rename(renameLayout));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Delete layout…", null, (sender, args) => Delete(deleteLayout));
            minimize = CaptionButton("Minimize", "minimize", () => WindowState = FormWindowState.Minimized);
            maximize = CaptionButton("Maximize", "maximize", () => WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized);
            close = CaptionButton("Close", "close", Close);
            close.Destructive = true;

            layouts = new ListBox { AccessibleName = "Live desktop and saved layouts", BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Px(59), IntegralHeight = false, BackColor = Glass.Film, ForeColor = Glass.Text, Font = rowFont };
            layouts.DrawItem += DrawLayout;
            // The native scrollbar is light by default; give it the dark Windows style.
            layouts.HandleCreated += (sender, args) =>
            {
                if (!SystemInformation.HighContrast) Native.SetWindowTheme(layouts.Handle, "DarkMode_Explorer", null);
            };
            layouts.SelectedIndexChanged += (sender, args) => ShowSelection();
            layouts.DoubleClick += (sender, args) => { if (!busy && Selected != null) restoreLayout(Selected, launch.Checked); };
            layouts.KeyDown += (sender, args) =>
            {
                if (busy || Selected == null) return;
                switch (args.KeyCode)
                {
                    case Keys.F2: Rename(renameLayout); break;
                    case Keys.Delete: Delete(deleteLayout); break;
                    default: return;
                }
                args.SuppressKeyPress = true;
            };
            Controls.Add(layouts);

            preview = new WorkspacePreview(UiScale, UseFont(21, true), UseFont(13, false), UseFont(12, false), rowFont);
            Controls.Add(preview);
            actions.BringToFront();

            launch = new GlassToggle(UiScale) { Text = "Open closed apps", Font = UseFont(14, false) };
            launch.AccessibleDescription = "Save whether this layout starts its closed applications during restore.";
            launch.CheckedChanged += (sender, args) =>
            {
                Workspace selected = Selected;
                if (updatingLaunchPreference || selected == null || busy) return;
                if (!saveLaunchPreference(selected, launch.Checked)) ShowLaunchPreference(selected.LaunchMissing);
            };
            Controls.Add(launch);
            feedback = new Label { AccessibleName = "Layout status", Font = UseFont(13, false), ForeColor = Glass.Muted,
                BackColor = Glass.Surface, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            feedback.TextChanged += (sender, args) =>
            {
                feedback.AccessibleDescription = feedback.Text;
                feedbackTip.SetToolTip(feedback, feedback.Text);
            };
            Controls.Add(feedback);

            var work = display.Work;
            int width = Math.Min(Px(1040), work.Width - Px(24)), height = Math.Min(Px(628), work.Height - Px(24));
            MinimumSize = new Size(Math.Min(Px(750), width), Math.Min(Px(440), height));
            Bounds = new Rectangle(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
            Arrange();
            UpdateDesktop(desktop);
        }

        private Font UseFont(int size, bool strong)
        {
            var font = Glass.Font(size * UiScale, strong);
            fonts.Add(font);
            return font;
        }

        private GlassButton Button(string name, string glyph, Action clicked)
        {
            var button = new GlassButton { Text = name, AccessibleName = name, Glyph = glyph, Font = rowFont, DisplayScale = UiScale };
            button.Click += (sender, args) => clicked();
            Controls.Add(button);
            return button;
        }

        private GlassButton CaptionButton(string name, string glyph, Action clicked)
        {
            var button = Button(name, glyph, clicked);
            button.IconOnly = button.Quiet = true;
            button.TabStop = false;
            return button;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (feedback != null) Arrange();
        }

        private void Arrange()
        {
            int width = ClientSize.Width, height = ClientSize.Height;
            if (width < Px(100) || height < Px(100)) return;
            minimize.SetBounds(width - Px(119), Px(7), Px(33), Px(34));
            maximize.SetBounds(width - Px(82), Px(7), Px(33), Px(34));
            close.SetBounds(width - Px(45), Px(7), Px(33), Px(34));
            bool maximized = WindowState == FormWindowState.Maximized;
            maximize.Glyph = maximized ? "unmaximize" : "maximize";
            maximize.AccessibleName = maximized ? "Restore window size" : "Maximize";
            capture.SetBounds(width - Px(180), Px(78), Px(154), Px(42));

            int sidebarWidth = Math.Min(Px(258), width / 3), panelTop = Px(147), panelBottom = height - Px(78);
            libraryBounds = new Rectangle(Px(26), panelTop, sidebarWidth, Math.Max(Px(110), panelBottom - panelTop));
            layouts.SetBounds(libraryBounds.X + Px(9), panelTop + Px(41), libraryBounds.Width - Px(18), libraryBounds.Height - Px(97));
            history.SetBounds(libraryBounds.X + Px(11), libraryBounds.Bottom - Px(45), libraryBounds.Width - Px(22), Px(35));
            int previewX = libraryBounds.Right + Px(14);
            preview.SetBounds(previewX, panelTop, width - previewX - Px(26), libraryBounds.Height);
            actions.SetBounds(preview.Right - Px(48), preview.Top + Px(12), Px(33), Px(33));

            restore.SetBounds(width - Px(158), height - Px(61), Px(132), Px(40));
            launch.SetBounds(restore.Left - Px(180), height - Px(61), Px(166), Px(40));
            int statusRight = Selected == null ? width - Px(26) : launch.Left - Px(18);
            feedback.SetBounds(Px(27), height - Px(61), Math.Max(0, statusRight - Px(27)), Px(40));
            Invalidate();
        }

        internal void RefreshLayouts(Workspace[] saved, Guid? selection, string message)
        {
            Guid? selectedId = selection ?? (Selected == null ? (Guid?)null : Selected.Id);
            layouts.BeginUpdate();
            try
            {
                layouts.Items.Clear();
                layouts.Items.Add(LiveDesktop);
                layouts.Items.AddRange(saved.Cast<object>().ToArray());
                var selected = saved.FirstOrDefault(item => item.Id == selectedId);
                if (selected == null) layouts.SelectedIndex = 0;
                else layouts.SelectedItem = selected;
            }
            finally { layouts.EndUpdate(); }
            ShowSelection();
            if (message != null) feedback.Text = message;
            Invalidate(libraryBounds);
        }

        internal void SetBusy(bool value, string message)
        {
            busy = value;
            UpdateActions();
            if (message != null) feedback.Text = message;
        }

        private Workspace Selected { get { return layouts.SelectedItem as Workspace; } }

        internal void UpdateDesktop(DesktopLayout desktop)
        {
            if (currentDesktop != null && desktop.SameWindows(currentDesktop)) return;
            currentDesktop = desktop;
            openWindows = WorkspaceApps.ReadWindows(desktop);
            liveLayout = WorkspaceApps.Snapshot(desktop, openWindows, false);
            liveLayout.Name = LiveDesktop;
            UpdatePreview();
            layouts.Invalidate();
        }

        private void ShowSelection()
        {
            var selected = Selected;
            UpdatePreview();
            ShowLaunchPreference(selected != null && selected.LaunchMissing);
            UpdateActions();
        }

        private void UpdatePreview()
        {
            if (liveLayout == null) return;
            var selected = Selected;
            if (selected == null) { preview.ShowLayout(liveLayout, null, null, null); return; }
            var matches = WorkspaceRestore.Inspect(selected, currentDesktop, openWindows);
            preview.ShowLayout(selected, matches, new HashSet<string>(openWindows.Select(window => window.AppKey), StringComparer.OrdinalIgnoreCase),
                (saved, live) => WorkspaceRestore.IsAtSavedPosition(selected, saved, live, currentDesktop.Displays));
        }

        private void ShowLaunchPreference(bool launchMissing)
        {
            updatingLaunchPreference = true;
            try { launch.Checked = launchMissing; }
            finally { updatingLaunchPreference = false; }
        }

        private void UpdateActions()
        {
            layouts.Enabled = capture.Enabled = history.Enabled = !busy;
            bool saved = Selected != null;
            actions.Visible = launch.Visible = restore.Visible = saved;
            actions.Enabled = launch.Enabled = !busy && saved;
            restore.Enabled = !busy && saved && Selected.Windows.Count > 0;
            feedback.Width = Math.Max(0, (saved ? launch.Left - Px(18) : ClientSize.Width - Px(26)) - feedback.Left);
        }

        private void DrawLayout(object sender, DrawItemEventArgs e)
        {
            var graphics = e.Graphics;
            using (var background = new SolidBrush(Glass.Film)) graphics.FillRectangle(background, e.Bounds);
            if (e.Index < 0 || e.Index >= layouts.Items.Count) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool focused = (e.State & DrawItemState.Focus) != 0;
            var bounds = Rectangle.Inflate(e.Bounds, -Px(2), -Px(3));
            if (selected)
                Glass.Panel(graphics, bounds, Glass.Tint, focused ? Glass.Highlight : Glass.Rim, Px(10));
            var icon = new Rectangle(bounds.X + Px(9), bounds.Y + Px(11), Px(30), Px(30));
            using (var fill = new SolidBrush(selected ? Color.FromArgb(57, 84, 105) : Glass.Card)) Glass.Fill(graphics, fill, icon, Px(8));
            Glass.Icon(graphics, "desktop", icon.X + icon.Width / 2f, icon.Y + icon.Height / 2f, UiScale,
                selected ? Glass.Accent : Glass.Muted);
            var layout = layouts.Items[e.Index] as Workspace;
            int left = icon.Right + Px(9), width = bounds.Right - left - Px(7);
            string name = layout == null ? LiveDesktop : layout.Name;
            string detail = layout == null ? Count(currentDesktop == null ? 0 : currentDesktop.Windows.Length, "open window") : Count(layout.Windows.Count, "window");
            Glass.TextAt(graphics, name, rowFont, new Rectangle(left, bounds.Y + Px(7), width, Px(22)), Glass.Text);
            Glass.TextAt(graphics, detail, smallFont,
                new Rectangle(left, bounds.Y + Px(29), width, Px(17)), Glass.Muted);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (feedback == null) return;
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var border = new Pen(Glass.Rim))
            {
                graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                graphics.DrawLine(border, 1, Px(49), Width - 2, Px(49));
            }
            Glass.DrawMark(graphics, new Rectangle(Px(20), Px(12), Px(26), Px(26)));
            Glass.TextAt(graphics, DeskRewindApp.DisplayName, captionFont, new Rectangle(Px(55), Px(8), Px(210), Px(34)), Glass.Text);
            Glass.TextAt(graphics, "Window layouts", headingFont,
                new Rectangle(Px(26), Px(67), capture.Left - Px(38), Px(50)), Glass.Text);
            if (captureShortcut != null)
                Glass.TextAt(graphics, captureShortcut.Replace("+", " + "), smallFont,
                    new Rectangle(capture.Left, Px(121), capture.Width, Px(19)), Glass.Muted, TextFormatFlags.HorizontalCenter);

            Glass.Panel(graphics, libraryBounds, Glass.Film, Glass.Rim, Px(14));
            using (var border = new Pen(Glass.Rim))
                graphics.DrawLine(border, libraryBounds.X + Px(11), libraryBounds.Bottom - Px(49), libraryBounds.Right - Px(11), libraryBounds.Bottom - Px(49));
            Glass.TextAt(graphics, "Layouts", captionFont,
                new Rectangle(libraryBounds.X + Px(15), libraryBounds.Y + Px(9), libraryBounds.Width - Px(50), Px(25)), Glass.Text);
            Glass.TextAt(graphics, Math.Max(0, layouts.Items.Count - 1).ToString(), smallFont,
                new Rectangle(libraryBounds.Right - Px(42), libraryBounds.Y + Px(9), Px(27), Px(25)), Glass.Muted, TextFormatFlags.Right);
        }

        private void Rename(Func<Workspace, string, bool> save)
        {
            var selected = Selected;
            if (selected == null) return;
            using (var dialog = new LayoutDialog(UiScale, "Rename layout", selected.Name, "Save name", false, value =>
            {
                if (value.Length == 0) return "Enter a layout name.";
                return save(selected, value) ? null : "That name could not be saved.";
            })) dialog.ShowDialog(this);
        }

        private void Delete(Action<Workspace> remove)
        {
            var selected = Selected;
            if (selected == null) return;
            using (var dialog = new LayoutDialog(UiScale, "Delete layout?", selected.Name, "Delete layout", true,
                value => { remove(selected); return null; })) dialog.ShowDialog(this);
        }

        private static string Count(int value, string noun) { return value + " " + noun + (value == 1 ? "" : "s"); }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            menu.Dispose();
            feedbackTip.Dispose();
            foreach (var font in fonts) font.Dispose();
        }

        private sealed class WorkspacePreview : Control
        {
            private readonly float scale;
            private readonly Font titleFont, detailFont, deviceFont, windowFont;
            private readonly PreviewWindowList windows;
            private readonly ToolTip titles = new ToolTip { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 9000 };
            private Workspace layout;
            private bool saved;
            private int hoveredRow = -1;
            private Rectangle mapBounds, listBounds, scrollTrack;
            private int thumbGrab;
            private bool draggingThumb;

            internal WorkspacePreview(float scale, Font title, Font detail, Font device, Font window)
            {
                this.scale = scale;
                titleFont = title;
                detailFont = detail;
                deviceFont = device;
                windowFont = window;
                DoubleBuffered = ResizeRedraw = true;
                BackColor = Glass.Film;
                windows = new PreviewWindowList { AccessibleName = "Open windows", BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
                    ItemHeight = Px(53), IntegralHeight = false, BackColor = Glass.Surface, ForeColor = Glass.Text, Font = windowFont };
                windows.DrawItem += DrawWindow;
                windows.SelectedIndexChanged += (sender, args) => Invalidate();
                windows.ViewChanged += () => { if (!scrollTrack.IsEmpty) Invalidate(scrollTrack); };
                windows.MouseMove += UpdateTooltip;
                windows.MouseLeave += (sender, args) => ResetTooltip();
                Controls.Add(windows);
            }

            internal void ShowLayout(Workspace item, Dictionary<WorkspaceWindow, WindowLayout> matches, HashSet<string> openApps,
                Func<WorkspaceWindow, WindowLayout, bool> atSavedPosition)
            {
                bool isSaved = matches != null;
                bool sameView = layout != null && saved == isSaved && (!saved || layout.Id == item.Id);
                var previous = sameView ? windows.SelectedItem as WindowRow : null;
                int top = sameView && windows.Items.Count > 0 ? windows.TopIndex : 0;
                layout = item;
                saved = isSaved;
                AccessibleName = item.Name + ", " + Count(item.Screens.Count, "display");
                windows.AccessibleName = saved ? "Saved windows and current status" : "Open windows";
                var screens = item.Screens.OrderBy(screen => screen.Bounds.X).ThenBy(screen => screen.Bounds.Y).ToArray();
                var rows = screens.SelectMany(screen => item.Windows.Select((window, index) => new { Window = window, Index = index })
                    .Where(entry => entry.Window.Device == screen.Device).OrderBy(entry => entry.Window.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Select(entry => new WindowRow(entry.Window, screen.Label, entry.Index, matches, openApps, atSavedPosition))).ToArray();
                windows.BeginUpdate();
                try
                {
                    windows.Items.Clear();
                    windows.Items.AddRange(rows.Cast<object>().ToArray());
                    if (rows.Length > 0)
                    {
                        int selection = previous == null ? -1 : Array.FindIndex(rows, row => ReferenceEquals(row.Window, previous.Window)
                            || (!saved && row.Window.SessionIdentity == previous.Window.SessionIdentity));
                        windows.SelectedIndex = Math.Max(0, selection);
                        if (selection >= top && selection < top + windows.VisibleItems)
                            windows.TopIndex = Math.Min(top, windows.MaximumTopIndex);
                    }
                }
                finally { windows.EndUpdate(); }
                ResetTooltip();
                ArrangePreview();
                Invalidate();
            }

            private int Px(int value) { return (int)Math.Round(value * scale); }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (windows != null) ArrangePreview();
            }

            private void ArrangePreview()
            {
                bool showWindows = windows.Items.Count != 0 && Width >= Px(250) && Height >= Px(144);
                windows.Visible = showWindows;
                bool compact = Height < Px(270);
                int top = Px(compact ? 66 : 76), inset = Px(13);
                int listWidth = showWindows ? Math.Min(Px(246), Width * 39 / 100) : 0;
                int gap = showWindows ? Px(10) : 0;
                mapBounds = new Rectangle(inset, top, Math.Max(0, Width - 2 * inset - gap - listWidth), Math.Max(0, Height - top - inset));
                listBounds = new Rectangle(mapBounds.Right + gap, top, listWidth, mapBounds.Height);
                scrollTrack = Rectangle.Empty;
                if (showWindows)
                {
                    int contentHeight = Math.Max(0, listBounds.Height - Px(39));
                    if (windows.Items.Count > Math.Max(1, contentHeight / windows.ItemHeight))
                        scrollTrack = new Rectangle(listBounds.Right - Px(18), listBounds.Y + Px(34), Px(15), Math.Max(0, contentHeight - Px(3)));
                    int reserve = scrollTrack.IsEmpty ? 0 : Px(14);
                    windows.SetBounds(listBounds.X + Px(5), listBounds.Y + Px(33), Math.Max(0, listBounds.Width - Px(10) - reserve), contentHeight);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var graphics = e.Graphics;
                graphics.Clear(Glass.Surface);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Glass.Panel(graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), Glass.Film, Glass.Rim, Px(14));
                if (layout == null || Width < Px(100) || Height < Px(130)) return;
                Glass.TextAt(graphics, layout.Name, titleFont,
                    new Rectangle(Px(18), Px(12), Width - Px(78), Px(28)), Glass.Text);
                string stats = (saved ? "Saved" : "Live") + "   ·   " + Count(layout.Screens.Count, "display")
                    + "   ·   " + Count(layout.Windows.Count, "window");
                Glass.TextAt(graphics, stats, detailFont, new Rectangle(Px(19), Px(42), Width - Px(70), Px(23)), Glass.Muted);

                if (mapBounds.Width <= 0 || mapBounds.Height <= 0) return;
                using (var backdrop = new LinearGradientBrush(mapBounds, Color.FromArgb(25, 36, 51), Glass.Surface, 90f))
                    Glass.Fill(graphics, backdrop, mapBounds, Px(10));
                using (var rim = new Pen(Glass.Rim)) Glass.Stroke(graphics, rim, mapBounds, Px(10));
                var selected = windows.SelectedItem as WindowRow;
                int footer = selected == null ? Px(10) : Px(35);
                var diagram = new Rectangle(mapBounds.X + Px(15), mapBounds.Y + Px(26), mapBounds.Width - Px(30),
                    mapBounds.Height - Px(26) - footer);
                LayoutDiagram.DrawWorkspace(graphics, diagram, layout, selected == null ? null : selected.Window, scale, deviceFont);
                if (selected != null) DrawSelection(graphics, selected);

                if (listBounds.Width > 0)
                {
                    Glass.Panel(graphics, listBounds, Glass.Surface, Glass.Rim, Px(10));
                    Glass.TextAt(graphics, saved ? "Saved windows" : "Open windows", windowFont, new Rectangle(listBounds.X + Px(10), listBounds.Y + Px(4),
                        listBounds.Width - Px(42), Px(25)), Glass.Text);
                    Glass.TextAt(graphics, windows.Items.Count.ToString(), deviceFont,
                        new Rectangle(listBounds.Right - Px(34), listBounds.Y + Px(4), Px(24), Px(25)), Glass.Muted, TextFormatFlags.Right);
                    DrawScrollbar(graphics);
                }
            }

            private void DrawScrollbar(Graphics graphics)
            {
                if (scrollTrack.Height <= 0 || windows.MaximumTopIndex == 0) return;
                var track = new Rectangle(ScrollRailLeft, scrollTrack.Y, Px(4), scrollTrack.Height);
                using (var rail = new SolidBrush(Glass.Card)) Glass.Fill(graphics, rail, track, Px(2));
                using (var thumb = new SolidBrush(draggingThumb ? Glass.Highlight : Color.FromArgb(89, 106, 129)))
                    Glass.Fill(graphics, thumb, ScrollThumb(), Px(2));
            }

            private int ScrollRailLeft { get { return scrollTrack.X + (scrollTrack.Width - Px(4)) / 2; } }

            private Rectangle ScrollThumb()
            {
                int height = Math.Min(scrollTrack.Height, Math.Max(Px(23), scrollTrack.Height * windows.VisibleItems / windows.Items.Count));
                int topIndex = Math.Min(windows.TopIndex, windows.MaximumTopIndex);
                int y = scrollTrack.Y + (scrollTrack.Height - height) * topIndex / Math.Max(1, windows.MaximumTopIndex);
                return new Rectangle(ScrollRailLeft, y, Px(4), height);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || !scrollTrack.Contains(e.Location) || windows.MaximumTopIndex == 0) return;
                var thumb = ScrollThumb();
                thumbGrab = e.Y >= thumb.Top && e.Y < thumb.Bottom ? e.Y - thumb.Top : thumb.Height / 2;
                Capture = true;
                draggingThumb = Capture;
                ScrollTo(e.Y);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (draggingThumb) ScrollTo(e.Y);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (draggingThumb && e.Button == MouseButtons.Left) Capture = false;
            }

            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                base.OnMouseCaptureChanged(e);
                if (Capture || !draggingThumb) return;
                draggingThumb = false;
                Invalidate(scrollTrack);
            }

            private void ScrollTo(int y)
            {
                int travel = Math.Max(1, scrollTrack.Height - ScrollThumb().Height);
                int target = (int)Math.Round((double)(y - thumbGrab - scrollTrack.Top) * windows.MaximumTopIndex / travel);
                windows.TopIndex = Math.Max(0, Math.Min(target, windows.MaximumTopIndex));
                ResetTooltip();
                Invalidate(scrollTrack);
            }

            private void DrawSelection(Graphics graphics, WindowRow selected)
            {
                int y = mapBounds.Bottom - Px(33);
                using (var divider = new Pen(Glass.Rim)) graphics.DrawLine(divider, mapBounds.X + Px(12), y, mapBounds.Right - Px(12), y);
                using (var dot = new SolidBrush(Glass.InkFor(selected.Index)))
                    graphics.FillEllipse(dot, mapBounds.X + Px(13), y + Px(13), Px(7), Px(7));
                Glass.TextAt(graphics, selected.Display + " · " + selected.Window.Title, detailFont,
                    new Rectangle(mapBounds.X + Px(27), y, mapBounds.Width - Px(39), Px(33)), Glass.Text);
            }

            private void DrawWindow(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= windows.Items.Count) return;
                var graphics = e.Graphics;
                using (var background = new SolidBrush(Glass.Surface)) graphics.FillRectangle(background, e.Bounds);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var row = (WindowRow)windows.Items[e.Index];
                bool selected = (e.State & DrawItemState.Selected) != 0;
                bool focused = (e.State & DrawItemState.Focus) != 0;
                var bounds = Rectangle.Inflate(e.Bounds, -Px(2), -Px(2));
                if (selected) Glass.Panel(graphics, bounds, Glass.Tint, focused ? Glass.Highlight : Glass.Rim, Px(6));
                using (var dot = new SolidBrush(selected ? Glass.InkFor(row.Index) : Glass.Muted))
                    graphics.FillEllipse(dot, bounds.X + Px(8), bounds.Y + Px(9), Px(6), Px(6));
                int x = bounds.X + Px(20), width = bounds.Width - Px(25);
                Glass.TextAt(graphics, row.Window.Title, windowFont, new Rectangle(x, bounds.Y + Px(1), width, Px(20)), Glass.Text);
                Glass.TextAt(graphics, row.Position, deviceFont, new Rectangle(x, bounds.Y + Px(18), width, Px(15)), Glass.Muted);
                Glass.TextAt(graphics, row.Status, deviceFont, new Rectangle(x, bounds.Y + Px(33), width, Px(15)), row.IsOpen ? Glass.Accent : Glass.Muted);
            }

            private void UpdateTooltip(object sender, MouseEventArgs e)
            {
                int index = windows.IndexFromPoint(e.Location);
                if (index == hoveredRow) return;
                hoveredRow = index;
                titles.SetToolTip(windows, index < 0 ? null : windows.Items[index].ToString());
            }

            private void ResetTooltip()
            {
                hoveredRow = -1;
                titles.SetToolTip(windows, null);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) titles.Dispose();
                base.Dispose(disposing);
            }

            private sealed class WindowRow
            {
                internal readonly WorkspaceWindow Window;
                internal readonly string Display, Position, Status;
                internal readonly int Index;
                internal readonly bool IsOpen;

                internal WindowRow(WorkspaceWindow window, string display, int index, Dictionary<WorkspaceWindow, WindowLayout> matches,
                    HashSet<string> openApps, Func<WorkspaceWindow, WindowLayout, bool> atSavedPosition)
                {
                    Window = window;
                    Index = index;
                    Display = display;
                    string state = window.State == DeskRewind.WindowState.Normal ? "" : " · " + window.State;
                    if (matches == null)
                    {
                        Position = display + state;
                        string appName = WorkspaceApps.AppName(window);
                        Status = string.Equals(appName, window.Title, StringComparison.OrdinalIgnoreCase) ? "" : appName;
                        IsOpen = true;
                        return;
                    }
                    Position = "Saved on " + display + state;
                    WindowLayout live;
                    IsOpen = matches.TryGetValue(window, out live);
                    if (!IsOpen) Status = openApps.Contains(window.AppKey) ? "Open app · window unmatched" : "Closed";
                    else if (atSavedPosition(window, live)) Status = "At saved position";
                    else Status = "Open on " + WorkspaceScreen.LabelFor(live.Display.Name);
                }

                public override string ToString() { return Window.Title + " · " + Position + " · " + Status; }
            }

            private sealed class PreviewWindowList : ListBox
            {
                internal event Action ViewChanged;
                internal int VisibleItems { get { return Math.Max(1, ClientSize.Height / ItemHeight); } }
                internal int MaximumTopIndex { get { return Math.Max(0, Items.Count - VisibleItems); } }
                private int wheelRemainder;

                protected override CreateParams CreateParams
                {
                    get
                    {
                        var parameters = base.CreateParams;
                        parameters.Style &= ~0x00200000; // The preview draws its own vertical scrollbar.
                        return parameters;
                    }
                }

                protected override void OnMouseWheel(MouseEventArgs e)
                {
                    var handled = e as HandledMouseEventArgs;
                    if (handled != null) handled.Handled = true;
                    wheelRemainder += e.Delta;
                    int steps = wheelRemainder / SystemInformation.MouseWheelScrollDelta;
                    wheelRemainder %= SystemInformation.MouseWheelScrollDelta;
                    int lines = SystemInformation.MouseWheelScrollLines;
                    TopIndex = Math.Max(0, Math.Min(TopIndex - steps * (lines < 0 ? VisibleItems : lines), MaximumTopIndex));
                }

                protected override void WndProc(ref Message message)
                {
                    base.WndProc(ref message);
                    if ((message.Msg == 0x0100 || message.Msg == 0x0197 || message.Msg == 0x0186) && ViewChanged != null)
                        ViewChanged();
                }
            }
        }
    }
}
