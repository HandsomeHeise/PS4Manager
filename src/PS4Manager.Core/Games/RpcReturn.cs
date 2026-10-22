namespace PS4Manager.Core.Games
{
    public static class RpcReturn
    {
        public static bool AsBool(ulong value) => (value & 0xffUL) != 0;
        public static int AsInt32(ulong value) => unchecked((int)(uint)value);
    }
}
