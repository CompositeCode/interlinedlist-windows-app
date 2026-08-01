using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly SessionService _session;

    [ObservableProperty]
    private string email = "";

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(ToggleModeText))]
    private bool isRegisterMode;

    public string PrimaryButtonText => IsRegisterMode ? "Create account" : "Log In";
    public string ToggleModeText => IsRegisterMode ? "Have an account? Log in" : "Create an account";

    public LoginViewModel(SessionService session)
    {
        _session = session;
    }

    /// <summary>Register, then attempt an immediate login. Returns true only if the login succeeds.</summary>
    public async Task<bool> RegisterAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = "Enter an email, username, and password.";
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _session.Api.RegisterAsync(Email.Trim(), Username.Trim(), password,
                string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim());
            try
            {
                await _session.LoginAsync(Email.Trim(), password);
                return true;
            }
            catch (InterlinedApiException)
            {
                ErrorMessage = "Account created. Verify your email, then log in.";
                IsRegisterMode = false;
                return false;
            }
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ForgotPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Enter your email above first.";
            return;
        }

        IsBusy = true;
        try
        {
            await _session.Api.ForgotPasswordAsync(Email.Trim());
            ErrorMessage = "If that email has an account, a reset link is on its way.";
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
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
