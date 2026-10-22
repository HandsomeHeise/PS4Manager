using System;
using System.Collections.Generic;

namespace FileManager.Core.Transfers
{
    /// <summary>Übertragungsrate, gleitend über die letzten drei Sekunden.</summary>
    internal sealed class RateMeter
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

        private readonly IClock _clock;
        private readonly Queue<KeyValuePair<DateTime, long>> _samples = new Queue<KeyValuePair<DateTime, long>>();

        public RateMeter(IClock clock)
        {
            _clock = clock;
        }

        public double BytesPerSecond { get; private set; }

        public void Sample(long doneBytes)
        {
            var now = _clock.UtcNow;
            _samples.Enqueue(new KeyValuePair<DateTime, long>(now, doneBytes));
            while (_samples.Count > 1 && now - _samples.Peek().Key > Window) _samples.Dequeue();

            var first = _samples.Peek();
            var seconds = (now - first.Key).TotalSeconds;
            if (seconds > 0) BytesPerSecond = (doneBytes - first.Value) / seconds;
        }
    }
}
