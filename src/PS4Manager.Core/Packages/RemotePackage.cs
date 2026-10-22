using System;
using FileManager.Core.Paths;

namespace PS4Manager.Core.Packages
{
    public sealed class RemotePackage
    {
        public RemotePackage(string location, string path, long size, DateTime modified)
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("The location cannot be empty.", nameof(location));
            Location = location;
            Path = RemotePath.Normalize(path);
            Name = RemotePath.Name(Path);
            Size = size;
            Modified = modified;
        }

        public string Location { get; }
        public string Path { get; }
        public string Name { get; }
        public long Size { get; }
        public DateTime Modified { get; }
    }
}
