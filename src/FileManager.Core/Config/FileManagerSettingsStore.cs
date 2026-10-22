using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace FileManager.Core.Config
{
    /// <summary>
    /// JSON über DataContractJsonSerializer (im Framework enthalten, kein
    /// NuGet). Korrupte Datei = Neuanfang, nie ein Absturz (Gallery-Regel).
    /// Gespeichert wird über eine .tmp-Datei, damit ein Absturz beim Schreiben
    /// keine halbe Einstellungsdatei hinterlässt.
    /// </summary>
    public static class FileManagerSettingsStore
    {
        public static FileManagerSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new FileManagerSettings();
                using (var stream = File.OpenRead(path))
                {
                    var loaded = Serializer().ReadObject(stream) as FileManagerSettings ?? new FileManagerSettings();
                    loaded.Repair();
                    return loaded;
                }
            }
            catch (Exception)
            {
                return new FileManagerSettings();
            }
        }

        public static void Save(string path, FileManagerSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                Serializer().WriteObject(stream, settings);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(FileManagerSettings),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }
    }
}
