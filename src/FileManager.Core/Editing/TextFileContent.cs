using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FileManager.Core.Editing
{
    /// <summary>Text einer bearbeitbaren Datei samt ursprünglicher Byte-Codierung und BOM.</summary>
    public sealed class TextFileContent
    {
        private readonly Encoding _encoding;
        private readonly byte[] _preamble;

        private TextFileContent(string text, Encoding encoding, byte[] preamble)
        {
            Text = text;
            _encoding = encoding;
            _preamble = preamble;
        }

        public string Text { get; }

        public static TextFileContent Load(string path)
        {
            return FromBytes(File.ReadAllBytes(path));
        }

        /// <summary>Wie <see cref="Load"/>, aber aus Bytes im Speicher — für gelesene Konsolendateien.</summary>
        public static TextFileContent FromBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            Encoding encoding;
            int preambleLength;
            Detect(bytes, out encoding, out preambleLength);
            var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new TextFileContent(text, encoding, bytes.Take(preambleLength).ToArray());
        }

        public void Save(string path, string text)
        {
            File.WriteAllBytes(path, ToBytes(text));
        }

        /// <summary>Genau die Bytes, die <see cref="Save"/> schreiben würde — für Sicherungen und Vergleiche.</summary>
        public byte[] ToBytes(string text)
        {
            var body = _encoding.GetBytes(text ?? "");
            var output = new byte[_preamble.Length + body.Length];
            Buffer.BlockCopy(_preamble, 0, output, 0, _preamble.Length);
            Buffer.BlockCopy(body, 0, output, _preamble.Length, body.Length);
            return output;
        }

        private static void Detect(byte[] bytes, out Encoding encoding, out int preambleLength)
        {
            if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            {
                encoding = new UTF32Encoding(true, false, true); preambleLength = 4; return;
            }
            if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            {
                encoding = new UTF32Encoding(false, false, true); preambleLength = 4; return;
            }
            if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            {
                encoding = new UTF8Encoding(false, true); preambleLength = 3; return;
            }
            if (StartsWith(bytes, 0xFE, 0xFF))
            {
                encoding = new UnicodeEncoding(true, false, true); preambleLength = 2; return;
            }
            if (StartsWith(bytes, 0xFF, 0xFE))
            {
                encoding = new UnicodeEncoding(false, false, true); preambleLength = 2; return;
            }

            try
            {
                var utf8 = new UTF8Encoding(false, true);
                utf8.GetString(bytes);
                encoding = utf8;
            }
            catch (DecoderFallbackException)
            {
                encoding = (Encoding)Encoding.Default.Clone();
                encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
                encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            }
            preambleLength = 0;
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (bytes[i] != prefix[i]) return false;
            return true;
        }
    }
}
