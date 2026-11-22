using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DOF.Desktop.ViewModels;

namespace DOF.Desktop.Views;

public partial class ShellView : UserControl
{
    private readonly List<KeyBinding> _bindings = [];

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
        base.OnUnloaded(e);
    }
}
