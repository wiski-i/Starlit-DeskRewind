using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskRewind
{
    internal static class WorkspaceApps
    {
        internal sealed class OpenWindow
        {
            internal readonly WindowLayout Layout;
            internal readonly string AppKey, Executable, Package;

            internal OpenWindow(WindowLayout layout)
            {
                Layout = layout;
                uint pid = layout.Process;
                ReadProcess(pid, out Executable, out Package);
                if (Package == null && string.Equals(Path.GetFileName(Executable), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string executable = Executable, package = null;
                    Native.EnumChildWindows(layout.Handle, delegate(IntPtr child, IntPtr data)
                    {
                        uint childPid;
                        Native.GetWindowThreadProcessId(child, out childPid);
                        if (childPid == 0 || childPid == pid) return true;
                        ReadProcess(childPid, out executable, out package);
                        return package == null;
                    }, IntPtr.Zero);
                    if (package != null) { Executable = executable; Package = package; }
                }
                string updater = UpdaterFor(Executable);
                AppKey = Package != null ? "package:" + Package : updater != null ?
                    "desktop:" + Path.Combine(Path.GetDirectoryName(updater), Path.GetFileName(Executable)) :
                    Executable != null ? "desktop:" + Executable : "title:" + layout.Title;
            }

            public override string ToString() { return Layout.Title + "  ·  " + new WorkspaceScreen(Layout.Display).Label; }

            internal WorkspaceWindow SaveTo(WorkspaceScreen screen)
            {
                var window = Layout;
                var frame = window.State == WindowState.Minimized ? window.Normal : window.Frame;
                if (frame.Width <= 0 || frame.Height <= 0) frame = new Rectangle(window.Display.Work.Location, new Size(600, 420));
                return new WorkspaceWindow
                {
                    SessionIdentity = window.Identity, Title = window.Title, AppKey = AppKey, Executable = Executable, Package = Package,
                    Device = screen.Device, State = window.State,
                    Frame = new SavedBounds(Workspace.Remap(frame, window.Display.Work, screen.Work.Rectangle)),
                    Normal = new SavedBounds(Workspace.Remap(window.Normal.Width > 0 && window.Normal.Height > 0 ? window.Normal : frame,
                        window.Display.Work, screen.Work.Rectangle)),
                    Insets = window.State == WindowState.Normal ? new SavedBounds(new Rectangle(frame.Left - window.Outer.Left,
                        frame.Top - window.Outer.Top, window.Outer.Right - frame.Right, window.Outer.Bottom - frame.Bottom)) : new SavedBounds()
                };
            }
        }

        internal static OpenWindow[] ReadWindows(DesktopLayout desktop)
        {
            return desktop.Windows.Select(window => new OpenWindow(window)).ToArray();
        }

        internal static Uri WebAddress(string target)
        {
            Uri uri;
            return Uri.TryCreate(target, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && !string.IsNullOrEmpty(uri.Host) ? uri : null;
        }

        private static bool ValidTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || WebAddress(target) != null) return true;
            if (target.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(target)) return false;
            string extension = Path.GetExtension(target);
            return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool LaunchOnce(WorkspaceWindow window)
        {
            // Packaged and updater-based apps usually reuse their existing process.
            return string.IsNullOrWhiteSpace(window.LaunchTarget) && !IsBrowser(window.Executable)
                && (!string.IsNullOrEmpty(window.Package) || UpdaterFor(window.Executable) != null);
        }

        internal static bool TryLaunch(WorkspaceWindow window, out int processId)
        {
            processId = 0;
            try { return Start(window, out processId); }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static bool Start(WorkspaceWindow window, out int processId)
        {
            processId = 0;
            string target = window.LaunchTarget;
            string arguments = "";
            if (!string.IsNullOrWhiteSpace(target))
            {
                if (!ValidTarget(target)) return false;
                var uri = WebAddress(target);
                if (uri != null)
                {
                    target = uri.AbsoluteUri;
                    if (File.Exists(window.Executable) && IsBrowser(window.Executable))
                    {
                        target = window.Executable;
                        arguments = "--new-window \"" + uri.AbsoluteUri.Replace("\"", "%22") + "\"";
                    }
                }
                else if (!File.Exists(target)) return false;
            }
            else if (!string.IsNullOrWhiteSpace(window.Package))
            {
                target = @"shell:AppsFolder\" + window.Package;
            }
            else
            {
                string updater = UpdaterFor(window.Executable);
                target = updater ?? window.Executable;
                if (!File.Exists(target)) return false;
                if (updater != null) arguments = "--processStart \"" + Path.GetFileName(window.Executable) + "\"";
                else if (IsBrowser(target)) arguments = "--new-window";
            }

            bool localFile = File.Exists(target);
            var start = new ProcessStartInfo(target, arguments)
            {
                UseShellExecute = !localFile || !string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase)
            };
            if (localFile) start.WorkingDirectory = Path.GetDirectoryName(target);
            using (var process = Process.Start(start)) if (process != null) processId = process.Id;
            return true;
        }

        private static bool IsBrowser(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            switch (Path.GetFileName(executable).ToLowerInvariant())
            {
                case "chrome.exe": case "msedge.exe": case "firefox.exe": case "brave.exe": case "vivaldi.exe": return true;
                default: return false;
            }
        }

        private static string UpdaterFor(string executable)
        {
            if (string.IsNullOrEmpty(executable) || executable.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            string folder = Path.GetDirectoryName(executable);
            if (string.IsNullOrEmpty(folder) || !Path.GetFileName(folder).StartsWith("app-", StringComparison.OrdinalIgnoreCase)) return null;
            string parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent)) return null;
            string updater = Path.Combine(parent, "Update.exe");
            return File.Exists(updater) ? updater : null;
        }

        private static void ReadProcess(uint pid, out string executable, out string package)
        {
            executable = package = null;
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return;
            try
            {
                var path = new StringBuilder(32768);
                int length = path.Capacity;
                if (QueryFullProcessImageName(process, 0, path, ref length)) executable = path.ToString();
                uint size = 0;
                if (GetApplicationUserModelId(process, ref size, null) == 122 && size > 0 && size <= 1024)
                {
                    var value = new StringBuilder((int)size);
                    if (GetApplicationUserModelId(process, ref size, value) == 0) package = value.ToString();
                }
            }
            finally { CloseHandle(process); }
        }

        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder applicationId);
    }
}
