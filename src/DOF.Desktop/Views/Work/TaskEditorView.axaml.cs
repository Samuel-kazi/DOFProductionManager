using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DOF.Desktop.Views.Work;

public partial class TaskEditorView : UserControl
{
    public TaskEditorView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        TitleBox.Focus();
    }
}
