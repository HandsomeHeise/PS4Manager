using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PS4Manager.UI
{
    // Legacy pixel rows must include editor/button text AND their margins.
    // Only single-row leaf controls participate; grids, spanning descriptions,
    // multiline documents and percentage/AutoSize rows retain their own layout.
    public static class UiLayout
    {
        public static void FitTableRows(Control root)
        {
            foreach (Control child in root.Controls) FitTableRows(child);
            if (!(root is TableLayoutPanel table)) return;
            table.SuspendLayout();
            try {
                foreach (Control control in table.Controls) {
                    int row = table.GetRow(control);
                    if (row < 0 || row >= table.RowStyles.Count || table.GetRowSpan(control) != 1 || table.RowStyles[row].SizeType != SizeType.Absolute) continue;
                    bool editor = control is UiButton || control is UiComboBox || control is UiTextBox field && !field.Multiline;
                    if (!editor && !(control is UiLabel)) continue;
                    int height = control.GetPreferredSize(Size.Empty).Height;
                    if (table.RowStyles[row].Height < height + control.Margin.Vertical) table.RowStyles[row].Height = height + control.Margin.Vertical;
                    if (editor && control.Dock == DockStyle.None && control.Height < height) control.Height = height;
                }
            } finally { table.ResumeLayout(true); }
        }

        // Command panes use content-sized label/action columns and flexible
        // editor columns. Explicit measured widths avoid WinForms retaining
        // pre-skin AutoSize widths after native label fonts have changed.
        public static void SizeCommandColumns(TableLayoutPanel table)
        {
            bool fitting = false;
            table.Layout += (s, e) => {
                if (fitting) return;
                fitting = true;
                try {
                    for (int column = 0; column < table.ColumnStyles.Count; column++) {
                        var style = table.ColumnStyles[column];
                        if (style.SizeType == SizeType.Percent) continue;
                        int width = table.Controls.Cast<Control>().Where(c => table.GetColumn(c) == column && table.GetColumnSpan(c) == 1)
                            .Select(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal).DefaultIfEmpty(0).Max();
                        style.SizeType = SizeType.Absolute;
                        if (style.Width != width) style.Width = width;
                    }
                } finally { fitting = false; }
            };
        }
    }

    // Single-line action/status bars measure the real WXI panel chrome instead
    // of assuming that a 34px panel can contain a 34px button.
    public sealed class UiToolbar : UiPanel
    {
        private bool arranging;
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (!arranging && (Dock == DockStyle.Top || Dock == DockStyle.Bottom)) {
                arranging = true;
                try {
                    int content = Math.Max(DpiScale.Scale(34, DeviceDpi), Controls.Cast<Control>().Where(c => c.Visible).Select(c => c.GetPreferredSize(Size.Empty).Height).DefaultIfEmpty(0).Max());
                    int chrome = Height - DisplayRectangle.Height;
                    if (Height != content + chrome) Height = content + chrome;
                } finally { arranging = false; }
            }
            base.OnLayout(e);
        }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            if (e.Control is UiLabel label && label.Dock == DockStyle.Fill && label.Padding == Padding.Empty)
                label.Padding = new Padding(DpiScale.Scale(8, DeviceDpi), 0, 0, 0);
            base.OnControlAdded(e);
        }
    }
}
