using System;
using System.IO;

namespace PS4Manager.Core.Frame4
{
    public interface IFrame4PacketConnection : IDisposable
    {
        Stream Read { get; }
        Stream Write { get; }
    }
}
