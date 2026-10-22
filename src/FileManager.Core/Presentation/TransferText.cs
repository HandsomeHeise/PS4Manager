using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileManager.Core.Transfers;

namespace FileManager.Core.Presentation
{
    /// <summary>Texte der Übertragungsleiste (Spec): Status, Fortschritt als Textbalken, Rate, Restzeit.</summary>
    public static class TransferText
    {
        private const int BarWidth = 10;
        private const char Full = '█';
        private const char Empty = '░';

        public static string State(TransferJob job)
        {
            switch (job.State)
            {
                case JobState.Waiting: return "Waiting";
                case JobState.Running: return "Running";
                case JobState.Completed: return "Completed";
                case JobState.CompletedWithErrors: return "Completed with errors";
                case JobState.Cancelled: return "Cancelled";
                case JobState.Interrupted: return "Interrupted: " + (job.InterruptReason ?? "Connection to the PS4 was interrupted");
                default: return job.State.ToString();
            }
        }

        public static string Bar(long done, long total)
        {
            double fraction = total > 0 ? Math.Max(0, Math.Min(1, (double)done / total)) : 0;
            int filled = (int)Math.Round(fraction * BarWidth, MidpointRounding.AwayFromZero);
            int percent = (int)Math.Floor(fraction * 100);
            var bar = new StringBuilder(BarWidth + 6);
            bar.Append(Full, filled).Append(Empty, BarWidth - filled);
            return bar + " " + percent + " %";
        }

        public static string Progress(TransferJob job)
        {
            if (job.IsFinished) return job.Summary;
            if (job.TotalBytes > 0) return Bar(job.DoneBytes, job.TotalBytes);
            int done = job.Steps.Count(s => s.State != StepState.Pending);
            return Bar(done, job.Steps.Count);
        }

        public static string Rate(TransferJob job)
        {
            return job.State == JobState.Running ? SizeFormat.Rate(job.BytesPerSecond) : "";
        }

        public static string Remaining(TransferJob job)
        {
            var remaining = job.Remaining;
            return job.State == JobState.Running && remaining.HasValue ? RemainingText(remaining.Value) : "";
        }

        public static string RemainingText(TimeSpan remaining)
        {
            var whole = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
            return whole.TotalHours >= 1
                ? (int)whole.TotalHours + ":" + whole.Minutes.ToString("00") + ":" + whole.Seconds.ToString("00") + " remaining"
                : whole.Minutes + ":" + whole.Seconds.ToString("00") + " remaining";
        }

        public static string Summary(IReadOnlyList<TransferJob> jobs)
        {
            var running = jobs.FirstOrDefault(j => j.State == JobState.Running);
            if (running != null)
            {
                var rate = Rate(running);
                return running.Title + " " + Progress(running) + (rate.Length > 0 ? " · " + rate : "");
            }
            int waiting = jobs.Count(j => j.State == JobState.Waiting);
            if (waiting > 0) return waiting + " waiting";
            int interrupted = jobs.Count(j => j.State == JobState.Interrupted);
            if (interrupted > 0) return interrupted + " interrupted";
            return "No active transfers";
        }
    }
}
