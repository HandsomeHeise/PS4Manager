using System;
using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Liest die Plugin-Zeilen einer GoldHEN-Konfiguration und schaltet sie um.
    /// Bewusst zeilenweise und ohne Formatannahme: Eine Zeile, die auf eine
    /// vorhandene Plugin-Datei verweist, ist ein Eintrag; ein Kommentarzeichen
    /// davor heißt „aus". Alles, was der Parser nicht erklärt, bleibt liegen.
    /// </summary>
    public static class IniPluginEditor
    {
        private const string CommentMarkers = ";#";

        public static IReadOnlyList<PluginEntry> Parse(string text, IReadOnlyList<string> pluginFileNames)
        {
            var entries = new List<PluginEntry>();
            if (string.IsNullOrEmpty(text)) return entries;

            var names = new HashSet<string>(pluginFileNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var lines = Split(text);
            string section = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Content.Trim();
                if (trimmed.Length == 0) continue;

                bool commented = CommentMarkers.IndexOf(trimmed[0]) >= 0;
                var candidate = commented ? trimmed.Substring(1).Trim() : trimmed;
                if (candidate.Length == 0) continue;
                var candidateSection = SectionName(candidate);
                if (candidateSection != null)
                {
                    section = candidateSection;
                    continue;
                }

                if (!IsSupportedPluginReference(candidate)) continue;
                var name = PluginName(candidate);
                if (!names.Contains(name)) continue;

                entries.Add(new PluginEntry(name, NormalizePath(candidate, name),
                    commented ? GoldHenActivation.Disabled : GoldHenActivation.Enabled, true, i,
                    IsDefaultSection(section) ? null : section));
            }
            return entries;
        }

        /// <summary>
        /// Schaltet einen Plugin-Eintrag um. Beim Schreiben wird auf das
        /// GoldHEN-Format normalisiert: Section plus absoluter Plugin-Pfad.
        /// </summary>
        public static string SetEnabled(string text, PluginEntry entry, bool enabled)
        {
            return SetEnabled(text, entry, enabled, entry != null ? entry.TitleId : null);
        }

        public static string SetEnabled(string text, PluginEntry entry, bool enabled, string titleId)
        {
            return SetEnabled(text, entry, enabled, titleId, true);
        }

        /// <summary>Optional targeted editing without migrating unrelated legacy entries.</summary>
        public static string SetEnabled(string text, PluginEntry entry, bool enabled, string titleId, bool normalizeUnrelated)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var source = text ?? "";
            var lines = Split(source);
            var targetTitleId = NormalizeTitleId(titleId);
            var terminator = source.Length == 0 ? "\n" : DominantTerminator(source);
            var entryText = CanonicalEntryText(entry);

            if (source.Length == 0)
            {
                if (!enabled) return source;
                var section = targetTitleId ?? "default";
                return "[" + section + "]" + terminator + entryText + terminator;
            }

            if (normalizeUnrelated) NormalizePluginLines(lines, terminator);
            var lineIndex = FindEntryLine(lines, entry.Name, targetTitleId);
            if (lineIndex >= 0)
            {
                var line = lines[lineIndex];
                var indent = LeadingWhitespace(line.Content);
                var content = indent + (enabled ? entryText : "; " + entryText);
                if (content == line.Content && Join(lines) == source) return source;
                lines[lineIndex] = new TextLine(content, line.Terminator);
                return Join(lines);
            }

            if (!enabled) return Join(lines) == source ? source : Join(lines);

            return AddToSection(lines, targetTitleId ?? "default", entryText, terminator);
        }

        private static string AddToSection(List<TextLine> lines, string titleId, string entryText, string terminator)
        {
            int section = FindSection(lines, titleId);
            if (section >= 0)
            {
                int insert = section + 1;
                while (insert < lines.Count &&
                       SectionName(lines[insert].Content.Trim()) == null &&
                       lines[insert].Content.Trim().Length != 0)
                    insert++;
                EnsurePreviousLineEnds(lines, insert - 1, terminator);
                lines.Insert(insert, new TextLine(entryText, terminator));
                return Join(lines);
            }

            EnsurePreviousLineEnds(lines, lines.Count - 1, terminator);
            var prefix = lines.Count > 0 && lines[lines.Count - 1].Content.Length > 0 ? terminator : "";
            return Join(lines) + prefix + "[" + titleId + "]" + terminator + entryText + terminator;
        }

        private static void EnsurePreviousLineEnds(List<TextLine> lines, int index, string terminator)
        {
            if (index < 0 || index >= lines.Count) return;
            if (lines[index].Terminator.Length != 0) return;
            lines[index] = new TextLine(lines[index].Content, terminator);
        }

        private static int FindSection(List<TextLine> lines, string titleId)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var section = SectionName(lines[i].Content.Trim());
                if (string.Equals(section, titleId, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static int FindEntryLine(List<TextLine> lines, string pluginName, string titleId)
        {
            string section = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Content.Trim();
                var nextSection = SectionName(trimmed);
                if (nextSection != null)
                {
                    section = nextSection;
                    continue;
                }

                string candidate;
                bool commented;
                if (!TryPluginReference(lines[i].Content, out candidate, out commented)) continue;
                if (!string.Equals(PluginName(candidate), pluginName, StringComparison.OrdinalIgnoreCase)) continue;
                var currentTitleId = IsDefaultSection(section) ? null : section;
                if (string.Equals(currentTitleId, titleId, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static void NormalizePluginLines(List<TextLine> lines, string terminator)
        {
            string section = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Content.Trim();
                var nextSection = SectionName(trimmed);
                if (nextSection != null)
                {
                    section = nextSection;
                    continue;
                }

                string candidate;
                bool commented;
                if (!TryPluginReference(lines[i].Content, out candidate, out commented)) continue;
                if (section == null)
                {
                    lines.Insert(i, new TextLine("[default]", terminator));
                    section = "default";
                    i++;
                }

                var line = lines[i];
                var name = PluginName(candidate);
                var path = NormalizePath(candidate, name);
                lines[i] = new TextLine(LeadingWhitespace(line.Content) + (commented ? "; " : "") + path,
                    line.Terminator);
            }
        }

        private static bool TryPluginReference(string content, out string candidate, out bool commented)
        {
            var body = content.TrimStart();
            commented = false;
            candidate = null;
            if (body.Length == 0) return false;

            commented = CommentMarkers.IndexOf(body[0]) >= 0;
            candidate = commented ? body.Substring(1).Trim() : body.Trim();
            if (candidate.Length == 0) return false;
            if (!IsSupportedPluginReference(candidate)) return false;
            return PluginName(candidate).EndsWith(".prx", StringComparison.OrdinalIgnoreCase);
        }

        private static string CanonicalEntryText(PluginEntry entry)
        {
            var source = string.IsNullOrEmpty(entry.Path) ? entry.Name : entry.Path;
            var normalized = source.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal)) return normalized;
            return "/data/GoldHEN/plugins/" + entry.Name;
        }

        private static string LeadingWhitespace(string text)
        {
            var body = text.TrimStart();
            return text.Substring(0, text.Length - body.Length);
        }

        private static string SectionName(string text)
        {
            if (text.Length < 3 || text[0] != '[' || text[text.Length - 1] != ']') return null;
            var section = text.Substring(1, text.Length - 2).Trim();
            return section.Length == 0 ? null : section;
        }

        private static bool IsDefaultSection(string section)
        {
            return section == null || string.Equals(section, "default", StringComparison.OrdinalIgnoreCase);
        }

        private static string PluginName(string candidate)
        {
            var normalized = candidate.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }

        private static bool IsSupportedPluginReference(string candidate)
        {
            var normalized = candidate.Replace('\\', '/');
            return normalized.IndexOf('/') < 0 ||
                   normalized.StartsWith("/data/GoldHEN/plugins/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string candidate, string name)
        {
            var normalized = candidate.Replace('\\', '/');
            return normalized.IndexOf('/') >= 0 ? normalized : "/data/GoldHEN/plugins/" + name;
        }

        private static string NormalizeTitleId(string titleId)
        {
            if (titleId == null) return null;
            var trimmed = titleId.Trim();
            return trimmed.Length == 0 || IsDefaultSection(trimmed) ? null : trimmed;
        }

        private static string DominantTerminator(string text)
        {
            return text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        }
        /// <summary>Zerlegt den Text in Zeilen samt ihrer Zeilenenden — die bleiben so erhalten, wie sie waren.</summary>
        internal static List<TextLine> Split(string text)
        {
            var lines = new List<TextLine>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                int contentEnd = i > start && text[i - 1] == '\r' ? i - 1 : i;
                lines.Add(new TextLine(text.Substring(start, contentEnd - start),
                    text.Substring(contentEnd, i + 1 - contentEnd)));
                start = i + 1;
            }
            if (start < text.Length || lines.Count == 0) lines.Add(new TextLine(text.Substring(start), ""));
            return lines;
        }

        internal static string Join(IEnumerable<TextLine> lines)
        {
            return string.Concat(lines.Select(l => l.Content + l.Terminator));
        }

        internal sealed class TextLine
        {
            public TextLine(string content, string terminator)
            {
                Content = content;
                Terminator = terminator;
            }

            public string Content { get; }
            public string Terminator { get; }
        }
    }
}
