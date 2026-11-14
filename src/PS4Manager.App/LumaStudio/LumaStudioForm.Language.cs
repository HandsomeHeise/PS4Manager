using System;
using PS4Manager.Core.Luma.Studio;
using ScintillaNET;

namespace PS4Manager.App
{
    internal sealed partial class LumaStudioForm
    {
        private static void ConfigureDiagnosticIndicators(Scintilla editor)
        {
            foreach (int id in new[] { 8, 9 }) {
                editor.Indicators[id].Style = IndicatorStyle.Squiggle;
                editor.Indicators[id].ForeColor = id == 8 ? StudioColors.Error : StudioColors.Number;
            }
        }
        private static void ClearDiagnosticIndicators(Scintilla editor)
        {
            foreach (int id in new[] { 8, 9 }) { editor.IndicatorCurrent = id; editor.IndicatorClearRange(0, editor.TextLength); }
        }
        private void PaintDiagnosticIndicators(Buffer buffer)
        {
            var editor = buffer.Editor; ClearDiagnosticIndicators(editor);
            if (buffer.Check == null || buffer.CheckedVersion != buffer.Document.Version) return;
            foreach (var diagnostic in buffer.Check.Diagnostics) {
                int line = Math.Min(editor.Lines.Count - 1, Math.Max(0, diagnostic.Line - 1));
                // Scintilla.NET's wrapper takes UTF-16 character offsets and maps
                // them to native UTF-8 byte positions (including surrogate pairs).
                int start = diagnostic.Start < 0 ? editor.Lines[line].Position : Math.Min(diagnostic.Start, editor.TextLength);
                int length = diagnostic.Start < 0 ? Math.Max(1, editor.Lines[line].EndPosition - start) : diagnostic.Length;
                length = Math.Min(length, editor.TextLength - start);
                if (length <= 0) continue;
                editor.IndicatorCurrent = diagnostic.Severity == LuaDiagnosticSeverity.Warning ? 9 : 8;
                editor.IndicatorValue = 1; editor.IndicatorFillRange(start, length);
            }
        }
        private void UpdateSignature(Buffer buffer)
        {
            if (buffer.AssistanceVersion == buffer.Document.Version && buffer.AssistanceCaret == buffer.Editor.CurrentPosition && buffer.AssistanceProfile == Profile) return;
            buffer.AssistanceVersion = buffer.Document.Version; buffer.AssistanceCaret = buffer.Editor.CurrentPosition; buffer.AssistanceProfile = Profile;
            if (!buffer.Editor.AutoCActive) ShowCallTip(buffer.Editor);
        }
    }
}
