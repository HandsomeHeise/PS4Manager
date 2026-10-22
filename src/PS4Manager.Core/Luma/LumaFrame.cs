using System.Text;

namespace PS4Manager.Core.Luma
{
    public sealed class LumaFrame
    {
        public LumaFrame(LumaOperation operation, ushort sequence, byte[] payload)
        {
            Operation = operation;
            Sequence = sequence;
            Payload = payload ?? new byte[0];
        }

        public LumaOperation Operation { get; }
        public ushort Sequence { get; }
        public byte[] Payload { get; }
        public string Text { get { return Encoding.UTF8.GetString(Payload); } }
    }
}
