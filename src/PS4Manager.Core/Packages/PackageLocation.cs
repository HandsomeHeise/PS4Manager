using System;
using FileManager.Core.Paths;

namespace PS4Manager.Core.Packages
{
    public sealed class PackageLocation
    {
        public PackageLocation(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("The name cannot be empty.", nameof(name));
            Name = name;
            Path = RemotePath.Normalize(path);
        }

        public string Name { get; }
        public string Path { get; }
    }
}
