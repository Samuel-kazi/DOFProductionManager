namespace DOF.Desktop.ViewModels.Pages;

/// <summary>
/// A section whose features are scheduled for a later phase. It says so plainly rather than showing
/// sample data that could be mistaken for real records.
/// </summary>
public sealed class PlaceholderPageViewModel(string title, int phase, string description) : PageViewModel
{
    public override string Title { get; } = title;

    public int Phase { get; } = phase;

    public string Description { get; } = description;

    public string PhaseText => $"Arrives in Phase {Phase}";
}
