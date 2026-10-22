using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraTab;

namespace PS4Manager.UI
{
    public static class AppTheme
    {
        public const string Dark = "Office 2019 Black";
        public const string Light = "Office 2019 Colorful";
        public static string Current {
            get => UserLookAndFeel.Default.SkinName;
            set => UserLookAndFeel.Default.SetSkinStyle(value);
        }
        public static bool IsLight => Current == Light;
        public static Color Accent => Color.FromArgb(0, 112, 209);
        public static Color Background => IsLight ? Color.FromArgb(245, 246, 248) : Color.FromArgb(31, 31, 31);
        public static Color Surface => IsLight ? Color.White : Color.FromArgb(42, 42, 42);
        public static Color Foreground => IsLight ? Color.FromArgb(30, 35, 42) : Color.FromArgb(235, 237, 240);
        public static event EventHandler Changed { add => UserLookAndFeel.Default.StyleChanged += value; remove => UserLookAndFeel.Default.StyleChanged -= value; }
    }

    public static class DpiScale
    {
        public static int Scale(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0);
    }

    public class AppWindow : XtraForm
    {
        static AppWindow() { AppearanceObject.DefaultFont = new Font("Segoe UI", 10F); }
        public AppWindow() {
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
        }
    }

    public class UiButton : SimpleButton
    {
        public UiButton() { Size = new Size(140, 34); }
        public override Size GetPreferredSize(Size proposedSize) {
            var size = base.GetPreferredSize(proposedSize);
            return new Size(Math.Max(90, size.Width + 12), Math.Max(32, size.Height));
        }
    }

    public class UiPanel : PanelControl
    {
        public UiPanel() { Padding = new Padding(8); }
    }

    public class UiLabel : LabelControl
    {
        public UiLabel() { AutoSize = true; UseMnemonic = false; }
        public override bool AutoSize { get => AutoSizeMode != LabelAutoSizeMode.None; set => AutoSizeMode = value ? LabelAutoSizeMode.Default : LabelAutoSizeMode.None; }
        public ContentAlignment TextAlignment {
            get => ContentAlignment.MiddleLeft;
            set {
                Appearance.Options.UseTextOptions = true;
                Appearance.TextOptions.HAlignment = value == ContentAlignment.MiddleRight || value == ContentAlignment.TopRight || value == ContentAlignment.BottomRight ? HorzAlignment.Far :
                    value == ContentAlignment.MiddleCenter || value == ContentAlignment.TopCenter || value == ContentAlignment.BottomCenter ? HorzAlignment.Center : HorzAlignment.Near;
                Appearance.TextOptions.VAlignment = VertAlignment.Center;
            }
        }
    }

    // A DevExpress memo editor also serves the fixed-height command/address fields.
    // Keeping one editor type lets shared dialogs inspect their contents consistently.
    public class UiTextBox : MemoEdit
    {
        private bool multiline;
        public UiTextBox() { Height = 32; Properties.ScrollBars = ScrollBars.None; Properties.WordWrap = false; Properties.AcceptsReturn = false; }
        public bool Multiline { get => multiline; set { multiline = value; Properties.AcceptsReturn = value && !ReadOnly; Properties.WordWrap = value; Properties.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None; } }
        public new bool ReadOnly { get => Properties.ReadOnly; set { Properties.ReadOnly = value; Properties.AcceptsReturn = multiline && !value; } }
        public string PlaceholderText { get => Properties.NullValuePrompt; set { Properties.NullValuePrompt = value; Properties.ShowNullValuePromptWhenFocused = true; } }
        public UiTextBox InnerTextBox => this;
        public ScrollBars ScrollBars { get => Properties.ScrollBars; set => Properties.ScrollBars = value; }
        public bool WordWrap { get => Properties.WordWrap; set => Properties.WordWrap = value; }
        public new bool AcceptsTab { get => Properties.AcceptsTab; set => Properties.AcceptsTab = value; }
        public override Size GetPreferredSize(Size proposedSize) => new Size(Math.Max(100, proposedSize.Width), Multiline ? Math.Max(80, Height) : 32);
        protected override void OnKeyPress(KeyPressEventArgs e) { if (!Multiline && (e.KeyChar == '\r' || e.KeyChar == '\n')) e.Handled = true; base.OnKeyPress(e); }
    }

    public class UiComboBox : ComboBoxEdit
    {
        public UiComboBox() { Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor; Height = 32; }
        public DevExpress.XtraEditors.Controls.ComboBoxItemCollection Items => Properties.Items;
    }

