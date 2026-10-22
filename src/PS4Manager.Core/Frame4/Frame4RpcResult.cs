namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4RpcResult
    {
        public Frame4RpcResult(int pid, ulong rax)
        {
            Pid = pid;
            Rax = rax;
        }

        public int Pid { get; private set; }
        public ulong Rax { get; private set; }
    }
}
