using PS4Manager.UI;
using System.Drawing;
using System.Windows.Forms;

namespace FileManager.App
{
    /// <summary>
    /// DevExpress-Fenster mit gemeinsamem Symbol und DPI-basierten Abständen.
    /// </summary>
    public class AppForm : AppWindow
    {
        public AppForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            WindowIcon.Apply(this);
        }

        /// <summary>Logische 96-dpi-Pixel in physische dieses Fensters. Erst ab OnLoad verlässlich.</summary>
        protected int Px(int logical)
        {
            return DpiScale.Scale(logical, DeviceDpi);
        }
    }
}
