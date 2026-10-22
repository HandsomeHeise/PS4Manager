namespace FileManager.Core.Ps4
{
    public enum QuickAccessKind
    {
        GoldHen, Plugins, Cheats, Patches, PkgFolder, Captures, Games, SaveData, Usb, ExtendedStorage, Pinned
    }

    public sealed class QuickAccessEntry
    {
        public QuickAccessEntry(string label, string path, QuickAccessKind kind)
        {
            Label = label;
            Path = path;
            Kind = kind;
        }

        public string Label { get; }
        public string Path { get; }
        public QuickAccessKind Kind { get; }
    }
}
