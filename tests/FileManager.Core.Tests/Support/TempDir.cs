using System;
using System.IO;

namespace FileManager.Core.Tests.Support
{
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fm-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string relative, string content)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllText(full, content);
            return full;
        }

        public string Sub(string relative)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(full);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (Exception) { }
        }
    }
}
