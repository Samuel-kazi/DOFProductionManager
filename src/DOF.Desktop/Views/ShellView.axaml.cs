using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using DOF.Desktop.ViewModels;

namespace DOF.Desktop.Views;

public partial class ShellView : UserControl
{
    private readonly List<KeyBinding> _bindings = [];
    private WindowNotificationManager? _popups;
    private ShellViewModel? _vm;

    public ShellView() => InitializeComponent();

    /// <summary>Registers window-wide shortcuts: ⌘/Ctrl+1–8 sections, ⌘/Ctrl+B sidebar, ⌘/Ctrl+, settings.</summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is not ShellViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        Key[] digits = [Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8];
        for (var i = 0; i < digits.Length; i++)
        {
            _bindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(digits[i], modifier),
                Command = vm.NavigateToIndexCommand,
                CommandParameter = i,
            });
        }
        _bindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.B, modifier), Command = vm.ToggleSidebarCommand });
        _bindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.OemComma, modifier), Command = vm.OpenSettingsCommand });

        top.KeyBindings.AddRange(_bindings);

        // Desktop pop-ups for notifications that arrive while the app is open.
        _vm = vm;
        _popups = new WindowNotificationManager(top) { Position = NotificationPosition.TopRight, MaxItems = 3 };
        vm.Notifications.Arrived += ShowPopups;
    }

    private void ShowPopups(object? sender, IReadOnlyList<DOF.Contracts.Responses.NotificationResponse> arrived)
    {
        foreach (var n in arrived.Take(3))
        {
            var type = n.NotificationType is "Overdue" or "Escalation" ? NotificationType.Warning : NotificationType.Information;
            _popups?.Show(new Notification(n.Title, n.Message, type, TimeSpan.FromSeconds(8)));
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            foreach (var binding in _bindings)
            {
                top.KeyBindings.Remove(binding);
            }
        }

        _bindings.Clear();
        if (_vm is { } vm)
        {
            vm.Notifications.Arrived -= ShowPopups;
            vm.StopBackgroundWork();
            _vm = null;
        }
        base.OnUnloaded(e);
    }
}
