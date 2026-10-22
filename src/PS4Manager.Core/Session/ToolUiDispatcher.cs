using System;
using System.Threading;

namespace PS4Manager.Core.Session
{
    /// <summary>
    /// Dispatches tool lifecycle work to the thread that created the tool.
    /// Unlike Control.BeginInvoke, the captured context is usable before the
    /// tool window creates its own native handle.
    /// </summary>
    public sealed class ToolUiDispatcher
    {
        private readonly SynchronizationContext _context;
        private readonly int _ownerThreadId;

        public ToolUiDispatcher(SynchronizationContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public void Dispatch(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (Thread.CurrentThread.ManagedThreadId == _ownerThreadId)
            {
                action();
                return;
            }
            _context.Post(state => ((Action)state)(), action);
        }
    }
}
