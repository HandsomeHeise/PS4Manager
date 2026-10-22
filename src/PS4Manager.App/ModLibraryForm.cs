using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4Manager.Core.Config;
using PS4Manager.Core.Luma;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Session;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using PS4Manager.App.Notifications;

namespace PS4Manager.App
{
    internal sealed class ModLibraryForm : AppWindow
    {
        private readonly ManagerSessionController sessions;
        private readonly ManagerSettings settings;
        private readonly Action saveSettings, openGoldHen, showTransfers;
        private readonly Func<bool> lumaOpen;
        private readonly TransferNotificationCoordinator notifications;
        private readonly ToolUiDispatcher ui;
        private readonly ToolSessionBinding binding;
        private readonly ModReceiptStore store;
        private readonly ModCatalogClient catalog;
        private readonly UiComboBox games = new UiComboBox { Dock = DockStyle.Fill };
        private readonly UiComboBox receipts = new UiComboBox { Dock = DockStyle.Top, Height = 36 };
        private readonly UiTextBox details = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiTextBox receiptDetails = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiTextBox status = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiTextBox profileLabel = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        private readonly UiButton refresh = Button("Refresh games"), profileButton = Button("Console profile"), install = Button("Review install"), cancel = Button("Cancel action"), previous = Button("Previous page"), next = Button("Next page"), resume = Button("Resume"), rollback = Button("Roll back"), uninstall = Button("Uninstall"), reload = Button("Reload scripts");
        private readonly UiTabs tabs = new UiTabs { Dock = DockStyle.Fill };
        private ManagerSession session;
        private ModConsoleProfile profile;
        private ModLibraryController controller;
        private CancellationTokenSource covers;
        private readonly SemaphoreSlim coverSlots = new SemaphoreSlim(3);
        private IReadOnlyList<ModGame> shownGames;
        private IReadOnlyList<ModReceipt> shownReceipts;
        private ModCatalogPage shownCatalog;
        private ModRelease shownRelease;
        private bool rendering, closing;

