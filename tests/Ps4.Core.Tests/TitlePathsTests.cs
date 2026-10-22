using Ps4.Core.Titles;
using Xunit;

namespace Ps4.Core.Tests
{
    public class TitlePathsTests
    {
        [Fact]
        public void The_appmeta_paths_are_shared_by_both_kinds()
        {
            Assert.Equal("/system_data/priv/appmeta/CUSA00411/param.sfo", TitlePaths.SfoPath("CUSA00411"));
            Assert.Equal("/user/appmeta/CUSA00411/icon0.png", TitlePaths.IconPath("CUSA00411"));
        }
    }
}
