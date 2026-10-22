using PS4Manager.UI;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Frame4;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    [Collection(UiControlCollection.Name)]
    public sealed class Frame4FormTests
    {
        [Fact]
        public void Window_has_process_list_controls()
        {
            using (var form = new Frame4Form(new ManagerSettings { Ip = "192.168.0.10" },
                new Frame4Client(new FixedTransport("[]"))))
            {
                form.CreateControl();

                Assert.Contains(FindButtons(form), b => b.Text == "Refresh");
                Assert.Contains(FindButtons(form), b => b.Text == "Load Details");
                Assert.Contains(FindButtons(form), b => b.Text == "Read");
                Assert.Contains(FindButtons(form), b => b.Text == "Prev");
                Assert.Contains(FindButtons(form), b => b.Text == "Next");
                Assert.Contains(FindButtons(form), b => b.Text == "Notify");
                Assert.Contains(FindButtons(form), b => b.Text == "Load Console");
                Assert.Contains(FindButtons(form), b => b.Text == "Find AOB");
                Assert.Contains(FindButtons(form), b => b.Text == "Load PRX List");
                Assert.Contains(FindButtons(form), b => b.Text == "Start Log");
                Assert.Contains(FindButtons(form), b => b.Text == "Stop Log");
                Assert.Contains(FindButtons(form), b => b.Text == "Clear Log");
                Assert.Equal("Frame4 Debugger", form.Text);
            }
        }

        [Fact]
        public void Window_exposes_frame4_detail_tabs()
        {
            using (var form = new Frame4Form(new ManagerSettings { Ip = "192.168.0.10" },
                new Frame4Client(new FixedTransport("[]"))))
            {
                form.CreateControl();

                Assert.Empty(FindControls<TabControl>(form));

                var tabs = FindControls<UiTabs>(form).Single();

                Assert.Equal(0, tabs.SelectedIndex);
            }
        }

        [Fact]
        public void Expert_actions_require_expert_mode()
        {
            using (var normal = new Frame4Form(new ManagerSettings { Ip = "192.168.0.10", ExpertMode = false },
                new Frame4Client(new FixedTransport("[]"))))
            {
                normal.CreateControl();

                Assert.False(FindButtons(normal).Single(b => b.Text == "Write").Enabled);
                Assert.False(FindButtons(normal).Single(b => b.Text == "Alloc").Enabled);
                Assert.False(FindButtons(normal).Single(b => b.Text == "Free").Enabled);
                Assert.False(FindButtons(normal).Single(b => b.Text == "Load PRX").Enabled);
                Assert.False(FindButtons(normal).Single(b => b.Text == "Unload PRX").Enabled);
                Assert.False(FindButtons(normal).Single(b => b.Text == "Call RPC").Enabled);
            }
        }

        private static System.Collections.Generic.IEnumerable<UiButton> FindButtons(Control root)
        {
            return FindControls<UiButton>(root);
        }

        private static System.Collections.Generic.IEnumerable<T> FindControls<T>(Control root)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var match = child as T;
                if (match != null) yield return match;
                foreach (var nested in FindControls<T>(child)) yield return nested;
            }
        }

        private sealed class FixedTransport : IFrame4HttpTransport
        {
            private readonly string _text;

            public FixedTransport(string text)
            {
                _text = text;
            }

            public Task<string> GetStringAsync(Uri uri, CancellationToken ct)
            {
                return Task.FromResult(_text);
            }
        }
    }
}