        public ModLibraryForm(ManagerSessionController sessions, ManagerSettings settings, Action saveSettings, Action openGoldHen, Action showTransfers, Func<bool> lumaOpen, TransferNotificationCoordinator notifications = null)
        {
            this.sessions = sessions; this.settings = settings; this.saveSettings = saveSettings; this.openGoldHen = openGoldHen; this.showTransfers = showTransfers; this.lumaOpen = lumaOpen;
            this.notifications = notifications;
            if (settings.Mods == null) settings.Mods = new ModSettings();
            ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            store = new ModReceiptStore(AppPaths.ModRoot); catalog = new ModCatalogClient(new Uri(settings.Mods.CatalogOrigin), Path.Combine(AppPaths.ModRoot, "catalog"));
            BuildUi(); Render(null);
            binding = new ToolSessionBinding(sessions, s => ui.Dispatch(() => { if (!closing && ReferenceEquals(sessions.Current, s)) Attach(s); }), s => ui.Dispatch(() => { if (ReferenceEquals(session, s)) Detach(); }));
        }
        private static UiButton Button(string text) { return new UiButton { Text = text, Width = 140, Height = 34, Margin = new Padding(4), AutoSize = false }; }
        private void BuildUi()
        {
            Text = "Mod Library"; ClientSize = new Size(1160, 760); MinimumSize = new Size(960, 640); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16), BackColor = Color.Transparent };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            heading.Controls.Add(new UiLabel { Text = "Mods for your console", Dock = DockStyle.Fill, AutoSize = false }, 0, 0); heading.Controls.Add(profileLabel, 1, 0); root.Controls.Add(heading, 0, 0);
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 }; toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); for (int i = 0; i < 3; i++) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            toolbar.Controls.Add(games, 0, 0); toolbar.Controls.Add(refresh, 1, 0); toolbar.Controls.Add(profileButton, 2, 0); var transfers = Button("Transfers"); transfers.Click += (s, e) => showTransfers(); toolbar.Controls.Add(transfers, 3, 0); root.Controls.Add(toolbar, 0, 1);
            var browse = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; browse.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49)); browse.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); left.Controls.Add(cards, 0, 0); var paging = new FlowLayoutPanel { Dock = DockStyle.Fill }; paging.Controls.Add(previous); paging.Controls.Add(next); left.Controls.Add(paging, 0, 1); browse.Controls.Add(left, 0, 0);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 0, 0, 0) }; right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); right.Controls.Add(details, 0, 0);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill }; commands.Controls.Add(install); var activate = Button("GoldHEN Manager"); activate.Click += (s, e) => openGoldHen(); commands.Controls.Add(activate); right.Controls.Add(commands, 0, 1); right.Controls.Add(reload, 0, 2); browse.Controls.Add(right, 1, 0);
            var local = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 }; local.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); local.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); local.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); local.Controls.Add(receipts, 0, 0); local.Controls.Add(receiptDetails, 0, 1);
            var recovery = new FlowLayoutPanel { Dock = DockStyle.Fill }; recovery.Controls.Add(resume); recovery.Controls.Add(rollback); recovery.Controls.Add(uninstall); var localRefresh = Button("Refresh receipts"); localRefresh.Click += (s, e) => RunSync(() => controller?.RefreshReceipts()); recovery.Controls.Add(localRefresh); local.Controls.Add(recovery, 0, 2);
            tabs.AddTab("Compatible mods", browse); tabs.AddTab("Installed & recovery", local); root.Controls.Add(tabs, 0, 2);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); footer.Controls.Add(status, 0, 0); footer.Controls.Add(cancel, 1, 0); root.Controls.Add(footer, 0, 3); Controls.Add(root);
            refresh.Click += async (s, e) => await Run(() => controller.RefreshGamesAsync()); profileButton.Click += (s, e) => ChooseProfile();
            games.SelectedIndexChanged += async (s, e) => { if (!rendering && games.SelectedItem is ModGame game) await Run(() => controller.SelectGameAsync(game)); };
            previous.Click += async (s, e) => await Page(-1); next.Click += async (s, e) => await Page(1); install.Click += async (s, e) => await ReviewInstall();
            cancel.Click += async (s, e) => { var c = controller; if (c == null) return; c.CancelRequest(); await c.CancelJobAsync(); };
            receipts.SelectedIndexChanged += (s, e) => RenderReceipt(); resume.Click += async (s, e) => await Recover("resume"); rollback.Click += async (s, e) => await Recover("rollback"); uninstall.Click += async (s, e) => await Recover("uninstall"); reload.Click += async (s, e) => await Reload();
            cards.SizeChanged += (s, e) => ResizeCards();
        }
        private void Attach(ManagerSession connected)
        {
            Detach(); session = connected; var saved = settings.Mods.Profiles.FirstOrDefault(p => p.Id == settings.Mods.SelectedProfileId);
            profile = new ModConsoleProfile(saved?.Id ?? Guid.NewGuid(), saved?.Name ?? "Unassigned console"); InitializeController();
        }
        private void InitializeController()
        {
            if (controller != null) { controller.Changed -= Changed; controller.Dispose(); }
            var discovery = new ModGameDiscovery(session.Explorer.Ftp, session.Gates);
            var planner = new ModInstallPlanner(store, new ModSignatureVerifier(ModTrustKeys.Production), discovery, session.Explorer.Ftp, session.Gates, profile, session, catalog);
            controller = new ModLibraryController(catalog, discovery, planner, store, profile, session.Queue, session.CancellationToken); controller.Changed += Changed;
            shownGames = null; shownCatalog = null; shownRelease = null; shownReceipts = null; RunSync(() => controller.RefreshReceipts()); Render(controller.State);
        }
        private void Detach()
        {
            covers?.Cancel(); if (controller != null) { controller.Changed -= Changed; controller.Dispose(); controller = null; }
            profile?.ForgetSession(); session = null; Render(null);
        }
        private void Changed(object sender, EventArgs e) { ui.Dispatch(() => { if (!closing && ReferenceEquals(sender, controller)) Render(controller.State); }); }
        private void ChooseProfile()
        {
            if (session == null || controller.State.Busy || controller.State.JobActive || controller.State.Plan != null) return;
            using (var dialog = new ModConsoleProfileDialog(settings.Mods, session.Endpoint.Host))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var selected = dialog.Selected; if (!settings.Mods.Profiles.Any(p => p.Id == selected.Id)) settings.Mods.Profiles.Add(selected); settings.Mods.SelectedProfileId = selected.Id; saveSettings();
                profile = new ModConsoleProfile(selected.Id, selected.Name); profile.ConfirmForSession(session); InitializeController();
            }
        }
        private void Render(ModLibraryState state)
        {
            if (closing || IsDisposed) return; rendering = true;
            try
            {
                bool connected = state != null && state.Connected; bool busy = state != null && (state.Busy || state.JobActive || state.Plan != null);
                profileLabel.Text = connected ? profile.Name + (profile.IsConfirmedForSession(session) ? " — confirmed for this connection" : " — confirmation required before writes") : "No console connected";
                refresh.Enabled = profileButton.Enabled = games.Enabled = connected && !busy; install.Enabled = connected && state.CanPrepare && profile.IsConfirmedForSession(session); cancel.Enabled = connected && (state.Busy || state.JobActive);
                status.Text = state?.Status ?? "Connect a console from the dashboard, then refresh its installed games. Existing recovery data stays on this PC.";
                previous.Enabled = connected && !busy && state.Catalog != null && state.Catalog.Page > 1; next.Enabled = connected && !busy && state.Catalog != null && state.Catalog.Items.Count == state.Catalog.PageSize;
                if (!ReferenceEquals(shownGames, state?.Games)) { shownGames = state?.Games; games.SelectedIndex = -1; games.Items.Clear(); if (shownGames != null) foreach (var game in shownGames) games.Items.Add(game); if (state?.Game != null) games.SelectedItem = state.Game; }
                if (!ReferenceEquals(shownCatalog, state?.Catalog)) { shownCatalog = state?.Catalog; RenderCards(shownCatalog); }
                if (!ReferenceEquals(shownRelease, state?.Release)) { shownRelease = state?.Release; RenderDetail(state); }
                if (!ReferenceEquals(shownReceipts, state?.Receipts)) { var selected = (receipts.SelectedItem as ReceiptRow)?.Receipt.TransactionId; shownReceipts = state?.Receipts; receipts.SelectedIndex = -1; receipts.Items.Clear(); if (shownReceipts != null) foreach (var r in shownReceipts.OrderByDescending(r => r.UpdatedUtc)) { var row = new ReceiptRow(r); receipts.Items.Add(row); if (r.TransactionId == selected) receipts.SelectedItem = row; } if (receipts.SelectedIndex < 0 && receipts.Items.Count > 0) receipts.SelectedIndex = 0; }
                reload.Enabled = connected && !busy && shownRelease?.PackageType == "luma-script" && state.Receipts.Any(r => r.State == ModInstallState.Installed && r.ReleaseId == shownRelease.ReleaseId && r.TitleId == state.Game?.TitleId);
                RenderReceipt(); if (state == null) { details.Text = "Choose an installed game to see releases for its exact title ID and version."; ClearCards(); }
            }
            finally { rendering = false; }
        }
        private void RenderDetail(ModLibraryState state)
        {
            if (state?.Release == null) { details.Text = "Select a mod. Packages are signature-checked and backed up before any console write."; return; }
            var r = state.Release;
            details.Text = r.Title + "\r\n" + r.ReleaseVersion + " by " + r.Author + "\r\n\r\n" + state.Detail?.Description + "\r\n\r\nCompatibility\r\n" + string.Join("\r\n", r.Compatibility.Select(c => c.Key)) + "\r\n\r\nPackage: " + r.PackageType + " — " + r.PackageSize.ToString("N0") + " bytes\r\n\r\nChanges\r\n" + r.Changelog + "\r\n\r\nTarget paths are verified against the signed archive during Review install.\r\n\r\n" + (r.PackageType == "files" ? "After successful PRX installation, choose Activate now or enable it later in GoldHEN Manager. Start (or restart) the game afterwards." : "Optional script reload becomes available after successful installation.");
        }
        private void ClearCards() { foreach (Control c in cards.Controls.Cast<Control>().ToArray()) { cards.Controls.Remove(c); c.Dispose(); } }
        private void RenderCards(ModCatalogPage page)
        {
            covers?.Cancel(); covers = new CancellationTokenSource(); var ct = covers.Token; ClearCards();
            if (page == null || page.Items.Count == 0) { cards.Controls.Add(new UiLabel { Text = page == null ? "Choose a game above." : "No releases match this exact game version.", AutoSize = false, Width = 420, Height = 90 }); return; }
            foreach (var item in page.Items)
            {
                var card = new UiPanel { Width = 450, Height = 166, Margin = new Padding(0, 0, 8, 12), Padding = new Padding(12) };
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 }; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = item.Title + " cover", TabStop = false }; picture.Disposed += (s, e) => picture.Image?.Dispose(); layout.Controls.Add(picture, 0, 0); layout.SetRowSpan(picture, 3);
                layout.Controls.Add(new UiLabel { Text = item.Title + "\r\n" + item.ReleaseVersion + " by " + item.Author, Dock = DockStyle.Fill, AutoSize = false }, 1, 0);
                layout.Controls.Add(new UiLabel { Text = item.Summary, Dock = DockStyle.Fill, AutoSize = false }, 1, 1); var view = Button("View details"); view.Click += async (s, e) => await Run(() => controller.SelectModAsync(item)); layout.Controls.Add(view, 1, 2); card.Controls.Add(layout); cards.Controls.Add(card);
                if (item.CoverUrl != null) _ = LoadCover(item.CoverUrl, picture, ct);
            }
            ResizeCards();
        }
        private async Task LoadCover(Uri uri, PictureBox target, CancellationToken ct)
        {
            bool held = false; try { await coverSlots.WaitAsync(ct); held = true; var bytes = await catalog.MediaAsync(uri, ct); var image = DecodeCover(bytes); if (ct.IsCancellationRequested || target.IsDisposed) { image.Dispose(); return; } target.Image = image; } catch (Exception) {/* A missing cover never prevents browsing. */} finally { if (held) coverSlots.Release(); }
        }
        internal static Bitmap DecodeCover(byte[] bytes)
        {
            // Parse dimensions before invoking a native decoder; only PNG/JPEG is accepted.
            int width = 0, height = 0;
            if (bytes.Length >= 24 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71) { width = Big(bytes, 16); height = Big(bytes, 20); }
            else if (bytes.Length > 4 && bytes[0] == 255 && bytes[1] == 216) { int p = 2; while (p + 4 < bytes.Length) { if (bytes[p++] != 255) throw new InvalidDataException(); int marker = bytes[p++]; if (marker == 255) { p--; continue; } if (marker == 217 || marker == 218) break; if (marker == 216 || marker == 1) continue; int len = bytes[p] * 256 + bytes[p + 1]; if (len < 2 || p + len > bytes.Length) throw new InvalidDataException(); if ((marker >= 192 && marker <= 195) || (marker >= 197 && marker <= 199) || (marker >= 201 && marker <= 203) || (marker >= 205 && marker <= 207)) { if (len < 7) throw new InvalidDataException(); height = bytes[p + 3] * 256 + bytes[p + 4]; width = bytes[p + 5] * 256 + bytes[p + 6]; break; } p += len; } }
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 16000000) throw new InvalidDataException("Cover dimensions exceed limits.");
            using (var stream = new MemoryStream(bytes, false)) using (var source = Image.FromStream(stream, true, true))
            {
                if (source.Width != width || source.Height != height) throw new InvalidDataException(); var factor = Math.Min(320.0 / width, 240.0 / height); var result = new Bitmap(Math.Max(1, (int)(width * factor)), Math.Max(1, (int)(height * factor))); using (var g = Graphics.FromImage(result)) g.DrawImage(source, 0, 0, result.Width, result.Height); return result;
            }
        }
        private static int Big(byte[] b, int i) { return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]; }
        private void ResizeCards() { foreach (Control card in cards.Controls) card.Width = Math.Max(200, cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12); }
        private async Task Page(int delta) { var s = controller?.State; if (s?.Catalog != null) await Run(() => controller.SelectGameAsync(s.Game, s.Catalog.Page + delta)); }
        private async Task ReviewInstall()
        {
            var c = controller; if (c == null) return; await Run(async () =>
            {
                var plan = await c.PrepareInstallAsync(); if (!ReferenceEquals(c, controller) || closing) return;
                using (var review = new ModInstallReviewDialog(plan.Title, plan.Targets.ToArray(), plan.Replacements.ToArray(), store.Root, plan.RequiresZoneApproval, c.State.Release.PackageType == "luma-script"))
                {
                    if (review.ShowDialog(this) == DialogResult.OK) await FinishInstallation(c,c.ConfirmAndEnqueue(plan,true,true,job=>ClaimCompletion(job,store.LoadTransaction(profile.Id,plan.TransactionId),"install")),plan.TransactionId,"install"); else c.DeclinePlan();
                }
            });
        }
        private void RenderReceipt()
        {
            var r = (receipts.SelectedItem as ReceiptRow)?.Receipt; var state = controller?.State; bool enabled = r != null && state != null && state.Connected && !state.Busy && !state.JobActive && state.Plan == null && profile.IsConfirmedForSession(session);
            bool ended = r == null || r.State == ModInstallState.Superseded || r.State == ModInstallState.RolledBack || r.State == ModInstallState.Uninstalled;
            resume.Enabled = enabled && !ended && r.State != ModInstallState.Installed && r.RecoveryAction == null; rollback.Enabled = enabled && !ended && (r.RecoveryAction == null || r.RecoveryAction == "rollback"); uninstall.Enabled = enabled && (r.State == ModInstallState.Installed || r.RecoveryAction == "uninstall");
            receiptDetails.Text = r == null ? "Receipts and recovery journals stay on this PC. Confirm the correct console profile before any recovery action." : r.Title + " " + r.ReleaseVersion + "\r\n" + r.State + " — " + r.TitleId + " @ " + r.GameVersion + "\r\n" + r.UpdatedUtc.ToLocalTime().ToString("g") + "\r\n\r\n" + r.LastError + "\r\n\r\n" + string.Join("\r\n\r\n", r.Files.Select(f => f.Target + "\r\n" + (f.Verification ?? "Not installed") + "\r\nOriginal retained: " + f.RemoteOriginalPath)) + "\r\n\r\nBackups: " + store.Root;
        }
        private async Task Recover(string action)
        {
            var r = (receipts.SelectedItem as ReceiptRow)?.Receipt; var c = controller; if (r == null || c == null) return;
            using (var review = new ModInstallReviewDialog(r.Title, r.Files.Select(f => f.Target).ToArray(), r.Files.Where(f => f.PriorExists).Select(f => f.Target).ToArray(), store.Root, r.Files.Any(f => new ZonePolicy().Classify(f.Target) == Zone.Warn), false, action))
            {
                if (review.ShowDialog(this) == DialogResult.OK) await Run(async () => { var job=c.Recover(r.TransactionId,action,true,true,j=>ClaimCompletion(j,r,action));await FinishInstallation(c,job,r.TransactionId,action); });
            }
        }
        private async Task FinishInstallation(ModLibraryController owner,TransferJob job,Guid transaction,string action)
        {
            var connected=session;var selectedProfile=profile;
            Func<bool> current=()=>!closing&&!IsDisposed&&ReferenceEquals(owner,controller)&&ReferenceEquals(connected,session)&&ReferenceEquals(selectedProfile,profile)&&selectedProfile.IsConfirmedForSession(connected);
            var result=await ModActivationPrompt.OfferResultAsync(job,action,current,
                ()=>store.LoadTransaction(selectedProfile.Id,transaction),
                receipt=>{using(var dialog=new ModPluginActivationDialog(receipt,owner.State.Games))return Task.FromResult(dialog.ShowDialog(this)==DialogResult.OK);},
                async id=>{
                    var activation=owner.ActivatePlugins(id,new GoldHenBackupWriter(AppPaths.GoldHenBackupRoot,SystemClock.Instance),j=>notifications?.Claim(j,false));
                    await activation.Completion;
                    if(activation.State!=JobState.Completed)throw new IOException(activation.InterruptReason??"Activation did not finish.");
                });
            if(result!=null&&current()){
                status.Text=result.Message;
                MessageDialog.ShowMessage(this,"Mod Library",result.Message,CompletionKind(result));
            }
        }
        internal static bool HasPluginPayload(ModReceipt receipt)
        { return receipt != null && receipt.Files.Any(f=>f.Source!=null && f.Target.EndsWith(".prx",StringComparison.OrdinalIgnoreCase)); }
        private void ClaimCompletion(TransferJob job,ModReceipt receipt,string action)
        { ClaimCompletion(notifications,job,receipt,action); }
        internal static void ClaimCompletion(TransferNotificationCoordinator notifications,TransferJob job,ModReceipt receipt,string action)
        { if((action=="install"||action=="resume")&&HasPluginPayload(receipt))notifications?.Claim(job,true); }
        internal static MessageKind CompletionKind(ModActivationOutcome result)
        { return result.Kind==ModActivationOutcomeKind.ActivationFailed?MessageKind.Warning:result.Kind==ModActivationOutcomeKind.Enabled?MessageKind.Success:MessageKind.Information; }
        private async Task Reload()
        {
            var c = controller; var selected = c?.State.Release; var game = c?.State.Game; var connected = session; if (selected == null || game == null || connected == null) return;
            var receipt = c.State.Receipts.SingleOrDefault(r => r.ReleaseId == selected.ReleaseId && r.State == ModInstallState.Installed && r.TitleId == game.TitleId); if (receipt == null) return;
            var reloadClient = new ModLumaReload(new LumaEndpoint(connected.Endpoint.Host, game.TitleId == "CUSA03041" ? LumaEndpoint.Rdr2Port : LumaEndpoint.GtaPort), settings.Luma?.Token ?? "", lumaOpen);
            var warning = await reloadClient.TryAsync(receipt, selected, game.TitleId, connected.CancellationToken); if (ReferenceEquals(c, controller)) status.Text = warning ?? "Scripts reloaded. Installed files remain verified.";
        }
        private async Task Run(Func<Task> action)
        {
            var owner=controller;var connected=session;var previousJob=owner?.State.Job;if(owner==null)return;
            try{await action();}catch(OperationCanceledException){}catch(Exception ex){ReportError(ex,owner,connected,previousJob);}
        }
        private void RunSync(Action action)
        {
            var owner=controller;var connected=session;var previousJob=owner?.State.Job;
            try{action();}catch(OperationCanceledException){}catch(Exception ex){ReportError(ex,owner,connected,previousJob);}
        }
        private void ReportError(Exception ex,ModLibraryController owner,ManagerSession connected,TransferJob previousJob)
        {
            if(closing||IsDisposed||!ReferenceEquals(owner,controller)||!ReferenceEquals(connected,session))return;
            status.Text=ex.Message;
            var job=owner?.State.Job;
            if(!ReferenceEquals(job,previousJob)&&job!=null&&(job.State==JobState.Interrupted||job.State==JobState.CompletedWithErrors))return;
            if(connected!=null)notifications?.ShowForQueue(connected.Queue,new DesktopNotification("Mod Library",ex.Message,MessageKind.Error));
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var state = controller?.State;
            if (!closing && state != null && (state.Busy || state.JobActive || state.Plan != null))
                using(var dialog=new MessageDialog("Mod Library","Closing cancels this library action. Any started transaction and its backups remain available under Installed & recovery. Close?",new[]{"Close","Cancel"},1,1,null,MessageKind.Warning,null))
                { dialog.ShowDialog(this); if(dialog.Choice!=0){e.Cancel=true;return;} }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing && !closing) { closing = true; binding?.Dispose(); controller?.Dispose(); covers?.Cancel(); ClearCards(); catalog.Dispose(); } base.Dispose(disposing); }
        private sealed class ReceiptRow { public ReceiptRow(ModReceipt receipt) { Receipt = receipt; } public ModReceipt Receipt { get; } public override string ToString() { return Receipt.Title + " " + Receipt.ReleaseVersion + " — " + Receipt.State + " — " + Receipt.TitleId; } }
    }
}
