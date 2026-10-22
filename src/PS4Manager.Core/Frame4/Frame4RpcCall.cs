using System;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4RpcCall
    {
        public Frame4RpcCall(int pid, ulong rpcStub, ulong rip, ulong rdi, ulong rsi, ulong rdx, ulong rcx,
            ulong r8, ulong r9)
        {
            Pid = pid;
            RpcStub = rpcStub;
            Rip = rip;
            Rdi = rdi;
            Rsi = rsi;
            Rdx = rdx;
            Rcx = rcx;
            R8 = r8;
            R9 = r9;
        }

        public int Pid { get; private set; }
        public ulong RpcStub { get; private set; }
        public ulong Rip { get; private set; }
        public ulong Rdi { get; private set; }
        public ulong Rsi { get; private set; }
        public ulong Rdx { get; private set; }
        public ulong Rcx { get; private set; }
        public ulong R8 { get; private set; }
        public ulong R9 { get; private set; }
    }
}
