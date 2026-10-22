using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FileManager.Core.Config
{
    /// <summary>
    /// Die Einstellungen des File Managers (Spec „Einstellungen"). Getrennt
    /// von der Gallery — die kennt nur Ip/Port.
    /// </summary>
    [DataContract]
    public sealed class FileManagerSettings
    {
        public const string DetailsView = "Details";
        public const string TilesView = "Tiles";

        public FileManagerSettings()
        {
            ApplyDefaults();
        }

        [DataMember] public string Ip { get; set; }
        [DataMember] public int Port { get; set; }
        [DataMember] public bool ExpertMode { get; set; }
        [DataMember] public string ViewMode { get; set; }
        [DataMember] public string LastPath { get; set; }
        [DataMember] public List<string> PinnedFolders { get; set; }
        [DataMember] public Dictionary<string, int> ColumnWidths { get; set; }
        [DataMember] public int TreeWidth { get; set; }

        /// <summary>
        /// DataContractJsonSerializer ruft KEINEN Konstruktor auf — ohne diesen
        /// Haken hätten in der Datei fehlende Felder 0/null statt ihrer Standards.
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            Ip = "";
            Port = 2121;
            ExpertMode = false;
            ViewMode = DetailsView;
            LastPath = "/data";
            PinnedFolders = new List<string>();
            ColumnWidths = new Dictionary<string, int>();
            TreeWidth = 260;
        }

        internal void Repair()
        {
            if (Ip == null) Ip = "";
            if (Port <= 0 || Port > 65535) Port = 2121;
            if (ViewMode != DetailsView && ViewMode != TilesView) ViewMode = DetailsView;
            if (string.IsNullOrEmpty(LastPath)) LastPath = "/data";
            if (PinnedFolders == null) PinnedFolders = new List<string>();
            if (ColumnWidths == null) ColumnWidths = new Dictionary<string, int>();
            if (TreeWidth < 120) TreeWidth = 260;
        }
    }
}
