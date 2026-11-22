using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskRewind
{
    internal class DesktopChrome : Form
    {
        private readonly bool resizable;
        protected readonly float UiScale;

        internal DesktopChrome(float scale, bool resizable)
        {
            UiScale = scale;
            this.resizable = resizable;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Glass.Surface;
            ForeColor = Glass.Text;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected int Px(int value) { return (int)Math.Round(value * UiScale); }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.Style |= 0x00080000; // WS_SYSMENU preserves the system menu and Alt+F4.
                if (resizable) parameters.Style |= 0x00070000; // Sizing frame and minimize/maximize boxes.
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1, corners = 2, border = ColorTranslator.ToWin32(Glass.Rim);
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            Native.DwmSetWindowAttribute(Handle, 33, ref corners, sizeof(int));
            Native.DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate | 0x0020);
        }

        protected override void WndProc(ref Message message)
        {
            const int ncCalcSize = 0x0083, getMinMaxInfo = 0x0024;
            if (message.Msg == ncCalcSize && message.WParam != IntPtr.Zero)
            {
                message.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref message);
            if (message.Msg == getMinMaxInfo && resizable && IsHandleCreated)
            {
                // Limit a maximized borderless window to the monitor's work area.
                var screen = Screen.FromHandle(Handle);
                Rectangle work = screen.WorkingArea;
                var info = (MinMaxInfo)Marshal.PtrToStructure(message.LParam, typeof(MinMaxInfo));
                info.MaximumPosition = new Native.Point(work.Left - screen.Bounds.Left, work.Top - screen.Bounds.Top);
                info.MaximumSize = new Native.Point(work.Width, work.Height);
                Marshal.StructureToPtr(info, message.LParam, false);
            }
            else if (message.Msg == Native.WmNcHitTest && message.Result == new IntPtr(1))
            {
                long xy = message.LParam.ToInt64();
                Point point = PointToClient(new Point(unchecked((short)xy), unchecked((short)(xy >> 16))));
                if (resizable && WindowState != FormWindowState.Maximized)
                {
                    int edge = Px(6);
                    bool left = point.X < edge, right = point.X >= Width - edge;
                    bool top = point.Y < edge, bottom = point.Y >= Height - edge;
                    int hit = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15)
                        : left ? 10 : right ? 11 : 0;
                    if (hit != 0) { message.Result = new IntPtr(hit); return; }
                }
                if (point.Y >= 0 && point.Y < Px(49) && GetChildAtPoint(point) == null) message.Result = new IntPtr(2);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            internal Native.Point Reserved, MaximumSize, MaximumPosition, MinimumTrack, MaximumTrack;
        }
    }
}
