using System;
using System.Drawing;
using System.Windows.Forms;

namespace PS4Manager.Gallery.App
{
    internal static class WindowIcon
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
                // Keep the window usable even if the host executable has no icon.
            }
        }
    }
}
