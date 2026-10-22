using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraBars;
using DevExpress.XtraBars.Ribbon;

namespace PS4Manager.UI
{
    public enum RibbonItemSize { Small, Large }
    public enum RibbonItemKind { Button, ToggleButton }
    public sealed class RibbonItem
    {
        internal readonly BarButtonItem Native = new BarButtonItem();
        public RibbonItem(string text) { Text = text; Native.ItemClick += (s, e) => Click?.Invoke(this, EventArgs.Empty); }
        public string Text { get => Native.Caption; set => Native.Caption = value; }
        public bool Enabled { get => Native.Enabled; set => Native.Enabled = value; }
        public bool Checked { get => Native.Down; set => Native.Down = value; }
        public RibbonItemSize Size { get => Native.RibbonStyle == RibbonItemStyles.Large ? RibbonItemSize.Large : RibbonItemSize.Small; set { Native.RibbonStyle = value == RibbonItemSize.Large ? RibbonItemStyles.Large : RibbonItemStyles.SmallWithText; Native.LargeWidth = value == RibbonItemSize.Large ? 95 : 0; } }
        public RibbonItemKind Kind { get => Native.ButtonStyle == BarButtonStyle.Check ? RibbonItemKind.ToggleButton : RibbonItemKind.Button; set => Native.ButtonStyle = value == RibbonItemKind.ToggleButton ? BarButtonStyle.Check : BarButtonStyle.Default; }
        public Image Image { get => Native.ImageOptions.LargeImage; set => Native.ImageOptions.LargeImage = value; }
        public event EventHandler Click;
    }
    public sealed class RibbonGroup
    {
        internal readonly RibbonPageGroup Native;
        public RibbonGroup(string title) { Native = new RibbonPageGroup(title) { ShowCaptionButton = false, AllowTextClipping = false }; Items = new ItemCollection(Native); }
        public string Title => Native.Text;
        public Collection<RibbonItem> Items { get; }
        private sealed class ItemCollection : Collection<RibbonItem> {
            private readonly RibbonPageGroup group;
            internal ItemCollection(RibbonPageGroup group) { this.group = group; }
            protected override void InsertItem(int index, RibbonItem item) { base.InsertItem(index, item); group.ItemLinks.Add(item.Native); }
        }
    }
    public sealed class RibbonTab
    {
        internal readonly RibbonPage Native;
        public RibbonTab(string text) { Native = new RibbonPage(text); Groups = new GroupCollection(Native); }
        public string Text => Native.Text;
        public Collection<RibbonGroup> Groups { get; }
        private sealed class GroupCollection : Collection<RibbonGroup> {
            private readonly RibbonPage page;
            internal GroupCollection(RibbonPage page) { this.page = page; }
            protected override void InsertItem(int index, RibbonGroup item) { base.InsertItem(index, item); page.Groups.Add(item.Native); }
        }
    }
    public class AppRibbon : DevExpress.XtraBars.Ribbon.RibbonControl
    {
        public AppRibbon() {
            RibbonStyle = RibbonControlStyle.Office2019;
            ShowApplicationButton = DevExpress.Utils.DefaultBoolean.False;
            ShowToolbarCustomizeItem = false; ToolbarLocation = RibbonQuickAccessToolbarLocation.Hidden;
            ShowPageHeadersMode = ShowPageHeadersMode.Show;
            ExpandCollapseItem.Visibility = BarItemVisibility.Never;
            Tabs = new TabCollection(this);
        }
        public Collection<RibbonTab> Tabs { get; }
        private sealed class TabCollection : Collection<RibbonTab> {
            private readonly AppRibbon ribbon;
            internal TabCollection(AppRibbon ribbon) { this.ribbon = ribbon; }
            protected override void InsertItem(int index, RibbonTab item) { base.InsertItem(index, item); ribbon.Pages.Add(item.Native); }
        }
    }

    public sealed class MenuEntry
    {
        public MenuEntry(string text) { Text = text; }
        public string Text { get; }
        public bool Enabled { get; set; } = true;
        public bool Checked { get; set; }
        internal bool IsSeparator { get; private set; }
        public event EventHandler Click;
        internal void Invoke() => Click?.Invoke(this, EventArgs.Empty);
        public static MenuEntry Separator() => new MenuEntry("") { IsSeparator = true };
    }
    public sealed class AppPopup : IDisposable
    {
        private readonly BarManager manager = new BarManager();
        private readonly DevExpress.XtraBars.PopupMenu menu;
        private readonly List<BarButtonItem> nativeItems = new List<BarButtonItem>();
        public AppPopup() { menu = new DevExpress.XtraBars.PopupMenu(manager); }
        public IList<MenuEntry> Items { get; } = new List<MenuEntry>();
        public void Show(Control owner, Point screenLocation) {
            manager.Form = owner.FindForm(); menu.ClearLinks();
            foreach (var item in nativeItems) { manager.Items.Remove(item); item.Dispose(); } nativeItems.Clear();
            bool separator = false;
            foreach (var entry in Items) {
                if (entry.IsSeparator) { separator = true; continue; }
                var item = new BarButtonItem(manager, entry.Text) { Enabled = entry.Enabled, Down = entry.Checked };
                item.ItemClick += (s, e) => entry.Invoke(); nativeItems.Add(item);
                var link = menu.AddItem(item); link.BeginGroup = separator; separator = false;
            }
            menu.ShowPopup(screenLocation);
        }
        public void Dispose() { menu.Dispose(); manager.Dispose(); }
    }
}
