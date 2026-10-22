using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.Games
{
    public sealed class InstalledGamesAdapter : IInstalledGames
    {
        private readonly ModGameDiscovery discovery;
        public InstalledGamesAdapter(ModGameDiscovery discovery) { this.discovery = discovery; }
        public async Task<IReadOnlyList<InstalledGame>> DiscoverAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try {
                var report = await discovery.DiscoverReportAsync(ct).ConfigureAwait(false);
                return Array.AsReadOnly(new GameCatalog().Entries.Select(g => Map(g.TitleId, report)).ToArray());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is FtpException || ex is IOException || ex is TimeoutException) {
                return Array.AsReadOnly(new GameCatalog().Entries.Select(g => new InstalledGame(g.TitleId, g.Name, null,
                    InstalledState.Unknown, "Installation status unavailable: " + ex.Message)).ToArray());
            }
        }
        public static InstalledGame Map(string titleId, ModGameDiscoveryReport report)
        {
            var game = report.Games.FirstOrDefault(g => g.TitleId == titleId);
            return game != null ? new InstalledGame(titleId, game.Name, game.Version, InstalledState.Installed, game.InvalidReason)
                : new InstalledGame(titleId, null, null, report.EnumerationComplete ? InstalledState.NotInstalled : InstalledState.Unknown,
                    report.EnumerationComplete ? null : "Installation status unavailable.");
        }
    }
}
