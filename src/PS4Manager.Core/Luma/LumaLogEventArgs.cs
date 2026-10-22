using System;

namespace PS4Manager.Core.Luma
{
    public sealed class LumaLogEventArgs : EventArgs
    {
        public LumaLogEventArgs(string text)
        {
            Text = text ?? "";
        }

        public string Text { get; }
    }
}
