namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4ProcessInfo
    {
        public Frame4ProcessInfo(int pid, string name, string path, string titleId, string contentId)
        {
            Pid = pid;
            Name = name ?? "";
            Path = path ?? "";
            TitleId = titleId ?? "";
            ContentId = contentId ?? "";
        }

        public int Pid { get; private set; }
        public string Name { get; private set; }
        public string Path { get; private set; }
        public string TitleId { get; private set; }
        public string ContentId { get; private set; }
    }
}
