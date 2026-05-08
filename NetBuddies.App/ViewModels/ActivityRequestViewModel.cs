using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NetBuddies.App.ViewModels;

public sealed partial class ActivityRequestViewModel(
    string title,
    string detail,
    Func<Task> acceptAsync,
    Func<Task> declineAsync,
    Action<ActivityRequestViewModel>? dismiss = null) : ViewModelBase
{
    private readonly Func<Task> _acceptAsync = acceptAsync;
    private readonly Func<Task> _declineAsync = declineAsync;
    private readonly Action<ActivityRequestViewModel>? _dismiss = dismiss;

    public string Title { get; } = title;
    public string Detail { get; } = detail;

    [ObservableProperty]
    private bool _isResolved;

    [ObservableProperty]
    private bool _areActionsEnabled = true;

    [ObservableProperty]
    private string _resultText = "";

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (IsResolved)
        {
            return;
        }

        IsResolved = true;
        AreActionsEnabled = false;
        ResultText = "Accepted";
        await _acceptAsync();
    }

    [RelayCommand]
    private async Task DeclineAsync()
    {
        if (IsResolved)
        {
            return;
        }

        IsResolved = true;
        AreActionsEnabled = false;
        ResultText = "Declined";
        await _declineAsync();
    }

    [RelayCommand]
    private void Dismiss()
    {
        _dismiss?.Invoke(this);
    }
}
