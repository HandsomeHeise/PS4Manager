using System;
using FileManager.Core.Paths;
using Xunit;

namespace FileManager.Core.Tests
{
    public class NavigationHistoryTests
    {
        [Fact]
        public void The_first_navigation_has_nothing_to_go_back_to()
        {
            var history = new NavigationHistory();
            Assert.Null(history.Current);

            history.Navigate("/data");

            Assert.Equal("/data", history.Current);
            Assert.False(history.CanGoBack);
            Assert.False(history.CanGoForward);
        }

        [Fact]
        public void Back_and_forward_walk_the_trail()
        {
            var history = new NavigationHistory();
            history.Navigate("/data");
            history.Navigate("/data/GoldHEN");
            history.Navigate("/data/GoldHEN/plugins");

            Assert.Equal("/data/GoldHEN", history.GoBack());
            Assert.Equal("/data", history.GoBack());
            Assert.False(history.CanGoBack);
            Assert.Equal("/data/GoldHEN", history.GoForward());
            Assert.True(history.CanGoForward);
        }

        [Fact]
        public void Navigating_after_going_back_drops_the_forward_trail()
        {
            var history = new NavigationHistory();
            history.Navigate("/a");
            history.Navigate("/b");
            history.GoBack();

            history.Navigate("/c");

            Assert.False(history.CanGoForward);
            Assert.Equal("/a", history.GoBack());
        }

        [Fact]
        public void Navigating_to_the_current_path_adds_nothing()
        {
            var history = new NavigationHistory();
            history.Navigate("/a");
            history.Navigate("/a/");          // gleicher Pfad, anders geschrieben

            Assert.False(history.CanGoBack);
        }

        [Fact]
        public void Going_back_without_history_throws()
        {
            Assert.Throws<InvalidOperationException>(() => new NavigationHistory().GoBack());
            Assert.Throws<InvalidOperationException>(() => new NavigationHistory().GoForward());
        }
        [Fact]
        public void The_targets_show_where_back_and_forward_would_go_without_moving()
        {
            var history = new NavigationHistory();
            Assert.Null(history.BackTarget);
            history.Navigate("/data");
            history.Navigate("/data/pkg");

            Assert.Equal("/data", history.BackTarget);
            Assert.Null(history.ForwardTarget);
            Assert.Equal("/data/pkg", history.Current);

            history.GoBack();
            Assert.Equal("/data/pkg", history.ForwardTarget);
        }
    }
}
