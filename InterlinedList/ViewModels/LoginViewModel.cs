using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly SessionService _session;

    [ObservableProperty]
    private string email = "";

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    public LoginViewModel(SessionService session)
    {
        _session = session;
    }

    // Password isn't an [ObservableProperty]: PasswordBox.Password can't be safely
    // data-bound in WPF, so the code-behind passes the plaintext value in directly.
    public async Task<bool> LoginAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = "Enter your email and password.";
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _session.LoginAsync(Email.Trim(), password);
            return true;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.StatusCode is 400 or 401
                ? "Invalid email or password."
                : ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
