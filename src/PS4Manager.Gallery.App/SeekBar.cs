using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace PS4Manager.Gallery.App
{
    internal sealed class SeekBar : TrackBarControl
    {
        public SeekBar() { Properties.Minimum = 0; Properties.Maximum = 10000; Properties.ShowLabels = false; Properties.ShowValueToolTip = false; Properties.TickStyle = TickStyle.None; Height = 24; }
        public new double Value { get => base.Value / 10000.0; set => base.Value = (int)Math.Round(Math.Max(0, Math.Min(1, double.IsNaN(value) ? 0 : value)) * 10000); }
        public bool IsDragging { get; private set; }
        public event EventHandler SeekRequested;
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) IsDragging = true; base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { bool seek = IsDragging && e.Button == MouseButtons.Left; base.OnMouseUp(e); IsDragging = false; if (seek) SeekRequested?.Invoke(this, EventArgs.Empty); }
        protected override void OnMouseCaptureChanged(EventArgs e) { IsDragging = false; base.OnMouseCaptureChanged(e); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End) SeekRequested?.Invoke(this, EventArgs.Empty); }
    }
}
