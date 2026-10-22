using System.Collections.Generic;

namespace PS4Manager.Gallery.Core.Ps4
{
    /// <summary>Ein Spiel samt seinen Fotos und Clips — eine Gruppe in der Gallery.</summary>
    public sealed class GameFolder
    {
        public GameFolder(string titleId, string name, byte[] iconBytes, IReadOnlyList<MediaItem> items)
        {
            TitleId = titleId;
            Name = name;
            IconBytes = iconBytes;
            Items = items;
        }

        public string TitleId { get; }
        public string Name { get; }
        public byte[] IconBytes { get; }
        public IReadOnlyList<MediaItem> Items { get; }
    }
}
