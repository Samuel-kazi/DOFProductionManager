using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DOF.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Put the cursor where the user needs to type first.
        (string.IsNullOrEmpty(EmailBox.Text) ? EmailBox : PasswordBox).Focus();
    }
}
