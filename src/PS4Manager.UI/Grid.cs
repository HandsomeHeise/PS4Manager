using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraGrid.Views.Grid;

namespace PS4Manager.UI
{
    public interface IRowSource
    {
        int RowCount { get; }
        object GetValue(int row, string key);
        void SetValue(int row, string key, object value);
        Image GetImage(int row, string key);
    }

    public sealed class ListDataSource<T> : IRowSource
    {
        private readonly IList<T> rows;
        private readonly Dictionary<string, Func<T, object>> values = new Dictionary<string, Func<T, object>>();
        private readonly Dictionary<string, Action<T, object>> setters = new Dictionary<string, Action<T, object>>();
        private readonly Dictionary<string, Func<T, Image>> images = new Dictionary<string, Func<T, Image>>();
        public ListDataSource(IList<T> rows) { this.rows = rows; }
        public int RowCount => rows.Count;
        public void Map(string key, Func<T, object> value) => values[key] = value;
        public void MapSet(string key, Action<T, object> value) => setters[key] = value;
        public void MapImage(string key, Func<T, Image> value) => images[key] = value;
        public object GetValue(int row, string key) => row >= 0 && row < RowCount && values.TryGetValue(key, out var get) ? get(rows[row]) : null;
        public void SetValue(int row, string key, object value) { if (row >= 0 && row < RowCount && setters.TryGetValue(key, out var set)) set(rows[row], value); }
        public Image GetImage(int row, string key) => row >= 0 && row < RowCount && images.TryGetValue(key, out var get) ? get(rows[row]) : null;
    }

    public sealed class FilteredSource : IRowSource
    {
        private readonly IRowSource source;
        private readonly Func<IRowSource, int, bool> predicate;
        private int[] indexes = new int[0];
        public FilteredSource(IRowSource source, Func<IRowSource, int, bool> predicate) { this.source = source; this.predicate = predicate; Refresh(); }
        public void Refresh() => indexes = Enumerable.Range(0, source.RowCount).Where(i => predicate(source, i)).ToArray();
        public int RowCount => indexes.Length;
        public object GetValue(int row, string key) => source.GetValue(indexes[row], key);
        public void SetValue(int row, string key, object value) => source.SetValue(indexes[row], key, value);
        public Image GetImage(int row, string key) => source.GetImage(indexes[row], key);
    }

    public enum SortDirection { Ascending, Descending }
    public sealed class SortedSource : IRowSource
    {
        private readonly IRowSource source;
        private int[] indexes = new int[0];
        public SortedSource(IRowSource source) { this.source = source; indexes = Enumerable.Range(0, source.RowCount).ToArray(); }
        public void Sort(string key, SortDirection direction, Func<int, int> rank) {
            var rows = Enumerable.Range(0, source.RowCount).OrderBy(i => rank(i));
            indexes = (direction == SortDirection.Ascending ? rows.ThenBy(i => source.GetValue(i, key), ValueComparer.Instance) : rows.ThenByDescending(i => source.GetValue(i, key), ValueComparer.Instance)).ToArray();
        }
        private sealed class ValueComparer : IComparer<object> {
            public static readonly ValueComparer Instance = new ValueComparer();
            public int Compare(object x, object y) { if (x is string a && y is string b) return StringComparer.CurrentCultureIgnoreCase.Compare(a, b); return System.Collections.Comparer.DefaultInvariant.Compare(x, y); }
        }
        public int RowCount => indexes.Length;
        public object GetValue(int row, string key) => source.GetValue(indexes[row], key);
        public void SetValue(int row, string key, object value) => source.SetValue(indexes[row], key, value);
        public Image GetImage(int row, string key) => source.GetImage(indexes[row], key);
    }

    public class GridColumn : DevExpress.XtraGrid.Columns.GridColumn
    {
        public GridColumn(string key, string caption) { FieldName = key; Caption = caption; UnboundDataType = typeof(object); Visible = true; OptionsColumn.AllowSort = DefaultBoolean.False; }
        public string Key => FieldName;
        public new bool ReadOnly { get => OptionsColumn.ReadOnly; set => OptionsColumn.ReadOnly = value; }
        public bool ShowImage { get; set; }
        public Func<Control> EditorFactory { get; set; }
    }
    public enum GridEditTrigger { None, F2 }
    public sealed class GridDragEventArgs : EventArgs { public GridDragEventArgs(IEnumerable<int> rows) { Rows = rows.ToArray(); } public IReadOnlyList<int> Rows { get; } }

