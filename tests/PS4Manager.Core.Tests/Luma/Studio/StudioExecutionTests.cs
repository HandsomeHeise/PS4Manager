using PS4Manager.Core.Luma.Studio;
using Xunit;

namespace PS4Manager.Core.Tests.Luma.Studio
{
    public sealed class StudioExecutionTests
    {
        [Fact]
        public void Delayed_runtime_errors_match_only_the_exact_execution_document_version_and_session()
        {
            var document = new StudioDocument("hello.lua", "error('test')");
            var first = new StudioExecution(document, 1);
            string error = first.ChunkName.Substring(1) + ":3: test error";
            Assert.Equal(3, first.Diagnostic(error, document, 1).Line);
            Assert.Null(new StudioExecution(document, 1).Diagnostic(error, document, 1));
            Assert.Null(first.Diagnostic(error, document, 2));
            Assert.Null(first.Diagnostic(error, new StudioDocument("other.lua"), 1));
            document.Source = "return 42";
            Assert.Null(first.Diagnostic(error, document, 1));
        }
    }
}
