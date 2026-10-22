using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace PS4Manager.Core.Config
{
    public static class ManagerSettingsStore
    {
        public static ManagerSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new ManagerSettings();
                using (var stream = File.OpenRead(path))
                {
                    var settings = Serializer().ReadObject(stream) as ManagerSettings ?? new ManagerSettings();
                    settings.Repair();
                    return settings;
                }
            }
            catch (Exception)
            {
                return new ManagerSettings();
            }
        }

        public static void Save(string path, ManagerSettings settings)
        {
            settings.Repair();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
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
            return new DataContractJsonSerializer(typeof(ManagerSettings),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }
    }
}
