using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PS4Manager.Core.Luma.Studio
{
    public sealed class LuaCompletion
    {
        internal LuaCompletion(int start, IEnumerable<LuaApiSymbol> items) { Start = start; Items = items.ToArray(); }
        public int Start { get; }
        public IReadOnlyList<LuaApiSymbol> Items { get; }
    }
    public sealed class LuaSignatureHelp
    {
        internal LuaSignatureHelp(LuaApiSymbol symbol, int parameter, int start) { Symbol = symbol; Parameter = parameter; Start = start; }
        public LuaApiSymbol Symbol { get; }
        public int Parameter { get; }
        public int Start { get; }
    }
    // Deliberately lexical assistance, not a Lua type/scope checker. Ambiguous calls
    // are ignored; hints never change the compiler's decision to accept valid Lua.
    public static class LuaLanguageService
    {
        private sealed class Token
        {
            public string Text, Kind;
            public int Start, End, Line;
            public bool Closed;
        }
        private sealed class CallFrame { public string Kind; public LuaApiSymbol Symbol; public int Parameter, Start; }
        private static readonly string[] Keywords = "and break do else elseif end false for function goto if in local nil not or repeat return then true until while".Split(' ');
        public static LuaCompletion Complete(string source, int caret, LuaApiCatalog catalog)
        {
            source = source ?? ""; caret = Math.Max(0, Math.Min(caret, source.Length));
            if (source.Length > LuaSyntaxChecker.MaxSourceBytes) return new LuaCompletion(caret, Array.Empty<LuaApiSymbol>());
            var tokens = Lex(source);
            if (InText(tokens, caret) || tokens.Count >= 200000 && tokens[tokens.Count - 1].End < caret) return new LuaCompletion(caret, Array.Empty<LuaApiSymbol>());
            int start = caret;
            while (start > 0 && (IsName(source[start - 1]) || source[start - 1] == '.' || source[start - 1] == ':')) start--;
            string prefix = source.Substring(start, caret - start);
            var local = tokens.Where(t => t.Kind == "id" && !Keywords.Contains(t.Text) && !(t.Start == start && t.End == caret)).Select(t => t.Text)
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal)
                .Select(n => new LuaApiSymbol(n, n, "Identifier in this Lua document."));
            var words = Keywords.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(k => new LuaApiSymbol(k, k, "Lua 5.4 keyword."));
            return new LuaCompletion(start, catalog.Complete(prefix).Concat(local).Concat(words).GroupBy(s => s.Name, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Take(150));
        }
        public static LuaSignatureHelp SignatureAt(string source, int caret, LuaApiCatalog catalog)
        {
            source = source ?? ""; caret = Math.Max(0, Math.Min(caret, source.Length));
            if (source.Length > LuaSyntaxChecker.MaxSourceBytes) return null;
            var all = Lex(source); if (InText(all, caret) || all.Count >= 200000 && all[all.Count - 1].End < caret) return null;
            var tokens = all.Where(t => t.End <= caret && t.Kind != "comment").ToArray();
            var stack = new Stack<CallFrame>();
            for (int i = 0; i < tokens.Length; i++) {
                var token = tokens[i]; if (token.Kind == "string") continue;
                if (token.Text == "(" || token.Text == "[" || token.Text == "{") {
                    string name = CallName(tokens, i - 1, out int start);
                    stack.Push(new CallFrame { Kind = token.Text, Symbol = token.Text == "(" && (i < 2 || tokens[i - 2].Text != "function") ? catalog.Find(name) : null, Start = start });
                } else if (token.Text == ")" || token.Text == "]" || token.Text == "}") {
                    if (stack.Count > 0) stack.Pop();
                } else if (token.Text == "," && stack.Count > 0) stack.Peek().Parameter++;
            }
            foreach (var frame in stack) if (frame.Symbol != null) return new LuaSignatureHelp(frame.Symbol, frame.Parameter, frame.Start);
            return null;
        }
        public static IReadOnlyList<LuaDiagnostic> Analyze(string source, string fileName, LuaApiCatalog catalog)
        {
            source = source ?? ""; if (source.Length > LuaSyntaxChecker.MaxSourceBytes) return Array.Empty<LuaDiagnostic>();
            var tokens = Lex(source).Where(t => t.Kind != "comment").ToArray();
            var bound = new HashSet<string>(StringComparer.Ordinal); int bindingBudget = 400000;
            // Conservative whole-document suppression: custom/dynamic bindings and
            // parameters may shadow a native. Missing a hint is safer than rejecting them.
            for (int i = 0; i < tokens.Length; i++) {
                if (tokens[i].Kind != "id") continue;
                if ((i + 1 < tokens.Length && tokens[i + 1].Text == "=") || (i > 0 && (tokens[i - 1].Text == "local" || tokens[i - 1].Text == "function"))) bound.Add(tokens[i].Text);
                // Parallel assignment / local declarations bind every name on the
                // left, not only the identifier immediately before '='.
                if (!bound.Contains(tokens[i].Text) && i + 1 < tokens.Length && tokens[i + 1].Text == ",") {
                    int last = i;
                    while (last + 2 < tokens.Length && tokens[last + 1].Text == "," && tokens[last + 2].Kind == "id") {
                        if (--bindingBudget < 0) return Array.Empty<LuaDiagnostic>();
                        last += 2;
                    }
                    if (last + 1 < tokens.Length && tokens[last + 1].Text == "=") for (int p = i; p <= last; p += 2) bound.Add(tokens[p].Text);
                }
                if (tokens[i].Text == "function") {
                    int open = i + 1; while (open < tokens.Length && (tokens[open].Kind == "id" || tokens[open].Text == "." || tokens[open].Text == ":")) open++;
                    if (open < tokens.Length && tokens[open].Text == "(") for (int p = open + 1; p < tokens.Length && tokens[p].Text != ")"; p++) if (tokens[p].Kind == "id") bound.Add(tokens[p].Text);
                }
            }
            var warnings = new List<LuaDiagnostic>(); int analysisBudget = 400000;
            for (int i = 0; i + 1 < tokens.Length && warnings.Count < 100; i++) {
                var token = tokens[i];
                if (token.Kind != "id" || tokens[i + 1].Text != "(" || bound.Contains(token.Text) ||
                    i > 0 && (tokens[i - 1].Text == "." || tokens[i - 1].Text == ":" || tokens[i - 1].Text == "function")) continue;
                var symbol = catalog.Find(token.Text);
                if (symbol == null) {
                    var canonical = catalog.FindIgnoringCase(token.Text);
                    string message = canonical != null ? "Lua names are case-sensitive. Did you mean " + canonical.Name + "?" :
                        Regex.IsMatch(token.Text, @"\A_?[A-Z][A-Z0-9_]*_[A-Z0-9_]+\z") ? token.Text + " is not in this game's Luma native catalog. A custom/dynamic binding may still be valid." : null;
                    if (message != null) warnings.Add(Warning(fileName, token, message));
                } else if (Regex.IsMatch(symbol.Name, @"\A[A-Z_][A-Z0-9_]*\z") && !symbol.Signature.Contains("...")) {
                    int expected = symbol.Signature.Substring(symbol.Signature.IndexOf('(') + 1).TrimEnd(')').Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    int? actual = CountSimpleArguments(tokens, i + 1, ref analysisBudget);
                    if (actual.HasValue && actual.Value != expected) warnings.Add(Warning(fileName, token, symbol.Signature + " expects " + expected + " argument(s); this call has " + actual.Value + ". Verify dynamic native signatures."));
                }
            }
            return warnings;
        }
        private static LuaDiagnostic Warning(string fileName, Token token, string message) => new LuaDiagnostic(fileName, token.Line, message, LuaDiagnosticSeverity.Warning, token.Start, token.End - token.Start);
        private static int? CountSimpleArguments(Token[] tokens, int open, ref int budget)
        {
            int depth = 0, commas = 0; bool hasValue = false;
            for (int i = open + 1; i < tokens.Length; i++) {
                if (--budget < 0) return null;
                string text = tokens[i].Text;
                if (text == "function" || text == "...") return null;
                if (tokens[i].Kind == "string") { hasValue = true; continue; }
                if (text == ")" && depth == 0) {
                    // Last function/parenthesized expression can expand multiple values.
                    if (i > open + 1 && tokens[i - 1].Text == ")") return null;
                    // Lua also allows calls without parentheses: values {},
                    // values 'text', and values [[text]] can expand many returns.
                    if (i > open + 2 && tokens[i - 1].Kind == "string" && CanEndCallee(tokens[i - 2])) return null;
                    if (i > open + 1 && tokens[i - 1].Text == "}") {
                        int braceDepth = 0;
                        for (int p = i - 1; p > open; p--) {
                            if (--budget < 0) return null;
                            if (tokens[p].Kind == "string") continue;
                            if (tokens[p].Text == "}") braceDepth++;
                            if (tokens[p].Text == "{" && --braceDepth == 0) {
                                if (p > open + 1 && CanEndCallee(tokens[p - 1])) return null;
                                break;
                            }
                        }
                    }
                    return hasValue ? commas + 1 : 0;
                }
                if (text == "(" || text == "{" || text == "[") depth++;
                if (text == ")" || text == "}" || text == "]") depth--;
                if (depth == 0 && text == ",") commas++; else hasValue = true;
            }
            return null;
        }
        private static bool CanEndCallee(Token token) => token.Kind == "id" || token.Text == ")" || token.Text == "]" || token.Text == "}";
        private static string CallName(Token[] tokens, int end, out int start)
        {
            start = end >= 0 ? tokens[end].Start : 0;
            if (end < 0 || tokens[end].Kind != "id") return null;
            int first = end;
            while (first >= 2 && (tokens[first - 1].Text == "." || tokens[first - 1].Text == ":") && tokens[first - 2].Kind == "id") first -= 2;
            start = tokens[first].Start;
            if (first == end) return tokens[end].Text;
            var name = new System.Text.StringBuilder(); for (int i = first; i <= end; i++) name.Append(tokens[i].Text);
            return name.ToString();
        }
        private static bool InText(List<Token> tokens, int caret) => tokens.Any(t => (t.Kind == "comment" || t.Kind == "string") && caret > t.Start && (caret < t.End || caret == t.End && !t.Closed));
        private static bool IsName(char c) => char.IsLetterOrDigit(c) || c == '_';
        private static List<Token> Lex(string source)
        {
            var tokens = new List<Token>(); int position = 0, line = 1;
            while (position < source.Length && tokens.Count < 200000) {
                if (char.IsWhiteSpace(source[position])) { if (source[position++] == '\n') line++; continue; }
                int start = position, startLine = line; string kind = "punct"; bool closed = true;
                bool comment = position + 1 < source.Length && source[position] == '-' && source[position + 1] == '-';
                if (comment) { kind = "comment"; position += 2; }
                if (position < source.Length && source[position] == '[' && LongBracket(source, position, out string end, out int length)) {
                    kind = comment ? "comment" : "string"; int finish = source.IndexOf(end, position + length, StringComparison.Ordinal);
                    closed = finish >= 0; position = finish < 0 ? source.Length : finish + end.Length;
                } else if (comment) {
                    while (position < source.Length && source[position] != '\n') position++; closed = false;
                } else if (source[position] == '\'' || source[position] == '"') {
                    kind = "string"; char quote = source[position++]; closed = false;
                    while (position < source.Length) { char c = source[position++]; if (c == '\\' && position < source.Length) position++; else if (c == quote) { closed = true; break; } }
                } else if (char.IsLetter(source[position]) || source[position] == '_') {
                    kind = "id"; while (position < source.Length && IsName(source[position])) position++;
                } else if (char.IsDigit(source[position])) {
                    kind = "number"; while (position < source.Length && (IsName(source[position]) || source[position] == '.')) position++;
                } else if (position + 2 < source.Length && source[position] == '.' && source[position + 1] == '.' && source[position + 2] == '.') position += 3;
                else position++;
                string text = source.Substring(start, position - start); line += text.Count(c => c == '\n');
                tokens.Add(new Token { Text = text, Kind = kind, Start = start, End = position, Line = startLine, Closed = closed });
            }
            return tokens;
        }
        private static bool LongBracket(string source, int position, out string end, out int length)
        {
            end = null; length = 0; if (position >= source.Length || source[position] != '[') return false;
            int next = position + 1; while (next < source.Length && source[next] == '=') next++;
            if (next >= source.Length || source[next] != '[') return false;
            end = "]" + new string('=', next - position - 1) + "]"; length = next - position + 1; return true;
        }
    }
}
