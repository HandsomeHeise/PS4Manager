namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Versioniert asynchrone Fensteranfragen. Nur die zuletzt gestartete
    /// Erkennung und der zuletzt gewählte Abschnitt dürfen UI-Zustand liefern.
    /// </summary>
    public sealed class GoldHenRequestGate
    {
        private int _refresh;
        private int _content;
        private GoldHenSection _section;

        public int BeginRefresh()
        {
            _content++;
            return ++_refresh;
        }

        public bool IsCurrentRefresh(int version)
        {
            return version == _refresh;
        }

        public int BeginContent(GoldHenSection section)
        {
            _section = section;
            return ++_content;
        }

        public bool IsCurrentContent(int version, GoldHenSection section)
        {
            return version == _content && section == _section;
        }
    }
}
