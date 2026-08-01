using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InterlinedList.Sync;

/// <summary>Minimal email/password sign-in that mints and stores the shared token.</summary>
internal sealed class SignInWindow : Window
{
    private readonly TextBox _email = new();
    private readonly PasswordBox _password = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _signIn = new() { Content = "Sign in", IsDefault = true, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
    private readonly AuthClient _auth;

    public SignInWindow(AuthClient auth)
    {
        _auth = auth;
        Title = "Sign in to InterlinedList";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "InterlinedList document sync", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Email" });
        panel.Children.Add(_email);
        panel.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(_password);
        panel.Children.Add(_error);
        _signIn.Click += OnSignIn;
        panel.Children.Add(_signIn);
        Content = panel;
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        _error.Text = string.Empty;
        _signIn.IsEnabled = false;
        try
        {
            await _auth.SignInAsync(_email.Text.Trim(), _password.Password);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _error.Text = ex.Message;
            _signIn.IsEnabled = true;
        }
    }
}
