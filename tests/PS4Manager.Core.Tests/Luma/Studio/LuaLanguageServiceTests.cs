using System.Linq;
using PS4Manager.Core.Luma.Studio;
using Xunit;

namespace PS4Manager.Core.Tests.Luma.Studio
{
    public sealed class LuaLanguageServiceTests
    {
        private static readonly LuaApiCatalog Gta = LuaApiCatalog.ForProfile("gta5");
        [Fact] public void Completion_combines_actual_natives_runtime_and_document_identifiers()
        {
            var natives = LuaLanguageService.Complete("get_player_p", 12, Gta);
            Assert.Contains(natives.Items, s => s.Name == "GET_PLAYER_PED"); Assert.Equal(0, natives.Start);
            Assert.Contains(LuaLanguageService.Complete("on_t", 4, Gta).Items, s => s.Name == "on_tick");
            string source = "local playerHealth = 100\nplayerH";
            Assert.Contains(LuaLanguageService.Complete(source, source.Length, Gta).Items, s => s.Name == "playerHealth");
            Assert.Contains(LuaLanguageService.Complete("fun", 3, Gta).Items, s => s.Name == "function");
        }
        [Theory] [InlineData("-- GET_PLAY")] [InlineData("local s = 'GET_PLAY")]
        [InlineData("local s = [==[GET_PLAY")] [InlineData("--[=[ GET_PLAY")]
        public void Completion_does_not_offer_code_in_comments_or_strings(string source)
        { Assert.Empty(LuaLanguageService.Complete(source, source.Length, Gta).Items); }
        [Fact] public void Signature_tracks_the_outer_call_after_nested_arguments_and_tables()
        {
            string source = "SET_ENTITY_HEALTH(GET_PLAYER_PED(-1), 100";
            var tip = LuaLanguageService.SignatureAt(source, source.Length, Gta);
            Assert.Equal("SET_ENTITY_HEALTH", tip.Symbol.Name); Assert.Equal(1, tip.Parameter);
            Assert.Null(LuaLanguageService.SignatureAt("-- GET_PLAYER_PED(", 18, Gta));
        }
        [Fact] public void Unknown_native_and_obvious_arity_are_nonblocking_located_warnings()
        {
            string source = "-- Grüße\nGET_PLAYER_PEDD(-1)\nGET_PLAYER_PED()";
            var diagnostics = LuaLanguageService.Analyze(source, "a.lua", Gta);
            Assert.Equal(2, diagnostics.Count); Assert.All(diagnostics, d => Assert.Equal(LuaDiagnosticSeverity.Warning, d.Severity));
            Assert.Equal(2, diagnostics[0].Line); Assert.Equal(9, diagnostics[0].Start); Assert.Contains("GET_PLAYER_PEDD", diagnostics[0].Message);
            Assert.Contains("GET_PLAYER_PED", diagnostics[1].Message);
            Assert.True(new LuaCheckResult(true, diagnostics).Success);
        }
        [Theory]
        [InlineData("-- MISSING_NATIVE()\nlocal s = 'GET_PLAYER_PED()'\nlocal q=[=[MISSING_NATIVE()]=]")]
        [InlineData("local MISSING_NATIVE = function() end\nMISSING_NATIVE()")]
        [InlineData("function MISSING_NATIVE() end\nMISSING_NATIVE()")]
        [InlineData("local function call(MISSING_NATIVE) MISSING_NATIVE() end")]
        [InlineData("local FIRST_NATIVE, SECOND_NATIVE, THIRD_NATIVE = print, print, print\nSECOND_NATIVE()")]
        [InlineData("FIRST_NATIVE, SECOND_NATIVE = print, print\nFIRST_NATIVE()")]
        [InlineData("local t = {}; t.MISSING_NATIVE()")]
        [InlineData("GET_PLAYER_PED(unpack(args))")]
        [InlineData("local function values(t) return 1, 100 end\nSET_ENTITY_HEALTH(values {})")]
        [InlineData("local function values(t) return 1, 100 end\nSET_ENTITY_HEALTH(values 'args')")]
        [InlineData("GET_PLAYER_PED(...)")]
        [InlineData("print('a', 'b'); on_tick(function() print('x') end)")]
        public void Dynamic_and_noncode_native_lookalikes_do_not_create_warnings(string source)
        { Assert.Empty(LuaLanguageService.Analyze(source, "a.lua", Gta)); }
        [Fact] public void Wrong_native_case_reports_canonical_name_and_profile_changes_lookup()
        {
            Assert.Contains(LuaLanguageService.Analyze("get_player_ped(-1)", "a.lua", Gta), d => d.Message.Contains("GET_PLAYER_PED"));
            Assert.NotNull(Gta.Find("GET_PLAYER_PED"));
            Assert.Empty(LuaLanguageService.Analyze("GET_PLAYER_PED(-1)", "a.lua", Gta));
        }
    }
}
