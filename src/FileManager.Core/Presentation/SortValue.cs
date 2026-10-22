using System;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Zellwert fürs Grid: zeigt Text, sortiert nach Schlüssel (Spec „Sortierung
    /// per Spaltenkopf mit Rohwerten"). Das Grid zeichnet ToString() und
    /// vergleicht über Comparer.Default — also über CompareTo.
    /// </summary>
    public sealed class SortValue : IComparable
    {
        public SortValue(string text, IComparable key)
        {
            Text = text ?? "";
            Key = key;
        }

        public string Text { get; }
        public IComparable Key { get; }

        public int CompareTo(object obj)
        {
            var other = obj as SortValue;
            if (other == null) return 1;
            if (Key == null) return other.Key == null ? 0 : -1;
            if (other.Key == null) return 1;
            return Key.CompareTo(other.Key);
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
