using CommunityToolkit.Mvvm.ComponentModel;

namespace DOF.Desktop.ViewModels.Work;

/// <summary>A person in an assignment picker.</summary>
public sealed partial class PersonOption(Guid id, string fullName) : ObservableObject
{
    public Guid Id { get; } = id;
    public string FullName { get; } = fullName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsPrimary { get; set; }

    partial void OnIsPrimaryChanged(bool value)
    {
        if (value)
        {
            IsSelected = true;
        }
    }

    public override string ToString() => FullName;
}

/// <summary>An option in a combo box with a "none" choice (e.g. no project, nobody).</summary>
public sealed record ChoiceOption(Guid? Id, string Label)
{
    public override string ToString() => Label;
}
