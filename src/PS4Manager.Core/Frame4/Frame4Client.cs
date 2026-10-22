using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4Client
    {
        private readonly IFrame4HttpTransport _transport;

        public Frame4Client()
            : this(new Frame4WebRequestTransport())
        {
        }

        public Frame4Client(IFrame4HttpTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<IReadOnlyList<Frame4Process>> GetProcessesAsync(Frame4Endpoint endpoint, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var text = await _transport.GetStringAsync(new Uri(endpoint.HttpBaseUri, "process-list"), ct)
                .ConfigureAwait(false);
            var error = TryReadError(text);
            if (error != null) throw new Frame4Exception(error);

            var dto = ReadJson<List<ProcessDto>>(text);
            var result = new List<Frame4Process>();
            foreach (var process in dto)
                result.Add(new Frame4Process(process.Name, process.Pid));
            return result;
        }

        public async Task<Frame4ProcessInfo> GetProcessInfoAsync(Frame4Endpoint endpoint, int pid, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var text = await GetAsync(endpoint, "process-info?pid=" + pid, ct).ConfigureAwait(false);
            var dto = ReadJson<ProcessInfoDto>(text);
            return new Frame4ProcessInfo(dto.Pid, dto.Name, dto.Path, dto.TitleId, dto.ContentId);
        }

        public async Task<IReadOnlyList<Frame4MemoryMap>> GetProcessMapsAsync(Frame4Endpoint endpoint, int pid,
            CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var text = await GetAsync(endpoint, "process-maps?pid=" + pid, ct).ConfigureAwait(false);
            var dto = ReadJson<List<MapDto>>(text);
            var result = new List<Frame4MemoryMap>();
            foreach (var map in dto)
            {
                var start = ParseHex(map.Start);
                var end = ParseHex(map.End);
                if (end <= start) continue;
                result.Add(new Frame4MemoryMap(map.Name, start, end, ParseHex(map.Offset),
                    ParseInt(map.Protection)));
            }
            return result;
        }

        public async Task<byte[]> ReadMemoryAsync(Frame4Endpoint endpoint, int pid, ulong address, uint length,
            CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var text = await GetAsync(endpoint, "read-memory?pid=" + pid + "&address=" + Hex(address) +
                                                "&length=" + Hex(length), ct).ConfigureAwait(false);
            var dto = ReadJson<ReadMemoryDto>(text);
            if (string.IsNullOrWhiteSpace(dto.Bytes)) return new byte[0];
            return dto.Bytes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                .ToArray();
        }

        public async Task WriteMemoryAsync(Frame4Endpoint endpoint, int pid, ulong address, byte[] bytes,
            CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            await ExpectSuccessAsync(endpoint, "write-memory?pid=" + pid + "&address=" + Hex(address) +
                "&bytes=" + ToHex(bytes), ct).ConfigureAwait(false);
        }

        public async Task<ulong> AllocateMemoryAsync(Frame4Endpoint endpoint, int pid, uint length, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var text = await GetAsync(endpoint, "allocate-memory?pid=" + pid + "&length=" + Hex(length), ct)
                .ConfigureAwait(false);
            var dto = ReadJson<AllocateDto>(text);
            if (!dto.Success) throw new Frame4Exception("frame4 did not allocate memory.");
            return ParseHex(dto.AllocatedAddress);
        }

        public async Task FreeMemoryAsync(Frame4Endpoint endpoint, int pid, ulong address, uint length,
            CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            await ExpectSuccessAsync(endpoint, "free-memory?pid=" + pid + "&address=" + Hex(address) +
                "&length=" + Hex(length), ct).ConfigureAwait(false);
        }

        public async Task NotifyAsync(Frame4Endpoint endpoint, int messageType, string message, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(message ?? ""));
            await ExpectSuccessAsync(endpoint, "notify?messageType=" + messageType + "&message=" + encoded, ct)
                .ConfigureAwait(false);
        }

        private async Task<string> GetAsync(Frame4Endpoint endpoint, string relative, CancellationToken ct)
        {
            var text = await _transport.GetStringAsync(new Uri(endpoint.HttpBaseUri, relative), ct)
                .ConfigureAwait(false);
            var error = TryReadError(text);
            if (error != null) throw new Frame4Exception(error);
            return text;
        }

        private async Task ExpectSuccessAsync(Frame4Endpoint endpoint, string relative, CancellationToken ct)
        {
            var text = await GetAsync(endpoint, relative, ct).ConfigureAwait(false);
            var success = ReadJson<SuccessDto>(text);
            if (!success.Success) throw new Frame4Exception("frame4 reported failure.");
        }

        private static string TryReadError(string text)
        {
            try
            {
                var error = ReadJson<ErrorDto>(text);
                return string.IsNullOrEmpty(error.Message) ? null : error.Message;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static T ReadJson<T>(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(SanitizeJson(text));
            using (var stream = new MemoryStream(bytes))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }

        private static string SanitizeJson(string text)
        {
            if (text == null) return "";
            StringBuilder builder = null;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var invalidControl = c < ' ' && c != '\r' && c != '\n' && c != '\t';
                if (!invalidControl)
                {
                    if (builder != null) builder.Append(c);
                    continue;
                }

                if (builder == null)
                {
                    builder = new StringBuilder(text.Length);
                    builder.Append(text, 0, i);
                }
                builder.Append(' ');
            }
            return builder == null ? text : builder.ToString();
        }

        private static string Hex(ulong value)
        {
            return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static ulong ParseHex(string value)
        {
            var text = (value ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            return ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        [DataContract]
        private sealed class ProcessDto
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "pid")] public int Pid { get; set; }
        }

        [DataContract]
        private sealed class ProcessInfoDto
        {
            [DataMember(Name = "pid")] public int Pid { get; set; }
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "title_id")] public string TitleId { get; set; }
            [DataMember(Name = "content_id")] public string ContentId { get; set; }
        }

        [DataContract]
        private sealed class MapDto
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "start")] public string Start { get; set; }
            [DataMember(Name = "end")] public string End { get; set; }
            [DataMember(Name = "offset")] public string Offset { get; set; }
            [DataMember(Name = "prot")] public string Protection { get; set; }
        }

        [DataContract]
        private sealed class ReadMemoryDto
        {
            [DataMember(Name = "bytes")] public string Bytes { get; set; }
        }

        [DataContract]
        private sealed class SuccessDto
        {
            [DataMember(Name = "success")] public bool Success { get; set; }
        }

        [DataContract]
        private sealed class AllocateDto
        {
            [DataMember(Name = "success")] public bool Success { get; set; }
            [DataMember(Name = "allocated_address")] public string AllocatedAddress { get; set; }
        }

        [DataContract]
        private sealed class ErrorDto
        {
            [DataMember(Name = "message")] public string Message { get; set; }
        }
    }
}