    public class AppGrid : DevExpress.XtraGrid.GridControl
    {
        private IRowSource source;
        private bool refreshing, allowEdit;
        private Point dragStart;
        private bool dragPending;
        private int dropHighlight = -1;
        public int DropHighlight { get => dropHighlight; set { dropHighlight = value; Invalidate(); } }
        public GridView View { get; }
        public Collection<GridColumn> Columns { get; }
        public GridSelection Selection { get; }
        public bool AllowItemDrag { get; set; }
        public GridEditTrigger EditTriggers { get; set; }
        private string typeAheadColumnKey, typeAheadText = "";
        private DateTime lastTypeAhead;
        public string TypeAheadColumnKey {
            get => typeAheadColumnKey;
            set { typeAheadColumnKey = value; View.OptionsBehavior.AllowIncrementalSearch = !string.IsNullOrEmpty(value); }
        }
        public event EventHandler<int> HeaderClick;
        public event EventHandler<int> RowActivated;
        public event EventHandler<GridDragEventArgs> ItemDrag;
        public AppGrid() {
            View = new GridView(this); MainView = View; ViewCollection.Add(View);
            View.OptionsBehavior.AutoPopulateColumns = false; View.OptionsView.ShowGroupPanel = false;
            View.OptionsView.ShowIndicator = false; View.OptionsView.ColumnAutoWidth = false;
            View.OptionsSelection.MultiSelect = true; View.OptionsSelection.EnableAppearanceFocusedCell = false;
            View.OptionsBehavior.EditorShowMode = EditorShowMode.MouseUp;
            View.OptionsFind.AllowFindPanel = false;
            View.OptionsCustomization.AllowSort = false; View.OptionsCustomization.AllowFilter = false;
            Columns = new ColumnCollection(View);
            Selection = new GridSelection(this);
            View.FocusedRowChanged += (s, e) => Selection.RaiseChanged();
            View.SelectionChanged += (s, e) => Selection.RaiseChanged();
            View.CustomUnboundColumnData += (s, e) => {
                if (source == null || e.ListSourceRowIndex < 0 || e.ListSourceRowIndex >= source.RowCount) return;
                if (e.IsGetData) e.Value = source.GetValue(e.ListSourceRowIndex, e.Column.FieldName);
                if (e.IsSetData) source.SetValue(e.ListSourceRowIndex, e.Column.FieldName, e.Value);
            };
            View.ShowingEditor += (s, e) => e.Cancel = !allowEdit;
            View.HiddenEditor += (s, e) => allowEdit = false;
            View.DoubleClick += (s, e) => { var row = GetDropTarget(PointToClient(Cursor.Position)); if (row >= 0) RowActivated?.Invoke(this, row); };
            View.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !View.IsEditing) { e.Handled = true; RowActivated?.Invoke(this, Selection.CurrentRow); } };
            View.MouseDown += (s, e) => {
                dragStart = e.Location;
                var hit = View.CalcHitInfo(e.Location);
                dragPending = e.Button == MouseButtons.Left && hit.InRow;
                if (e.Button == MouseButtons.Left && hit.InColumn && hit.Column != null) HeaderClick?.Invoke(this, Columns.IndexOf((GridColumn)hit.Column));
            };
            View.MouseMove += (s, e) => {
                if (AllowItemDrag && dragPending && e.Button == MouseButtons.Left && !View.IsEditing &&
                    (Math.Abs(e.X - dragStart.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - dragStart.Y) > SystemInformation.DragSize.Height)) {
                    dragPending = false; if (Selection.SelectedRows.Any()) ItemDrag?.Invoke(this, new GridDragEventArgs(Selection.SelectedRows));
                }
            };
            View.CustomDrawCell += (s, e) => {
                var column = e.Column as GridColumn;
                if (column == null || !column.ShowImage || source == null) return;
                var image = source.GetImage(View.GetDataSourceRowIndex(e.RowHandle), column.Key);
                if (image == null) return;
                e.Appearance.DrawBackground(e.Cache, e.Bounds);
                e.Cache.Graphics.DrawImage(image, new Rectangle(e.Bounds.Left + 3, e.Bounds.Top + Math.Max(0, (e.Bounds.Height - 18) / 2), 18, 18));
                TextRenderer.DrawText(e.Cache.Graphics, e.DisplayText, e.Appearance.Font, new Rectangle(e.Bounds.Left + 26, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 28), e.Bounds.Height), e.Appearance.ForeColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                e.Handled = true;
            };
            View.RowHeight = 26;
            View.RowStyle += (s, e) => { if (dropHighlight >= 0 && View.GetDataSourceRowIndex(e.RowHandle) == dropHighlight) { e.Appearance.BackColor = AppTheme.Accent; e.Appearance.ForeColor = Color.White; e.HighPriority = true; } };
        }
        public new IRowSource DataSource {
            get => source;
            set {
                dragPending = false; source = value; base.DataSource = value == null ? null : Enumerable.Range(0, value.RowCount).Select(i => new BoundRow { Index = i }).ToList();
                View.ClearSelection(); View.FocusedRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle; Selection.RaiseChanged();
            }
        }
        public new void Invalidate() {
            if (View != null && !refreshing && !IsDisposed) {
                refreshing = true; try { View.RefreshData(); } finally { refreshing = false; }
            }
            base.Invalidate();
        }
        public int GetDropTarget(Point point) { var hit = View.CalcHitInfo(point); return hit.InRow ? View.GetDataSourceRowIndex(hit.RowHandle) : -1; }
        public void BeginEdit(int row, int column) {
            if (source == null || row < 0 || row >= source.RowCount || column < 0 || column >= Columns.Count || Columns[column].ReadOnly) return;
            View.FocusedRowHandle = View.GetRowHandle(row); View.FocusedColumn = Columns[column]; allowEdit = true; View.ShowEditor(); View.ActiveEditor?.SelectAll();
        }
        protected override void OnKeyPress(KeyPressEventArgs e) {
            if (!char.IsControl(e.KeyChar) && !View.IsEditing && !string.IsNullOrEmpty(typeAheadColumnKey)) {
                var column = Columns.FirstOrDefault(c => c.Key == typeAheadColumnKey);
                if (column != null) {
                    var now = DateTime.UtcNow; if ((now - lastTypeAhead).TotalSeconds > 1) typeAheadText = "";
                    lastTypeAhead = now; typeAheadText += e.KeyChar; View.FocusedColumn = column; View.StartIncrementalSearch(typeAheadText); e.Handled = true; return;
                }
            }
            base.OnKeyPress(e);
        }
        public sealed class BoundRow { public int Index { get; set; } }
        private sealed class ColumnCollection : Collection<GridColumn> {
            private readonly GridView view;
            public ColumnCollection(GridView view) { this.view = view; }
            protected override void InsertItem(int index, GridColumn item) { base.InsertItem(index, item); view.Columns.Add(item); item.VisibleIndex = index; }
        }
    }

    public sealed class GridSelection
    {
        private readonly AppGrid grid;
        internal GridSelection(AppGrid grid) { this.grid = grid; }
        public event EventHandler Changed;
        internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public int CurrentRow => grid.View.FocusedRowHandle < 0 ? -1 : grid.View.GetDataSourceRowIndex(grid.View.FocusedRowHandle);
        public IEnumerable<int> SelectedRows => grid.View.GetSelectedRows().Where(r => r >= 0).Select(grid.View.GetDataSourceRowIndex);
        public bool IsSelected(int row) => grid.View.IsRowSelected(grid.View.GetRowHandle(row));
        public void Clear() { grid.View.ClearSelection(); grid.View.FocusedRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle; RaiseChanged(); }
        public void Select(int row) {
            grid.View.ClearSelection(); if (grid.DataSource == null || row < 0 || row >= grid.DataSource.RowCount) return;
            grid.View.FocusedRowHandle = grid.View.GetRowHandle(row); grid.View.SelectRow(grid.View.FocusedRowHandle); RaiseChanged();
        }
        public void ExtendTo(int row) { if (CurrentRow >= 0) grid.View.SelectRange(grid.View.FocusedRowHandle, grid.View.GetRowHandle(row)); RaiseChanged(); }
    }
}
