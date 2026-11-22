using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskRewind
{
    internal sealed class Display
    {
        internal readonly IntPtr Handle;
        internal readonly string Name;
        internal readonly Rectangle Bounds, Work;
        private string monitorId;
        private bool readMonitorId;

        private Display(IntPtr handle, Native.MonitorInfo info)
        {
            Handle = handle;
            Name = info.Device;
            Bounds = info.Bounds.ToRectangle();
            Work = info.Work.ToRectangle();
        }

        internal string MonitorId
        {
            get
            {
                if (readMonitorId) return monitorId;
                readMonitorId = true;
                var device = new Native.DisplayDevice { Size = Marshal.SizeOf(typeof(Native.DisplayDevice)) };
                const uint activeMonitor = 1, deviceInterfaceName = 1;
                for (uint index = 0; Native.EnumDisplayDevices(Name, index, ref device, deviceInterfaceName); index++)
                {
                    if ((device.State & activeMonitor) != 0 && !string.IsNullOrEmpty(device.Id))
                    {
                        monitorId = device.Id;
                        break;
                    }
                }
                return monitorId;
            }
        }

        internal float Scale
        {
            get
            {
                uint x, y;
                return Native.GetDpiForMonitor(Handle, 0, out x, out y) == 0 ? x / 96f : 1f;
            }
        }

        internal static Display[] ReadAll()
        {
            var displays = new List<Display>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr monitor, IntPtr dc, ref Native.Rect bounds, IntPtr data)
                {
                    var info = Native.MonitorInfo.Create();
                    if (Native.GetMonitorInfo(monitor, ref info)) displays.Add(new Display(monitor, info));
                    return true;
                }, IntPtr.Zero);
            return displays.ToArray();
        }

        internal static Display Nearest(Display[] displays, Rectangle bounds)
        {
            var center = new Native.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            IntPtr handle = Native.MonitorFromPoint(center, 2);
            return displays.FirstOrDefault(display => display.Handle == handle) ?? displays.First();
        }

        internal Rectangle WorkspaceOffset(Rectangle bounds, int direction)
        {
            bounds.Offset(direction * (Work.Left - Bounds.Left), direction * (Work.Top - Bounds.Top));
            return bounds;
        }
    }

    internal enum WindowState { Normal, Minimized, Maximized }

    internal sealed class WindowLayout
    {
        internal IntPtr Handle;
        internal long Identity;
        internal uint Process, Thread;
        internal string Title;
        internal WindowState State;
        internal Rectangle Frame, Outer, Normal;
        internal Display Display;

        internal bool SamePlacement(WindowLayout other)
        {
            return Identity == other.Identity && State == other.State && Frame == other.Frame
                && Outer == other.Outer && Normal == other.Normal;
        }

        internal Rectangle TargetFrame(Display[] displays)
        {
            if (State == WindowState.Minimized) return Rectangle.Empty;
            var target = TargetDisplay(displays);
            if (SameDisplay(target)) return Frame;
            if (State == WindowState.Maximized) return target.Work;
            return MoveToWorkArea(Frame, target);
        }

        internal Display TargetDisplay(Display[] displays)
        {
            return displays.FirstOrDefault(display => display.Name == Display.Name)
                ?? DeskRewind.Display.Nearest(displays, Frame.IsEmpty ? Normal : Frame);
        }

        internal bool SameDisplay(Display target)
        {
            return target.Bounds == Display.Bounds && target.Work == Display.Work;
        }

        internal Rectangle MoveToWorkArea(Rectangle bounds, Display target)
        {
            if (SameDisplay(target)) return bounds;
            Rectangle work = target.Work;
            bounds.Offset(work.Left - Display.Work.Left, work.Top - Display.Work.Top);
            int width = Math.Min(bounds.Width, work.Width);
            int height = Math.Min(bounds.Height, work.Height);
            return new Rectangle(Math.Max(work.Left, Math.Min(bounds.X, work.Right - width)),
                Math.Max(work.Top, Math.Min(bounds.Y, work.Bottom - height)), width, height);
        }
    }

    internal sealed class DesktopLayout
    {
        internal readonly DateTime Time;
        internal readonly Display[] Displays;
        internal readonly WindowLayout[] Windows;
        internal readonly Dictionary<long, WindowLayout> ByIdentity;

        internal DesktopLayout(Display[] displays, WindowLayout[] windows)
        {
            Time = DateTime.Now;
            Displays = displays;
            Windows = windows;
            ByIdentity = windows.ToDictionary(window => window.Identity);
        }

        internal bool SamePlacement(DesktopLayout other)
        {
            if (Displays.Length != other.Displays.Length || Windows.Length != other.Windows.Length) return false;
            foreach (var display in Displays)
            {
                if (!other.Displays.Any(item => item.Name == display.Name && item.Bounds == display.Bounds && item.Work == display.Work))
                    return false;
            }
            foreach (var window in Windows)
            {
                WindowLayout old;
                if (!other.ByIdentity.TryGetValue(window.Identity, out old) || !window.SamePlacement(old)) return false;
            }
            return true;
        }
    }

    internal sealed class LayoutHistory
    {
        private const int Capacity = 32;
        private readonly List<DesktopLayout> layouts = new List<DesktopLayout>();

        internal void Record(DesktopLayout layout)
        {
            if (layout == null || (layouts.Count > 0 && layouts[0].SamePlacement(layout))) return;
            layouts.Insert(0, layout);
            if (layouts.Count > Capacity) layouts.RemoveAt(Capacity);
        }

        internal DesktopLayout[] Read() { return layouts.ToArray(); }
    }

    internal sealed class DesktopWindows : IDisposable
    {
        private readonly Dictionary<IntPtr, WindowLayout> identities = new Dictionary<IntPtr, WindowLayout>();
        private readonly HashSet<IntPtr> moving = new HashSet<IntPtr>();
        private readonly Native.WinEvent eventHandler;
        private readonly IntPtr moveHook, destroyHook, shell;
        private readonly uint process;
        private long nextIdentity;

        internal DesktopWindows()
        {
            using (var current = System.Diagnostics.Process.GetCurrentProcess()) process = (uint)current.Id;
            shell = Native.GetShellWindow();
            eventHandler = OnWindowEvent;
            moveHook = Native.SetWinEventHook(Native.EventMoveStart, Native.EventMoveEnd, IntPtr.Zero,
                eventHandler, 0, 0, Native.SkipOwnProcess);
            destroyHook = Native.SetWinEventHook(Native.EventDestroy, Native.EventDestroy, IntPtr.Zero,
                eventHandler, 0, 0, Native.SkipOwnProcess);
        }

        internal bool IsMoving
        {
            get
            {
                moving.RemoveWhere(window => !Native.IsWindow(window) || !Native.IsWindowVisible(window));
                return moving.Count != 0;
            }
        }

        private void OnWindowEvent(IntPtr hook, uint id, IntPtr window, int objectId, int childId, uint thread, uint time)
        {
            if (window == IntPtr.Zero || objectId != 0 || childId != 0) return;
            if (id == Native.EventMoveStart) moving.Add(window);
            else moving.Remove(window);
            if (id == Native.EventDestroy) identities.Remove(window);
        }

        internal DesktopLayout Capture(Display[] displays)
        {
            if (displays.Length == 0) return null;
            var windows = new List<WindowLayout>();
            var title = new StringBuilder(512);
            Native.EnumWindows(delegate(IntPtr handle, IntPtr data)
            {
                if (handle == shell || !Native.IsWindowVisible(handle)) return true;
                uint pid;
                uint thread = Native.GetWindowThreadProcessId(handle, out pid);
                if (thread == 0 || pid == process) return true;
                int styles = Native.GetWindowLong(handle, -20);
                if ((styles & Native.AppWindow) == 0 && ((styles & Native.ToolWindow) != 0
                    || Native.GetWindow(handle, 4) != IntPtr.Zero)) return true;
                if (Native.GetWindowText(handle, title, title.Capacity) == 0) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(handle, Native.DwmCloaked, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                var placement = Native.Placement.Create();
                Native.Rect rect;
                if (!Native.GetWindowPlacement(handle, ref placement) || !Native.GetWindowRect(handle, out rect)) return true;
                Rectangle outer = rect.ToRectangle();
                if (outer.Width <= 0 || outer.Height <= 0) return true;
                bool minimized = Native.IsIconic(handle);
                Rectangle frame = Rectangle.Empty;
                if (!minimized)
                {
                    frame = Native.DwmGetWindowAttribute(handle, Native.DwmExtendedFrameBounds, out rect,
                        Marshal.SizeOf(typeof(Native.Rect))) == 0 ? rect.ToRectangle() : outer;
                    if (frame.Width <= 0 || frame.Height <= 0) frame = outer;
                }

                Rectangle rawNormal = placement.NormalPosition.ToRectangle();
                var display = minimized ? DeskRewind.Display.Nearest(displays, rawNormal)
                    : DeskRewind.Display.Nearest(displays, frame);
                WindowLayout known;
                long identity = identities.TryGetValue(handle, out known) && known.Process == pid && known.Thread == thread
                    ? known.Identity : ++nextIdentity;
                var window = new WindowLayout
                {
                    Handle = handle, Process = pid, Thread = thread, Identity = identity, Title = title.ToString(),
                    Display = display, Frame = frame, Outer = minimized ? Rectangle.Empty : outer,
                    State = minimized ? WindowState.Minimized : (Native.IsZoomed(handle) ? WindowState.Maximized : WindowState.Normal),
                    Normal = (styles & Native.ToolWindow) == 0 ? display.WorkspaceOffset(rawNormal, 1) : rawNormal
                };
                identities[handle] = window;
                windows.Add(window);
                return true;
            }, IntPtr.Zero);
            foreach (var handle in identities.Keys.Where(handle => !Native.IsWindow(handle)).ToArray()) identities.Remove(handle);
            return new DesktopLayout(displays, windows.ToArray());
        }

        internal static bool Restore(WindowLayout target, WindowLayout current, Display[] displays)
        {
            uint process;
            if (Native.GetWindowThreadProcessId(current.Handle, out process) != current.Thread || process != current.Process)
                return false;
            var display = target.TargetDisplay(displays);
            if (target.State == WindowState.Normal)
            {
                var outer = target.Outer;
                if (!target.SameDisplay(display))
                {
                    var frame = target.TargetFrame(displays);
                    outer = Rectangle.FromLTRB(frame.Left + outer.Left - target.Frame.Left,
                        frame.Top + outer.Top - target.Frame.Top, frame.Right + outer.Right - target.Frame.Right,
                        frame.Bottom + outer.Bottom - target.Frame.Bottom);
                }
                if (current.State != WindowState.Normal && !SetPlacement(target, current, display, outer)) return false;
                // Restoring a maximized window can reuse its live normal rectangle across monitors.
                return Native.SetWindowPos(current.Handle, IntPtr.Zero, outer.X, outer.Y, outer.Width, outer.Height,
                    Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpNoOwnerZOrder | Native.SwpAsync);
            }

            return SetPlacement(target, current, display, target.MoveToWorkArea(target.Normal, display));
        }

        private static bool SetPlacement(WindowLayout target, WindowLayout current, Display display, Rectangle normal)
        {
            var placement = Native.Placement.Create();
            if (!Native.GetWindowPlacement(current.Handle, ref placement)) return false;
            if ((Native.GetWindowLong(current.Handle, -20) & Native.ToolWindow) == 0)
                normal = display.WorkspaceOffset(normal, -1);
            placement.NormalPosition = new Native.Rect(normal);
            placement.MaxPosition = new Native.Point(-1, -1);
            placement.Flags = Native.AsyncWindowPlacement;
            switch (target.State)
            {
                case WindowState.Minimized: placement.ShowCommand = Native.SwShowMinNoActive; break;
                case WindowState.Maximized: placement.ShowCommand = Native.SwShowMaximized; break;
                default: placement.ShowCommand = Native.SwShowNoActivate; break;
            }
            return Native.SetWindowPlacement(current.Handle, ref placement);
        }

        public void Dispose()
        {
            if (moveHook != IntPtr.Zero) Native.UnhookWinEvent(moveHook);
            if (destroyHook != IntPtr.Zero) Native.UnhookWinEvent(destroyHook);
        }
    }
}
