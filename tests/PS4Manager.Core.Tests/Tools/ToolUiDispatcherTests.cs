using System;
using System.Collections.Generic;
using System.Threading;
using PS4Manager.Core.Session;
using Xunit;

namespace PS4Manager.Core.Tests.Tools
{
    public sealed class ToolUiDispatcherTests
    {
        [Fact]
        public void Background_dispatch_posts_to_captured_context_without_a_control_handle()
        {
            var context = new RecordingSynchronizationContext();
            var dispatcher = new ToolUiDispatcher(context);
            var called = false;

            var worker = new Thread(() => dispatcher.Dispatch(() => called = true));
            worker.IsBackground = true;
            worker.Start();
            Assert.True(worker.Join(3000), "Background dispatch did not finish.");

            Assert.False(called);
            Assert.Equal(1, context.PendingCount);
            context.RunAll();
            Assert.True(called);
        }

        [Fact]
        public void Owner_thread_dispatch_runs_inline()
        {
            var context = new RecordingSynchronizationContext();
            var dispatcher = new ToolUiDispatcher(context);
            var called = false;

            dispatcher.Dispatch(() => called = true);

            Assert.True(called);
            Assert.Equal(0, context.PendingCount);
        }

        private sealed class RecordingSynchronizationContext : SynchronizationContext
        {
            private readonly object _sync = new object();
            private readonly Queue<Tuple<SendOrPostCallback, object>> _pending =
                new Queue<Tuple<SendOrPostCallback, object>>();

            public int PendingCount
            {
                get { lock (_sync) return _pending.Count; }
            }

            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (_sync) _pending.Enqueue(Tuple.Create(callback, state));
            }

            public void RunAll()
            {
                while (true)
                {
                    Tuple<SendOrPostCallback, object> work;
                    lock (_sync)
                    {
                        if (_pending.Count == 0) return;
                        work = _pending.Dequeue();
                    }
                    work.Item1(work.Item2);
                }
            }
        }
    }
}
