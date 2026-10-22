namespace PS4Manager.Gallery.Core.Ps4
{
    /// <summary>
    /// Foto oder Videoclip. Die PS4 legt beide strukturgleich ab (gleiche
    /// Hash-Ordner, gleiche Namensregel, gleiche .dat, gleiche Thumbnail-Regel;
    /// verifiziert 2026-08-15) — darum eine Sorte an EINEM Typ statt zweier
    /// Modelle, die dasselbe beschreiben.
    /// </summary>
    public enum MediaKind
    {
        Photo,
        Video
    }
}
