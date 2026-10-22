using System;
using System.Drawing;
using System.Windows.Forms;

namespace FileManager.App
{
    public static class WindowIcon
    {
        public static void Apply(Form form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) form.Icon = icon;
            }
            catch
            {
                // The default Windows Forms icon is better than failing a dialog.
            }
        }
    }
}
