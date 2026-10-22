using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Ps4;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Eine Verbindung zur Konsole, fertig verdrahtet (Plan-1-Übergabe): EIN
    /// Wächter um den FTP-Client für alle Bausteine, gemeinsame Tore, gemeinsamer
    /// Verzeichnis-Cache. Die App baut pro „Verbinden" eine neue Sitzung und
    /// entsorgt die alte.
    /// </summary>
    public sealed class ExplorerSession : IDisposable
    {
        private const string Data = "/data";
        private const string UserData = "/user/data";

        // This constructor is the composition seam for ManagerSession: it creates
        // the only guard, gate set, and queue for a connected console.
        public ExplorerSession(IFtpClient ftp, bool expertMode, IUserPrompts prompts, IConflictResolver resolver,
            string workRoot, IFileChangeSource changes, IClock clock)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (prompts == null) throw new ArgumentNullException(nameof(prompts));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (workRoot == null) throw new ArgumentNullException(nameof(workRoot));
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (clock == null) throw new ArgumentNullException(nameof(clock));

            Policy = new ZonePolicy { ExpertMode = expertMode };
            Ftp = new ZoneGuardFtpClient(ftp, Policy);
            Gates = new FtpGates();
            Cache = new DirectoryCache(Ftp, Gates);
            Titles = new TitleCatalog(Ftp, Gates);
            QuickAccess = new QuickAccessBuilder(Cache, Ftp, Gates);
            Operations = new FileOperations(Ftp, Gates, Policy, prompts);
            Queue = new TransferQueue(Ftp, Gates, resolver, clock);
            Queue.JobChanged += OnQueueJobChanged;
            Editing = new EditSessionManager(workRoot, Ftp, Gates, changes, clock, Queue);
            Browser = new FolderBrowser(Cache, Policy);
        }

        public ZonePolicy Policy { get; }
        public ZoneGuardFtpClient Ftp { get; }
        public FtpGates Gates { get; }
        public DirectoryCache Cache { get; }
        public TitleCatalog Titles { get; }
        public QuickAccessBuilder QuickAccess { get; }
        public FileOperations Operations { get; }
        public TransferQueue Queue { get; }
        public EditSessionManager Editing { get; }
        public FolderBrowser Browser { get; }

        public bool ExpertMode
        {
            get { return Policy.ExpertMode; }
            set { Policy.ExpertMode = value; }
        }

        /// <summary>Ein LIST der Wurzel — scheitert laut, wenn die Konsole nicht antwortet.</summary>
        public Task CheckAsync(CancellationToken ct)
        {
            return Cache.ListAsync(RemotePath.Root, ct);
        }

        /// <summary>
        /// Nach einem fertigen Auftrag: betroffene Ordner neu lesen lassen
        /// (Plan-1-Übergabe). Der Cache kennt Pfade so, wie gelistet wurde —
        /// /data und /user/data sind zwei Schlüssel für einen Ordner, deshalb
        /// werden beide Schreibweisen verworfen. Liefert die Ordner, die die
        /// Ansicht neu anfordern sollte.
        /// </summary>
        public IReadOnlyCollection<string> ApplyFinished(TransferJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            foreach (var gone in job.RemoteSubtreesGone)
            {
                foreach (var alias in Aliases(gone)) Cache.InvalidateBelow(alias);
            }
            foreach (var folder in job.RemoteFoldersToRefresh)
            {
                foreach (var alias in Aliases(folder)) Cache.Invalidate(alias);
            }
            return job.RemoteFoldersToRefresh;
        }

        private void OnQueueJobChanged(object sender, TransferJob job)
        {
            if (job != null && job.IsFinished) ApplyFinished(job);
        }

        private static IEnumerable<string> Aliases(string path)
        {
            var p = RemotePath.Normalize(path);
            yield return p;
            if (RemotePath.IsSameOrBelow(p, UserData)) yield return Data + p.Substring(UserData.Length);
            else if (RemotePath.IsSameOrBelow(p, Data)) yield return UserData + p.Substring(Data.Length);
        }

        public void Dispose()
        {
            Queue.JobChanged -= OnQueueJobChanged;
            Editing.Dispose();
        }
    }
}
