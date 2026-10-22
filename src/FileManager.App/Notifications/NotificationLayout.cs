using System;
using System.Collections.Generic;
using System.Drawing;

namespace FileManager.App.Notifications
{
    public static class NotificationLayout
    {
        public static IReadOnlyList<Rectangle> Arrange(Rectangle workingArea, int dpi, IReadOnlyList<Size> preferredSizes)
        {
            var result = new List<Rectangle>();
            int margin = Math.Max(1, (int)Math.Round(12 * Math.Max(1, dpi) / 96d));
            int availableWidth = workingArea.Width - margin * 2, y = workingArea.Bottom - margin;
            if (availableWidth <= 0) return result;
            foreach (var size in preferredSizes) {
                int width = Math.Min(availableWidth, size.Width), height = size.Height;
                if (width <= 0 || height <= 0 || y - height < workingArea.Top + margin || result.Count == 3) break;
                result.Add(new Rectangle(workingArea.Right - margin - width, y - height, width, height));
                y -= height + margin;
            }
            return result;
        }
    }
}
