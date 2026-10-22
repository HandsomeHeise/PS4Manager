using System.Linq;
using System.Text;
using FileManager.Core.Editing;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class IniPluginEditorTests
    {
        private static readonly string[] Files = { "hen.prx", "debug.prx", "tool.prx" };

        [Fact]
        public void A_line_naming_a_plugin_file_is_enabled()
        {
            var entries = IniPluginEditor.Parse("hen.prx\n", Files);

            var entry = Assert.Single(entries);
            Assert.Equal("hen.prx", entry.Name);
            Assert.Equal(GoldHenActivation.Enabled, entry.Activation);
            Assert.True(entry.FilePresent);
            Assert.Equal(0, entry.LineIndex);
        }

        [Fact]
        public void A_commented_line_naming_a_plugin_file_is_disabled()
        {
            var entries = IniPluginEditor.Parse("; hen.prx\n# debug.prx\n", Files);

            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal(GoldHenActivation.Disabled, e.Activation));
            Assert.Equal(new[] { "hen.prx", "debug.prx" }, entries.Select(e => e.Name));
        }

        [Fact]
        public void Comments_blank_lines_and_unknown_names_are_not_entries()
        {
            var text = "; a comment\n\n[misc]\nother.prx\n/tmp/hen.prx\nhen.prx\n";

            var entries = IniPluginEditor.Parse(text, Files);

            var entry = Assert.Single(entries);
            Assert.Equal("hen.prx", entry.Name);
            Assert.Equal(5, entry.LineIndex);
        }

        [Fact]
        public void The_file_name_comparison_ignores_case()
        {
            var entries = IniPluginEditor.Parse("HEN.PRX\n", Files);

            Assert.Equal(GoldHenActivation.Enabled, Assert.Single(entries).Activation);
        }

        [Fact]
        public void An_empty_text_yields_no_entries()
        {
            Assert.Empty(IniPluginEditor.Parse("", Files));
            Assert.Empty(IniPluginEditor.Parse(null, Files));
        }

        [Fact]
        public void Disabling_comments_exactly_the_adressed_line()
        {
            var text = "[default]\n/data/GoldHEN/plugins/hen.prx\n/data/GoldHEN/plugins/tool.prx\n";
            var entry = IniPluginEditor.Parse(text, Files).Single(e => e.Name == "tool.prx");

            var changed = IniPluginEditor.SetEnabled(text, entry, false);

            Assert.Equal("[default]\n/data/GoldHEN/plugins/hen.prx\n; /data/GoldHEN/plugins/tool.prx\n", changed);
        }

        [Fact]
        public void Enabling_removes_the_marker_and_keeps_the_rest_of_the_line()
        {
            var text = "[default]\n#  /data/GoldHEN/plugins/tool.prx   \n";
            var entry = IniPluginEditor.Parse(text, Files).Single();

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("[default]\n/data/GoldHEN/plugins/tool.prx\n", changed);
        }

        [Fact]
        public void Windows_line_endings_survive_the_toggle()
        {
            var text = "[default]\r\n; /data/GoldHEN/plugins/hen.prx\r\n/data/GoldHEN/plugins/tool.prx\r\n";
            var entry = IniPluginEditor.Parse(text, Files).Single(e => e.Name == "hen.prx");

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("[default]\r\n/data/GoldHEN/plugins/hen.prx\r\n/data/GoldHEN/plugins/tool.prx\r\n", changed);
        }

        [Fact]
        public void An_unmentioned_plugin_is_appended_with_the_files_line_ending()
        {
            var text = "[default]\r\n; /data/GoldHEN/plugins/hen.prx\r\n";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx", GoldHenActivation.Disabled, true, -1);

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("[default]\r\n; /data/GoldHEN/plugins/hen.prx\r\n/data/GoldHEN/plugins/tool.prx\r\n", changed);
        }

        [Fact]
        public void Appending_to_a_file_without_a_trailing_newline_adds_one()
        {
            var text = "[default]\n; /data/GoldHEN/plugins/hen.prx";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx", GoldHenActivation.Disabled, true, -1);

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("[default]\n; /data/GoldHEN/plugins/hen.prx\n/data/GoldHEN/plugins/tool.prx\n", changed);
        }

        [Fact]
        public void Turning_a_plugin_off_that_has_no_line_changes_nothing()
        {
            var text = "[default]\n/data/GoldHEN/plugins/hen.prx\n";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx", GoldHenActivation.Disabled, true, -1);

            Assert.Equal(text, IniPluginEditor.SetEnabled(text, entry, false));
        }

        [Fact]
        public void A_document_survives_a_round_trip_through_the_text_codec()
        {
            var original = "; hen.prx\r\n; debug.prx\r\n";
            var entries = IniPluginEditor.Parse(original, Files);
            var enabled = IniPluginEditor.SetEnabled(original, entries.First(e => e.Name == "hen.prx"), true);
            var bytes = new UTF8Encoding(true).GetPreamble()
                .Concat(new UTF8Encoding(false).GetBytes(enabled)).ToArray();

            var document = TextFileContent.FromBytes(bytes);

            Assert.Equal(enabled, document.Text);
        }

        [Fact]
        public void Sectioned_absolute_paths_keep_their_title_id_scope()
        {
            var text = "[default]\n/data/GoldHEN/plugins/hen.prx\n\n[CUSA00411]\n;/data/GoldHEN/plugins/tool.prx\n";

            var entries = IniPluginEditor.Parse(text, Files);

            var global = entries.Single(e => e.Name == "hen.prx");
            var game = entries.Single(e => e.Name == "tool.prx");
            Assert.Null(global.TitleId);
            Assert.Equal("CUSA00411", game.TitleId);
            Assert.Equal("/data/GoldHEN/plugins/tool.prx", game.Path);
            Assert.Equal(GoldHenActivation.Disabled, game.Activation);
        }

        [Fact]
        public void Enabling_for_a_title_adds_the_plugin_path_under_that_section()
        {
            var text = "[default]\n/data/GoldHEN/plugins/hen.prx\n";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx",
                GoldHenActivation.Disabled, true, -1);

            var changed = IniPluginEditor.SetEnabled(text, entry, true, "CUSA00411");

            Assert.Equal("[default]\n/data/GoldHEN/plugins/hen.prx\n\n[CUSA00411]\n/data/GoldHEN/plugins/tool.prx\n", changed);
        }

        [Fact]
        public void Enabling_for_an_existing_title_adds_the_path_inside_that_section()
        {
            var text = "[CUSA00411]\n/data/GoldHEN/plugins/hen.prx\n\n[PPSA00001]\n/data/GoldHEN/plugins/debug.prx\n";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx",
                GoldHenActivation.Disabled, true, -1);

            var changed = IniPluginEditor.SetEnabled(text, entry, true, "CUSA00411");

            Assert.Equal("[CUSA00411]\n/data/GoldHEN/plugins/hen.prx\n/data/GoldHEN/plugins/tool.prx\n\n[PPSA00001]\n/data/GoldHEN/plugins/debug.prx\n", changed);
        }

        [Fact]
        public void Enabling_a_global_plugin_creates_the_default_section_and_full_path()
        {
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx",
                GoldHenActivation.Disabled, true, -1);

            var changed = IniPluginEditor.SetEnabled("", entry, true);

            Assert.Equal("[default]\n/data/GoldHEN/plugins/tool.prx\n", changed);
        }

        [Fact]
        public void Enabling_a_legacy_bare_plugin_normalizes_the_file_to_default_paths()
        {
            var text = "; GTALua.prx\nAWClient.prx\nLuma.prx\n";
            var files = new[] { "GTALua.prx", "AWClient.prx", "Luma.prx" };
            var entry = IniPluginEditor.Parse(text, files).Single(e => e.Name == "GTALua.prx");

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("[default]\n/data/GoldHEN/plugins/GTALua.prx\n/data/GoldHEN/plugins/AWClient.prx\n/data/GoldHEN/plugins/Luma.prx\n", changed);
        }

        [Fact]
        public void Enabling_for_a_title_does_not_toggle_a_global_legacy_line()
        {
            var text = "; tool.prx\n";
            var entry = IniPluginEditor.Parse(text, Files).Single();

            var changed = IniPluginEditor.SetEnabled(text, entry, true, "CUSA00411");

            Assert.Equal("[default]\n; /data/GoldHEN/plugins/tool.prx\n\n[CUSA00411]\n/data/GoldHEN/plugins/tool.prx\n", changed);
        }
    }
}
