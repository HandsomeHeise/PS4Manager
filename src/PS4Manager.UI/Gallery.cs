using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraBars.Ribbon;
using DevExpress.XtraEditors;

namespace PS4Manager.UI
{
    public sealed class GalleryItem
    {
        internal readonly DevExpress.XtraBars.Ribbon.GalleryItem Native = new DevExpress.XtraBars.Ribbon.GalleryItem();
        internal AppGallery Owner;
        private bool detached;
        public GalleryItem() { Native.Tag = this; }
        public string Caption { get => Native.Caption; set => Native.Caption = value; }
        public string Description { get => Native.Description; set => Native.Description = value; }
        public object Tag { get; set; }
        public bool Checked { get => Native.Checked; set => Native.Checked = value; }
        public Image Image {
            get => Native.ImageOptions.Image;
            set {
                if (detached || Owner?.IsDisposed == true) { value?.Dispose(); return; }
                var owner = Owner;
                if (owner != null && owner.InvokeRequired) {
                    try { owner.BeginInvoke((Action)(() => Image = value)); } catch (InvalidOperationException) { value?.Dispose(); }
                    return;
                }
                Native.ImageOptions.Image = value;
            }
        }
        internal void Attach(AppGallery owner) { if (Owner != null && owner == null) detached = true; if (owner != null) detached = false; Owner = owner; }
    }
    public sealed class GalleryItemGroup
    {
        internal readonly DevExpress.XtraBars.Ribbon.GalleryItemGroup Native = new DevExpress.XtraBars.Ribbon.GalleryItemGroup();
        internal AppGallery Owner;
        private Image captionImage;
        public GalleryItemGroup() { Items = new ItemCollection(this); }
        public string Caption { get => Native.Caption; set { Native.Caption = value; UpdateCaption(); } }
        public object Tag { get; set; }
        public Image CaptionImage { get => captionImage; set { captionImage = value; UpdateCaption(); } }
        public Collection<GalleryItem> Items { get; }
        private void UpdateCaption() {
            if (captionImage == null) { var old = Native.CaptionControl; Native.CaptionControl = null; old?.Dispose(); return; }
            if (!(Native.CaptionControl is PanelControl panel)) {
                panel = new PanelControl { Height = 42, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
                panel.Controls.Add(new UiLabel { Dock = DockStyle.Fill, AutoSize = false });
                panel.Controls.Add(new PictureEdit { Dock = DockStyle.Left, Width = 38, Properties = { SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder } });
                Native.CaptionControl = panel; Native.CaptionControlSize = new Size(400, 42);
            }
            panel.Controls.OfType<UiLabel>().Single().Text = Caption;
            panel.Controls.OfType<PictureEdit>().Single().Image = captionImage;
        }
        internal void Attach(AppGallery owner) { Owner = owner; foreach (var item in Items) item.Attach(owner); }
        private sealed class ItemCollection : Collection<GalleryItem> {
            private readonly GalleryItemGroup group;
            internal ItemCollection(GalleryItemGroup group) { this.group = group; }
            protected override void InsertItem(int index, GalleryItem item) { base.InsertItem(index, item); item.Attach(group.Owner); if (group.Owner == null) item.Owner = null; group.Native.Items.Add(item.Native); }
            protected override void RemoveItem(int index) { var item = this[index]; item.Attach(null); group.Native.Items.Remove(item.Native); base.RemoveItem(index); }
            protected override void ClearItems() { foreach (var item in this) item.Attach(null); group.Native.Items.Clear(); base.ClearItems(); }
        }
    }
    public enum ItemCheckMode { Multiple }
    public sealed class GalleryItemEventArgs : EventArgs { public GalleryItemEventArgs(GalleryItem item) { Item = item; } public GalleryItem Item { get; } }
    public sealed class GalleryDragEventArgs : EventArgs { public GalleryDragEventArgs(IEnumerable<GalleryItem> items) { Items = items.ToArray(); } public IReadOnlyList<GalleryItem> Items { get; } }

