namespace Ps4.Core.Titles
{
    /// <summary>
    /// Wo die Konsole Name und Icon eines Titels ablegt — von beiden Apps
    /// gebraucht, darum nicht mehr in den Medienpfaden der Gallery.
    /// Verifiziert mit der Gallery am 2026-08-01.
    /// </summary>
    public static class TitlePaths
    {
        public static string SfoPath(string titleId)
        {
            return "/system_data/priv/appmeta/" + titleId + "/param.sfo";
        }

        public static string IconPath(string titleId)
        {
            return "/user/appmeta/" + titleId + "/icon0.png";
        }
    }
}
