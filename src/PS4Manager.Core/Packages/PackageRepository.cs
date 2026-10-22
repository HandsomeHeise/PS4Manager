using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.Packages
{
    public sealed class PackageRepository
    {
        public static readonly IReadOnlyList<PackageLocation> DefaultLocations = new[]
        {
            new PackageLocation("Internal", "/data/pkg"),
            new PackageLocation("USB 0", "/mnt/usb0"),
            new PackageLocation("USB 1", "/mnt/usb1")
        };

        private readonly IFtpClient _ftp;
        private readonly IReadOnlyList<PackageLocation> _locations;

        public PackageRepository(IFtpClient ftp)
            : this(ftp, DefaultLocations)
        {
        }

        public PackageRepository(IFtpClient ftp, IReadOnlyList<PackageLocation> locations)
        {
            _ftp = ftp ?? throw new ArgumentNullException(nameof(ftp));
            _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        }

        public async Task<IReadOnlyList<RemotePackage>> DiscoverAsync(CancellationToken ct)
        {
            var result = new List<RemotePackage>();
            foreach (var location in _locations)
            {
                IReadOnlyList<FtpEntry> entries;
                try
                {
                    entries = await _ftp.ListAsync(location.Path, ct).ConfigureAwait(false);
                }
                catch (FtpException ex)
                {
                    if (ex.StatusCode == 550 || ex.Kind == FtpErrorKind.Permanent) continue;
                    throw;
                }

                foreach (var entry in entries.Where(e => e.Kind == FtpEntryKind.File && IsPackageFile(e.Name)))
                {
                    result.Add(new RemotePackage(location.Name, RemotePath.Combine(location.Path, entry.Name),
                        entry.Size, entry.Modified));
                }
            }

            return result
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Location, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsPackageFile(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                   && name.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase);
        }
    }
}
