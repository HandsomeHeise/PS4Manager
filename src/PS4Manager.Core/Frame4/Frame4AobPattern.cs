using System;
using System.Collections.Generic;
using System.Globalization;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4AobPattern
    {
        private const int MaxPatternLength = 4096;

        private Frame4AobPattern(byte[] bytes, byte[] mask)
        {
            Bytes = bytes;
            Mask = mask;
        }

        public byte[] Bytes { get; private set; }
        public byte[] Mask { get; private set; }
        public int Length { get { return Bytes.Length; } }

        public static Frame4AobPattern Parse(string text)
        {
            var tokens = (text ?? "").Split(new[] { ' ', '\t', '\r', '\n', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) throw new ArgumentException("AOB pattern cannot be empty.", nameof(text));
            if (tokens.Length > MaxPatternLength)
                throw new ArgumentException("AOB pattern cannot be longer than " + MaxPatternLength + " bytes.", nameof(text));

            var bytes = new List<byte>(tokens.Length);
            var mask = new List<byte>(tokens.Length);
            foreach (var token in tokens)
            {
                if (token == "?" || token == "??")
                {
                    bytes.Add(0);
                    mask.Add(0);
                    continue;
                }

                if (token.Length != 2)
                    throw new ArgumentException("Invalid AOB token '" + token + "'. Use two hex digits or ?.", nameof(text));

                byte value;
                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                    throw new ArgumentException("Invalid AOB token '" + token + "'. Use two hex digits or ?.", nameof(text));

                bytes.Add(value);
                mask.Add(1);
            }

            return new Frame4AobPattern(bytes.ToArray(), mask.ToArray());
        }
    }
}
