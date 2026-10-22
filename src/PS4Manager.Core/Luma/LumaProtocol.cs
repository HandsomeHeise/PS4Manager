using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Luma
{
    public static class LumaProtocol
    {
        public const int HeaderSize = 12;
        public const int MaxPayload = 8 * 1024 * 1024;

        public static byte[] EncodeFrame(LumaOperation operation, ushort sequence, byte[] payload)
        {
            payload = payload ?? new byte[0];
            if (payload.Length > MaxPayload) throw new ArgumentOutOfRangeException(nameof(payload));
            var frame = new byte[HeaderSize + payload.Length];
            frame[0] = (byte)'G';
            frame[1] = (byte)'L';
            frame[2] = (byte)'U';
            frame[3] = (byte)'A';
            frame[4] = (byte)operation;
            frame[5] = 0;
            frame[6] = (byte)sequence;
            frame[7] = (byte)(sequence >> 8);
            WriteUInt32(frame, 8, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, frame, HeaderSize, payload.Length);
            return frame;
        }

        public static int FrameSize(byte[] buffer, int offset)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length - offset < HeaderSize) throw new ArgumentException("A Luma frame header needs 12 bytes.");
            return HeaderSize + (int)ReadUInt32(buffer, offset + 8);
        }

        public static async Task<LumaFrame> ReadFrameAsync(Stream stream, CancellationToken ct)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var header = new byte[HeaderSize];
            await ReadExactAsync(stream, header, 0, header.Length, ct).ConfigureAwait(false);
            if (header[0] != 'G' || header[1] != 'L' || header[2] != 'U' || header[3] != 'A')
                throw new LumaException("The console sent an invalid Luma frame header.");
            var length = ReadUInt32(header, 8);
            if (length > MaxPayload) throw new LumaException("The console sent an oversized Luma payload.");
            var payload = new byte[length];
            if (payload.Length > 0)
                await ReadExactAsync(stream, payload, 0, payload.Length, ct).ConfigureAwait(false);
            return new LumaFrame((LumaOperation)header[4], (ushort)(header[6] | (header[7] << 8)), payload);
        }

        public static byte[] Text(string value)
        {
            return string.IsNullOrEmpty(value) ? new byte[0] : Encoding.UTF8.GetBytes(value);
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int length,
            CancellationToken ct)
        {
            var read = 0;
            while (read < length)
            {
                var count = await stream.ReadAsync(buffer, offset + read, length - read, ct).ConfigureAwait(false);
                if (count <= 0) throw new EndOfStreamException("The Luma control channel closed unexpectedly.");
                read += count;
            }
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) |
                          (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
