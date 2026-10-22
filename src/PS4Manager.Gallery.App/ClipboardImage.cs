using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PS4Manager.Gallery.App
{
    /// <summary>
    /// Legt ein Bild so in die Zwischenablage, dass gängige Zielprogramme es
    /// auch finden.
    ///
    /// Warum nicht einfach Clipboard.SetImage: das veröffentlicht im .NET
    /// Framework AUSSCHLIESSLICH CF_BITMAP. Paint kommt damit zurecht, Office,
    /// Browser, Chat-Programme und die meisten Bildeditoren suchen aber nach
    /// CF_DIB oder PNG — sie finden dann nichts, und für den Anwender sieht es
    /// aus, als hätte das Kopieren stillschweigend nicht funktioniert
    /// (gemessen am 2026-08-15: SetImage liefert nur "System.Drawing.Bitmap,
    /// Bitmap", diese Klasse liefert zusätzlich "DeviceIndependentBitmap, PNG").
    /// </summary>
    internal static class ClipboardImage
    {
        /// <summary>
        /// Übernimmt die Bytes eines JPEGs in die Zwischenablage, in drei
        /// Formaten gleichzeitig. Wirft weiter wie Clipboard.SetImage, wenn die
        /// Zwischenablage gerade von einem anderen Programm blockiert wird —
        /// der Aufrufer meldet das in der Statuszeile.
        /// </summary>
        public static void Copy(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("Keine Bilddaten.", nameof(imageBytes));

            using (var stream = new MemoryStream(imageBytes))
            using (var decoded = Image.FromStream(stream))
            using (var bitmap = new Bitmap(decoded))
            {
                var data = new DataObject();

                // CF_BITMAP — was SetImage bisher allein geliefert hat.
                data.SetData(DataFormats.Bitmap, true, bitmap);

                using (var opaque = ToOpaque(bitmap))
                {
                    data.SetData(DataFormats.Dib, false, ToDib(opaque));
                    data.SetData("PNG", false, ToPng(opaque));
                }

                // copy: true erzwingt das sofortige Rendern aller Formate
                // (OleFlushClipboard). Erst dadurch überlebt der Inhalt das
                // Dispose der Bitmap unten und das Beenden der Anwendung.
                Clipboard.SetDataObject(data, true);
            }
        }

        /// <summary>
        /// Kopie ohne Alphakanal. new Bitmap(Image) liefert 32 bpp mit
        /// Alpha-Bytes; als 32-Bit-DIB eingefügt zeigen manche Programme das
        /// Bild dann komplett schwarz. Screenshots sind JPEGs und haben
        /// ohnehin nie Transparenz, also ist 24 bpp die verlustfreie Wahl.
        /// </summary>
        private static Bitmap ToOpaque(Bitmap source)
        {
            var opaque = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(opaque))
            {
                g.DrawImageUnscaled(source, 0, 0);
            }
            return opaque;
        }

        /// <summary>
        /// Ein DIB ist eine BMP-Datei ohne die vorderen 14 Byte
        /// (BITMAPFILEHEADER) — die gehören zum Dateiformat, nicht zum
        /// Zwischenablage-Format.
        /// </summary>
        private static MemoryStream ToDib(Bitmap bitmap)
        {
            using (var bmp = new MemoryStream())
            {
                bitmap.Save(bmp, ImageFormat.Bmp);
                var all = bmp.ToArray();

                var dib = new MemoryStream();
                dib.Write(all, 14, all.Length - 14);
                dib.Position = 0;
                return dib;
            }
        }

        private static MemoryStream ToPng(Bitmap bitmap)
        {
            var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            png.Position = 0;
            return png;
        }
    }
}
