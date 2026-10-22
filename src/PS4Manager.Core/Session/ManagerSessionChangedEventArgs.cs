using System;

namespace PS4Manager.Core.Session
{
    public sealed class ManagerSessionChangedEventArgs : EventArgs
    {
        public ManagerSessionChangedEventArgs(ManagerSession session, ConnectionState state)
        {
            Session = session;
            State = state;
        }

        public ManagerSession Session { get; }
        public ConnectionState State { get; }
    }
}
