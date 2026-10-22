using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    public sealed class GoldHenConfigurationRow
    {
        public GoldHenConfigurationRow(string[] cells, RemoteItem item, bool isPluginLine)
        {
            Cells = cells;
            Item = item;
            IsPluginLine = isPluginLine;
        }

        public string[] Cells { get; private set; }
        public RemoteItem Item { get; private set; }
        public bool IsPluginLine { get; private set; }
    }
}
