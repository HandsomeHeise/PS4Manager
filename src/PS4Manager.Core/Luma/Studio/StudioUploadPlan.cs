namespace PS4Manager.Core.Luma.Studio
{
    public sealed class StudioUploadPlan
    {
        internal StudioUploadPlan(long generation, string profile, LumaEndpoint endpoint, string fileName)
        {
            Generation = generation; FileName = fileName;
            RelativePath = "scripts/" + fileName;
            string game = profile == "gta5" ? "GTA V" : "Red Dead Redemption 2";
            string expected = profile == "gta5" ? "/data/gtalua/" : "/data/rdr2lua/";
            ConfirmationText = "Console: " + endpoint.Host + ":" + endpoint.Port + "\r\nSelected game: " + game +
                "\r\nRemote path: " + RelativePath + "\r\nExpected sandbox (not verified): " + expected +
                "\r\n\r\nThis Luma protocol does not report its game identity. Confirm that the correct game is running. " +
                "The connected plugin resolves this relative path inside its own game sandbox.\r\n\r\n" +
                "An existing file will be REPLACED. Scripts will NOT be executed or reloaded. Use Reload separately when ready.\r\n\r\nUpload this buffer?";
        }
        public long Generation { get; }
        public string FileName { get; }
        public string RelativePath { get; }
        public string ConfirmationText { get; }
    }
}
