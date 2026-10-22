using System.Runtime.InteropServices.ComTypes;

namespace FileManager.Core.Shell
{
    internal sealed class FormatEnumerator : IEnumFORMATETC
    {
        private readonly FORMATETC[] _formats;
        private int _index;

        public FormatEnumerator(FORMATETC[] formats)
        {
            _formats = formats;
        }

        public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
        {
            int n = 0;
            while (n < celt && _index < _formats.Length) rgelt[n++] = _formats[_index++];
            if (pceltFetched != null && pceltFetched.Length > 0) pceltFetched[0] = n;
            return n == celt ? 0 : 1;                                   // S_OK / S_FALSE
        }

        public int Skip(int celt)
        {
            _index += celt;
            return _index <= _formats.Length ? 0 : 1;
        }

        public int Reset()
        {
            _index = 0;
            return 0;
        }

        public void Clone(out IEnumFORMATETC newEnum)
        {
            newEnum = new FormatEnumerator(_formats) { _index = _index };
        }
    }
}
