using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PS4Manager.Core.Luma.Studio
{
    public sealed class LuaApiSymbol
    {
        public LuaApiSymbol(string name, string signature, string documentation) { Name = name; Signature = signature; Documentation = documentation; }
        public string Name { get; }
        public string Signature { get; }
        public string Documentation { get; }
        public override string ToString() => Signature;
    }
    public sealed class LuaApiCatalog
    {
        private static readonly Lazy<LuaApiCatalog> Gta = new Lazy<LuaApiCatalog>(() => Load("gta5"));
        private static readonly Lazy<LuaApiCatalog> Rdr = new Lazy<LuaApiCatalog>(() => Load("rdr2"));
        private readonly Dictionary<string, LuaApiSymbol> symbols;
        private LuaApiCatalog(Dictionary<string, LuaApiSymbol> symbols) { this.symbols = symbols; }
        public static LuaApiCatalog ForProfile(string profile) => profile == "gta5" ? Gta.Value : profile == "rdr2" ? Rdr.Value : throw new ArgumentException("Unknown game profile.", nameof(profile));
        public LuaApiSymbol Find(string name) => name != null && symbols.TryGetValue(name, out var symbol) ? symbol : null;
        public IReadOnlyList<LuaApiSymbol> Complete(string prefix, int limit = 150) => symbols.Values.Where(s => s.Name.StartsWith(prefix ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
        private static LuaApiCatalog Load(string profile) {
            var symbols = new Dictionary<string, LuaApiSymbol>(StringComparer.Ordinal);
            var assembly = typeof(LuaApiCatalog).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.Contains(".Definitions." + profile + ".") && n.EndsWith(".lua"))) {
                using (var stream = assembly.GetManifestResourceStream(resource)) using (var reader = new StreamReader(stream)) {
                    string line; var docs = new List<string>();
                    while ((line = reader.ReadLine()) != null) {
                        if (line.StartsWith("---")) { docs.Add(line.Substring(3).TrimStart('@', ' ')); continue; }
                        var match = Regex.Match(line, @"^function\s+([\w.:]+)\s*\(([^)]*)\)");
                        if (match.Success) { string name = match.Groups[1].Value; symbols[name] = new LuaApiSymbol(name, name + "(" + match.Groups[2].Value + ")", string.Join(Environment.NewLine, docs)); }
                        docs.Clear();
                    }
                }
            }
            foreach (var signature in new[] { "assert(value, message)", "error(message, level)", "pcall(function, ...)", "xpcall(function, handler, ...)", "pairs(table)", "ipairs(table)", "next(table, key)", "type(value)", "tostring(value)", "tonumber(value, base)", "select(index, ...)", "require(module)", "math.sin(value)", "math.floor(value)", "string.format(format, ...)", "string.sub(text, first, last)", "table.insert(table, value)", "table.remove(table, index)" }) {
                string name = signature.Substring(0, signature.IndexOf('(')); if (!symbols.ContainsKey(name)) symbols[name] = new LuaApiSymbol(name, signature, "Lua 5.4 standard library function.");
            }
            return new LuaApiCatalog(symbols);
        }
    }
    public sealed class StudioTemplate
    {
        public StudioTemplate(string name, string fileName, string source) { Name = name; FileName = fileName; Source = source; }
        public string Name { get; }
        public string FileName { get; }
        public string Source { get; }
        public override string ToString() => Name;
    }
    public static class StudioTemplates
    {
        public static IReadOnlyList<StudioTemplate> ForProfile(string profile) {
            LuaApiCatalog.ForProfile(profile);
            string ped = profile == "gta5" ? "GET_PLAYER_PED(-1)" : "PLAYER_PED_ID()";
            return new[] {
                new StudioTemplate("Hello / logging", "hello.lua", "-- Luma · Lua 5.4\nlocal greeting <const> = 'Hello from Luma Studio'\nprint(greeting)\n"),
                new StudioTemplate("Frame callback", "tick.lua", "-- Register every reload; Luma clears tick callbacks before reloading.\nlocal frames = 0\non_tick(function()\n    frames = frames + 1\n    if frames % 300 == 0 then\n        log('Luma tick: ' .. frames)\n    end\nend)\n"),
                new StudioTemplate("Player native example", "player.lua", "-- " + (profile == "gta5" ? "GTA V CUSA00411 / 1.57" : "RDR2 CUSA03041 / 1.32") + "\nif not natives_ready() then\n    log('Native registry is not ready')\n    return\nend\nlocal ped = " + ped + "\nlog('Player ped: ' .. tostring(ped))\n")
            };
        }
    }
}
