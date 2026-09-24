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

    /// <summary>
    /// Completing email verification, or confirming/undoing an email change, with
    /// the token from the relevant email. One mode covers all three because all
    /// three endpoints are unauthenticated and take nothing but <c>{ token }</c> —
    /// the user picks the action matching the email they received.
    /// </summary>
    VerifyEmail,
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

    /// <summary>The token pasted out of a verification / email-change email.</summary>
    [ObservableProperty]
    private string verificationToken = "";

    /// <summary>
    /// A new address awaiting confirmation, from <c>pendingEmail</c> on
    /// <c>GET /api/user</c>. Null when nothing is in flight — or when there's no
    /// session to read it with, which is why <see cref="EmailStandingKnown"/>
    /// exists separately.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingEmail))]
    [NotifyPropertyChangedFor(nameof(ShowEmailChangeActions))]
    [NotifyPropertyChangedFor(nameof(PendingEmailSummary))]
    private string? pendingEmail;

    /// <summary>
    /// True once <c>GET /api/user</c> has actually been read. Distinguishes
    /// "no change is pending" from "we have no session, so we can't tell" —
    /// without it the confirm/undo buttons would be hidden from exactly the
    /// signed-out user who needs them.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmailChangeActions))]
    [NotifyPropertyChangedFor(nameof(PendingEmailSummary))]
    private bool emailStandingKnown;

    /// <summary>
    /// <c>accountStatus</c> from <c>GET /api/user</c> — <c>new</c> until the
    /// account is cleared, which is what verification is the fastest route out of.
    /// Re-read after every successful verification so it can't go stale.
    /// </summary>
    [ObservableProperty]
    private string? accountStatus;

    /// <summary><c>emailVerified</c> from <c>GET /api/user</c>.</summary>
    [ObservableProperty]
    private bool emailVerified;

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
    [NotifyPropertyChangedFor(nameof(IsVerifyEmailMode))]
    [NotifyPropertyChangedFor(nameof(ShowRegisterFields))]
    [NotifyPropertyChangedFor(nameof(ShowResetFields))]
    [NotifyPropertyChangedFor(nameof(ShowVerifyFields))]
    [NotifyPropertyChangedFor(nameof(ShowEmailChangeActions))]
    [NotifyPropertyChangedFor(nameof(ShowPasswordField))]
    [NotifyPropertyChangedFor(nameof(ShowEmailField))]
    [NotifyPropertyChangedFor(nameof(ShowForgotLink))]
    [NotifyPropertyChangedFor(nameof(ShowToggleMode))]
    [NotifyPropertyChangedFor(nameof(ShowBackToLogin))]
    private LoginMode mode = LoginMode.Login;

    public bool IsRegisterMode => Mode == LoginMode.Register;
    public bool IsResetPasswordMode => Mode == LoginMode.ResetPassword;
    public bool IsVerifyEmailMode => Mode == LoginMode.VerifyEmail;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool HasPendingEmail => !string.IsNullOrEmpty(PendingEmail);

    public bool ShowRegisterFields => Mode == LoginMode.Register;
    public bool ShowResetFields => Mode == LoginMode.ResetPassword;
    public bool ShowVerifyFields => Mode == LoginMode.VerifyEmail;

    /// <summary>
    /// The confirm/undo pair, driven by <c>pendingEmail</c> as the issue requires:
    /// hidden only when we've actually read <c>GET /api/user</c> and it says no
    /// change is pending. With no session we can't know, and the endpoints work on
    /// the emailed token alone, so they stay available.
    /// </summary>
    public bool ShowEmailChangeActions
        => Mode == LoginMode.VerifyEmail && (!EmailStandingKnown || HasPendingEmail);

    /// <summary>The single password box. Reset mode has its own new/confirm pair instead.</summary>
    public bool ShowPasswordField => Mode is LoginMode.Login or LoginMode.Register;

    /// <summary>Verification needs nothing but the token, so the email box is noise there.</summary>
    public bool ShowEmailField => Mode != LoginMode.VerifyEmail;

    public bool ShowForgotLink => Mode == LoginMode.Login;
    public bool ShowToggleMode => Mode is LoginMode.Login or LoginMode.Register;
    public bool ShowBackToLogin => Mode is LoginMode.ResetPassword or LoginMode.VerifyEmail;

    public string PendingEmailSummary
        => HasPendingEmail
            ? $"A change to {PendingEmail} is awaiting confirmation."
            : EmailStandingKnown
                ? "No email change is pending on this account."
                : "Paste the token from the email, then pick the matching action.";

    public string HeadingText => Mode switch
    {
        LoginMode.Register => "Create an InterlinedList account",
        LoginMode.ResetPassword => "Set a new password",
        LoginMode.VerifyEmail => "Verify your email",
        _ => "Log in to InterlinedList",
    };

    public string PrimaryButtonText => Mode switch
    {
        LoginMode.Register => "Create account",
        LoginMode.ResetPassword => "Set new password",
        LoginMode.VerifyEmail => "Verify email",
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

        if (target != LoginMode.VerifyEmail)
        {
            VerificationToken = "";
            PendingEmail = null;
            AccountStatus = null;
            EmailVerified = false;
            EmailStandingKnown = false;
        }
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

    // ── Email verification & email-change confirmation ──────────────────────────
    // All three endpoints are unauthenticated and take nothing but { token }, so
    // this works from the login card with no session — which is the point: you
    // must verify before posting or attaching media, and requiring the website
    // for it made the app a dead end. Each success re-reads GET /api/user when a
    // session exists, because the write responses aren't trusted.

    /// <summary>
    /// Enter verification mode and, if there's a session to read with, pull
    /// <c>pendingEmail</c> so the confirm/undo affordances reflect reality.
    /// </summary>
    public async Task GoToVerifyEmailModeAsync()
    {
        GoToMode(LoginMode.VerifyEmail);
        // No status line here: PendingEmailSummary is already bound under the token
        // box, and saying the same thing twice reads as two separate messages.
        await RefreshEmailStandingAsync();
    }

    /// <summary>
    /// Re-read <c>GET /api/user</c>. Needs a bearer token, unlike the verify calls
    /// themselves, so it no-ops while signed out and leaves
    /// <see cref="EmailStandingKnown"/> false.
    /// </summary>
    public async Task RefreshEmailStandingAsync()
    {
        if (!_session.IsAuthenticated)
        {
            EmailStandingKnown = false;
            PendingEmail = null;
            return;
        }

        try
        {
            var (user, standing) = await _session.Api.GetUserAndEmailStandingAsync();
            PendingEmail = standing.PendingEmail;
            AccountStatus = standing.AccountStatus;
            EmailVerified = standing.EmailVerified;
            EmailStandingKnown = true;

            // Keep the rest of the app honest too: CurrentUser carries
            // emailVerified, and the whole reason to verify is that it gates
            // posting. Reassigning it republishes to every bound view.
            if (user is not null)
                _session.CurrentUser = user;
        }
        catch (InterlinedApiException)
        {
            // A stale or revoked token shouldn't break the verify flow — the
            // emailed token is all the endpoints actually need.
            EmailStandingKnown = false;
            PendingEmail = null;
            AccountStatus = null;
        }
    }

    public Task<bool> VerifyEmailAsync()
        => RunTokenActionAsync(
            (api, token) => api.VerifyEmailAsync(token),
            "Paste the verification token (or the whole link) from the email.",
            "Email verified.");

    public Task<bool> ConfirmEmailChangeAsync()
        => RunTokenActionAsync(
            (api, token) => api.VerifyEmailChangeAsync(token),
            "Paste the token (or the whole link) from the confirmation email.",
            "New address confirmed.");

    public Task<bool> UndoEmailChangeAsync()
        => RunTokenActionAsync(
            (api, token) => api.UndoEmailChangeAsync(token),
            "Paste the token (or the whole link) from the undo email.",
            "Email change reversed.");

    /// <summary>
    /// The three verify/undo calls differ only in endpoint and wording, so they
    /// share one pipeline: normalize the pasted token, call, then re-read
    /// <c>GET /api/user</c> and report the resulting standing.
    /// </summary>
    private async Task<bool> RunTokenActionAsync(
        Func<InterlinedApiClient, string, Task> action,
        string missingTokenMessage,
        string successMessage)
    {
        ErrorMessage = null;
        StatusMessage = null;

        var token = NormalizeToken(VerificationToken);
        if (token.Length == 0)
        {
            ErrorMessage = missingTokenMessage;
            return false;
        }

        IsBusy = true;
        try
        {
            await action(_session.Api, token);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = DescribeTokenFailure(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        VerificationToken = "";

        IsBusy = true;
        try
        {
            await RefreshEmailStandingAsync();
        }
        finally
        {
            IsBusy = false;
        }

        // Report what the re-read actually said, not what the call implied — the
        // write response body is never trusted, so this is the only real evidence.
        StatusMessage = EmailStandingKnown
            ? $"{successMessage} Email is {(EmailVerified ? "verified" : "not yet verified")}"
              + (string.IsNullOrEmpty(AccountStatus) ? "." : $"; account status is {AccountStatus}.")
            : $"{successMessage} Log in to continue.";
        return true;
    }

    private static string DescribeTokenFailure(InterlinedApiException ex)
    {
        if (ex.StatusCode == 429)
            return "Too many attempts. Wait a minute and try again.";

        // Undo is checked first because it's the more specific match: the undo
        // endpoint says "Invalid or expired undo link" (no "token" in it) while its
        // missing-field message *does* contain "token". Live-verified wording —
        // don't collapse these two branches.
        if (ex.StatusCode == 400 && ex.Message.Contains("undo", StringComparison.OrdinalIgnoreCase))
            return ex.Message + " Undo links expire; if it's too late, request a new email change instead.";

        if (ex.StatusCode == 400 && ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase))
            return ex.Message + " These links are single-use and time-limited — "
                              + "make sure you pasted the newest email, or resend it from the web.";

        // 409 is documented on verify-email-change and undo-email-change but was
        // not reachable by probing (it needs a real email change in flight, which
        // would have mutated the shared test account), so its message is surfaced
        // as-is rather than wrapped in invented wording.
        return ex.Message;
    }

    /// <summary>
    /// Opens the web settings page so the user can resend their verification
    /// email. Not an in-app call on purpose:
    /// <c>POST /api/auth/send-verification-email</c> is cookie-session-only and
    /// answers 401 even with a valid bearer sync token (probed live 2026-09-16),
    /// so a native client can only hand this one off — the same pattern as OAuth
    /// linking and "Manage account on the web".
    /// </summary>
    public void OpenWebVerificationSettings()
    {
        _session.Api.OpenWebVerificationSettings();
        StatusMessage = "Opened InterlinedList in your browser — resend the email from Settings, "
                      + "then paste the new token here.";
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
