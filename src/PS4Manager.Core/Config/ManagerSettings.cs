using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.Serialization;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Luma;

namespace PS4Manager.Core.Config
{
    [DataContract]
    public sealed class ManagerSettings
    {
        public ManagerSettings()
        {
            ApplyDefaults();
        }

        [DataMember] public string Ip { get; set; }
        [DataMember] public int Port { get; set; }
        [DataMember] public bool ExpertMode { get; set; }
        [DataMember] public string Theme { get; set; }
        [DataMember] public bool MigrationCompleted { get; set; }
        [DataMember] public FileManagerViewSettings FileManager { get; set; }
        [DataMember] public Frame4Settings Frame4 { get; set; }
        [DataMember] public UpdateSettings Updates { get; set; }
        [DataMember] public LumaSettings Luma { get; set; }
        [DataMember] public ModSettings Mods {get;set;}

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
            Theme = "Dark";
            MigrationCompleted = false;
            FileManager = new FileManagerViewSettings();
            Frame4 = new Frame4Settings();
            Updates = new UpdateSettings();
            Luma = new LumaSettings();
            Mods = new ModSettings();
        }

        internal void Repair()
        {
            if (Ip == null) Ip = "";
            if (Port <= 0 || Port > 65535) Port = 2121;
            if (string.IsNullOrEmpty(Theme)) Theme = "Dark";
            if (FileManager == null) FileManager = new FileManagerViewSettings();
            FileManager.Repair();
            if (Frame4 == null) Frame4 = new Frame4Settings();
            Frame4.Repair();
            if (Updates == null) Updates = new UpdateSettings();
            Updates.Repair();
            if (Luma == null) Luma = new LumaSettings();
            Luma.Repair();
            if(Mods==null)Mods=new ModSettings();Mods.Repair();
        }
    }

    [DataContract]
    public sealed class ModSettings
    {
        public const string DefaultOrigin="https://ps4manager.devkit.online/";
        public ModSettings(){CatalogOrigin=DefaultOrigin;Profiles=new List<ModProfileSettings>();}
        [DataMember] public string CatalogOrigin {get;set;}
        [DataMember] public Guid SelectedProfileId {get;set;}
        [DataMember] public List<ModProfileSettings> Profiles {get;set;}
        internal void Repair(){Uri uri;if(!Uri.TryCreate(CatalogOrigin,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0||uri.AbsolutePath!="/"||uri.Query.Length!=0||uri.Fragment.Length!=0)CatalogOrigin=DefaultOrigin;
            Profiles=(Profiles??new List<ModProfileSettings>()).Where(p=>p!=null&&p.Id!=Guid.Empty&&!string.IsNullOrWhiteSpace(p.Name)).GroupBy(p=>p.Id).Select(g=>g.First()).Take(100).ToList();}
    }
    [DataContract]
    public sealed class ModProfileSettings
    {
        [DataMember] public Guid Id {get;set;}
        [DataMember] public string Name {get;set;}
        public override string ToString(){return Name;}
    }

    [DataContract]
    public sealed class LumaSettings
    {
        public const string Gta5 = "gta5";
        public const string Rdr2 = "rdr2";

        public LumaSettings()
        {
            ApplyDefaults();
        }

        [DataMember] public string Game { get; set; }
        [DataMember] public int Port { get; set; }
        [DataMember] public string Token { get; set; }
        [DataMember] public List<string> RecentProjects { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            Game = Gta5;
            Port = LumaEndpoint.GtaPort;
            Token = "";
            RecentProjects = new List<string>();
        }

        internal void Repair()
        {
            if (Game != Gta5 && Game != Rdr2) Game = Gta5;
            if (Port <= 0 || Port > 65535) Port = Game == Rdr2 ? LumaEndpoint.Rdr2Port : LumaEndpoint.GtaPort;
            if (Token == null) Token = "";
            RecentProjects = (RecentProjects ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 1024)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        }
    }

    [DataContract]
    public sealed class UpdateSettings
    {
        public UpdateSettings()
        {
            ApplyDefaults();
        }

        [DataMember] public string ManifestUrl { get; set; }
        [DataMember] public string Channel { get; set; }
        [DataMember] public string PublicKeyXml { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            ManifestUrl = "";
            Channel = "stable";
            PublicKeyXml = "";
        }

        internal void Repair()
        {
            if (ManifestUrl == null) ManifestUrl = "";
            if (string.IsNullOrWhiteSpace(Channel)) Channel = "stable";
            if (PublicKeyXml == null) PublicKeyXml = "";
        }
    }

    [DataContract]
    public sealed class Frame4Settings
    {
        public Frame4Settings()
        {
            ApplyDefaults();
        }

        [DataMember] public bool Enabled { get; set; }
        [DataMember] public int HttpPort { get; set; }
        [DataMember] public int UartPort { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            Enabled = false;
            HttpPort = Frame4Endpoint.DefaultHttpPort;
            UartPort = Frame4Endpoint.DefaultUartPort;
        }

        internal void Repair()
        {
            if (HttpPort <= 0 || HttpPort > 65535) HttpPort = Frame4Endpoint.DefaultHttpPort;
            if (UartPort <= 0 || UartPort > 65535) UartPort = Frame4Endpoint.DefaultUartPort;
        }
    }

    [DataContract]
    public sealed class FileManagerViewSettings
    {
        public const string DetailsView = "Details";
        public const string TilesView = "Tiles";

        public FileManagerViewSettings()
        {
            ApplyDefaults();
        }

        [DataMember] public string ViewMode { get; set; }
        [DataMember] public string LastPath { get; set; }
        [DataMember] public List<string> PinnedFolders { get; set; }
        [DataMember] public Dictionary<string, int> ColumnWidths { get; set; }
        [DataMember] public int TreeWidth { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            ViewMode = DetailsView;
            LastPath = "/data";
            PinnedFolders = new List<string>();
            ColumnWidths = new Dictionary<string, int>();
            TreeWidth = 260;
        }

        internal void Repair()
        {
            if (ViewMode != DetailsView && ViewMode != TilesView) ViewMode = DetailsView;
            if (string.IsNullOrEmpty(LastPath)) LastPath = "/data";
            if (PinnedFolders == null) PinnedFolders = new List<string>();
            if (ColumnWidths == null) ColumnWidths = new Dictionary<string, int>();
            if (TreeWidth < 120) TreeWidth = 260;
        }
    }
}
