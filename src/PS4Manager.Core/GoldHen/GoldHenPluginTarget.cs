namespace PS4Manager.Core.GoldHen
{
    public sealed class GoldHenPluginTarget
    {
        public GoldHenPluginTarget(string titleId)
        {
            TitleId = string.IsNullOrEmpty(titleId) ? null : titleId;
            DisplayName = TitleId ?? "default";
        }

        public string TitleId { get; private set; }
        public string DisplayName { get; private set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
