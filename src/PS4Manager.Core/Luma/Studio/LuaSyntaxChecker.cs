using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PS4Manager.Core.Luma.Studio
{
    public sealed class LuaDiagnostic
    {
        public LuaDiagnostic(string filePath, int line, string message) { FilePath = filePath; Line = Math.Max(1, line); Message = message; }
        public string FilePath { get; }
        public int Line { get; }
        public string Message { get; }
        public override string ToString() => FilePath + ":" + Line + ": " + Message;
    }
    public sealed class LuaCheckResult
    {
        public LuaCheckResult(bool available, IEnumerable<LuaDiagnostic> diagnostics) { Available = available; Diagnostics = diagnostics.ToArray(); }
        public bool Available { get; }
        public bool Success => Available && Diagnostics.Count == 0;
        public IReadOnlyList<LuaDiagnostic> Diagnostics { get; }
    }
    public interface ILuaSyntaxChecker { LuaCheckResult Check(string source, string fileName); }
    public sealed class LuaSyntaxChecker : ILuaSyntaxChecker
    {
        public const int MaxSourceBytes = 1024 * 1024;
        public LuaCheckResult Check(string source, string fileName)
        {
            source = source ?? ""; fileName = fileName ?? "untitled.lua";
            Func<string, LuaCheckResult> failure = message => new LuaCheckResult(true, new[] { new LuaDiagnostic(fileName, 1, message) });
            if (source.IndexOf('\0') >= 0 || (source.Length != 0 && source[0] == '\u001b')) return failure("Only Lua source text is accepted, not binary chunks or NUL characters.");
            var bytes = Encoding.UTF8.GetBytes(source);
            if (bytes.Length > MaxSourceBytes) return failure("Script exceeds the 1 MiB limit.");
            var messageBuffer = new byte[8192]; var chunk = Encoding.UTF8.GetBytes("@" + fileName.Replace('\0', '_').Replace('\r', '_').Replace('\n', '_') + "\0");
            try {
                int version = Environment.Is64BitProcess ? Native64.Version() : Native32.Version();
                if (version != 50407) return new LuaCheckResult(false, new[] { new LuaDiagnostic(fileName, 1, "Lua 5.4.7 syntax runtime is required.") });
                int code = Environment.Is64BitProcess ? Native64.Check(bytes, (UIntPtr)bytes.Length, chunk, messageBuffer, (UIntPtr)messageBuffer.Length) : Native32.Check(bytes, (UIntPtr)bytes.Length, chunk, messageBuffer, (UIntPtr)messageBuffer.Length);
                if (code == 0) return new LuaCheckResult(true, new LuaDiagnostic[0]);
                int end = Array.IndexOf(messageBuffer, (byte)0); string message = Encoding.UTF8.GetString(messageBuffer, 0, end < 0 ? messageBuffer.Length : end);
                var match = Regex.Match(message, @":(?<line>\d+):\s*(?<message>.*)$");
                return new LuaCheckResult(true, new[] { new LuaDiagnostic(fileName, match.Success && int.TryParse(match.Groups["line"].Value, out var line) ? line : 1, match.Success ? match.Groups["message"].Value : string.IsNullOrEmpty(message) ? "Lua parser could not compile this script (" + code + ")." : message) });
            } catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is EntryPointNotFoundException) {
                return new LuaCheckResult(false, new[] { new LuaDiagnostic(fileName, 1, "Lua syntax runtime unavailable. Build scripts/build-lua.ps1 or reinstall the complete ZIP.") });
            }
        }
        private static class Native64 {
            [DllImport("lua54-x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "luma_lua_version")] internal static extern int Version();
            [DllImport("lua54-x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "luma_lua_check")] internal static extern int Check(byte[] source, UIntPtr length, byte[] name, byte[] message, UIntPtr capacity);
        }
        private static class Native32 {
            [DllImport("lua54-x86.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "luma_lua_version")] internal static extern int Version();
            [DllImport("lua54-x86.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "luma_lua_check")] internal static extern int Check(byte[] source, UIntPtr length, byte[] name, byte[] message, UIntPtr capacity);
        }
    }
}
