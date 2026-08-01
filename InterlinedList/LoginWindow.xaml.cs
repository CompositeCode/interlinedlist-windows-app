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
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Login ──────────────────────────────────────────────────────────────────

    private async void BtnLogin_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        var succeeded = _viewModel.IsRegisterMode
            ? await _viewModel.RegisterAsync(PasswordInput.Password)
            : await _viewModel.LoginAsync(PasswordInput.Password);
        if (succeeded)
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }

    private async void BtnForgot_Click(object sender, RoutedEventArgs e)
        => await _viewModel.ForgotPasswordAsync();

    private void BtnToggleMode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsRegisterMode = !_viewModel.IsRegisterMode;
        RegisterFields.Visibility = _viewModel.IsRegisterMode ? Visibility.Visible : Visibility.Collapsed;
        BtnLogin.Content = _viewModel.PrimaryButtonText;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LoginViewModel.ErrorMessage):
                ErrorText.Visibility = string.IsNullOrEmpty(_viewModel.ErrorMessage)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
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