    // App models carry media/remote-file identity; DevExpress owns layout, selection and painting.
    public sealed class GalleryModel
    {
        private readonly AppGallery owner;
        private readonly DevExpress.XtraBars.Ribbon.Gallery.BaseGallery native;
        private readonly HashSet<GalleryItem> requested = new HashSet<GalleryItem>();
        internal GalleryModel(AppGallery owner, DevExpress.XtraBars.Ribbon.Gallery.BaseGallery native) {
            this.owner = owner; this.native = native; Groups = new GroupCollection(owner, native, requested);
            native.ShowItemText = true; native.ItemCheckMode = DevExpress.XtraBars.Ribbon.Gallery.ItemCheckMode.Multiple;
            native.ItemDoubleClick += (s, e) => { if (e.Item.Tag is GalleryItem item) ItemDoubleClick?.Invoke(this, new GalleryItemEventArgs(item)); };
            native.ItemCheckedChanged += (s, e) => SelectionChanged?.Invoke(this, EventArgs.Empty);
            native.CustomDrawItemImage += (s, e) => NeedImage(e.Item.Tag as GalleryItem);
            native.CustomDrawItemText += (s, e) => NeedImage(e.Item.Tag as GalleryItem);
        }
        private void NeedImage(GalleryItem item) {
            if (item == null || item.Image != null || ItemImageNeeded == null || !owner.IsHandleCreated || !requested.Add(item)) return;
            owner.BeginInvoke((Action)(() => { if (!owner.IsDisposed && ReferenceEquals(item.Owner, owner)) ItemImageNeeded?.Invoke(this, new GalleryItemEventArgs(item)); }));
        }
        public Collection<GalleryItemGroup> Groups { get; }
        public Size ImageSize { get => native.ImageSize; set => native.ImageSize = value; }
        public DevExpress.Utils.Drawing.ImageLayoutMode ItemImageLayout { get => native.ItemImageLayout; set => native.ItemImageLayout = value; }
        public ItemCheckMode ItemCheckMode { get; set; }
        public bool ShowGroupCaption { get => native.ShowGroupCaption; set => native.ShowGroupCaption = value; }
        public IEnumerable<GalleryItem> CheckedItems => Groups.SelectMany(g => g.Items).Where(i => i.Checked);
        public event EventHandler<GalleryItemEventArgs> ItemImageNeeded;
        public event EventHandler<GalleryItemEventArgs> ItemDoubleClick;
        public event EventHandler SelectionChanged;
        private sealed class GroupCollection : Collection<GalleryItemGroup> {
            private readonly AppGallery owner;
            private readonly DevExpress.XtraBars.Ribbon.Gallery.BaseGallery gallery;
            private readonly HashSet<GalleryItem> requested;
            internal GroupCollection(AppGallery owner, DevExpress.XtraBars.Ribbon.Gallery.BaseGallery gallery, HashSet<GalleryItem> requested) { this.owner = owner; this.gallery = gallery; this.requested = requested; }
            protected override void InsertItem(int index, GalleryItemGroup group) { group.Attach(owner); base.InsertItem(index, group); gallery.Groups.Add(group.Native); }
            protected override void RemoveItem(int index) { var group = this[index]; foreach (var item in group.Items) requested.Remove(item); group.Attach(null); gallery.Groups.Remove(group.Native); group.Native.CaptionControl?.Dispose(); base.RemoveItem(index); }
            protected override void ClearItems() { foreach (var group in this) { group.Attach(null); group.Native.CaptionControl?.Dispose(); } requested.Clear(); gallery.Groups.Clear(); base.ClearItems(); }
        }
    }
    public class AppGallery : DevExpress.XtraBars.Ribbon.GalleryControl
    {
        private Point dragStart;
        public AppGallery() {
            Gallery = new GalleryModel(this, base.Gallery);
            MouseDown += (s, e) => {
                dragStart = e.Location;
                if (e.Button == MouseButtons.Right) { var item = GetDropTarget(e.Location); if (item != null && !item.Checked) { foreach (var selected in Gallery.CheckedItems.ToArray()) selected.Checked = false; item.Checked = true; } }
            };
            MouseMove += (s, e) => {
                if (AllowItemDrag && e.Button == MouseButtons.Left && (Math.Abs(e.X - dragStart.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - dragStart.Y) > SystemInformation.DragSize.Height)) {
                    dragStart = e.Location; var selected = Gallery.CheckedItems.ToArray(); if (selected.Length != 0) ItemDrag?.Invoke(this, new GalleryDragEventArgs(selected));
                }
            };
        }
        public new GalleryModel Gallery { get; }
        public bool AllowItemDrag { get; set; }
        private GalleryItem dropHighlight;
        private DevExpress.XtraBars.Ribbon.GalleryItem beforeDrop;
        public GalleryItem DropHighlight {
            get => dropHighlight;
            set {
                if (dropHighlight == null && value != null) beforeDrop = base.Gallery.FocusedItem;
                dropHighlight = value; base.Gallery.FocusedItem = value?.Native ?? beforeDrop; if (value == null) beforeDrop = null;
            }
        }
        public event EventHandler<GalleryDragEventArgs> ItemDrag;
        public GalleryItem GetDropTarget(Point point) => CalcHitInfo(point).GalleryItem?.Tag as GalleryItem;
    }
}
