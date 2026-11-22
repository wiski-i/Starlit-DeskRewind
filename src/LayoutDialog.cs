using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskRewind
{
    internal sealed class LayoutDialog : DesktopChrome
    {
        private readonly string title, value;
        private readonly bool destructive;
        private readonly Font titleFont, bodyFont;
        private readonly TextBox input;
        private readonly Label error;

        internal LayoutDialog(float scale, string title, string value, string action, bool destructive, Func<string, string> submit)
            : base(scale, false)
        {
            this.title = title;
            this.value = value;
            this.destructive = destructive;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(Px(430), Px(180));
            titleFont = Glass.Font(Px(23), true);
            bodyFont = Glass.Font(Px(14), false);

            if (!destructive)
            {
                input = new TextBox { AccessibleName = "Layout name", Text = value, MaxLength = 64, BorderStyle = BorderStyle.None,
                    BackColor = Glass.Film, ForeColor = Glass.Text, Font = bodyFont };
                input.SetBounds(Px(34), Px(81), Width - Px(68), Px(22));
                Controls.Add(input);
                ActiveControl = input;
                input.SelectAll();
            }

            error = new Label { Font = bodyFont, ForeColor = Glass.Danger, BackColor = Glass.Surface, AutoEllipsis = true };
            error.SetBounds(Px(25), Px(111), Width - Px(50), Px(18));
            Controls.Add(error);
            var cancel = Button("Cancel", ClientSize.Width - Px(226), 78);
            cancel.DialogResult = DialogResult.Cancel;
            var confirm = Button(action, ClientSize.Width - Px(138), 112);
            confirm.Primary = !destructive;
            confirm.Destructive = destructive;
            confirm.Click += (sender, args) =>
            {
                string problem = submit(input == null ? value : input.Text.Trim());
                if (problem == null) DialogResult = DialogResult.OK;
                else error.Text = problem;
            };
            AcceptButton = destructive ? cancel : confirm;
            CancelButton = cancel;
            if (destructive) ActiveControl = cancel;
        }

        private GlassButton Button(string caption, int left, int width)
        {
            var button = new GlassButton { Text = caption, AccessibleName = caption, Font = bodyFont, DisplayScale = UiScale };
            button.SetBounds(left, Px(134), Px(width), Px(36));
            Controls.Add(button);
            return button;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (titleFont == null) return;
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var rim = new Pen(Glass.Rim)) graphics.DrawRectangle(rim, 0, 0, Width - 1, Height - 1);
            Glass.TextAt(graphics, title, titleFont, new Rectangle(Px(24), Px(17), Width - Px(48), Px(35)), Glass.Text);
            if (destructive)
            {
                Glass.TextAt(graphics, value, bodyFont, new Rectangle(Px(25), Px(64), Width - Px(50), Px(30)), Glass.Muted);
            }
            else
            {
                Glass.Panel(graphics, new Rectangle(Px(24), Px(68), Width - Px(48), Px(41)), Glass.Film, Glass.Rim, Px(8));
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            titleFont.Dispose();
            bodyFont.Dispose();
        }
    }
}
