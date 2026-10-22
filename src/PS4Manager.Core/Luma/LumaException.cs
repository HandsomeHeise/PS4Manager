using System;

namespace PS4Manager.Core.Luma
{
    public sealed class LumaException : Exception
    {
        public LumaException(string message) : base(message) { }
        public LumaException(string message, Exception innerException) : base(message, innerException) { }
    }
}
