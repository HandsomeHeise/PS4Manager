using PS4Manager.UI;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileManager.App.Dialogs;

namespace FileManager.App.Notifications
{
    public sealed class NotificationWindow : AppForm
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
        private readonly UiLabel heading = new UiLabel { AutoSize = false };
        private readonly UiLabel title = new UiLabel { AutoSize = false };
        private readonly MessageTextBox message = new MessageTextBox();
        private readonly UiButton close = new UiButton { Text = "×", AccessibleName = "Close notification" };
        private readonly UiButton details = new UiButton { Text = "Details", AccessibleName = "Notification details" };
        public NotificationWindow(DesktopNotification notice)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; ClientSize = new Size(360, 210);
            var panel = new UiPanel { Dock = DockStyle.Fill };
            panel.Controls.AddRange(new Control[] { heading, title, message, close, details }); Controls.Add(panel);
            close.Click += (s,e) => Close();
            details.Click += (s,e) => DetailsRequested?.Invoke(this, EventArgs.Empty);
            FormClosed += (s,e) => Dismissed?.Invoke(this, EventArgs.Empty);
            UpdateNotice(notice);
        }
        public event EventHandler Dismissed;
        public event EventHandler DetailsRequested;
        public bool InteractionActive => ContainsFocus || Bounds.Contains(Cursor.Position);
        internal void UpdateNotice(DesktopNotification notice)
        {
            Text = notice.Title; title.Text = notice.Title; heading.Text = MessageKinds.Caption(notice.Kind);
            message.Text = notice.Message.Length <= 700 ? notice.Message : notice.Message.Substring(0, 700) + "…";
            AccessibleName = notice.Title; AccessibleDescription = notice.Message;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x88; return value; } } // TOOLWINDOW | TOPMOST; retained when WinForms updates styles.
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
            if (!value) return;
            // Form.TopMost reapplies SetWindowPos during CreateHandle without NOACTIVATE.
            // Use the native z-order operation explicitly, preserving normal click/keyboard focus.
            if (!SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013)) // NOSIZE | NOMOVE | NOACTIVATE
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (heading == null) return;
            int margin = Px(12), w = Math.Max(1, ClientSize.Width - margin * 2);
            close.SetBounds(ClientSize.Width - margin - Px(30), margin, Px(30), Px(28));
            heading.SetBounds(margin, margin, Math.Max(1,w - Px(38)), Px(28));
            title.SetBounds(margin, margin + Px(30), w, Px(26));
            int bottom = ClientSize.Height - margin;
            details.SetBounds(margin, bottom - Px(32), Math.Min(w,Px(100)), Px(32));
            message.SetBounds(margin, margin + Px(60), w, Math.Max(1,bottom - Px(40) - margin - Px(60)));
        }
    }
}
