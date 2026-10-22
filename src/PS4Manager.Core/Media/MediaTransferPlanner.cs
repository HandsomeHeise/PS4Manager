using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using PS4Manager.Gallery.Core.Ps4;

namespace PS4Manager.Core.Media
{
    /// <summary>
    /// Converts gallery actions into the same guarded transfer jobs used by
    /// the File Manager.
    /// </summary>
    public sealed class MediaTransferPlanner
    {
        private readonly FileOperations _operations;

        public MediaTransferPlanner(FileOperations operations)
        {
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        }

        public TransferJob PlanDownload(IReadOnlyList<MediaItem> items, string localDirectory)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var remoteItems = items.Select(ToRemoteItem).ToList();
            return _operations.PlanDownloadAsync(remoteItems, localDirectory, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        public async Task<TransferJob> PlanDeleteAsync(
            IReadOnlyList<MediaItem> items,
            CancellationToken cancellationToken)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var remoteItems = new List<RemoteItem>(items.Count);
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("Media items cannot contain null.", nameof(items));
                remoteItems.Add(ToRemoteItem(item));
            }

            var approved = await _operations.PlanDeleteAsync(remoteItems, cancellationToken).ConfigureAwait(false);
            if (approved == null) return null;

            var steps = new List<TransferStep>(items.Count * 3);
            foreach (var item in items)
            {
                steps.Add(approved.Steps.Single(step => step.RemotePath == Ps4Paths.MediaPath(item)));
                steps.Add(new TransferStep(
                    StepKind.DeleteRemoteFile,
                    Ps4Paths.DatPath(item),
                    null,
                    null,
                    0) { MissingIsSuccess = true });
                steps.Add(new TransferStep(
                    StepKind.DeleteRemoteFile,
                    Ps4Paths.ThumbnailPath(item),
                    null,
                    null,
                    0) { MissingIsSuccess = true });
            }
            return new TransferJob(approved.Kind, approved.Title, steps);
        }

        private static RemoteItem ToRemoteItem(MediaItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return new RemoteItem(Ps4Paths.MediaPath(item), false, item.Size, item.Timestamp);
        }
    }
}
