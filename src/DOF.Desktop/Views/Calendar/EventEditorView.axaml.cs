using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DOF.Desktop.Views.Calendar;

public partial class EventEditorView : UserControl
{
    public EventEditorView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        TitleBox.Focus();
    }
}
