namespace PS4Manager.Core.GoldHen
{
    /// <summary>Ein Plugin und was die Konfiguration über es sagt.</summary>
    public sealed class PluginEntry
    {
        public PluginEntry(string name, string path, GoldHenActivation activation, bool filePresent, int lineIndex)
            : this(name, path, activation, filePresent, lineIndex, null)
        {
        }

        public PluginEntry(string name, string path, GoldHenActivation activation, bool filePresent, int lineIndex,
            string titleId)
        {
            Name = name;
            Path = path;
            Activation = activation;
            FilePresent = filePresent;
            LineIndex = lineIndex;
            TitleId = titleId;
        }

        public string Name { get; }
        public string Path { get; }
        public GoldHenActivation Activation { get; }
        public bool FilePresent { get; }
        public int LineIndex { get; }
        public string TitleId { get; }
    }
}
