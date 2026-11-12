using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using PS4Manager.Core.Games;
using PS4Manager.UI;

namespace PS4Manager.App
{
    internal sealed partial class DashboardForm
    {
        private readonly UiLabel consoleCount = CountLabel("0");
        private readonly UiLabel connectionHint = HintLabel("No console connected");
        private readonly UiLabel savedConsole = new UiLabel { AutoSize = false, Dock = DockStyle.Bottom, Height = 70 };
        private readonly UiLabel dashboardStatus = new UiLabel { AutoSize = false, Dock = DockStyle.Bottom, Height = 28 };

        private static UiLabel CountLabel(string text) => new UiLabel { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 4), Appearance = { Font = new Font("Segoe UI Semibold", 28) } };
        private static UiLabel HintLabel(string text) => new UiLabel { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 8), Appearance = { ForeColor = Color.FromArgb(80, 181, 247) } };
        private static UiLabel Paragraph(string text) => new UiLabel { Text = text, AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(6), Appearance = { TextOptions = { WordWrap = DevExpress.Utils.WordWrap.Wrap, VAlignment = DevExpress.Utils.VertAlignment.Top } } };
        private static GroupControl Section(string title) => new GroupControl { Text = title, Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(6), AllowHtmlText = false, AppearanceCaption = { Font = new Font("Segoe UI Semibold", 10) } };

        private Control BuildDashboard()
        {
            var scroll = new XtraScrollableControl { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 690, ColumnCount = 1, RowCount = 3, Padding = new Padding(10), BackColor = Color.Transparent };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new DashboardBanner { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 6, 8) }, 0, 0);

            var counts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
            for (int i = 0; i < 4; i++) counts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            counts.Controls.Add(Summary("Consoles", "console", consoleCount, connectionHint, "Console details", () => _ip.Focus()), 0, 0);
            counts.Controls.Add(Summary("Supported games", "games", CountLabel(new GameCatalog().Entries.Count.ToString()), HintLabel("Offline trainer modules"), "Browse games", () => OpenTool(GamesKey)), 1, 0);
            counts.Controls.Add(Summary("Mods and plugins", "goldhen", CountLabel("PRX"), HintLabel("GoldHEN & mod library"), "Browse mods", () => OpenTool(ModsKey)), 2, 0);
            counts.Controls.Add(Summary("Tools", "tools", CountLabel(Tools.Length.ToString()), HintLabel("Tools available"), "Open tools", () => _ribbon.SelectedPage = _ribbon.Pages[3]), 3, 0);
            layout.Controls.Add(counts, 0, 1);

            var sections = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
            sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29)); sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            var connection = Section("Your PS4");
            _connectionCard.Dock = DockStyle.Fill; _connectionCard.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            _state.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            _connectionCard.Controls.Add(savedConsole); connection.Controls.Add(_connectionCard); sections.Controls.Add(connection, 0, 0);

            var supported = Section("Supported games");
            var gameBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            gameBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); gameBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); gameBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            gameBody.Controls.Add(new UiLabel { Text = "Call of Duty: Black Ops III", Dock = DockStyle.Fill, AutoSize = false, Appearance = { Font = new Font("Segoe UI Semibold", 12) } }, 0, 0);
            gameBody.Controls.Add(Paragraph("Offline Zombies\r\nCUSA02624 · verified build required\r\n\r\nGodmode, ammunition and weapon actions."), 0, 1);
            var browse = new UiButton { Text = "Open game trainers", Dock = DockStyle.Fill }; browse.Click += (s, e) => OpenTool(GamesKey); gameBody.Controls.Add(browse, 0, 2);
            supported.Controls.Add(gameBody); sections.Controls.Add(supported, 1, 0);

            var library = Section("Mods and console tools");
            var shortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            for (int i = 0; i < 3; i++) shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            shortcuts.Controls.Add(Shortcut("Mod library", "Browse compatible releases and manage installations", ModsKey), 0, 0);
            shortcuts.Controls.Add(Shortcut("GoldHEN plugins", "Manage plugins, configuration and backups", GoldHenKey), 0, 1);
            shortcuts.Controls.Add(Shortcut("Luma Studio", "Write, check and deploy Lua scripts", LumaKey), 0, 2);
            library.Controls.Add(shortcuts); sections.Controls.Add(library, 2, 0);
            layout.Controls.Add(sections, 0, 2);
            scroll.Controls.Add(layout);
            scroll.SizeChanged += (s, e) => { layout.Height = Math.Max(Px(640), scroll.ClientSize.Height); };
            _transferBar.Controls.Add(dashboardStatus); dashboardStatus.Dock = DockStyle.Right; dashboardStatus.Width = 300;
            return scroll;
        }

        private Control Summary(string title, string icon, UiLabel count, UiLabel hint, string action, Action click)
        {
            var group = Section(title);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var picture = new PictureEdit { Image = DashboardArtwork.Icon(icon), Size = new Size(40, 40), Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty, Properties = { SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Squeeze, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder, ShowMenu = false } };
            body.Controls.Add(picture, 0, 0); body.SetRowSpan(picture, 2); body.Controls.Add(count, 1, 0); body.Controls.Add(hint, 1, 1);
            var button = new UiButton { Text = action, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = Padding.Empty }; button.Click += (s, e) => click(); body.Controls.Add(button, 1, 2);
            group.Controls.Add(body); return group;
        }
        private Control Shortcut(string title, string subtitle, string key)
        {
            var button = new UiButton { Text = title + "\r\n" + subtitle, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3), ImageOptions = { Image = DashboardArtwork.Icon(key) }, Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Near } } };
            button.Click += (s, e) => OpenTool(key); return button;
        }
        private void UpdateDashboardState()
        {
            bool connected = _sessions.Current != null;
            consoleCount.Text = connected ? "1" : "0";
            connectionHint.Text = connected ? "1 console connected" : "No console connected";
            savedConsole.Text = string.IsNullOrWhiteSpace(_settings.Ip) ? "Enter your console's FTP address above.\r\nEnable the GoldHEN FTP server on your PS4." : "Recent console\r\n" + _settings.Ip + ":" + _settings.Port + "\r\n" + (connected ? "Connected" : "Ready to connect");
            dashboardStatus.Text = connected ? "PS4 connected  |  " + _sessions.Current.Endpoint.Host : "No console connected";
        }
        private void OnDashboardThemeChanged(object sender, EventArgs e) => ApplyDashboardTheme();
        private void ApplyDashboardTheme() { BackColor = AppTheme.Background; foreach (var paragraph in new[] { savedConsole, dashboardStatus }) paragraph.ForeColor = AppTheme.Foreground; Invalidate(true); }
        private void ShowSettings()
        {
            using (var dialog = new ManagerSettingsDialog(_settings)) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SaveSettings(); UpdateConnectionState();
            }
        }
    }

    internal sealed class DashboardBanner : PanelControl
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(3, 15, 34), Color.FromArgb(0, 53, 105), 0f)) g.FillRectangle(background, ClientRectangle);
            int h = Height; float scale = Math.Min(1.5f, h / 170f);
            using (var title = new Font("Segoe UI Semibold", 28 * scale)) using (var body = new Font("Segoe UI", 12 * scale)) using (var muted = new SolidBrush(Color.FromArgb(170, 201, 232))) {
                g.DrawString("PS4 Modding Tool", title, Brushes.White, 32 * scale, 30 * scale);
                g.DrawString("Your console. Your games. Your workspace.", body, Brushes.White, 34 * scale, 82 * scale);
                g.DrawString("Files, trainers, plugins and media in one place.", body, muted, 34 * scale, 112 * scale);
            }
            if (Width < 900) return;
            g.TranslateTransform(Width - 380 * scale, 18 * scale); g.ScaleTransform(scale, scale);
            using (var blue = new Pen(Color.FromArgb(55, 140, 230), 1.5f)) {
                for (int i = 0; i < 6; i++) g.DrawBezier(blue, 0, 145 - i * 9, 120, 10 + i * 9, 200, 150 - i * 6, 400, 15 + i * 12);
            }
            using (var console = new GraphicsPath()) using (var brush = new LinearGradientBrush(new Rectangle(15, 34, 300, 100), Color.FromArgb(62, 65, 72), Color.FromArgb(10, 12, 17), 90)) {
                console.AddPolygon(new[] { new Point(35, 28), new Point(300, 28), new Point(330, 62), new Point(65, 62) }); g.FillPath(brush, console);
                g.FillPolygon(Brushes.Black, new[] { new Point(65, 62), new Point(330, 62), new Point(317, 122), new Point(52, 122) });
                using (var pen = new Pen(AppTheme.Accent, 3)) g.DrawLine(pen, 60, 85, 325, 85);
                using (var font = new Font("Segoe UI Semibold", 13)) g.DrawString("PS4", font, Brushes.White, 270, 92);
                using (var pen = new Pen(Color.FromArgb(110, 116, 128))) g.DrawLine(pen, 75, 52, 315, 52);
            }
            g.ResetTransform();
        }
    }

    internal static class DashboardArtwork
    {
        private static readonly System.Collections.Generic.Dictionary<string, Bitmap> cache = new System.Collections.Generic.Dictionary<string, Bitmap>();
        internal static Bitmap Icon(string key)
        {
            if (cache.TryGetValue(key, out var image)) return image;
            image = new Bitmap(40, 40);
            using (var g = Graphics.FromImage(image)) using (var pen = new Pen(Color.FromArgb(100, 189, 247), 3)) using (var brush = new SolidBrush(Color.FromArgb(100, 189, 247))) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (key == "files" || key == "backup" || key == "packages") { using (var folder = new SolidBrush(Color.FromArgb(249, 199, 60))) { g.FillRectangle(folder, 3, 12, 34, 23); g.FillRectangle(folder, 3, 7, 15, 9); } }
                else if (key == "games") { g.DrawArc(pen, 4, 12, 31, 22, 180, 180); g.DrawLine(pen, 4, 23, 1, 33); g.DrawLine(pen, 35, 23, 38, 33); g.DrawLine(pen, 1, 33, 12, 25); g.DrawLine(pen, 38, 33, 28, 25); g.DrawLine(pen, 8, 20, 17, 20); g.DrawLine(pen, 12, 16, 12, 25); g.FillEllipse(brush, 27, 16, 4, 4); g.FillEllipse(brush, 31, 21, 4, 4); }
                else if (key == "console" || key == "connect" || key == "health") { g.FillPolygon(brush, new[] { new Point(10, 4), new Point(30, 4), new Point(26, 36), new Point(6, 36) }); g.DrawLine(Pens.Black, 8, 19, 29, 19); g.FillEllipse(Brushes.White, 15, 27, 3, 3); }
                else if (key == "settings" || key == "tools") { for (int i = 0; i < 8; i++) { g.TranslateTransform(20, 20); g.RotateTransform(i * 45); g.FillRectangle(brush, -3, -18, 6, 10); g.ResetTransform(); } g.FillEllipse(brush, 8, 8, 24, 24); g.FillEllipse(Brushes.DimGray, 15, 15, 10, 10); }
                else if (key == "goldhen" || key == "mods" || key == "prx") { g.FillRectangle(brush, 7, 13, 26, 21); g.FillEllipse(brush, 15, 4, 10, 15); g.FillEllipse(Brushes.DimGray, 3, 20, 10, 8); }
                else if (key == "media") { g.DrawRectangle(pen, 4, 6, 31, 28); g.FillEllipse(brush, 25, 10, 5, 5); g.DrawLines(pen, new[] { new Point(7, 30), new Point(16, 17), new Point(23, 26), new Point(29, 21), new Point(34, 30) }); }
                else if (key == "dark" || key == "light") { g.FillEllipse(brush, 6, 6, 28, 28); if (key == "dark") g.FillEllipse(Brushes.DimGray, 15, 2, 25, 25); }
                else { g.DrawRectangle(pen, 6, 6, 28, 28); g.DrawLine(pen, 12, 20, 28, 20); g.DrawLine(pen, 20, 12, 20, 28); }
            }
            cache[key] = image; return image;
        }
    }
}
