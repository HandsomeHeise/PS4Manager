using System;
using System.Text.RegularExpressions;

namespace PS4Manager.Core.Luma.Studio
{
    public sealed class StudioExecution
    {
        private readonly long version, generation;
        public StudioExecution(StudioDocument document, long generation)
        {
            DocumentId = document.Id; version = document.Version; this.generation = generation;
            ChunkName = "@studio-" + Guid.NewGuid().ToString("N").Substring(0, 16) + ".lua";
        }
        public Guid DocumentId { get; }
        public string ChunkName { get; }
        public LuaDiagnostic Diagnostic(string message, StudioDocument document, long currentGeneration)
        {
            if (document.Id != DocumentId || document.Version != version || generation != currentGeneration) return null;
            var match = Regex.Match(message ?? "", Regex.Escape(ChunkName.Substring(1)) + @":(\d+):\s*([^\r\n]+)");
            int line;
            return match.Success && int.TryParse(match.Groups[1].Value, out line)
                ? new LuaDiagnostic(document.FilePath ?? document.Title, line, "Runtime: " + match.Groups[2].Value) : null;
        }
    }
}
