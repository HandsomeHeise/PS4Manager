using System;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4PrxModule
    {
        public Frame4PrxModule(uint handle, string name, ulong textAddress, uint textSize, ulong dataAddress,
            uint dataSize)
        {
            Handle = handle;
            Name = name ?? "";
            TextAddress = textAddress;
            TextSize = textSize;
            DataAddress = dataAddress;
            DataSize = dataSize;
        }

        public uint Handle { get; private set; }
        public string Name { get; private set; }
        public ulong TextAddress { get; private set; }
        public uint TextSize { get; private set; }
        public ulong DataAddress { get; private set; }
        public uint DataSize { get; private set; }
    }
}
