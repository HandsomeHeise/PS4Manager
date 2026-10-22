using System;
using System.IO;

namespace PS4Manager.Core.Frame4
{
    public interface IFrame4UartConnection : IDisposable
    {
        Stream Read { get; }
    }
}
