namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4MemoryMap
    {
        public Frame4MemoryMap(string name, ulong start, ulong end, ulong offset, int protection)
        {
            Name = name ?? "";
            Start = start;
            End = end;
            Offset = offset;
            Protection = protection;
        }

        public string Name { get; private set; }
        public ulong Start { get; private set; }
        public ulong End { get; private set; }
        public ulong Offset { get; private set; }
        public int Protection { get; private set; }
    }
}
