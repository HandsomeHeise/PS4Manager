using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Prüft die GoldHEN-Orte EINZELN und meldet, was da ist. Fehlende Orte
    /// werden über das jeweils lesbare Eltern-Listing erkannt; kann ein dort
    /// sichtbarer Ort nicht gelesen werden, bleibt der echte Fehler sichtbar.
    /// </summary>
    public sealed class GoldHenDiscoveryService
    {
        public const string Root = "/data/GoldHEN";
        public static readonly string PluginsPath = Root + "/plugins";
        public static readonly string CheatsPath = Root + "/cheats";
        public static readonly string PatchesPath = Root + "/patches";

        /// <summary>Reihenfolge der Kandidaten: der erste vorhandene ist die Aktivierungsquelle.</summary>
        public static readonly string[] ConfigurationCandidates =
        {
            Root + "/plugins.ini", Root + "/config.ini", Root + "/plugins.txt"
        };

        private readonly DirectoryCache _listings;
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;

        public GoldHenDiscoveryService(DirectoryCache listings, IFtpClient ftp, FtpGates gates)
        {
            if (listings == null) throw new ArgumentNullException(nameof(listings));
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            _listings = listings;
            _ftp = ftp;
            _gates = gates;
        }

        public async Task<GoldHenDiscovery> DiscoverAsync(CancellationToken ct)
        {
            var data = await _listings.ListAsync("/data", ct).ConfigureAwait(false);
            var rootExists = HasDir(data, "GoldHEN");
            var root = rootExists
                ? await _listings.ListAsync(Root, ct).ConfigureAwait(false)
                : (IReadOnlyList<FtpEntry>)new FtpEntry[0];
            var plugins = await ProbeAsync(GoldHenSection.Plugins, PluginsPath,
                rootExists && HasDir(root, "plugins"), ct).ConfigureAwait(false);
            var cheats = await ProbeAsync(GoldHenSection.Cheats, CheatsPath,
                rootExists && HasDir(root, "cheats"), ct).ConfigureAwait(false);
            var patches = await ProbeAsync(GoldHenSection.Patches, PatchesPath,
                rootExists && HasDir(root, "patches"), ct).ConfigureAwait(false);

            var configurationFiles = new List<string>();
            if (rootExists)
            {
                foreach (var candidate in ConfigurationCandidates)
                {
                    var name = RemotePath.Name(candidate);
                    if (root.Any(e => !e.IsDirectory && e.Name == name)) configurationFiles.Add(candidate);
                }
            }

            var configurationPath = configurationFiles.Count > 0 ? configurationFiles[0] : null;
            var configuration = new GoldHenLocation(GoldHenSection.Configuration, Root,
                configurationFiles.Count > 0, ConfigurationItems(root, configurationFiles));

            var pluginEntries = configurationPath == null
                ? UnknownPlugins(plugins.Entries)
                : await ReadPluginsAsync(configurationPath, plugins.Entries, ct).ConfigureAwait(false);

            return new GoldHenDiscovery(Root, rootExists,
                new[] { plugins, cheats, patches, configuration },
                configurationFiles, configurationPath, pluginEntries);
        }

        /// <summary>Liest die Konfiguration und verschmilzt sie mit den vorhandenen Plugin-Dateien.</summary>
        public async Task<IReadOnlyList<PluginEntry>> ReadPluginsAsync(string configurationPath,
            IReadOnlyList<RemoteItem> pluginFiles, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(configurationPath))
            {
                return UnknownPlugins(pluginFiles);
            }

            var files = pluginFiles ?? (IReadOnlyList<RemoteItem>)new RemoteItem[0];
            var bytes = await FtpGates.RunAsync(_gates.Browse,
                () => _ftp.DownloadAsync(configurationPath, ct), ct).ConfigureAwait(false);
            var document = TextFileContent.FromBytes(bytes);

            var names = files.Select(f => RemotePath.Name(f.Path)).ToList();
            var parsed = IniPluginEditor.Parse(document.Text, names);
            var entries = new List<PluginEntry>();
            foreach (var file in files)
            {
                var name = RemotePath.Name(file.Path);
                var match = parsed.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                entries.Add(match ?? new PluginEntry(name, file.Path, GoldHenActivation.Disabled, true, -1));
            }
            return entries;
        }

        private static IReadOnlyList<PluginEntry> UnknownPlugins(IReadOnlyList<RemoteItem> pluginFiles)
        {
            return (pluginFiles ?? (IReadOnlyList<RemoteItem>)new RemoteItem[0])
                .Select(f => new PluginEntry(RemotePath.Name(f.Path), f.Path, GoldHenActivation.Unknown, true, -1))
                .ToList();
        }

        private static IReadOnlyList<RemoteItem> ConfigurationItems(IReadOnlyList<FtpEntry> root,
            IReadOnlyList<string> configurationFiles)
        {
            var items = new List<RemoteItem>();
            foreach (var path in configurationFiles)
            {
                var name = RemotePath.Name(path);
                var entry = root.First(e => !e.IsDirectory && e.Name == name);
                items.Add(new RemoteItem(path, false, entry.Size, entry.Modified));
            }
            return items;
        }

        private async Task<GoldHenLocation> ProbeAsync(GoldHenSection section, string path, bool exists, CancellationToken ct)
        {
            var entries = new List<RemoteItem>();
            if (exists)
            {
                var listing = await _listings.ListAsync(path, ct).ConfigureAwait(false);
                foreach (var entry in listing.Where(e => !e.IsDirectory))
                    entries.Add(new RemoteItem(path + "/" + entry.Name, false, entry.Size, entry.Modified));
            }
            return new GoldHenLocation(section, path, exists, entries);
        }

        private static bool HasDir(IReadOnlyList<FtpEntry> entries, string name)
        {
            return entries.Any(e => e.IsDirectory && e.Name == name);
        }
    }
}
