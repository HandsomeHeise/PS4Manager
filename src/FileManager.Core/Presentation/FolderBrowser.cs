using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>Ein gelisteter Ordner, wie die Liste ihn zeigt.</summary>
    public sealed class FolderView
    {
        public FolderView(string path, IReadOnlyList<FtpEntry> entries, Zone zone)
        {
            Path = path;
            Entries = entries;
            Zone = zone;
        }

        public string Path { get; }
        public IReadOnlyList<FtpEntry> Entries { get; }

        /// <summary>Zone fürs Schreiben IN diesen Ordner (Anlegen, Hochladen, Einfügen).</summary>
        public Zone Zone { get; }
    }

    /// <summary>
    /// Wo die Liste gerade steht (Spec „Pfade und Verlauf"): Navigieren über
    /// aufgelöste Pfade, Zurück/Vor/Hoch, Aktualisieren. Der Verlauf ändert sich
    /// erst nach erfolgreichem Listen — eine abgebrochene oder gescheiterte
    /// Navigation lässt den Ort stehen. Aufrufer ist der UI-Thread; bewusst ohne
    /// ConfigureAwait(false), damit der Zustand nur dort wechselt.
    /// </summary>
    public sealed class FolderBrowser
    {
        private const string ContentProbe = "x";

        private readonly DirectoryCache _cache;
        private readonly ZonePolicy _policy;
        private readonly NavigationHistory _history = new NavigationHistory();

        public FolderBrowser(DirectoryCache cache, ZonePolicy policy)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            _cache = cache;
            _policy = policy;
        }

        public string CurrentPath { get { return _history.Current; } }
        public bool CanGoBack { get { return _history.CanGoBack; } }
        public bool CanGoForward { get { return _history.CanGoForward; } }
        public bool CanGoUp { get { return CurrentPath != null && CurrentPath != RemotePath.Root; } }

        /// <summary>Zone fürs Schreiben in einen Ordner: die eines gedachten Kindes (die Wurzeln selbst sind gesperrt, ihr Inhalt nicht).</summary>
        public static Zone ContentsZone(ZonePolicy policy, string folder)
        {
            return policy.Effective(RemotePath.Combine(folder, ContentProbe));
        }

        public async Task<FolderView> NavigateAsync(string path, CancellationToken ct)
        {
            var resolved = await PathResolver.ResolveAsync(_cache, path, ct);
            var entries = await _cache.ListAsync(resolved, ct);
            ct.ThrowIfCancellationRequested();
            _history.Navigate(resolved);
            return View(resolved, entries);
        }

        public async Task<FolderView> BackAsync(CancellationToken ct)
        {
            var target = _history.BackTarget;
            if (target == null) throw new InvalidOperationException("No previous location.");
            var entries = await _cache.ListAsync(target, ct);
            ct.ThrowIfCancellationRequested();
            if (_history.BackTarget == target) _history.GoBack();
            return View(target, entries);
        }

        public async Task<FolderView> ForwardAsync(CancellationToken ct)
        {
            var target = _history.ForwardTarget;
            if (target == null) throw new InvalidOperationException("No next location.");
            var entries = await _cache.ListAsync(target, ct);
            ct.ThrowIfCancellationRequested();
            if (_history.ForwardTarget == target) _history.GoForward();
            return View(target, entries);
        }

        public Task<FolderView> UpAsync(CancellationToken ct)
        {
            if (!CanGoUp) throw new InvalidOperationException("Already at the top level.");
            return NavigateAsync(RemotePath.Parent(CurrentPath), ct);
        }

        public async Task<FolderView> RefreshAsync(CancellationToken ct)
        {
            var path = CurrentPath;
            if (path == null) throw new InvalidOperationException("No folder is open.");
            _cache.Invalidate(path);
            var entries = await _cache.ListAsync(path, ct);
            ct.ThrowIfCancellationRequested();
            return View(path, entries);
        }

        private FolderView View(string path, IReadOnlyList<FtpEntry> entries)
        {
            return new FolderView(path, entries, ContentsZone(_policy, path));
        }
    }
}
