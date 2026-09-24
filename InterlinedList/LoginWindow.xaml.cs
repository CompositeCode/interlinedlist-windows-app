using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using InterlinedList.ViewModels;

namespace InterlinedList;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public event EventHandler? LoginSucceeded;

    public LoginWindow()
    {
        InitializeComponent();

        _viewModel = new LoginViewModel(Services.AppServices.Session);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        ApplyMode();
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Submit ─────────────────────────────────────────────────────────────────

    private async void BtnLogin_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        // Verification is the odd one out: succeeding doesn't produce a session
        // (the endpoint is unauthenticated), so it must not raise LoginSucceeded.
        if (_viewModel.Mode == LoginMode.VerifyEmail)
        {
            await _viewModel.VerifyEmailAsync();
            return;
        }

        var succeeded = _viewModel.Mode switch
        {
            LoginMode.Register => await _viewModel.RegisterAsync(PasswordInput.Password),
            LoginMode.ResetPassword => await _viewModel.ResetPasswordAsync(
                NewPasswordInput.Password, ConfirmPasswordInput.Password),
            _ => await _viewModel.LoginAsync(PasswordInput.Password),
        };

        if (succeeded)
        {
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
            return;
        }

        // A reset that succeeded but couldn't auto-sign-in drops back to Login
        // mode. Clear the plaintext we're still holding instead of leaving it in
        // the (now hidden) boxes.
        if (_viewModel.Mode != LoginMode.ResetPassword)
            ClearResetInputs();
    }

    // ── Mode switching ─────────────────────────────────────────────────────────

    private async void BtnForgot_Click(object sender, RoutedEventArgs e)
        => await _viewModel.ForgotPasswordAsync();

    private void BtnHaveResetToken_Click(object sender, RoutedEventArgs e)
        => _viewModel.GoToResetPasswordMode();

    private async void BtnVerifyEmail_Click(object sender, RoutedEventArgs e)
        => await _viewModel.GoToVerifyEmailModeAsync();

    private async void BtnConfirmEmailChange_Click(object sender, RoutedEventArgs e)
        => await _viewModel.ConfirmEmailChangeAsync();

    private async void BtnUndoEmailChange_Click(object sender, RoutedEventArgs e)
        => await _viewModel.UndoEmailChangeAsync();

    private void BtnResendVerification_Click(object sender, RoutedEventArgs e)
        => _viewModel.OpenWebVerificationSettings();

    private void BtnBackToLogin_Click(object sender, RoutedEventArgs e)
    {
        ClearResetInputs();
        _viewModel.GoToMode(LoginMode.Login);
    }

    private void BtnToggleMode_Click(object sender, RoutedEventArgs e)
        => _viewModel.ToggleRegisterMode();

    private void ClearResetInputs()
    {
        NewPasswordInput.Clear();
        ConfirmPasswordInput.Clear();
        _viewModel.ResetToken = "";
    }

    /// <summary>
    /// Push the ViewModel's mode flags onto the parts of the card that can't be
    /// bound without a converter. PasswordBox in particular has no bindable
    /// Password, so this window drives its visibility and reads its value directly.
    /// </summary>
    private void ApplyMode()
    {
        EmailFields.Visibility = Vis(_viewModel.ShowEmailField);
        RegisterFields.Visibility = Vis(_viewModel.ShowRegisterFields);
        ResetFields.Visibility = Vis(_viewModel.ShowResetFields);
        VerifyFields.Visibility = Vis(_viewModel.ShowVerifyFields);
        PasswordFields.Visibility = Vis(_viewModel.ShowPasswordField);
        EmailChangeActions.Visibility = Vis(_viewModel.ShowEmailChangeActions);
        BtnResendVerification.Visibility = Vis(_viewModel.IsVerifyEmailMode);
        BtnForgot.Visibility = Vis(_viewModel.ShowForgotLink);
        BtnHaveResetToken.Visibility = Vis(_viewModel.ShowForgotLink);
        BtnVerifyEmail.Visibility = Vis(_viewModel.ShowForgotLink);
        BtnToggleMode.Visibility = Vis(_viewModel.ShowToggleMode);
        BtnBackToLogin.Visibility = Vis(_viewModel.ShowBackToLogin);
        BtnLogin.Content = _viewModel.IsBusy ? "Working…" : _viewModel.PrimaryButtonText;
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LoginViewModel.ErrorMessage):
                ErrorText.Visibility = Vis(_viewModel.HasError);
                break;

            case nameof(LoginViewModel.StatusMessage):
                StatusText.Visibility = Vis(_viewModel.HasStatus);
                break;

            case nameof(LoginViewModel.Mode):
                ApplyMode();
                break;

            // Raised on its own when pendingEmail arrives from GET /api/user, not
            // just on a mode change — that read is async and lands later.
            case nameof(LoginViewModel.ShowEmailChangeActions):
                EmailChangeActions.Visibility = Vis(_viewModel.ShowEmailChangeActions);
                break;

            case nameof(LoginViewModel.IsBusy):
                BtnLogin.IsEnabled = !_viewModel.IsBusy;
                BtnLogin.Content = _viewModel.IsBusy
                    ? "Working…"
                    : _viewModel.PrimaryButtonText;
                break;
        }
    }
}
