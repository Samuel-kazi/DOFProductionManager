using CommunityToolkit.Mvvm.ComponentModel;

namespace DOF.Desktop.ViewModels;

public sealed partial class NavigationItem(string title, string iconKey, Func<PageViewModel> createPage, string? shortcut = null)
    : ObservableObject
{
    public string Title { get; } = title;

    /// <summary>Resource key of the icon geometry in Themes/Icons.axaml.</summary>
    public string IconKey { get; } = iconKey;

    public string? Shortcut { get; } = shortcut;

    public string ToolTip => Shortcut is null ? Title : $"{Title} ({Shortcut})";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public PageViewModel CreatePage() => createPage();
}
