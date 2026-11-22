using CommunityToolkit.Mvvm.ComponentModel;

namespace DOF.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "DOF Production Manager — desktop shell arrives in Phase 2.";
}
