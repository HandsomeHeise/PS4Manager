using System;
using System.IO;

namespace FileManager.Core.Editing
{
    /// <summary>Naht vor dem FileSystemWatcher — der Test löst Änderungen von Hand aus.</summary>
    public interface IFileChangeSource : IDisposable
    {
        /// <summary>Voller Pfad der geänderten Datei. Kann auf einem fremden Thread feuern.</summary>
        event EventHandler<string> Changed;

        void Watch(string directory);
    }

    /// <summary>
    /// Produktion. Changed, Created UND Renamed: viele Editoren speichern über
    /// eine Temp-Datei und benennen sie dann um — ein reines Changed verpasste das.
    /// Nur per Fahrprobe geprüft (Zeitverhalten des Watchers).
    /// </summary>
    public sealed class FileSystemChangeSource : IFileChangeSource
    {
        private FileSystemWatcher _watcher;

        public event EventHandler<string> Changed;

        public void Watch(string directory)
        {
            if (_watcher != null) _watcher.Dispose();
            _watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += (s, e) => Raise(e.FullPath);
            _watcher.Created += (s, e) => Raise(e.FullPath);
            _watcher.Renamed += (s, e) => Raise(e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }

        private void Raise(string path)
        {
            var handler = Changed;
            if (handler != null) handler(this, path);
        }

        public void Dispose()
        {
            if (_watcher != null) _watcher.Dispose();
            _watcher = null;
        }
    }
}
