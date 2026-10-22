using System;
using System.Collections.Generic;

namespace FileManager.Core.Paths
{
    /// <summary>Zurück/Vor wie im Explorer: Navigieren nach einem Zurück kappt den Vor-Stapel.</summary>
    public sealed class NavigationHistory
    {
        private readonly Stack<string> _back = new Stack<string>();
        private readonly Stack<string> _forward = new Stack<string>();

        public string Current { get; private set; }
        public bool CanGoBack { get { return _back.Count > 0; } }
        public bool CanGoForward { get { return _forward.Count > 0; } }

        /// <summary>Wohin Zurück ginge — ohne den Verlauf zu ändern; null, wenn nicht möglich.</summary>
        public string BackTarget { get { return _back.Count > 0 ? _back.Peek() : null; } }

        /// <summary>Wohin Vor ginge — ohne den Verlauf zu ändern; null, wenn nicht möglich.</summary>
        public string ForwardTarget { get { return _forward.Count > 0 ? _forward.Peek() : null; } }

        public void Navigate(string path)
        {
            var target = RemotePath.Normalize(path);
            if (target == Current) return;
            if (Current != null) _back.Push(Current);
            _forward.Clear();
            Current = target;
        }

        public string GoBack()
        {
            if (!CanGoBack) throw new InvalidOperationException("No previous location.");
            _forward.Push(Current);
            Current = _back.Pop();
            return Current;
        }

        public string GoForward()
        {
            if (!CanGoForward) throw new InvalidOperationException("No next location.");
            _back.Push(Current);
            Current = _forward.Pop();
            return Current;
        }
    }
}
