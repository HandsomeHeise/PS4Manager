using System;
using FileManager.Core.Transfers;

namespace FileManager.Core.Tests.Support
{
    internal sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow + by;
        }
    }
}
