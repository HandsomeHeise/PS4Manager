using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace PS4Manager.Core.Tests.TestSupport
{
    internal static class StaTest
    {
        public static void Run(Action action)
        {
            ExceptionDispatchInfo failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); } });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("STA UI test did not finish.");
            failure?.Throw();
        }
    }
}
