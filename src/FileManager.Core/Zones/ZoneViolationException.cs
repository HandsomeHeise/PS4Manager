using System;

namespace FileManager.Core.Zones
{
    public sealed class ZoneViolationException : Exception
    {
        public ZoneViolationException(string path)
            : base("Locked area: " + path + " — writing here is not allowed (Expert Mode overrides the lock).")
        {
            Path = path;
        }

        public string Path { get; }
    }
}
