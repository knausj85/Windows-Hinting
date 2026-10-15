// PROTOTYPE — throwaway. Fixture for prototype/PagingProbe: one window full of scrollable
// controls with ~500 items each, so the probe can measure paging mechanisms per framework.
//   PagingFixture            -> WinForms window ("PagingFixture WinForms")
//   PagingFixture --wpf      -> WPF window      ("PagingFixture WPF")
using System;
using System.Linq;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PagingFixture;

internal static class Program
{
    private const int N = 500;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--wpf")) RunWpf(); else RunWinForms();
    }

    private static string Lines(string prefix) =>
        string.Join("\r\n", Enumerable.Range(0, N).Select(i => $"{prefix} line {i:000} - the quick brown fox jumps over the lazy dog"));

    // ---------------- WinForms ----------------
    private static void RunWinForms()
    {
        WF.Application.EnableVisualStyles();
        var f = new WF.Form { Text = "PagingFixture WinForms", Width = 1500, Height = 700, StartPosition = WF.FormStartPosition.Manual, Left = 40, Top = 40 };
        var grid = new WF.TableLayoutPanel { Dock = WF.DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
        for (int i = 0; i < 6; i++) grid.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f / 6));

        var lb = new WF.ListBox { Dock = WF.DockStyle.Fill, Name = "WfListBox", AccessibleName = "WfListBox", IntegralHeight = false };
        lb.Items.AddRange(Enumerable.Range(0, N).Select(i => (object)$"ListBox item {i:000}").ToArray());

        var tv = new WF.TreeView { Dock = WF.DockStyle.Fill, Name = "WfTreeView", AccessibleName = "WfTreeView", HideSelection = false };
        for (int p = 0; p < 100; p++)
        {
            var n = tv.Nodes.Add($"Node {p:000}");
            for (int c = 0; c < 4; c++) n.Nodes.Add($"Node {p:000}.{c}");
        }
        tv.ExpandAll();

        var lv = new WF.ListView { Dock = WF.DockStyle.Fill, View = WF.View.Details, Name = "WfListView", AccessibleName = "WfListView", FullRowSelect = true };
        lv.Columns.Add("Item", 140); lv.Columns.Add("Sub", 80);
        lv.Items.AddRange(Enumerable.Range(0, N).Select(i => new WF.ListViewItem(new[] { $"ListView item {i:000}", $"sub {i}" })).ToArray());

        var dgv = new WF.DataGridView { Dock = WF.DockStyle.Fill, Name = "WfDataGridView", AccessibleName = "WfDataGridView", AllowUserToAddRows = false, ReadOnly = true };
        dgv.Columns.Add("c0", "Row"); dgv.Columns.Add("c1", "Value");
        for (int i = 0; i < N; i++) dgv.Rows.Add($"Grid row {i:000}", i);

        var tb = new WF.TextBox { Dock = WF.DockStyle.Fill, Multiline = true, ScrollBars = WF.ScrollBars.Both, WordWrap = false, Name = "WfTextBox", AccessibleName = "WfTextBox", Text = Lines("TextBox") };
        var rtb = new WF.RichTextBox { Dock = WF.DockStyle.Fill, WordWrap = false, Name = "WfRichTextBox", AccessibleName = "WfRichTextBox", Text = Lines("RichTextBox").Replace("\r\n", "\n") };

        grid.Controls.Add(lb, 0, 0); grid.Controls.Add(tv, 1, 0); grid.Controls.Add(lv, 2, 0);
        grid.Controls.Add(dgv, 3, 0); grid.Controls.Add(tb, 4, 0); grid.Controls.Add(rtb, 5, 0);
        f.Controls.Add(grid);
        // Keep caret/selection at the start so selection side effects are detectable.
        f.Shown += (_, _) => { tb.Select(0, 0); rtb.Select(0, 0); lb.SelectedIndex = 0; tv.SelectedNode = tv.Nodes[0]; tv.Nodes[0].EnsureVisible(); };
        WF.Application.Run(f);
    }

    // ---------------- WPF ----------------
    private static void RunWpf()
    {
        var app = new W.Application();
        var w = new W.Window { Title = "PagingFixture WPF", Width = 1500, Height = 700, Left = 60, Top = 60, WindowStartupLocation = W.WindowStartupLocation.Manual };
        var g = new WC.Grid();
        for (int i = 0; i < 6; i++) g.ColumnDefinitions.Add(new WC.ColumnDefinition());

        var lb = new WC.ListBox { Name = "WpfListBox" };
        W.Automation.AutomationProperties.SetName(lb, "WpfListBox");
        foreach (var i in Enumerable.Range(0, N)) lb.Items.Add($"WPF ListBox item {i:000}");

        var tv = new WC.TreeView { Name = "WpfTreeView" };
        W.Automation.AutomationProperties.SetName(tv, "WpfTreeView");
        for (int p = 0; p < 100; p++)
        {
            var n = new WC.TreeViewItem { Header = $"WPF Node {p:000}", IsExpanded = true };
            for (int c = 0; c < 4; c++) n.Items.Add(new WC.TreeViewItem { Header = $"WPF Node {p:000}.{c}" });
            tv.Items.Add(n);
        }

        var dg = new WC.DataGrid { Name = "WpfDataGrid", AutoGenerateColumns = true, IsReadOnly = true,
            ItemsSource = Enumerable.Range(0, N).Select(i => new Row { Name = $"WPF grid row {i:000}", Value = i }).ToList() };
        W.Automation.AutomationProperties.SetName(dg, "WpfDataGrid");

        var tb = new WC.TextBox { Name = "WpfTextBox", Text = Lines("WPF TextBox"), AcceptsReturn = true,
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Auto, TextWrapping = W.TextWrapping.NoWrap };
        W.Automation.AutomationProperties.SetName(tb, "WpfTextBox");

        // Nested case: a ListBox with its own scrolling disabled, inside an outer ScrollViewer.
        var inner = new WC.ListBox { Name = "WpfNestedListBox" };
        W.Automation.AutomationProperties.SetName(inner, "WpfNestedListBox");
        WC.ScrollViewer.SetVerticalScrollBarVisibility(inner, WC.ScrollBarVisibility.Disabled);
        foreach (var i in Enumerable.Range(0, N)) inner.Items.Add($"WPF nested item {i:000}");
        var outer = new WC.ScrollViewer { Name = "WpfOuterScrollViewer", Content = inner, VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto };
        W.Automation.AutomationProperties.SetName(outer, "WpfOuterScrollViewer");

        var rtb = new WC.RichTextBox { Name = "WpfRichTextBox", VerticalScrollBarVisibility = WC.ScrollBarVisibility.Visible };
        W.Automation.AutomationProperties.SetName(rtb, "WpfRichTextBox");
        rtb.Document.Blocks.Clear();
        foreach (var i in Enumerable.Range(0, N)) rtb.Document.Blocks.Add(new W.Documents.Paragraph(new W.Documents.Run($"WPF RichTextBox para {i:000}")) { Margin = new W.Thickness(0) });

        Place(g, lb, 0); Place(g, tv, 1); Place(g, dg, 2); Place(g, tb, 3); Place(g, outer, 4); Place(g, rtb, 5);
        w.Content = g;
        app.Run(w);
    }

    private static void Place(WC.Grid g, W.UIElement e, int col) { WC.Grid.SetColumn(e, col); g.Children.Add(e); }

    public sealed class Row { public string Name { get; set; } = ""; public int Value { get; set; } }
}
