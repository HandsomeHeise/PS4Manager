using System;
using System.IO;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    public sealed class WorkflowContractTests
    {
        private static string Workflow(string name) => File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WorkflowFixtures", name)).Replace("\r\n", "\n");
        [Fact]
        public void Tagged_release_waits_for_the_reusable_source_and_server_checks()
        {
            string desktop = Workflow("desktop.yml"), checks = Workflow("checks.yml");
            Assert.Contains("  workflow_call:", checks);
            Assert.Matches(@"(?s)  source-checks:\n.*?if: startsWith\(github.ref, 'refs/tags/v'\).*?uses: \./\.github/workflows/checks.yml", desktop);
            Assert.Contains("    needs: source-checks", desktop);
            Assert.Contains("needs.source-checks.result == 'success'", desktop);
            Assert.Contains("!cancelled()", desktop);
        }
        [Fact]
        public void Desktop_CI_tests_real_Studio_runtimes_in_x86_as_well_as_the_full_suite()
        {
            string desktop = Workflow("desktop.yml");
            Assert.Contains("dotnet test PS4Manager.sln", desktop);
            Assert.Contains("RunConfiguration.TargetPlatform=x86", desktop);
            Assert.Contains("FullyQualifiedName~LumaStudioFormTests", desktop);
            Assert.Contains("scripts/build-lua.ps1", desktop);
        }
    }
}
