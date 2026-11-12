using System;
using System.IO;
using System.Linq;
using PS4Manager.Core.Luma.Studio;
using Xunit;

namespace PS4Manager.Core.Tests.Luma.Studio
{
    public sealed class StudioWorkspaceTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "studio-project-" + Guid.NewGuid().ToString("N"));
        public StudioWorkspaceTests() { Directory.CreateDirectory(root); }
        public void Dispose() { Directory.Delete(root, true); }

        [Fact] public void Create_reopens_metadata_and_a_real_lua_entry_without_credentials()
        {
            var project = StudioWorkspace.Create(root, "My scripts", "rdr2");
            var opened = StudioWorkspace.Open(project.Root, "gta5");
            Assert.Equal("My scripts", opened.Name); Assert.Equal("rdr2", opened.Profile);
            Assert.Equal(new[] { "main.lua" }, opened.ScriptPaths());
            Assert.True(new LuaSyntaxChecker().Check(File.ReadAllText(Path.Combine(opened.Root, "main.lua")), "main.lua").Success);
            Assert.DoesNotContain("token", File.ReadAllText(Path.Combine(opened.Root, "luma-project.json")));
            Assert.Throws<IOException>(() => StudioWorkspace.Create(root, "My scripts", "gta5"));
        }
        [Fact] public void Existing_folder_can_be_adopted_without_replacing_scripts_or_unknown_metadata()
        {
            File.WriteAllText(Path.Combine(root, "hello.lua"), "return 42");
            var project = StudioWorkspace.Open(root, "gta5"); project.Save();
            Assert.Equal("return 42", File.ReadAllText(Path.Combine(root, "hello.lua")));
            Assert.Equal("gta5", StudioWorkspace.Open(root, "rdr2").Profile);
            File.WriteAllText(Path.Combine(root, "luma-project.json"), "{\"schema\":99}");
            Assert.ThrowsAny<Exception>(() => StudioWorkspace.Open(root, "gta5"));
            Assert.Throws<StudioFileConflictException>(() => project.Save());
        }
        [Theory] [InlineData("../escape")] [InlineData("CON")] [InlineData("a/b")] [InlineData("a.")]
        public void Unsafe_project_names_do_not_create_directories(string name)
        { Assert.Throws<ArgumentException>(() => StudioWorkspace.Create(root, name, "gta5")); Assert.Empty(Directory.GetFileSystemEntries(root)); }

        [Fact] public void Script_paths_preserve_subfolders_and_exclude_build_folders()
        {
            Directory.CreateDirectory(Path.Combine(root, "lib")); Directory.CreateDirectory(Path.Combine(root, "node_modules"));
            File.WriteAllText(Path.Combine(root, "main.lua"), ""); File.WriteAllText(Path.Combine(root, "lib", "util.lua"), "");
            File.WriteAllText(Path.Combine(root, "node_modules", "ignored.lua"), "");
            Assert.Equal(new[] { "lib/util.lua", "main.lua" }, StudioWorkspace.Open(root, "gta5").ScriptPaths());
        }
        [Fact] public void More_than_200_scripts_is_an_error_not_a_partial_sync()
        {
            for (int i = 0; i < 201; i++) File.WriteAllText(Path.Combine(root, "file" + i + ".lua"), "");
            Assert.Throws<IOException>(() => StudioWorkspace.Open(root, "gta5").ScriptPaths());
        }
        [Theory] [InlineData("lib/test.lua")] [InlineData("a.lua")]
        public void Safe_script_paths_are_unchanged(string path) => Assert.Equal(path, StudioScriptPath.Validate(path));
        [Theory] [InlineData("../a.lua")] [InlineData("a/../b.lua")] [InlineData("/a.lua")]
        [InlineData("a\\b.lua")] [InlineData("a.txt")] [InlineData("a.lua\0")]
        [InlineData("a//b.lua")] [InlineData("a /b.lua")]
        public void Unsafe_script_paths_are_rejected(string path) => Assert.Throws<ArgumentException>(() => StudioScriptPath.Validate(path));

        [Fact] public void Junctions_inside_or_above_project_cannot_import_outside_files()
        {
            string outside = Path.Combine(Path.GetTempPath(), "studio-outside-" + Guid.NewGuid().ToString("N"));
            string link = Path.Combine(root, "linked"); Directory.CreateDirectory(outside);
            try {
                File.WriteAllText(Path.Combine(outside, "outside.lua"), "return 1");
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = System.Diagnostics.Process.Start(start)) { process.WaitForExit(); Assert.Equal(0, process.ExitCode); }
                Assert.Throws<IOException>(() => StudioWorkspace.Open(root, "gta5").ScriptPaths());
                Assert.Throws<IOException>(() => StudioWorkspace.Open(link, "gta5"));
                Assert.True(File.Exists(Path.Combine(outside, "outside.lua")));
            } finally { if (Directory.Exists(link)) Directory.Delete(link); Directory.Delete(outside, true); }
        }
    }
}
