using System.Collections.Generic;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>Cheats oder Patches eines Spiels; ohne erkennbare Titel-ID ist TitleId null.</summary>
    public sealed class GoldHenTitleGroup
    {
        public GoldHenTitleGroup(string titleId, string label, IReadOnlyList<RemoteItem> files)
        {
            TitleId = titleId;
            Label = label;
            Files = files;
        }

        public string TitleId { get; }
        public string Label { get; }
        public IReadOnlyList<RemoteItem> Files { get; }
    }
}
