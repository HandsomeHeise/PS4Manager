using System;
using PS4Manager.Core.Presentation;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    public sealed class ToolWindowRegistryModelTests
    {
        private readonly ToolWindowRegistryModel<FakeWindow> _registry =
            new ToolWindowRegistryModel<FakeWindow>();
        private readonly CountingFactory _factory = new CountingFactory();

        [Fact]
        public void Reopening_a_tool_activates_the_existing_instance()
        {
            var first = _registry.Open("files", _factory);
            var second = _registry.Open("files", _factory);

            Assert.Same(first, second);
            Assert.Equal(1, _factory.CreateCount);
            Assert.Equal(1, first.ActivateCount);
        }

        [Fact]
        public void Closing_a_tool_allows_a_new_instance()
        {
            var first = _registry.Open("files", _factory);
            first.RaiseClosed();
            var second = _registry.Open("files", _factory);

            Assert.NotSame(first, second);
            Assert.Equal(2, _factory.CreateCount);
        }

        [Fact]
        public void A_disposed_tool_is_replaced_even_when_its_close_event_was_missed()
        {
            var first = _registry.Open("files", _factory);
            first.MarkClosed();

            var second = _registry.Open("files", _factory);

            Assert.NotSame(first, second);
            Assert.Equal(2, _factory.CreateCount);
        }

        [Fact]
        public void A_late_close_event_cannot_remove_the_replacement_instance()
        {
            var first = _registry.Open("files", _factory);
            first.RaiseClosed();
            var second = _registry.Open("files", _factory);

            first.RaiseClosed();
            var reopened = _registry.Open("files", _factory);

            Assert.Same(second, reopened);
            Assert.Equal(2, _factory.CreateCount);
            Assert.Equal(1, second.ActivateCount);
        }

        [Fact]
        public void Closing_all_tools_closes_every_registered_instance()
        {
            var files = _registry.Open("files", _factory);
            var media = _registry.Open("media", _factory);

            _registry.CloseAll();

            Assert.Equal(1, files.CloseCount);
            Assert.Equal(1, media.CloseCount);
        }

        [Fact]
        public void Open_windows_are_listed_for_the_leave_check()
        {
            var files = _registry.Open("files", _factory);
            var media = _registry.Open("media", _factory);

            Assert.Equal(2, _registry.Windows.Count);
            Assert.Contains(files, _registry.Windows);
            Assert.Contains(media, _registry.Windows);

            files.RaiseClosed();

            Assert.Equal(new[] { media }, _registry.Windows);
        }

        private sealed class CountingFactory : IToolWindowFactory<FakeWindow>
        {
            public int CreateCount { get; private set; }

            public FakeWindow Create()
            {
                CreateCount++;
                return new FakeWindow();
            }
        }

        private sealed class FakeWindow : IToolWindow
        {
            public event EventHandler Closed;

            public bool IsClosed { get; private set; }
            public int ActivateCount { get; private set; }
            public int CloseCount { get; private set; }

            public void Activate()
            {
                ActivateCount++;
            }

            public void MarkClosed()
            {
                IsClosed = true;
            }

            public void Close()
            {
                CloseCount++;
                RaiseClosed();
            }

            public void RaiseClosed()
            {
                IsClosed = true;
                var closed = Closed;
                if (closed != null) closed(this, EventArgs.Empty);
            }
        }
    }
}
