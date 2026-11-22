using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DeskRewind
{
    internal static class Native
    {
        internal const int WmHotkey = 0x0312;
        internal const int WmNcHitTest = 0x0084;
        internal const int HtTransparent = -1;
        internal const int ToolWindow = 0x00000080;
        internal const int AppWindow = 0x00040000;
        internal const int Layered = 0x00080000;
        internal const int NoActivate = 0x08000000;
        internal const int Transparent = 0x00000020;
        internal const int Popup = unchecked((int)0x80000000);
        internal const int SwShowNoActivate = 4;
        internal const int SwShowMinNoActive = 7;
        internal const int SwShowMaximized = 3;
        internal const int SwHide = 0;
        internal const int AsyncWindowPlacement = 0x0004;
        internal const uint SwpNoSize = 0x0001;
        internal const uint SwpNoMove = 0x0002;
        internal const uint SwpNoZOrder = 0x0004;
        internal const uint SwpNoActivate = 0x0010;
        internal const uint SwpNoOwnerZOrder = 0x0200;
        internal const uint SwpAsync = 0x4000;
        internal const uint EventMoveStart = 0x000A;
        internal const uint EventMoveEnd = 0x000B;
        internal const uint EventMinimizeStart = 0x0016;
        internal const uint EventMinimizeEnd = 0x0017;
        internal const uint EventDestroy = 0x8001;
        internal const uint EventShow = 0x8002;
        internal const uint EventHide = 0x8003;
        internal const uint EventLocation = 0x800B;
        internal const uint EventName = 0x800C;
        internal const uint EventCloaked = 0x8017;
        internal const uint EventUncloaked = 0x8018;
        internal const uint SkipOwnProcess = 2;
        internal const int DwmExtendedFrameBounds = 9;
        internal const int DwmCloaked = 14;
        internal static readonly IntPtr Topmost = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X, Y;

            internal Point(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Size
        {
            internal int Width, Height;

            internal Size(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left, Top, Right, Bottom;

            internal Rect(Rectangle rectangle)
            {
                Left = rectangle.Left;
                Top = rectangle.Top;
                Right = rectangle.Right;
                Bottom = rectangle.Bottom;
            }

            internal Rectangle ToRectangle()
            {
                return Rectangle.FromLTRB(Left, Top, Right, Bottom);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Placement
        {
            internal int Length, Flags, ShowCommand;
            internal Point MinPosition, MaxPosition;
            internal Rect NormalPosition;

            internal static Placement Create()
            {
                return new Placement { Length = Marshal.SizeOf(typeof(Placement)) };
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfo
        {
            internal int Size;
            internal Rect Bounds, Work;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string Device;

            internal static MonitorInfo Create()
            {
                return new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayDevice
        {
            internal int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
            internal uint State;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string RegistryKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Blend
        {
            internal byte Operation, Flags, Opacity, AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, SizeImage;
            internal int XPelsPerMeter, YPelsPerMeter;
            internal uint ColorsUsed, ColorsImportant;
        }

        internal delegate bool EnumWindow(IntPtr window, IntPtr data);
        internal delegate bool EnumMonitor(IntPtr monitor, IntPtr dc, ref Rect bounds, IntPtr data);
        internal delegate void WinEvent(IntPtr hook, uint id, IntPtr window, int objectId, int childId, uint thread, uint time);

        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindow callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumMonitor callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromRect(ref Rect bounds, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int maxCount);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect bounds);
        [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr targetDc, ref Point position, ref Size size, IntPtr sourceDc, ref Point source, int colorKey, ref Blend blend, uint flags);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr item);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    }
}
