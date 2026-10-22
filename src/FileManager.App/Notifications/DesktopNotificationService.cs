using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using Microsoft.Win32;
using PS4Manager.Core.Session;

namespace FileManager.App.Notifications
{
    /// <summary>One presenter per application. All windows and queue mutations live on its UI thread.</summary>
    public sealed class DesktopNotificationService : IDisposable
    {
        private readonly Form dashboard;
        private readonly ToolUiDispatcher ui;
        private readonly NotificationQueue queue;
        private readonly Dictionary<Guid, NotificationWindow> windows = new Dictionary<Guid, NotificationWindow>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private TimeSpan lastTick;
        private string monitor;
        private volatile bool disposed;
        private bool rendering, clearing;

        public DesktopNotificationService(Form dashboard, Action openOverflowDetails)
        {
            this.dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            queue = new NotificationQueue(openOverflowDetails);
            monitor = Screen.FromControl(dashboard).DeviceName;
            dashboard.LocationChanged += Changed; dashboard.SizeChanged += Changed; dashboard.Disposed += HostDisposed;
            AppTheme.Changed += Changed;
            SystemEvents.DisplaySettingsChanged += Changed; SystemEvents.UserPreferenceChanged += PreferencesChanged;
            timer.Tick += Tick; timer.Start();
        }
        public void Show(DesktopNotification notice)
        {
            if (notice == null) throw new ArgumentNullException(nameof(notice));
            Dispatch(() => { Advance(); queue.Add(notice); Render(); });
        }
        public void Clear() { Dispatch(ClearCore); }
        private void ClearCore()
        {
            clearing = true;
            try { queue.Clear(); foreach (var window in windows.Values.ToArray()) window.Dispose(); windows.Clear(); lastTick = clock.Elapsed; }
            finally { clearing = false; }
        }
        private void Dispatch(Action action)
        {
            if (disposed) return;
            try { ui.Dispatch(() => { if (disposed) return; try { action(); } catch (Exception ex) { Trace.TraceError("Notification display failed: {0}", ex.Message); } }); }
            catch (InvalidOperationException) { }
        }
        private void Tick(object sender, EventArgs e) { Dispatch(() => { Advance(); Render(); }); }
        private void Advance()
        {
            var now = clock.Elapsed;
            queue.Advance(now - lastTick, new HashSet<Guid>(windows.Where(w => w.Value.InteractionActive).Select(w => w.Key)));
            lastTick = now;
        }
        private void Changed(object sender, EventArgs e) { Dispatch(Render); }
        private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs e) { Dispatch(Render); }
        private void HostDisposed(object sender, EventArgs e) { Dispose(); }
        private void Render()
        {
            if (rendering || disposed || clearing) return;
            rendering = true;
            try {
                if (dashboard.WindowState != FormWindowState.Minimized) monitor = Screen.FromControl(dashboard).DeviceName;
                var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == monitor) ?? Screen.PrimaryScreen;
                int dpi = dashboard.DeviceDpi;
                // Once a card has moved monitors, its own DPI is more accurate than a minimized host's.
                var existing = windows.Values.FirstOrDefault();
                if (existing != null && Screen.FromControl(existing).DeviceName == screen.DeviceName) dpi = existing.DeviceDpi;
                var size = new Size((int)Math.Round(360 * dpi / 96d), (int)Math.Round(210 * dpi / 96d));
                var positions = NotificationLayout.Arrange(screen.WorkingArea, dpi, new[] { size, size, size });
                queue.SetVisibleCapacity(positions.Count);
                var notices = queue.Visible;
                foreach (var id in windows.Keys.Where(id => !notices.Any(n => n.Id == id)).ToArray()) {
                    var old = windows[id]; windows.Remove(id); old.Dispose();
                }
                for (int i = 0; i < notices.Count; i++) {
                    var notice = notices[i];
                    NotificationWindow window;
                    if (!windows.TryGetValue(notice.Id, out window)) {
                        window = new NotificationWindow(notice); var id = notice.Id;
                        window.Dismissed += (s,e) => { if (!clearing && !disposed) { queue.Dismiss(id); windows.Remove(id); Render(); } };
                        window.DetailsRequested += (s,e) => OpenDetails(id);
                        window.DpiChanged += (s,e) => Dispatch(Render);
                        windows.Add(id, window);
                    }
                    window.UpdateNotice(notice);
                    if (window.Bounds != positions[i]) window.Bounds = positions[i];
                    if (!window.Visible) window.Show();
                }
            } finally { rendering = false; }
        }
        private void OpenDetails(Guid id)
        {
            var notice = queue.Visible.FirstOrDefault(n => n.Id == id);
            if (notice == null || disposed) return;
            try {
                if (notice.OpenDetails != null) notice.OpenDetails();
                else MessageDialog.ShowMessage(dashboard, notice.Title, notice.Message, notice.Kind, notice.Details);
            } catch (Exception ex) {
                Show(new DesktopNotification("Could not open details", ex.Message, MessageKind.Error));
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ui.Dispatch(() => {
                timer.Stop(); timer.Tick -= Tick; timer.Dispose();
                dashboard.LocationChanged -= Changed; dashboard.SizeChanged -= Changed; dashboard.Disposed -= HostDisposed;
                AppTheme.Changed -= Changed; SystemEvents.DisplaySettingsChanged -= Changed; SystemEvents.UserPreferenceChanged -= PreferencesChanged;
                ClearCore(); clock.Stop();
            });
        }
    }
}
