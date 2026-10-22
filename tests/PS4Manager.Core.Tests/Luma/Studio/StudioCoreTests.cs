using System;
using System.IO;
using System.Linq;
using System.Text;
using PS4Manager.Core.Luma.Studio;
using Xunit;

namespace PS4Manager.Core.Tests.Luma.Studio
{
    public sealed class StudioCoreTests
    {
        [Fact]
        public void Project_scan_has_an_entry_budget_even_for_large_trees()
        {
            string root = Path.Combine(Path.GetTempPath(), "luma-budget-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                File.WriteAllText(Path.Combine(root, "a.lua"), "");
                File.WriteAllText(Path.Combine(root, "b.lua"), "");
                Assert.Single(StudioProject.Files(root, 1));
            } finally { Directory.Delete(root, true); }
        }
        [Fact] public void Checks_real_Lua_54_without_running_the_script()
        {
            var result = new LuaSyntaxChecker().Check("local x <const> = 1\nos.exit(123)\nreturn x // 2", "check.lua");
            Assert.True(result.Available); Assert.True(result.Success); Assert.Empty(result.Diagnostics);
        }
        [Fact] public void Reports_the_actual_file_and_error_line_with_utf8()
        {
            var result = new LuaSyntaxChecker().Check("-- Grüße\nlocal x =\n)", "C:\\scripts\\hello.lua");
            Assert.True(result.Available); Assert.False(result.Success);
            Assert.Equal(3, result.Diagnostics.Single().Line); Assert.Equal("C:\\scripts\\hello.lua", result.Diagnostics.Single().FilePath);
        }
        [Theory] [InlineData("\u001bLua")] [InlineData("local x = '\0'")]
        public void Rejects_binary_or_nul_input(string source) => Assert.False(new LuaSyntaxChecker().Check(source, "bad.lua").Success);
        [Fact] public void Rejects_large_input_before_native_compilation() => Assert.False(new LuaSyntaxChecker().Check(new string('x', 1024 * 1024 + 1), "large.lua").Success);
        [Fact] public void Api_completion_uses_real_profile_specific_definitions()
        {
            var gta = LuaApiCatalog.ForProfile("gta5"); var rdr = LuaApiCatalog.ForProfile("rdr2");
            Assert.Contains(gta.Complete("on_t"), s => s.Name == "on_tick");
            Assert.Contains(gta.Complete("GET_PLAYER_PED"), s => s.Name == "GET_PLAYER_PED");
            Assert.Contains(rdr.Complete("PLAYER_PED_ID"), s => s.Name == "PLAYER_PED_ID");
            Assert.NotEmpty(gta.Find("on_tick").Documentation); Assert.NotEmpty(gta.Find("string.format").Signature);
            Assert.Throws<ArgumentException>(() => LuaApiCatalog.ForProfile("unknown"));
        }
        [Fact] public void Every_shipped_template_is_valid_lua()
        {
            foreach (var profile in new[] { "gta5", "rdr2" }) foreach (var template in StudioTemplates.ForProfile(profile))
                Assert.True(new LuaSyntaxChecker().Check(template.Source, template.FileName).Success, template.Name);
        }
        [Fact] public void Save_preserves_utf8_bom_and_detects_external_changes_without_losing_edits()
        {
            string root = Path.Combine(Path.GetTempPath(), "studio-doc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string file = Path.Combine(root, "test.lua"); File.WriteAllText(file, "print('alt')", new UTF8Encoding(true));
                var document = StudioDocument.Load(file); document.Source = "print('Grüße')";
                Assert.True(document.IsDirty); document.Save(); Assert.False(document.IsDirty); Assert.Equal(new byte[] { 239,187,191 }, File.ReadAllBytes(file).Take(3));
                document.Source = "print('meine Änderung')"; File.WriteAllText(file, "-- another editor");
                Assert.Throws<StudioFileConflictException>(() => document.Save()); Assert.Contains("meine", document.Source); Assert.True(document.IsDirty); Assert.Equal("-- another editor", File.ReadAllText(file));
            } finally { Directory.Delete(root, true); }
        }
        [Fact] public void Project_enumeration_ignores_build_and_git_folders()
        {
            string root = Path.Combine(Path.GetTempPath(), "studio-project-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                File.WriteAllText(Path.Combine(root, "main.lua"), "-- source"); Directory.CreateDirectory(Path.Combine(root, "bin")); File.WriteAllText(Path.Combine(root, "bin", "hidden.lua"), "-- hidden");
                Directory.CreateDirectory(Path.Combine(root, ".git")); File.WriteAllText(Path.Combine(root, ".git", "hidden.lua"), "-- hidden");
                Assert.Equal(new[] { "main.lua" }, StudioProject.Files(root).Select(Path.GetFileName));
            } finally { Directory.Delete(root, true); }
        }
    }
}
