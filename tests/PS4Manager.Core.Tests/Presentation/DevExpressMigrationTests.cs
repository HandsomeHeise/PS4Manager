using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Session;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class DevExpressMigrationTests
    {
        [Theory]
        [InlineData(false, 1440, 900)]
        [InlineData(true, 1440, 900)]
        [InlineData(false, 1080, 720)]
        public void Dashboard_uses_native_ribbon_and_renders_reference_layout(bool light, int width, int height)
        {
            StaTest.Run(() => {
                string previous = AppTheme.Current;
                try {
                    AppTheme.Current = light ? AppTheme.Light : AppTheme.Dark;
                    using (var form = new DashboardForm(new ManagerSettings())) {
                        form.Show(); form.ClientSize = new Size(width, height); form.PerformLayout(); Application.DoEvents();
                        Assert.IsAssignableFrom<XtraForm>(form);
                        var ribbon = Controls(form).OfType<DevExpress.XtraBars.Ribbon.RibbonControl>().Single();
                        Assert.Equal(new[] { "Start", "PS4 / Modding", "Games", "Tools", "View" }, ribbon.Pages.Cast<DevExpress.XtraBars.Ribbon.RibbonPage>().Select(p => p.Text));
                        Assert.Equal(7, Controls(form).OfType<GroupControl>().Count());
                        Assert.Contains(Controls(form).OfType<UiLabel>(), l => l.Text == "No console connected");
                        var root = Environment.GetEnvironmentVariable("PS4MANAGER_VISUAL_DIR");
                        if (!string.IsNullOrEmpty(root)) {
                            Directory.CreateDirectory(root);
                            using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "dashboard-" + (light ? "light" : "dark") + "-" + width + ".png")); }
                        }
                    }
                } finally { AppTheme.Current = previous; }
            });
        }

        [Fact]
        public void App_assemblies_reference_DevExpress_and_do_not_reference_the_removed_framework()
        {
            foreach (var assembly in new[] { typeof(DashboardForm).Assembly, typeof(FileManager.App.FileManagerForm).Assembly, typeof(PS4Manager.Gallery.App.MediaGalleryForm).Assembly, typeof(AppWindow).Assembly }) {
                Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name.StartsWith("UIFramework", StringComparison.Ordinal));
                Assert.Contains(assembly.GetReferencedAssemblies(), a => a.Name.StartsWith("DevExpress", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void Native_grid_preserves_sorted_row_identity_selection_and_edit_commit()
        {
            StaTest.Run(() => {
                var rows = new List<Row> { new Row { Name = "Zulu" }, new Row { Name = "Alpha" } };
                var source = new ListDataSource<Row>(rows); source.Map("Name", r => r.Name); source.Map("Row", r => r);
                Row edited = null; source.MapSet("Name", (r, value) => { edited = r; r.Name = (string)value; });
                var filtered = new FilteredSource(source, (s, i) => true); var sorted = new SortedSource(filtered); sorted.Sort("Name", SortDirection.Ascending, i => 0);
                using (var form = new AppWindow()) using (var grid = new AppGrid { Dock = DockStyle.Fill }) {
                    grid.Columns.Add(new GridColumn("Name", "Name")); grid.DataSource = sorted; form.Controls.Add(grid); form.Show(); Application.DoEvents();
                    Assert.IsAssignableFrom<DevExpress.XtraGrid.GridControl>(grid);
                    Assert.Equal("Alpha", grid.View.GetRowCellDisplayText(0, "Name"));
                    grid.Selection.Select(0); Assert.Equal(new[] { 0 }, grid.Selection.SelectedRows); Assert.Same(rows[1], sorted.GetValue(grid.Selection.CurrentRow, "Row"));
                    grid.View.SetRowCellValue(0, "Name", "Renamed");
                    Assert.Same(rows[1], edited); Assert.Equal("Renamed", rows[1].Name);
                    grid.Selection.ExtendTo(1); Assert.Equal(2, grid.Selection.SelectedRows.Count());
                }
            });
        }

        [Fact]
        public void Native_tree_keeps_remote_identity_when_async_children_are_replaced()
        {
            StaTest.Run(() => {
                using (var tree = new AppTree()) {
                    var console = new NavigationNode("PS4") { Tag = "/", HasChildrenHint = true }; tree.Nodes.Add(console);
                    var child = new NavigationNode("Game") { Tag = "/user/app/CUSA02624" }; console.Children.ReplaceWith(new[] { child });
                    var native = tree.GetNodeList().Single(n => ReferenceEquals(n.Tag, child)); tree.FocusedNode = native;
                    Assert.Same(child, tree.SelectedNode);
                    child.Text = "Black Ops III";
                    Assert.Same(child, tree.SelectedNode); Assert.Equal("/user/app/CUSA02624", tree.SelectedNode.Tag);
                    console.Children.ReplaceWith(new NavigationNode[0]); Assert.DoesNotContain(tree.GetNodeList(), n => ReferenceEquals(n.Tag, child));
                }
            });
        }

        [Fact]
        public void Gallery_selection_preserves_media_identity_and_marshals_thumbnail_completion()
        {
            StaTest.Run(() => {
                using (var form = new AppWindow()) using (var gallery = new AppGallery { Dock = DockStyle.Fill }) {
                    form.Controls.Add(gallery); form.Show();
                    var group = new GalleryItemGroup { Caption = "Game" }; var media = new object(); var item = new GalleryItem { Caption = "Capture", Tag = media };
                    group.Items.Add(item); gallery.Gallery.Groups.Add(group); item.Checked = true;
                    Assert.Same(media, gallery.Gallery.CheckedItems.Single().Tag);
                    var image = new Bitmap(16, 16); Task.Run(() => item.Image = image).GetAwaiter().GetResult(); Application.DoEvents();
                    Assert.Same(image, item.Image);
                    item.Image = null; image.Dispose(); gallery.Gallery.Groups.Clear(); Assert.Empty(gallery.Gallery.CheckedItems);
                }
            });
        }
        private sealed class Row { public string Name; }
        [Fact]
        public void Tree_drag_uses_the_pressed_folder_even_when_pointer_crosses_the_next_row()
        {
            StaTest.Run(() => {
                using (var form = new AppWindow()) using (var tree = new TreeHarness { Dock = DockStyle.Fill, AllowItemDrag = true }) {
                    var first = new NavigationNode("First") { Tag = "/first" }; var second = new NavigationNode("Second") { Tag = "/second" };
                    tree.Nodes.Add(first); tree.Nodes.Add(second); form.Controls.Add(tree); form.Show(); Application.DoEvents();
                    int firstY = Enumerable.Range(0, tree.Height).First(y => ReferenceEquals(tree.HitTest(new Point(50, y)), first));
                    int secondY = Enumerable.Range(0, tree.Height).First(y => ReferenceEquals(tree.HitTest(new Point(50, y)), second));
                    NavigationNode dragged = null; int events = 0; tree.ItemDrag += (s, e) => { dragged = e.Node; events++; };
                    tree.Press(new Point(50, firstY + 2)); tree.MovePointer(new Point(80, secondY + 2)); tree.MovePointer(new Point(100, secondY + 4));
                    Assert.Same(first, dragged); Assert.Equal(1, events);
                }
            });
        }
        [Fact]
        public void Type_ahead_searches_file_name_when_another_column_is_focused()
        {
            StaTest.Run(() => {
                var source = new ListDataSource<Row>(new[] { new Row { Name = "Zulu" }, new Row { Name = "Alpha" } }); source.Map("Name", r => r.Name); source.Map("Other", r => "Other");
                using (var form = new AppWindow()) using (var grid = new GridHarness { Dock = DockStyle.Fill }) {
                    grid.Columns.Add(new GridColumn("Name", "Name")); grid.Columns.Add(new GridColumn("Other", "Other"));
                    grid.TypeAheadColumnKey = "Name"; grid.DataSource = source; form.Controls.Add(grid); form.Show(); Application.DoEvents();
                    grid.Selection.Select(0); grid.View.FocusedColumn = grid.Columns[1]; grid.Type('a'); Application.DoEvents();
                    Assert.Equal(1, grid.Selection.CurrentRow); Assert.Equal("Name", grid.View.FocusedColumn.FieldName);
                }
            });
        }
        [Fact]
        public void Breadcrumb_nodes_and_direct_path_entry_keep_unix_remote_paths()
        {
            StaTest.Run(() => {
                using (var form = new AppWindow()) using (var address = new AddressBar { Dock = DockStyle.Top }) {
                    form.Controls.Add(address); form.Show(); address.Path = "/user/app/CUSA02624";
                    Assert.Equal("CUSA02624", address.SelectedNode.Caption); string clicked = null, submitted = null;
                    address.SegmentClicked += (s, e) => clicked = e.Path; address.PathSubmitted += (s, e) => submitted = e.Path;
                    address.SelectedNode = address.SelectedNode.Parent; Assert.Equal("/user/app", clicked);
                    address.BeginEdit(); address.Text = "/data/GoldHEN/plugins";
                    var arguments = new object[] { new Message(), Keys.Enter };
                    typeof(AddressBar).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(address, arguments);
                    Assert.Equal("/data/GoldHEN/plugins", submitted); Assert.Equal("/user/app/CUSA02624", address.Path);
                }
            });
        }
        [Fact]
        public void Single_line_editor_routes_enter_to_form_default_but_script_editor_accepts_newlines()
        {
            StaTest.Run(() => {
                using (var form = new AppWindow()) using (var edit = new UiTextBox { Dock = DockStyle.Top }) using (var save = new UiButton { Text = "Save", Dock = DockStyle.Bottom }) {
                    int clicked = 0; save.Click += (s, e) => clicked++;
                    form.Controls.Add(edit); form.Controls.Add(save); form.AcceptButton = save; form.Show(); edit.Focus(); Application.DoEvents();
                    Assert.False(edit.Properties.AcceptsReturn);
                    var message = Message.Create(edit.Handle, 0x100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                    Assert.True(edit.PreProcessMessage(ref message)); Assert.Equal(1, clicked);
                    edit.Multiline = true; Assert.True(edit.Properties.AcceptsReturn);
                    edit.ReadOnly = true; Assert.False(edit.Properties.AcceptsReturn);
                }
            });
        }
        private sealed class TreeHarness : AppTree { public void Press(Point point) => base.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); public void MovePointer(Point point) => base.OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, point.X, point.Y, 0)); }
        private sealed class GridHarness : AppGrid { public void Type(char key) => base.OnKeyPress(new KeyPressEventArgs(key)); }
        private static IEnumerable<Control> Controls(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Controls(child)) yield return nested; } }
    }
}
