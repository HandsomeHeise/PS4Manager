using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace PS4Manager.Updater
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = UpdateOptions.Parse(args);
                WaitForProcess(options.ProcessId);
                ApplyZip(options.PackagePath, options.TargetDirectory);
                if (!string.IsNullOrWhiteSpace(options.RestartPath) && File.Exists(options.RestartPath))
                    Process.Start(options.RestartPath);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PS4Manager.Updater.log"), ex.ToString());
                return 1;
            }
        }

        private static void WaitForProcess(int processId)
        {
            if (processId <= 0) return;
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    process.WaitForExit(60000);
                }
            }
            catch (ArgumentException)
            {
            }
        }

        private static void ApplyZip(string packagePath, string targetDirectory)
        {
            if (!File.Exists(packagePath)) throw new FileNotFoundException("Update package not found.", packagePath);
            if (string.IsNullOrWhiteSpace(targetDirectory)) throw new ArgumentException("Target directory is required.", nameof(targetDirectory));

            var staging = Path.Combine(Path.GetTempPath(), "PS4Manager-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                ZipFile.ExtractToDirectory(packagePath, staging);
                CopyTree(staging, targetDirectory);
            }
            finally
            {
                try { Directory.Delete(staging, true); }
                catch { }
            }
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(target, relative));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
        }

        private sealed class UpdateOptions
        {
            public int ProcessId { get; private set; }
            public string PackagePath { get; private set; }
            public string TargetDirectory { get; private set; }
            public string RestartPath { get; private set; }

            public static UpdateOptions Parse(string[] args)
            {
                var options = new UpdateOptions();
                for (int i = 0; i < args.Length; i++)
                {
                    var value = i + 1 < args.Length ? args[i + 1] : "";
                    switch (args[i])
                    {
                        case "--pid":
                            int pid;
                            if (int.TryParse(value, out pid)) options.ProcessId = pid;
                            i++;
                            break;
                        case "--package":
                            options.PackagePath = value;
                            i++;
                            break;
                        case "--target":
                            options.TargetDirectory = value;
                            i++;
                            break;
                        case "--restart":
                            options.RestartPath = value;
                            i++;
                            break;
                    }
                }

                if (new[] { options.PackagePath, options.TargetDirectory }.Any(string.IsNullOrWhiteSpace))
                    throw new ArgumentException("Usage: PS4Manager.Updater --pid <id> --package <zip> --target <dir> [--restart <exe>]");
                return options;
            }
        }
    }
}
