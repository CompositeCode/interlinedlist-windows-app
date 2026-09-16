using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>Which of the pre-login flows the single login card is showing.</summary>
public enum LoginMode
{
    Login,
    Register,

    /// <summary>
    /// Finishing a password reset with the token from the emailed link.
    /// <c>POST /api/auth/reset-password</c> is unauthenticated, so this mode is
    /// reachable without a session — which is the whole point, since the user
    /// can't log in until it succeeds.
    /// </summary>
    ResetPassword,
}

public partial class LoginViewModel : ObservableObject
{
    private readonly SessionService _session;

    [ObservableProperty]
    private string email = "";

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string displayName = "";

    /// <summary>The token pasted out of the password-reset email (bare, or the whole link).</summary>
    [ObservableProperty]
    private string resetToken = "";

    /// <summary>A failure. Rendered in the error color.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    /// <summary>
    /// Progress or success, kept separate from <see cref="ErrorMessage"/> so
    /// "a reset link is on its way" stops being rendered as an error.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? statusMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(HeadingText))]
    [NotifyPropertyChangedFor(nameof(ToggleModeText))]
    [NotifyPropertyChangedFor(nameof(IsRegisterMode))]
    [NotifyPropertyChangedFor(nameof(IsResetPasswordMode))]
    [NotifyPropertyChangedFor(nameof(ShowRegisterFields))]
    [NotifyPropertyChangedFor(nameof(ShowResetFields))]
    [NotifyPropertyChangedFor(nameof(ShowPasswordField))]
    [NotifyPropertyChangedFor(nameof(ShowForgotLink))]
    [NotifyPropertyChangedFor(nameof(ShowToggleMode))]
    [NotifyPropertyChangedFor(nameof(ShowBackToLogin))]
    private LoginMode mode = LoginMode.Login;

    public bool IsRegisterMode => Mode == LoginMode.Register;
    public bool IsResetPasswordMode => Mode == LoginMode.ResetPassword;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool ShowRegisterFields => Mode == LoginMode.Register;
    public bool ShowResetFields => Mode == LoginMode.ResetPassword;

    /// <summary>The single password box. Reset mode has its own new/confirm pair instead.</summary>
    public bool ShowPasswordField => Mode != LoginMode.ResetPassword;

    public bool ShowForgotLink => Mode == LoginMode.Login;
    public bool ShowToggleMode => Mode is LoginMode.Login or LoginMode.Register;
    public bool ShowBackToLogin => Mode == LoginMode.ResetPassword;

    public string HeadingText => Mode switch
    {
        LoginMode.Register => "Create an InterlinedList account",
        LoginMode.ResetPassword => "Set a new password",
        _ => "Log in to InterlinedList",
    };

    public string PrimaryButtonText => Mode switch
    {
        LoginMode.Register => "Create account",
        LoginMode.ResetPassword => "Set new password",
        _ => "Log In",
    };

    public string ToggleModeText => IsRegisterMode ? "Have an account? Log in" : "Create an account";

    public LoginViewModel(SessionService session)
    {
        _session = session;
    }

    // ── Mode switching ──────────────────────────────────────────────────────────

    public void GoToMode(LoginMode target)
    {
        Mode = target;
        ErrorMessage = null;
        StatusMessage = null;
    }

    public void ToggleRegisterMode()
        => GoToMode(IsRegisterMode ? LoginMode.Login : LoginMode.Register);

    /// <summary>
    /// Enter reset-completion mode <b>without</b> sending another email — for the
    /// user who already has the link in their inbox. Reset emails are rate-limited
    /// (the endpoint answers 429), so re-requesting one just to reach this screen
    /// would be a trap.
    /// </summary>
    public void GoToResetPasswordMode()
    {
        GoToMode(LoginMode.ResetPassword);
        StatusMessage = "Paste the token (or the whole link) from the reset email.";
    }

