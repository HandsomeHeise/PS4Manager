namespace Ps4.Core.Titles
{
    public sealed class TitleInfo
    {
        public TitleInfo(string id, string name, byte[] iconPng)
        {
            Id = id;
            Name = string.IsNullOrEmpty(name) ? id : name;
            IconPng = iconPng;
        }

        public string Id { get; }

        /// <summary>Anzeigename; fehlt er auf der Konsole, die Title-ID.</summary>
        public string Name { get; }

        /// <summary>icon0.png als Bytes; null, wenn keins da ist.</summary>
        public byte[] IconPng { get; }

        public bool HasRealName { get { return Name != Id; } }
    }
}
