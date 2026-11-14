using System;

namespace PS4Manager.Gallery.Core.Clips
{
    public sealed class ClipRange
    {
        public ClipRange(TimeSpan start, TimeSpan end, TimeSpan sourceDuration)
        {
            if (sourceDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceDuration));
            if (start < TimeSpan.Zero || start >= sourceDuration) throw new ArgumentOutOfRangeException(nameof(start));
            if (end <= start || end > sourceDuration) throw new ArgumentOutOfRangeException(nameof(end));
            Start = start;
            End = end;
        }

        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan Duration => End - Start;
    }

    public sealed class ClipMarks
    {
        public TimeSpan? Start { get; private set; }
        public TimeSpan? End { get; private set; }

        public void SetStart(TimeSpan time) => Start = time;
        public void SetEnd(TimeSpan time) => End = time;
        public void Reset() { Start = null; End = null; }

        public bool TryGetRange(TimeSpan duration, out ClipRange range)
        {
            range = null;
            if (!Start.HasValue || !End.HasValue || Start.Value < TimeSpan.Zero || End.Value <= Start.Value || End.Value > duration) return false;
            range = new ClipRange(Start.Value, End.Value, duration);
            return true;
        }
    }
}
