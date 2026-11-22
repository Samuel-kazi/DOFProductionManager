using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DOF.Desktop.ViewModels.Pages;

namespace DOF.Desktop.Views.Pages;

public partial class ReportsView : UserControl
{
    private ReportsViewModel? _vm;

    public ReportsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
        }
        _vm = DataContext as ReportsViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            BuildTable();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportsViewModel.Report))
        {
            BuildTable();
        }
    }

    /// <summary>One header row and one row per report line; rows linked to a record open it when clicked.</summary>
    private void BuildTable()
    {
        Table.Children.Clear();
        Table.ColumnDefinitions.Clear();
        Table.RowDefinitions.Clear();
        if (_vm?.Report is not { } report)
        {
            return;
        }

        foreach (var _ in report.Columns)
        {
            Table.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }
        Table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < report.Columns.Count; c++)
        {
            var header = new TextBlock { Text = report.Columns[c], FontWeight = FontWeight.SemiBold, Margin = new Thickness(6, 4, 18, 6) };
            Grid.SetColumn(header, c);
            Table.Children.Add(header);
        }

        for (var r = 0; r < report.Rows.Count; r++)
        {
            Table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = report.Rows[r];
            var index = r;
            var linked = report.Links.ElementAtOrDefault(r) is not null;
            for (var c = 0; c < report.Columns.Count; c++)
            {
                var text = new TextBlock
                {
                    Text = c < row.Count ? row[c] : string.Empty,
                    Margin = new Thickness(6, 3, 18, 3),
                    MaxWidth = 360,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Control cell = text;
                if (linked && c == 0)
                {
                    var open = new Button { Content = text, Padding = new Thickness(0), Classes = { "toolbar" } };
                    ToolTip.SetTip(open, "Open");
                    open.Click += async (_, _) => await _vm.OpenRowAsync(index);
                    // Theme-aware (light/dark) like {DynamicResource Brush.Accent} in XAML.
                    text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Brush.Accent"));
                    cell = open;
                }
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                Table.Children.Add(cell);
            }
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export report",
            SuggestedFileName = _vm.SuggestedFileName,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file is null)
        {
            return;
        }
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await _vm.ExportCsvAsync(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.ReportExportFailure(ex.Message);
        }
    }
}