    // ── Register ────────────────────────────────────────────────────────────────

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
        StatusMessage = null;
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
                Mode = LoginMode.Login;
                StatusMessage = "Account created. Verify your email, then log in.";
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

    // ── Password reset ──────────────────────────────────────────────────────────

    public async Task ForgotPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Enter your email above first.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _session.Api.ForgotPasswordAsync(Email.Trim());
            // The server answers identically whether or not the address exists, to
            // prevent email enumeration — so this wording can't promise an email.
            Mode = LoginMode.ResetPassword;
            StatusMessage = "If that email has an account, a reset link is on its way. "
                          + "Paste the token from it below.";
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.StatusCode == 429
                ? "Too many reset requests. Wait a minute before trying again."
                : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Completes the reset, then signs in with the password just set. Returns true
    /// only when that sign-in succeeds (i.e. the caller should show the main window).
    /// </summary>
    public async Task<bool> ResetPasswordAsync(string newPassword, string confirmPassword)
    {
        ErrorMessage = null;
        StatusMessage = null;

        var token = NormalizeToken(ResetToken);
        if (token.Length == 0)
        {
            ErrorMessage = "Paste the reset token (or the whole link) from the email.";
            return false;
        }
        if (string.IsNullOrEmpty(newPassword))
        {
            ErrorMessage = "Enter a new password.";
            return false;
        }
        // Checked client-side on purpose: the server validates password length
        // *before* the token, so a short password would mask an invalid token and
        // send the user off fixing the wrong thing.
        if (newPassword.Length < InterlinedApiClient.MinimumPasswordLength)
        {
            ErrorMessage = $"Password must be at least {InterlinedApiClient.MinimumPasswordLength} characters.";
            return false;
        }
        if (newPassword != confirmPassword)
        {
            ErrorMessage = "The two new passwords don't match.";
            return false;
        }

        IsBusy = true;
        try
        {
            await _session.Api.ResetPasswordAsync(token, newPassword);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = DescribeResetFailure(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        // reset-password is unauthenticated, so success leaves us with no session.
        // Sign in with the new password — that is also what proves the reset landed,
        // since the write response body isn't trusted (read-after-write).
        if (string.IsNullOrWhiteSpace(Email))
        {
            GoToMode(LoginMode.Login);
            StatusMessage = "Password updated. Log in with your new password.";
            return false;
        }

        IsBusy = true;
        try
        {
            await _session.LoginAsync(Email.Trim(), newPassword);
            return true;
        }
        catch (InterlinedApiException)
        {
            GoToMode(LoginMode.Login);
            StatusMessage = "Password updated. Log in with your new password.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Server validation text is surfaced verbatim rather than replaced with a
    /// generic failure — the three real 400 bodies ("Token and password are
    /// required", "Password must be at least 10 characters", "Invalid or expired
    /// reset token") are each actionable on their own.
    /// </summary>
    private static string DescribeResetFailure(InterlinedApiException ex)
    {
        if (ex.StatusCode == 429)
            return "Too many attempts. Wait a minute and try again.";

        // The server collapses *invalid* and *expired* into one message and gives a
        // client no way to tell them apart, so don't pretend to: append the remedy
        // that covers both instead of guessing which one it was.
        if (ex.StatusCode == 400 && ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase))
            return ex.Message + " Reset links are single-use and time-limited — request a new one above.";

        return ex.Message;
    }

    /// <summary>
    /// Accepts either a bare token or the whole reset/verification URL pasted out
    /// of the email, because that is what people actually copy. Without this a
    /// perfectly valid link fails as an "invalid token".
    /// </summary>
    internal static string NormalizeToken(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length == 0) return "";

        var marker = value.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return value;

        var start = marker + "token=".Length;
        var end = value.IndexOfAny(['&', '#'], start);
        value = end < 0 ? value[start..] : value[start..end];
        return Uri.UnescapeDataString(value).Trim();
    }

    // ── Login ───────────────────────────────────────────────────────────────────

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
        StatusMessage = null;
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
