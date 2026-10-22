using PS4Manager.UI;
using System;
using System.Windows.Forms;

namespace FileManager.App.Views
{
    /// <summary>
    /// Zurück/Vor/Hoch/Aktualisieren, Adressleiste, Filterfeld (Spec
    /// „Navigationszeile"). Aufbau wie ExplorerForm.BuildStrip: die Leiste
    /// setzt ihre Höhe selbst, der Streifen folgt ihr.
    /// </summary>
    internal sealed class NavigationStrip : UiPanel
    {
        private const int FilterWidthLogical = 200;

        public NavigationStrip()
        {
            Dock = DockStyle.Top;
            Back = Button("←");
            Forward = Button("→");
            Up = Button("↑");
            Reload = Button("↻");
            Filter = new UiTextBox { Dock = DockStyle.Right, Width = FilterWidthLogical, PlaceholderText = "Filter …" };
            Address = new AddressBar { Dock = DockStyle.Top, RootText = "PS4" };

            // Zuletzt Hinzugefügtes dockt zuerst: Knöpfe links, Filter rechts,
            // die Adressleiste nimmt die Breite dazwischen.
            Controls.Add(Address);
            Controls.Add(Filter);
            Controls.Add(Reload);
            Controls.Add(Up);
            Controls.Add(Forward);
            Controls.Add(Back);

            Address.SizeChanged += (s, e) => SyncHeight();
            SyncHeight();
        }

        public UiButton Back { get; }
        public UiButton Forward { get; }
        public UiButton Up { get; }
        public UiButton Reload { get; }
        public AddressBar Address { get; }
        public UiTextBox Filter { get; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Filter.Width = DpiScale.Scale(FilterWidthLogical, DeviceDpi);
            SyncHeight();
        }

        private void SyncHeight()
        {
            Height = Address.Height + (Height - DisplayRectangle.Height);
        }

        private static UiButton Button(string text)
        {
            return new UiButton { Text = text, Dock = DockStyle.Left };
        }
    }
}
