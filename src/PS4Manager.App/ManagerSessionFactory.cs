using System;
using System.Windows.Forms;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Session;
using Ps4.Core.Ftp;

namespace PS4Manager.App
{
    /// <summary>
    /// Baut die EINE Sitzung der Anwendung (Spec „Gemeinsame Verbindung"): ein
    /// echter FTP-Client, ein Wächter, ein Tor-Satz, eine Warteschlange. Vorbei
    /// ist es mit fenstereigenen Verbindungen — jedes Werkzeug konsumiert diese.
    /// </summary>
    internal sealed class ManagerSessionFactory : IManagerSessionFactory
    {
        private readonly Func<Form> _promptOwner;

        public ManagerSessionFactory(Func<Form> promptOwner)
        {
            _promptOwner = promptOwner ?? throw new ArgumentNullException(nameof(promptOwner));
        }

        public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode)
        {
            var explorer = new ExplorerSession(
                new FtpClient(endpoint.Host, endpoint.Port),
                expertMode,
                new ManagerPrompts(_promptOwner),
                new ManagerConflictResolver(_promptOwner),
                AppPaths.WorkRoot,
                new FileSystemChangeSource(),
                SystemClock.Instance);
            return new ManagerSession(endpoint, explorer);
        }
    }
}