    public class UiCheckBox : CheckEdit { }

    public class UiTabs : XtraTabControl
    {
        public void AddTab(string text, Control content) {
            var page = new XtraTabPage { Text = text }; content.Dock = DockStyle.Fill; page.Controls.Add(content); TabPages.Add(page);
        }
        public int SelectedIndex { get => SelectedTabPageIndex; set => SelectedTabPageIndex = value; }
    }

    public class UiSplit : SplitContainerControl
    {
        public int SplitterDistance { get => SplitterPosition; set => SplitterPosition = value; }
        public int Panel1MinSize { get => Panel1.MinSize; set => Panel1.MinSize = value; }
        public int Panel2MinSize { get => Panel2.MinSize; set => Panel2.MinSize = value; }
        public new System.Windows.Forms.FixedPanel FixedPanel {
            get => base.FixedPanel == DevExpress.XtraEditors.SplitFixedPanel.Panel1 ? System.Windows.Forms.FixedPanel.Panel1 : base.FixedPanel == DevExpress.XtraEditors.SplitFixedPanel.Panel2 ? System.Windows.Forms.FixedPanel.Panel2 : System.Windows.Forms.FixedPanel.None;
            set => base.FixedPanel = value == System.Windows.Forms.FixedPanel.Panel1 ? DevExpress.XtraEditors.SplitFixedPanel.Panel1 : value == System.Windows.Forms.FixedPanel.Panel2 ? DevExpress.XtraEditors.SplitFixedPanel.Panel2 : DevExpress.XtraEditors.SplitFixedPanel.None;
        }
        public Orientation Orientation { get => Horizontal ? Orientation.Vertical : Orientation.Horizontal; set => Horizontal = value == Orientation.Vertical; }
    }

    public sealed class AddressBar : BreadCrumbEdit
    {
        private string rootText = "PS4", remotePath = "/";
        private bool updating;
        public AddressBar() {
            Height = 32; Properties.PathSeparator = "/"; Properties.AllowEdit = true;
            Properties.ValidatePath += (s, e) => e.ValidationResult = BreadCrumbValidatePathResult.CreateNodes;
            SelectedNodeChanged += (s, e) => {
                if (!updating && Properties.BreadCrumbMode == BreadCrumbMode.Select && SelectedNode != null)
                    SegmentClicked?.Invoke(this, new PathEventArgs(SelectedNode.Tag as string ?? ToRemote(base.Path)));
            };
            Path = "/";
        }
        public string RootText { get => rootText; set { rootText = value; Path = remotePath; } }
        public new string Path {
            get => remotePath;
            set {
                updating = true;
                try {
                    remotePath = string.IsNullOrWhiteSpace(value) ? "/" : "/" + value.Trim('/');
                    Properties.Nodes.Clear();
                    var root = new BreadCrumbNode(rootText, "PS4") { Tag = "/" }; Properties.Nodes.Add(root);
                    string current = "";
                    foreach (string part in remotePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)) {
                        current += "/" + part; var child = new BreadCrumbNode(part, part) { Tag = current }; root.ChildNodes.Add(child); root = child;
                    }
                    Properties.BreadCrumbMode = BreadCrumbMode.Select;
                    base.Path = "PS4" + (remotePath == "/" ? "" : remotePath);
                } finally { updating = false; }
            }
        }
        public event EventHandler<PathEventArgs> PathSubmitted;
        public event EventHandler<PathEventArgs> SegmentClicked;
        public void NavigateSegment(string path) => SegmentClicked?.Invoke(this, new PathEventArgs(path));
        public void BeginEdit() { Focus(); Properties.BreadCrumbMode = BreadCrumbMode.Edit; Text = remotePath; SelectAll(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (Properties.BreadCrumbMode == BreadCrumbMode.Edit && (keyData == Keys.Enter || keyData == Keys.Escape)) {
                string entered = Text; Path = remotePath;
                if (keyData == Keys.Enter) PathSubmitted?.Invoke(this, new PathEventArgs(ToRemote(entered)));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private static string ToRemote(string path) {
            if (path == "PS4") return "/";
            if (path.StartsWith("PS4/", StringComparison.Ordinal)) path = path.Substring(4);
            return "/" + path.Trim('/');
        }
    }
    public sealed class PathEventArgs : EventArgs { public PathEventArgs(string path) { Path = path; } public string Path { get; } }
}
