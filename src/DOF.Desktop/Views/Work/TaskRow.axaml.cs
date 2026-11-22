using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Windows.Input;

namespace DOF.Desktop.Views.Work;

/// <summary>A task row. The host sets <see cref="ToggleCommand"/> and <see cref="OpenCommand"/>; the row passes itself.</summary>
public partial class TaskRow : UserControl
{
    public static readonly StyledProperty<ICommand?> ToggleCommandProperty =
        AvaloniaProperty.Register<TaskRow, ICommand?>(nameof(ToggleCommand));

    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<TaskRow, ICommand?>(nameof(OpenCommand));

    public TaskRow() => InitializeComponent();

    public ICommand? ToggleCommand
    {
        get => GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        // The checkbox reflects the server; the page decides and reloads.
        Done.IsChecked = !Done.IsChecked;
        ToggleCommand?.Execute(DataContext);
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e) => OpenCommand?.Execute(DataContext);
}
