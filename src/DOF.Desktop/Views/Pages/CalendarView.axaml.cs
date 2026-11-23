using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DOF.Desktop.Controls;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Pages;

namespace DOF.Desktop.Views.Pages;

/// <summary>
/// Calendar interactions that are purely visual: click-to-open, drag-to-reschedule (month: by day;
/// week/day: by day and 15-minute steps) and scrolling the time grid to the working day.
/// </summary>
public partial class CalendarView : UserControl
{
    private const double DragThreshold = 4;
    private const double SnapMinutes = 15;
    private const double HourHeight = 48;

    private Control? _pressed;
    private Point _pressPoint;
    private bool _dragging;

    public CalendarView() => InitializeComponent();

    private CalendarViewModel? ViewModel => DataContext as CalendarViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ScrollToWorkingDay();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarViewModel.Mode))
        {
            Dispatcher.UIThread.Post(ScrollToWorkingDay, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Week and day views open at 07:00 rather than midnight.</summary>
    private void ScrollToWorkingDay() => TimeScroll.Offset = new Vector(0, 7 * HourHeight);

    private void OnMonthCellDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: MonthDayCell cell } && ViewModel is { CanCreate: true } vm)
        {
            vm.NewEventCommand.Execute(cell.Date);
        }
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = control;
        _pressPoint = e.GetPosition(this);
        _dragging = false;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(sender, _pressed) || _pressed is null)
        {
            return;
        }

        var delta = e.GetPosition(this) - _pressPoint;
        if (!_dragging && (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold) && CanDrag(_pressed))
        {
            _dragging = true;
            _pressed.ZIndex = 100;
            _pressed.Opacity = 0.85;
        }

        if (_dragging)
        {
            _pressed.RenderTransform = new TranslateTransform(delta.X, delta.Y);
        }
    }

    private async void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(sender, _pressed) || _pressed is not { } control || ViewModel is not { } vm)
        {
            return;
        }

        var delta = e.GetPosition(this) - _pressPoint;
        var wasDragging = _dragging;
        Reset();
        e.Pointer.Capture(null);
        e.Handled = true;

        var item = ItemOf(control);
        if (item is null)
        {
            return;
        }

        if (!wasDragging)
        {
            await vm.OpenItemCommand.ExecuteAsync(item);
            return;
        }

        if (NewStart(control, item, delta) is { } newStart && newStart != item.Start)
        {
            await vm.RescheduleAsync(item, newStart);
        }
    }

    private void OnItemCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(sender, _pressed))
        {
            Reset();
        }
    }

    private void Reset()
    {
        if (_pressed is not null)
        {
            _pressed.RenderTransform = null;
            _pressed.ZIndex = 0;
            _pressed.Opacity = 1;
        }
        _pressed = null;
        _dragging = false;
    }

    private static CalendarItemViewModel? ItemOf(Control control) => control.DataContext switch
    {
        CalendarItemViewModel item => item,
        TimedItemViewModel timed => timed.Item,
        _ => null,
    };

    private bool CanDrag(Control control) =>
        ViewModel is { IsAgenda: false } && ItemOf(control) is { CanReschedule: true };

    /// <summary>Translates a drag distance into a new start: whole days horizontally (and per row in the month), 15 minutes vertically in the time grid.</summary>
    private static DateTime? NewStart(Control control, CalendarItemViewModel item, Vector delta)
    {
        if (control.DataContext is TimedItemViewModel && control.FindAncestorOfType<TimeGridPanel>() is { } grid)
        {
            var days = (int)Math.Round(delta.X / Math.Max(1, grid.Bounds.Width));
            var minutes = Math.Round(delta.Y / (grid.HourHeight / 60) / SnapMinutes) * SnapMinutes;
            return item.Start.AddDays(days).AddMinutes(minutes);
        }

        var monthCell = control.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("monthCell"));
        if (monthCell is not null)
        {
            var columns = (int)Math.Round(delta.X / Math.Max(1, monthCell.Bounds.Width));
            var rows = (int)Math.Round(delta.Y / Math.Max(1, monthCell.Bounds.Height));
            return item.Start.AddDays(columns + 7 * rows);
        }

        var allDayCell = control.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("allDayCell"));
        if (allDayCell is not null)
        {
            return item.Start.AddDays((int)Math.Round(delta.X / Math.Max(1, allDayCell.Bounds.Width)));
        }

        return null;
    }

    /// <summary>Asks where to save (starting in the export folder) and downloads the chosen period's PDF.</summary>
    private async void OnDownloadPdf(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || sender is not MenuItem { Tag: string period }
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save schedule PDF",
            SuggestedFileName = vm.SuggestedPdfName(period),
            SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(vm.ExportDirectory),
            DefaultExtension = "pdf",
            FileTypeChoices = [FilePickerFileTypes.Pdf],
        });
        if (file is null)
        {
            return;
        }
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await vm.DownloadPdfAsync(period, stream, file.Name, file.TryGetLocalPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.ReportPdfFailure(ex.Message);
        }
    }

    private async void OnOpenPdf(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SavedPdfPath is { } path && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }
}
