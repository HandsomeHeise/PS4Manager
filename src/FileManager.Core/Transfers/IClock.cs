using System;

namespace FileManager.Core.Transfers
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        private SystemClock()
        {
        }

        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }
}
