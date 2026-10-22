using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraTreeList;
using NativeNode = DevExpress.XtraTreeList.Nodes.TreeListNode;

namespace PS4Manager.UI
{
    public sealed class NavigationNode
    {
        private string text;
        private Image image;
        private bool hint;
        internal Action Changed;
        public NavigationNode(string text) { this.text = text; Children = new NavigationNodes(() => Changed?.Invoke()); }
        public string Text { get => text; set { text = value; Changed?.Invoke(); } }
        public object Tag { get; set; }
        public Image Image { get => image; set { image = value; Changed?.Invoke(); } }
        public bool HasChildrenHint { get => hint; set { hint = value; Changed?.Invoke(); } }
        public NavigationNodes Children { get; }
        public void BeginLoading() { }
        internal bool ExpandRequested;
        public void Expand() { ExpandRequested = true; Changed?.Invoke(); }
        public void EndLoadingWithError(string reason) => Children.ReplaceWith(new[] { new NavigationNode(reason) });
    }
    public sealed class NavigationNodes : Collection<NavigationNode>
    {
        private readonly Action changed;
        public NavigationNodes(Action changed) { this.changed = changed; }
        protected override void InsertItem(int index, NavigationNode item) { item.Changed = changed; base.InsertItem(index, item); changed(); }
        protected override void ClearItems() { foreach (var node in this) node.Changed = null; base.ClearItems(); changed(); }
        public void ReplaceWith(IEnumerable<NavigationNode> nodes) { var items = nodes.ToArray(); foreach (var node in this) node.Changed = null; Items.Clear(); foreach (var node in items) { node.Changed = changed; Items.Add(node); } changed(); }
    }
    public sealed class NavigationNodeEventArgs : EventArgs { public NavigationNodeEventArgs(NavigationNode node) { Node = node; } public NavigationNode Node { get; } }
    public class AppTree : TreeList
    {
        private bool rebuilding;
        private bool insideExpand, rebuildPosted;
        private Point dragStart;
        private NavigationNode dragNode;
        private readonly Dictionary<NavigationNode, NativeNode> native = new Dictionary<NavigationNode, NativeNode>();
        private readonly ImageList images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        public AppTree() {
            Columns.Add().Visible = true; Columns[0].FieldName = "Name";
            OptionsView.ShowColumns = false; OptionsView.ShowIndicator = false;
            OptionsBehavior.Editable = false; OptionsView.ShowHorzLines = false; OptionsView.ShowVertLines = false;
            StateImageList = images;
            Nodes = new NavigationNodes(Rebuild);
            BeforeExpand += (s, e) => {
                if (rebuilding || !(e.Node.Tag is NavigationNode node)) return;
                insideExpand = true; try { NodeExpanding?.Invoke(this, new NavigationNodeEventArgs(node)); } finally { insideExpand = false; }
            };
            NodeCellStyle += (s, e) => { if (ReferenceEquals(e.Node.Tag, dropHighlight)) { e.Appearance.BackColor = AppTheme.Accent; e.Appearance.ForeColor = Color.White; } };
            FocusedNodeChanged += (s, e) => { if (!rebuilding) SelectedNodeChanged?.Invoke(this, EventArgs.Empty); };
            MouseDown += (s, e) => { dragStart = e.Location; dragNode = e.Button == MouseButtons.Left ? HitTest(e.Location) : null; };
            MouseMove += (s, e) => {
                if (AllowItemDrag && dragNode != null && e.Button == MouseButtons.Left && (Math.Abs(e.X - dragStart.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - dragStart.Y) > SystemInformation.DragSize.Height)) {
                    var node = dragNode; dragNode = null; if (native.ContainsKey(node)) ItemDrag?.Invoke(this, new NavigationNodeEventArgs(node));
                }
            };
        }
        public new NavigationNodes Nodes { get; }
        public NavigationNode SelectedNode => FocusedNode?.Tag as NavigationNode;
        public bool AllowItemDrag { get; set; }
        private NavigationNode dropHighlight;
        public NavigationNode DropHighlight { get => dropHighlight; set { dropHighlight = value; Invalidate(); } }
        public event EventHandler<NavigationNodeEventArgs> NodeExpanding;
        public event EventHandler SelectedNodeChanged;
        public event EventHandler<NavigationNodeEventArgs> ItemDrag;
        public NavigationNode HitTest(Point point) => CalcHitInfo(point).Node?.Tag as NavigationNode;
        public NavigationNode GetDropTarget(Point point) => HitTest(point);
        private void Rebuild() {
            if (rebuilding || IsDisposed) return;
            if (insideExpand && IsHandleCreated) {
                if (!rebuildPosted) { rebuildPosted = true; BeginInvoke((Action)(() => { rebuildPosted = false; Rebuild(); })); }
                return;
            }
            rebuilding = true; BeginUpdate();
            try {
                var expanded = new HashSet<NavigationNode>(native.Where(p => p.Value.Expanded).Select(p => p.Key));
                var selected = SelectedNode;
                base.Nodes.Clear(); native.Clear(); images.Images.Clear();
                foreach (var node in Nodes) Append(node, null, expanded);
                if (selected != null && native.TryGetValue(selected, out var focused)) FocusedNode = focused;
            } finally { EndUpdate(); rebuilding = false; }
        }
        private void Append(NavigationNode node, NativeNode parent, HashSet<NavigationNode> expanded) {
            var added = AppendNode(new object[] { node.Text }, parent); added.Tag = node; native[node] = added;
            if (node.Image != null) { images.Images.Add(node.Image); added.StateImageIndex = images.Images.Count - 1; }
            added.HasChildren = node.HasChildrenHint || node.Children.Count > 0;
            node.Changed = Rebuild;
            foreach (var child in node.Children) Append(child, added, expanded);
            added.Expanded = expanded.Contains(node) || node.ExpandRequested || (parent == null && node.Tag == null);
            node.ExpandRequested = false;
        }
        protected override void Dispose(bool disposing) { if (disposing) images.Dispose(); base.Dispose(disposing); }
    }
}
