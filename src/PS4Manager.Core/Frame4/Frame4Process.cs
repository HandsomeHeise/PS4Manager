namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4Process
    {
        public Frame4Process(string name, int pid)
        {
            Name = name ?? "";
            Pid = pid;
        }

        public string Name { get; private set; }
        public int Pid { get; private set; }
    }
}
