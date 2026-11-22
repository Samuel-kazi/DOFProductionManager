using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels;

/// <summary>Base for every page in the shell: a title plus consistent loading and error states.</summary>
public abstract partial class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    [ObservableProperty]
    public partial bool IsLoading { get; protected set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; protected set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>Called each time the page is shown.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    /// <summary>
    /// Runs an API operation with loading and error state. Expected API failures become a visible
    /// message; anything unexpected is logged and rethrown to the global handler, never swallowed.
    /// </summary>
    protected async Task<bool> RunAsync(Func<Task> operation)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await operation();
            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (ApiUnavailableException ex)
        {
            AppLog.Warning($"{Title}: {ex.InnerException?.Message}");
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
