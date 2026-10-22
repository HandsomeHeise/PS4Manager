using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PS4Manager.Core.Games;

namespace PS4Manager.App
{
    internal sealed partial class GamesForm
    {
        private readonly UiTextBox search = new UiTextBox { PlaceholderText = "Search games or Title ID", AccessibleName = "Search games", Dock = DockStyle.Top };
        private readonly UiCheckBox onlyInstalled = new UiCheckBox { Text = "Only installed", AutoSize = true, AccessibleName = "Only installed" };
        private readonly UiButton game = new UiButton { Text = "Call of Duty: Black Ops III", AutoSize = true, AccessibleName = "Select Black Ops III" };
        private readonly PictureBox icon = new PictureBox { Size = new Size(112, 112), SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "Game icon" };
        private readonly GamesLabel installation = new GamesLabel();
        private readonly GamesLabel empty = new GamesLabel { Text = "No matching games. Clear the search or Only installed filter." };
        private readonly GamesLabel connection = new GamesLabel();
        private readonly GamesLabel observed = new GamesLabel();
        private readonly GamesLabel reason = new GamesLabel { AccessibleName = "Trainer availability" };
        private readonly GamesLabel footer = new GamesLabel { Text = "No trainer action has been requested.", AccessibleName = "Trainer result" };
        private readonly UiButton refresh = new UiButton { Text = "Refresh", AutoSize = true, AccessibleName = "Refresh games and trainer status" };
        private readonly UiButton enable = ActionButton(TrainerAction.EnableGodmode);
        private readonly UiButton disable = ActionButton(TrainerAction.DisableGodmode);
        private readonly UiButton refill = ActionButton(TrainerAction.RefillAmmo);
        private readonly UiButton give = ActionButton(TrainerAction.GiveRk5);
        private IReadOnlyList<InstalledGame> disabledInstalled = new InstalledGame[0];
        private static UiButton ActionButton(TrainerAction action) => new UiButton {
            Text = GamesController.ActionName(action), AccessibleName = GamesController.ActionName(action), AutoSize = true, Tag = action
        };
        private static FlowLayoutPanel Stack() => new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(12), BackColor = Color.Transparent
        };
        private void BuildUi()
        {
            Text = "Games"; ClientSize = new Size(1060, 700); MinimumSize = new Size(780, 500);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8), BackColor = Color.Transparent };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            var leftCard = new UiPanel { Dock = DockStyle.Fill }; var rightCard = new UiPanel { Dock = DockStyle.Fill };
            var left = Stack(); var right = Stack(); leftCard.Controls.Add(left); rightCard.Controls.Add(right);
            root.Controls.Add(leftCard, 0, 0); root.Controls.Add(rightCard, 1, 0); Controls.Add(root);
            left.Controls.Add(new GamesLabel { Text = "Supported games" }); left.Controls.Add(search); left.Controls.Add(onlyInstalled);
            left.Controls.Add(icon); left.Controls.Add(game); left.Controls.Add(installation); left.Controls.Add(empty);
            right.Controls.Add(new GamesLabel { Text = "Call of Duty: Black Ops III\r\nOffline Zombies" });
            right.Controls.Add(connection); right.Controls.Add(refresh);
            right.Controls.Add(new GamesLabel { Text = "Experimental — CUSA02624 / build md5-bf1ffcd3.\r\nLimited to the tested build and plain Bloodhound/RK5. Local offline play only; no concurrent trainers." });
            right.Controls.Add(observed); right.Controls.Add(reason);
            var godButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent };
            godButtons.Controls.Add(enable); godButtons.Controls.Add(disable); right.Controls.Add(godButtons);
            right.Controls.Add(new GamesLabel { Text = "Godmode is never disabled on close or automatically restored after a new round." });
            right.Controls.Add(refill); right.Controls.Add(new GamesLabel { Text = "Fill the held Bloodhound or RK5 once. This is not an unlimited-ammo loop." });
            right.Controls.Add(give); right.Controls.Add(new GamesLabel { Text = "Add an RK5 with starting ammo. Requires room beside one supported pistol; never replaces another weapon." });
            right.Controls.Add(footer);
            Action<FlowLayoutPanel> fit = panel => {
                int width = Math.Max(120, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
                foreach (Control c in panel.Controls) {
                    if (c is GamesLabel || c == search) c.Width = width;
                    if (c is FlowLayoutPanel) c.MaximumSize = new Size(width, 0);
                }
            };
            left.SizeChanged += (s, e) => fit(left); right.SizeChanged += (s, e) => fit(right);
            search.TextChanged += (s, e) => Render(); onlyInstalled.CheckedChanged += (s, e) => Render();
            game.Click += (s, e) => controller.Select("bo3-zombies");
            refresh.Click += async (s, e) => await RefreshAsync(true);
            foreach (var button in new[] { enable, disable, refill, give }) button.Click += async (s, e) => await ActAsync((TrainerAction)((UiButton)s).Tag);
        }
        private void Render()
        {
            if (closing) return;
            var status = controller.Status; var installed = settings.Frame4.Enabled ? controller.Installed : disabledInstalled;
            bool match = catalog.Filter(search.Text, onlyInstalled.Checked, installed).Count != 0;
            game.Visible = installation.Visible = match; icon.Visible = match && icon.Image != null; empty.Visible = !match;
            var item = installed.FirstOrDefault(g => g.TitleId == "CUSA02624");
            installation.Text = "CUSA02624\r\nInstallation: " + (item?.State.ToString() ?? "Unknown") +
                (status.Identity == null ? "" : "\r\nRunning now") + (string.IsNullOrEmpty(item?.Reason) ? "" : "\r\n" + item.Reason);
            connection.Text = "Console: " + (session?.Endpoint.Host ?? "Not connected") + "\r\nSFO version: " + (item?.Version ?? "Unknown") +
                "\r\nFrame4: " + (!settings.Frame4.Enabled ? "Disabled in settings" : status.Ready ? "Build and player verified" : "Not ready");
            string god = status.Godmode == GodmodeState.Off ? "Off" : status.Godmode == GodmodeState.OnOwned ? "On — enabled here" :
                status.Godmode == GodmodeState.OnExternal ? "On — enabled elsewhere or in a previous window" : "Unknown / may still be enabled";
            observed.Text = "Godmode: " + god + "\r\nPlayer: " + (status.PlayerName ?? "Unknown") + "\r\nHeld weapon: " + (status.WeaponName ?? "Unknown");
            if (status.Godmode == GodmodeState.OnOwned || status.Godmode == GodmodeState.OnExternal) everMayBeEnabled = true;
            if (status.Ready && status.Godmode == GodmodeState.Off && !controller.ActionPending) everMayBeEnabled = false;
            reason.Text = session == null ? "Connect a console to use trainers." : !settings.Frame4.Enabled ? "Enable Frame4 in Settings, then press Refresh. The game catalog remains available." :
                controller.ActionPending ? "Checking or awaiting confirmation. Do not resume until the result appears." : status.Reason;
            bool available = session != null && settings.Frame4.Enabled && status.Ready && !controller.ActionPending && !manualRefreshing;
            enable.Enabled = available && status.Godmode == GodmodeState.Off;
            disable.Enabled = available && (status.Godmode == GodmodeState.OnOwned || status.Godmode == GodmodeState.OnExternal);
            refill.Enabled = give.Enabled = available;
            refresh.Enabled = session != null && !controller.ActionPending && !manualRefreshing;
        }
    }

    // UiLabel normally measures one line. These status paragraphs must wrap instead of clipping.
    internal sealed class GamesLabel : UiLabel
    {
        internal GamesLabel() { AutoSize = false; Width = 300; Margin = new Padding(3, 4, 3, 10); Padding = new Padding(6); Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap; }
        private Font SkinFont => Font;
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
        private void Fit()
        {
            int height = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, SkinFont, new Size(Math.Max(1, Width - 12), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 12;
            if (Height != height) Height = height;
        }
    }
}
