using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskRewind
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\DeskRewind", out firstInstance))
            {
                if (!firstInstance)
                {
                    try
                    {
                        using (var open = EventWaitHandle.OpenExisting(@"Local\DeskRewind.Open")) open.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }
                using (var open = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DeskRewind.Open"))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                        var store = new WorkspaceStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "DeskRewind", "workspaces.json"));
                        using (var app = new DeskRewindApp(open, store)) Application.Run(app);
                }
            }
        }
    }

    internal sealed class DeskRewindApp : ApplicationContext
    {
        private const int HistoryHotkey = 1;
        private const int CaptureHotkey = 2;
        private const uint Modifiers = 0x4003;
        private const uint Shift = 0x0004;
        private readonly DesktopWindows desktop = new DesktopWindows();
        private readonly LayoutHistory history = new LayoutHistory();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer timer;
        private readonly HotkeyWindow hotkey = new HotkeyWindow();
        private readonly EventWaitHandle openSignal;
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly string historyShortcut, captureShortcut;
        private readonly WorkspaceLibrary library;
        private DesktopLayout candidate;
        private int stablePolls;
        private long resumeAt;
        private Timeline timeline;
        private WorkspaceForm workspaces;
        private bool applyingWorkspace, disposed;
        private IntPtr previousForeground;
        private string lastWorkspaceStatus;

        internal DeskRewindApp(EventWaitHandle openSignal, WorkspaceStore store)
        {
            this.openSignal = openSignal;
            library = new WorkspaceLibrary(store);
            hotkey.Pressed += id =>
            {
                if (id == HistoryHotkey) ToggleTimeline();
                if (id == CaptureHotkey) CaptureWorkspace();
            };
            historyShortcut = Register(HistoryHotkey, Keys.Z);
            captureShortcut = Register(CaptureHotkey, Keys.S);

            icon = CreateIcon();
            var menu = new GlassMenu();
            menu.Items.Add("Workspaces", null, (sender, args) => OpenWorkspaces());
            menu.Items.Add(new ToolStripMenuItem("Capture layout", null, (sender, args) => CaptureWorkspace())
                { ShortcutKeyDisplayString = captureShortcut ?? "" });
            var restoreMenu = new ToolStripMenuItem("Restore layout") { DropDown = new GlassMenu() };
            menu.Items.Add(restoreMenu);
            menu.Opening += (sender, args) => FillRestoreMenu(restoreMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Window history", null, (sender, args) => OpenTimeline())
                { ShortcutKeyDisplayString = historyShortcut ?? "" });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit DeskRewind", null, (sender, args) => ExitThread());
            tray = new NotifyIcon { Icon = icon, Text = "DeskRewind", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (sender, args) => { if (args.Button == MouseButtons.Left) OpenWorkspaces(); };
            timer = new System.Windows.Forms.Timer { Interval = 275 };
            timer.Tick += OnTick;
            history.Record(Capture());
            timer.Start();
            OpenWorkspaces();
        }

        private string Register(int id, Keys key)
        {
            if (Native.RegisterHotKey(hotkey.Handle, id, Modifiers, (uint)key)) return "Ctrl+Alt+" + key;
            if (Native.RegisterHotKey(hotkey.Handle, id, Modifiers | Shift, (uint)key)) return "Ctrl+Alt+Shift+" + key;
            return null;
        }

        private void FillRestoreMenu(ToolStripMenuItem menu)
        {
            while (menu.DropDownItems.Count > 0)
            {
                var item = menu.DropDownItems[0];
                menu.DropDownItems.RemoveAt(0);
                item.Dispose();
            }
            var saved = library.Read();
            menu.Enabled = !applyingWorkspace && saved.Length > 0;
            foreach (var layout in saved)
                menu.DropDownItems.Add(new ToolStripMenuItem(layout.Name.Replace("&", "&&"), null,
                    (sender, args) => RestoreWorkspace(layout, layout.LaunchMissing)));
        }

        private DesktopLayout Capture() { return desktop.Capture(Display.ReadAll()); }

        private void OnTick(object sender, EventArgs e)
        {
            if (openSignal.WaitOne(0)) OpenWorkspaces();
            if (applyingWorkspace) return;
            if (desktop.IsMoving || clock.ElapsedMilliseconds < resumeAt)
            {
                candidate = null;
                return;
            }
            var current = Capture();
            if (current == null) return;
            if (candidate == null || !candidate.SamePlacement(current))
            {
                candidate = current;
                stablePolls = 0;
            }
            if (++stablePolls != 2) return;
            if (timeline == null) history.Record(current);
            else timeline.RaiseSelection();
        }

        private void ToggleTimeline()
        {
            if (timeline == null) OpenTimeline();
            else timeline.Close();
        }

        private void OpenTimeline()
        {
            if (applyingWorkspace) return;
            if (timeline != null) { timeline.Activate(); return; }
            var current = Capture();
            if (current == null) return;
            if (!desktop.IsMoving) history.Record(current);
            DesktopLayout[] layouts = history.Read();
            if (layouts.Length == 0) return;
            var cursor = Cursor.Position;
            var display = Display.Nearest(current.Displays, new Rectangle(cursor, Size.Empty));
            previousForeground = Native.GetForegroundWindow();
            var preview = new FramePreview();
            var panel = new Timeline(layouts, display) { Icon = icon };
            timeline = panel;
            panel.SelectionChanged += layout =>
            {
                if (timeline != panel) return;
                if (candidate != null) current = candidate;
                panel.UpdateStatus(current);
                if (layouts.Length > 1) preview.Show(layout, current, panel.Bounds);
            };
            panel.RestoreRequested += layout => Restore(panel, layout);
            panel.WorkspacesRequested += OpenWorkspaces;
            panel.FormClosed += (sender, args) =>
            {
                if (timeline != panel) return;
                timeline = null;
                candidate = null;
                preview.Dispose();
            };
            candidate = current;
            stablePolls = 0;
            panel.Show();
            if (!panel.IsDisposed)
            {
                panel.Activate();
                panel.RaiseSelection();
            }
        }

        private void OpenWorkspaces()
        {
            if (timeline != null) timeline.Close();
            if (workspaces != null)
            {
                if (workspaces.WindowState == FormWindowState.Minimized) workspaces.WindowState = FormWindowState.Normal;
                workspaces.Activate();
                return;
            }
            var current = Capture();
            if (current == null) { Notify("Windows could not read the current displays."); return; }
            var editor = new WorkspaceForm(current, captureShortcut, CaptureWorkspace, RestoreWorkspace, RenameWorkspace, DeleteWorkspace,
                OpenTimeline, SaveLaunchPreference) { Icon = icon };
            editor.FormClosed += (sender, args) => { if (workspaces == editor) workspaces = null; };
            workspaces = editor;
            var saved = library.Read();
            editor.RefreshLayouts(saved, null, library.LoadError ?? lastWorkspaceStatus ?? library.LoadNotice
                ?? (saved.Length == 0 ? "Capture your first layout." : ""));
            editor.SetBusy(applyingWorkspace, applyingWorkspace ? "Restoring layout…" : null);
            editor.Show();
            editor.Activate();
        }

        private bool SaveLaunchPreference(Workspace workspace, bool launch)
        {
            try
            {
                library.SetLaunchPreference(workspace, launch);
                lastWorkspaceStatus = "Restore preference saved.";
                if (workspaces != null) workspaces.SetBusy(false, lastWorkspaceStatus);
                return true;
            }
            catch (Exception exception)
            {
                if (!WorkspaceLibrary.StorageError(exception)) throw;
                Status("Could not save the restore preference. " + exception.Message);
                return false;
            }
        }

        private void CaptureWorkspace()
        {
            if (applyingWorkspace) return;
            var desktopLayout = Capture();
            if (desktopLayout == null || desktopLayout.Windows.Length == 0) { Status("Open a window to capture a layout."); return; }
            try
            {
                var layout = library.Capture(desktopLayout);
                lastWorkspaceStatus = "Layout saved.";
                if (workspaces != null) workspaces.RefreshLayouts(library.Read(), layout.Id, lastWorkspaceStatus);
                if (workspaces == null || Native.GetForegroundWindow() != workspaces.Handle) Notify(lastWorkspaceStatus);
            }
            catch (Exception exception)
            {
                if (!WorkspaceLibrary.StorageError(exception)) throw;
                Status("Could not save the layout. " + exception.Message);
            }
        }

        private bool RenameWorkspace(Workspace workspace, string name)
        {
            try
            {
                if (!library.Rename(workspace, name)) { Status("A layout with that name already exists."); return false; }
                lastWorkspaceStatus = "Layout renamed.";
                if (workspaces != null) workspaces.RefreshLayouts(library.Read(), workspace.Id, lastWorkspaceStatus);
                return true;
            }
            catch (Exception exception)
            {
                if (!WorkspaceLibrary.StorageError(exception)) throw;
                Status("Could not rename the layout. " + exception.Message);
                return false;
            }
        }

        private void DeleteWorkspace(Workspace workspace)
        {
            try
            {
                library.Delete(workspace);
                lastWorkspaceStatus = "Layout deleted.";
                if (workspaces != null) workspaces.RefreshLayouts(library.Read(), null, lastWorkspaceStatus);
            }
            catch (Exception exception)
            {
                if (!WorkspaceLibrary.StorageError(exception)) throw;
                Status("Could not delete the layout. " + exception.Message);
            }
        }

        private async void RestoreWorkspace(Workspace workspace, bool launchMissing)
        {
            if (applyingWorkspace) return;
            applyingWorkspace = true;
            if (timeline != null) timeline.Close();
            if (workspaces != null)
            {
                workspaces.SetBusy(true, "Restoring layout…");
                workspaces.WindowState = FormWindowState.Minimized;
            }
            history.Record(Capture());
            string result = null;
            try
            {
                result = await WorkspaceRestore.Apply(workspace, launchMissing, Capture, message =>
                {
                    if (workspaces != null) workspaces.SetBusy(true, message);
                });
                history.Record(Capture());
            }
            finally
            {
                applyingWorkspace = false;
                candidate = null;
                lastWorkspaceStatus = result;
                if (workspaces != null) workspaces.SetBusy(false, result);
            }
            Notify(result);
        }

        private void Status(string message)
        {
            lastWorkspaceStatus = message;
            if (workspaces != null) workspaces.SetBusy(applyingWorkspace, message);
            Notify(message);
        }

        private void Restore(Timeline panel, DesktopLayout layout)
        {
            if (timeline != panel) return;
            IntPtr previous = previousForeground;
            var current = Capture();
            if (timeline != panel) return;
            bool hadFocus = Native.GetForegroundWindow() == panel.Handle;
            panel.Close();
            if (hadFocus && Native.IsWindow(previous)) Native.SetForegroundWindow(previous);
            if (current == null) return;

            int failed = 0;
            foreach (var target in layout.Windows)
            {
                WindowLayout live;
                if (!current.ByIdentity.TryGetValue(target.Identity, out live) || target.SamePlacement(live)) continue;
                if (!DesktopWindows.Restore(target, live, current.Displays)) failed++;
            }
            resumeAt = clock.ElapsedMilliseconds + 450;
            if (failed > 0) Notify(string.Format("Windows blocked restoration for {0} {1}.",
                failed, failed == 1 ? "window" : "windows"));
        }

        private void Notify(string message)
        {
            if (!disposed) tray.ShowBalloonTip(3000, "DeskRewind", message, ToolTipIcon.None);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                if (timeline != null) timeline.Close();
                var manager = workspaces;
                workspaces = null;
                if (manager != null) manager.Dispose();
                timer.Dispose();
                Native.UnregisterHotKey(hotkey.Handle, HistoryHotkey);
                Native.UnregisterHotKey(hotkey.Handle, CaptureHotkey);
                hotkey.DestroyHandle();
                desktop.Dispose();
                tray.Visible = false;
                tray.ContextMenuStrip.Dispose();
                tray.Dispose();
                icon.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        private static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (var graphics = Graphics.FromImage(bitmap)) Glass.DrawMark(graphics, new Rectangle(0, 0, 32, 32));
                IntPtr handle = bitmap.GetHicon();
                try { using (var temporary = Icon.FromHandle(handle)) return (Icon)temporary.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            internal event Action<int> Pressed;

            internal HotkeyWindow()
            {
                CreateHandle(new CreateParams { Caption = "DeskRewind Hotkey", Parent = new IntPtr(-3) });
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == Native.WmHotkey && Pressed != null) Pressed(message.WParam.ToInt32());
                base.WndProc(ref message);
            }
        }
    }
}
