using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Config;
using PS4Manager.Core.Updates;

namespace PS4Manager.App
{
    internal sealed class UpdateService
    {
        public Version CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0); }
        }

        public async Task<UpdateCheckResult> CheckAsync(UpdateSettings settings, CancellationToken token)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.ManifestUrl))
                return new UpdateCheckResult(null, UpdateValidationState.InvalidManifest, "Enter an update manifest URL first.");

            string json;
            using (var client = new WebClient())
            {
                token.Register(client.CancelAsync);
                json = await client.DownloadStringTaskAsync(new Uri(settings.ManifestUrl)).ConfigureAwait(false);
            }

            var manifest = UpdateManifest.Parse(json);
            return UpdateVerifier.Evaluate(manifest,
                new UpdateCheckContext(CurrentVersion, settings.Channel, settings.PublicKeyXml));
        }

        public async Task<string> DownloadPackageAsync(UpdateManifest manifest, CancellationToken token)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            var version = manifest.Version == null ? "unknown" : manifest.Version.ToString();
            var directory = Path.Combine(AppPaths.UpdateRoot, version);
            Directory.CreateDirectory(directory);
            var package = Path.Combine(directory, "PS4Manager-" + version + ".zip");

            using (var client = new WebClient())
            {
                token.Register(client.CancelAsync);
                await client.DownloadFileTaskAsync(new Uri(manifest.Url), package).ConfigureAwait(false);
            }

            if (!UpdateVerifier.VerifyFileHash(package, manifest.Sha256))
            {
                try { File.Delete(package); } catch { }
                throw new InvalidDataException("Downloaded update hash does not match the manifest.");
            }

            return package;
        }

        public void LaunchUpdater(string packagePath)
        {
            var appExe = Assembly.GetExecutingAssembly().Location;
            var appDir = Path.GetDirectoryName(appExe);
            var updater = Path.Combine(appDir, "PS4Manager.Updater.exe");
            if (!File.Exists(updater))
                throw new FileNotFoundException("PS4Manager.Updater.exe was not found next to the app.", updater);

            var process = Process.GetCurrentProcess();
            Process.Start(new ProcessStartInfo
            {
                FileName = updater,
                Arguments = "--pid " + process.Id
                            + " --package " + Quote(packagePath)
                            + " --target " + Quote(appDir)
                            + " --restart " + Quote(appExe),
                UseShellExecute = false
            });
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }
    }
}
