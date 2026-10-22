using System;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4ConsoleInfo
    {
        public Frame4ConsoleInfo(byte[] psid, uint updateVersion, uint sdkVersion, string kernelOsType,
            string kernelOsRelease, uint kernelOsRevision, string kernelVersion, string hardwareModel, uint cpuCount)
        {
            if (psid == null) throw new ArgumentNullException(nameof(psid));
            Psid = (byte[])psid.Clone();
            PsidHex = ToHex(Psid);
            UpdateVersion = updateVersion;
            SdkVersion = sdkVersion;
            KernelOsType = kernelOsType ?? "";
            KernelOsRelease = kernelOsRelease ?? "";
            KernelOsRevision = kernelOsRevision;
            KernelVersion = kernelVersion ?? "";
            HardwareModel = hardwareModel ?? "";
            CpuCount = cpuCount;
        }

        public byte[] Psid { get; private set; }
        public string PsidHex { get; private set; }
        public uint UpdateVersion { get; private set; }
        public uint SdkVersion { get; private set; }
        public string KernelOsType { get; private set; }
        public string KernelOsRelease { get; private set; }
        public uint KernelOsRevision { get; private set; }
        public string KernelVersion { get; private set; }
        public string HardwareModel { get; private set; }
        public uint CpuCount { get; private set; }

        private static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string alphabet = "0123456789ABCDEF";
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = alphabet[bytes[i] >> 4];
                chars[i * 2 + 1] = alphabet[bytes[i] & 0x0F];
            }
            return new string(chars);
        }
    }
}
