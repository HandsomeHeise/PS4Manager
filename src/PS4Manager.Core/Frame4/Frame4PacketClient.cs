using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4PacketClient
    {
        private const uint PacketMagic = 0xFFAABBCC;
        private const uint CommandVersion = 0xBD000001;
        private const uint CommandSuccess = 0x80000000;
        private const uint CommandConsoleInfo = 0xBDDD0005;
        private const uint CommandProcCall = 0xBDAA0006;
        private const uint CommandProcInstall = 0xBDAA0005;
        private const uint CommandProcPrxLoad = 0xBDAA000F;
        private const uint CommandProcPrxUnload = 0xBDAA0010;
        private const uint CommandProcAob = 0xBDAA0012;
        private const uint CommandProcPrxList = 0xBDAA0011;
        private const int HeaderLength = 12;
        private const int MaxSmallResponse = 4096;
        private const int ConsoleInfoLength = 332;
        private const int PrxModuleLength = 284;
        private const int PrxProcessNameLength = 32;
        private const int PrxPathLength = 100;

        private readonly IFrame4PacketTransport _transport;

        public Frame4PacketClient()
            : this(new Frame4TcpPacketTransport())
        {
        }

        public Frame4PacketClient(IFrame4PacketTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<string> GetVersionAsync(Frame4Endpoint endpoint, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false))
            {
                await SendHeaderAsync(connection.Write, CommandVersion, 0, ct).ConfigureAwait(false);
                var length = await ReadUInt32Async(connection.Read, ct).ConfigureAwait(false);
                if (length == 0 || length > MaxSmallResponse)
                    throw new Frame4Exception("Invalid frame4 version response length: " + length + ".");
                var bytes = new byte[length];
                await ReadExactAsync(connection.Read, bytes, ct).ConfigureAwait(false);
                return Encoding.ASCII.GetString(bytes);
            }
        }

        public async Task<Frame4ConsoleInfo> GetConsoleInfoAsync(Frame4Endpoint endpoint, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false))
            {
                await SendHeaderAsync(connection.Write, CommandConsoleInfo, 0, ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "console-info", ct).ConfigureAwait(false);
                var bytes = new byte[ConsoleInfoLength];
                await ReadExactAsync(connection.Read, bytes, ct).ConfigureAwait(false);

                var psid = new byte[16];
                Array.Copy(bytes, 0, psid, 0, psid.Length);
                return new Frame4ConsoleInfo(
                    psid,
                    ReadUInt32(bytes, 16),
                    ReadUInt32(bytes, 20),
                    ReadFixedString(bytes, 24, 50),
                    ReadFixedString(bytes, 74, 50),
                    ReadUInt32(bytes, 124),
                    ReadFixedString(bytes, 128, 100),
                    ReadFixedString(bytes, 228, 100),
                    ReadUInt32(bytes, 328));
            }
        }

        public async Task<ulong> FindAobAsync(Frame4Endpoint endpoint, int pid, ulong start, uint length,
            Frame4AobPattern pattern, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false))
            {
                var payload = new byte[20];
                WriteUInt32(payload, 0, unchecked((uint)pid));
                WriteUInt64(payload, 4, start);
                WriteUInt32(payload, 12, length);
                WriteUInt32(payload, 16, (uint)pattern.Length);

                await SendPacketAsync(connection.Write, CommandProcAob, payload, ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "aob-search", ct).ConfigureAwait(false);
                await connection.Write.WriteAsync(pattern.Bytes, 0, pattern.Bytes.Length, ct).ConfigureAwait(false);
                await connection.Write.WriteAsync(pattern.Mask, 0, pattern.Mask.Length, ct).ConfigureAwait(false);
                await connection.Write.FlushAsync(ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "aob-search", ct).ConfigureAwait(false);
                return await ReadUInt64Async(connection.Read, ct).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<Frame4PrxModule>> GetPrxModulesAsync(Frame4Endpoint endpoint, int pid,
            CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false))
            {
                var payload = new byte[4];
                WriteUInt32(payload, 0, unchecked((uint)pid));
                await SendPacketAsync(connection.Write, CommandProcPrxList, payload, ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "prx-list", ct).ConfigureAwait(false);
                var count = await ReadUInt32Async(connection.Read, ct).ConfigureAwait(false);
                if (count > 4096) throw new Frame4Exception("Invalid frame4 PRX module count: " + count + ".");

                var modules = new List<Frame4PrxModule>((int)count);
                var entry = new byte[PrxModuleLength];
                for (var i = 0; i < count; i++)
                {
                    await ReadExactAsync(connection.Read, entry, ct).ConfigureAwait(false);
                    modules.Add(new Frame4PrxModule(
                        ReadUInt32(entry, 0),
                        ReadFixedString(entry, 4, 256),
                        ReadUInt64(entry, 260),
                        ReadUInt32(entry, 268),
                        ReadUInt64(entry, 272),
                        ReadUInt32(entry, 280)));
                }
                return modules;
            }
        }

        public async Task<uint> LoadPrxAsync(Frame4Endpoint endpoint, string processName, string prxPath,
            CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var processBytes = FixedAscii(processName, PrxProcessNameLength, nameof(processName));
            var pathBytes = FixedAscii(prxPath, PrxPathLength, nameof(prxPath));
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false))
            {
                await SendHeaderAsync(connection.Write, CommandProcPrxLoad, 0, ct).ConfigureAwait(false);
                await connection.Write.WriteAsync(processBytes, 0, processBytes.Length, ct).ConfigureAwait(false);
                await connection.Write.WriteAsync(pathBytes, 0, pathBytes.Length, ct).ConfigureAwait(false);
                await connection.Write.FlushAsync(ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "prx-load", ct).ConfigureAwait(false);
                return await ReadUInt32Async(connection.Read, ct).ConfigureAwait(false);
            }
        }

        public async Task UnloadPrxAsync(Frame4Endpoint endpoint, string processName, uint handle, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var processBytes = FixedAscii(processName, PrxProcessNameLength, nameof(processName));
            var payload = new byte[4];
            WriteUInt32(payload, 0, handle);
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false))
            {
                await SendPacketAsync(connection.Write, CommandProcPrxUnload, payload, ct).ConfigureAwait(false);
                await connection.Write.WriteAsync(processBytes, 0, processBytes.Length, ct).ConfigureAwait(false);
                await connection.Write.FlushAsync(ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "prx-unload", ct).ConfigureAwait(false);
            }
        }

        public async Task<ulong> InstallRpcAsync(Frame4Endpoint endpoint, int pid, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
            ct.ThrowIfCancellationRequested();
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false)) {
                var payload = new byte[4]; WriteUInt32(payload, 0, (uint)pid);
                await SendPacketAsync(connection.Write, CommandProcInstall, payload, ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "rpc-install", ct).ConfigureAwait(false);
                var stub = await ReadUInt64Async(connection.Read, ct).ConfigureAwait(false);
                if (stub == 0) throw new Frame4Exception("Frame4 returned a zero RPC stub.");
                return stub;
            }
        }

        public async Task<Frame4RpcResult> CallRpcAsync(Frame4Endpoint endpoint, Frame4RpcCall call,
            CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (call == null) throw new ArgumentNullException(nameof(call));
            using (var connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false))
            {
                var payload = new byte[68];
                WriteUInt32(payload, 0, unchecked((uint)call.Pid));
                WriteUInt64(payload, 4, call.RpcStub);
                WriteUInt64(payload, 12, call.Rip);
                WriteUInt64(payload, 20, call.Rdi);
                WriteUInt64(payload, 28, call.Rsi);
                WriteUInt64(payload, 36, call.Rdx);
                WriteUInt64(payload, 44, call.Rcx);
                WriteUInt64(payload, 52, call.R8);
                WriteUInt64(payload, 60, call.R9);

                await SendPacketAsync(connection.Write, CommandProcCall, payload, ct).ConfigureAwait(false);
                await ExpectSuccessAsync(connection.Read, "rpc-call", ct).ConfigureAwait(false);
                var response = new byte[12];
                await ReadExactAsync(connection.Read, response, ct).ConfigureAwait(false);
                if (unchecked((int)ReadUInt32(response, 0)) != call.Pid)
                    throw new Frame4Exception("Frame4 RPC response belongs to another process.");
                return new Frame4RpcResult(unchecked((int)ReadUInt32(response, 0)), ReadUInt64(response, 4));
            }
        }

        private static async Task SendHeaderAsync(Stream stream, uint command, uint length, CancellationToken ct)
        {
            var header = new byte[HeaderLength];
            WriteUInt32(header, 0, PacketMagic);
            WriteUInt32(header, 4, command);
            WriteUInt32(header, 8, length);
            await stream.WriteAsync(header, 0, header.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static Task SendPacketAsync(Stream stream, uint command, byte[] payload, CancellationToken ct)
        {
            return SendPacketAsync(stream, command, payload, 0, payload == null ? 0 : payload.Length, ct);
        }

        private static async Task SendPacketAsync(Stream stream, uint command, byte[] payload, int offset, int length,
            CancellationToken ct)
        {
            await SendHeaderAsync(stream, command, (uint)length, ct).ConfigureAwait(false);
            if (length > 0)
                await stream.WriteAsync(payload, offset, length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static async Task ExpectSuccessAsync(Stream stream, string commandName, CancellationToken ct)
        {
            var status = await ReadUInt32Async(stream, ct).ConfigureAwait(false);
            if (status != CommandSuccess)
                throw new Frame4Exception("Frame4 " + commandName + " failed with status 0x" +
                                          status.ToString("X8") + ".");
        }

        private static async Task<uint> ReadUInt32Async(Stream stream, CancellationToken ct)
        {
            var bytes = new byte[4];
            await ReadExactAsync(stream, bytes, ct).ConfigureAwait(false);
            return (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
        }

        private static async Task<ulong> ReadUInt64Async(Stream stream, CancellationToken ct)
        {
            var bytes = new byte[8];
            await ReadExactAsync(stream, bytes, ct).ConfigureAwait(false);
            return ReadUInt64(bytes, 0);
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct).ConfigureAwait(false);
                if (read <= 0) throw new EndOfStreamException("Frame4 socket closed before the response was complete.");
                offset += read;
            }
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            for (var i = 0; i < 8; i++) buffer[offset + i] = (byte)(value >> (8 * i));
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) |
                          (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        private static ulong ReadUInt64(byte[] buffer, int offset)
        {
            var value = 0UL;
            for (var i = 0; i < 8; i++) value |= (ulong)buffer[offset + i] << (8 * i);
            return value;
        }

        private static string ReadFixedString(byte[] buffer, int offset, int length)
        {
            var end = offset;
            var limit = offset + length;
            while (end < limit && buffer[end] != 0) end++;
            return Encoding.ASCII.GetString(buffer, offset, end - offset).TrimEnd();
        }

        private static byte[] FixedAscii(string value, int length, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value cannot be empty.", name);
            var bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length >= length)
                throw new ArgumentException("Value must be shorter than " + length + " ASCII bytes.", name);
            var result = new byte[length];
            Array.Copy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
    }
}
