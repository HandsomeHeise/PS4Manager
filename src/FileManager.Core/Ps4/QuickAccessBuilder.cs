using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Ps4
{
    /// <summary>
    /// Baut den Schnellzugriff aus dem, was auf DIESER Konsole existiert
    /// (Spec „Schnellzugriff"). Fehlt ein Ort, fehlt sein Eintrag still; bricht
    /// dagegen die Verbindung ab, scheitert der Aufbau laut — sonst stünde
    /// ein leerer Schnellzugriff da, als wäre die Konsole leer.
    /// </summary>
    public sealed class QuickAccessBuilder
    {
        private readonly DirectoryCache _listings;
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;

        public QuickAccessBuilder(DirectoryCache listings, IFtpClient ftp, FtpGates gates)
        {
            if (listings == null) throw new ArgumentNullException(nameof(listings));
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            _listings = listings;
            _ftp = ftp;
            _gates = gates;
        }

        public async Task<IReadOnlyList<QuickAccessEntry>> BuildAsync(IEnumerable<string> pinned, CancellationToken ct)
        {
            var result = new List<QuickAccessEntry>();

            var data = await TryListAsync("/data", ct).ConfigureAwait(false);
            if (HasDir(data, "GoldHEN"))
            {
                result.Add(new QuickAccessEntry("GoldHEN", "/data/GoldHEN", QuickAccessKind.GoldHen));
                var goldhen = await TryListAsync("/data/GoldHEN", ct).ConfigureAwait(false);
                if (HasDir(goldhen, "plugins")) result.Add(new QuickAccessEntry("Plugins", "/data/GoldHEN/plugins", QuickAccessKind.Plugins));
                if (HasDir(goldhen, "cheats")) result.Add(new QuickAccessEntry("Cheats", "/data/GoldHEN/cheats", QuickAccessKind.Cheats));
                if (HasDir(goldhen, "patches")) result.Add(new QuickAccessEntry("Patches", "/data/GoldHEN/patches", QuickAccessKind.Patches));
            }
            if (HasDir(data, "pkg")) result.Add(new QuickAccessEntry("PKG folder", "/data/pkg", QuickAccessKind.PkgFolder));

            var user = await TryListAsync("/user", ct).ConfigureAwait(false);
            if (HasDir(user, "av_contents")) result.Add(new QuickAccessEntry("Captures", "/user/av_contents", QuickAccessKind.Captures));

            if (HasDir(user, "app") && NotEmpty(await TryListAsync("/user/app", ct).ConfigureAwait(false)))
                result.Add(new QuickAccessEntry("Games (internal)", "/user/app", QuickAccessKind.Games));

            var mnt = await TryListAsync("/mnt", ct).ConfigureAwait(false);
            bool ext0 = HasDir(mnt, "ext0");
            if (ext0)
            {
                var extUser = await TryListAsync("/mnt/ext0/user", ct).ConfigureAwait(false);
                if (HasDir(extUser, "app") && NotEmpty(await TryListAsync("/mnt/ext0/user/app", ct).ConfigureAwait(false)))
                    result.Add(new QuickAccessEntry("Games (extended storage)", "/mnt/ext0/user/app", QuickAccessKind.Games));
            }

            if (HasDir(user, "home"))
            {
                var homes = await TryListAsync("/user/home", ct).ConfigureAwait(false) ?? new FtpEntry[0];
                foreach (var home in homes.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                {
                    var content = await TryListAsync("/user/home/" + home.Name, ct).ConfigureAwait(false);
                    if (!HasDir(content, "savedata")) continue;
                    var name = await ReadUserNameAsync(home.Name, ct).ConfigureAwait(false) ?? home.Name;
                    result.Add(new QuickAccessEntry("Save data (" + name + ")", "/user/home/" + home.Name + "/savedata", QuickAccessKind.SaveData));
                }
            }

            for (int i = 0; i < 8; i++)
            {
                var usb = "usb" + i;
                if (HasDir(mnt, usb) && NotEmpty(await TryListAsync("/mnt/" + usb, ct).ConfigureAwait(false)))
                    result.Add(new QuickAccessEntry("USB (" + usb + ")", "/mnt/" + usb, QuickAccessKind.Usb));
            }

            if (ext0) result.Add(new QuickAccessEntry("Extended storage", "/mnt/ext0", QuickAccessKind.ExtendedStorage));

            foreach (var pin in pinned ?? Enumerable.Empty<string>())
            {
                var path = RemotePath.Normalize(pin);
                var label = path == RemotePath.Root ? RemotePath.Root : RemotePath.Name(path);
                result.Add(new QuickAccessEntry(label, path, QuickAccessKind.Pinned));
            }

            return result;
        }

        /// <summary>Fehlt ein Ort, ist das null; ein Verbindungsabbruch geht durch.</summary>
        private async Task<IReadOnlyList<FtpEntry>> TryListAsync(string path, CancellationToken ct)
        {
            try
            {
                return await _listings.ListAsync(path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (FtpException ex) when (ex.IsTransient) { throw; }
            catch (Exception) { return null; }
        }

        private async Task<string> ReadUserNameAsync(string id, CancellationToken ct)
        {
            try
            {
                var data = await FtpGates.RunAsync(_gates.Browse,
                    () => _ftp.DownloadAsync("/user/home/" + id + "/username.dat", ct), ct).ConfigureAwait(false);
                return UserNameReader.Parse(data);
            }
            catch (OperationCanceledException) { throw; }
            catch (FtpException ex) when (ex.IsTransient) { throw; }
            catch (Exception) { return null; }
        }

        private static bool HasDir(IReadOnlyList<FtpEntry> entries, string name)
        {
            return entries != null && entries.Any(e => e.IsDirectory && e.Name == name);
        }

        private static bool NotEmpty(IReadOnlyList<FtpEntry> entries)
        {
            return entries != null && entries.Count > 0;
        }
    }
}
