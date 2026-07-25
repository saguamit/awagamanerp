using System;
using System.Windows;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;

namespace Awagaman_ERP
{
    public partial class LoginWindow : Window
    {
        private bool _syncingPasswordFields;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => UsernameBox.Focus();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            LoginButton.IsEnabled = false;

            try
            {
                var username = UsernameBox.Text?.Trim();
                var password = ShowPasswordCheckBox.IsChecked == true
                    ? (PasswordTextBox.Text ?? string.Empty)
                    : (PasswordBox.Password ?? string.Empty);
                AppLogger.LogMessage("Login", $"Login requested for user '{username}'.");

                var response = RemoteApiClient.Post<LoginResponse>("api/auth/login", new LoginRequest
                {
                    Username = username,
                    Password = password
                });

                if (response == null || string.IsNullOrWhiteSpace(response.Token) || response.User == null)
                {
                    throw new InvalidOperationException("Login failed.");
                }

                AppLogger.LogMessage("Login", $"Login succeeded for user '{response.User.Username}'.");
                AuthSession.Set(response);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                AppLogger.LogException("Login", ex);
                ErrorText.Text = BuildUserMessage(ex);
                ErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private static string BuildUserMessage(Exception ex)
        {
            var message = ex?.Message ?? "Login failed.";
            if (message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("api/auth/login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "This app needs a newer server API. The VPS is missing the login endpoint.";
            }

            if (message.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Invalid username or password.";
            }

            if (message.IndexOf("No connection could be made", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Unable to connect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Unable to reach the server. Check internet or VPS API status.";
            }

            return message;
        }

        private void ShowPasswordCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var showPassword = ShowPasswordCheckBox.IsChecked == true;
            if (showPassword)
            {
                PasswordTextBox.Text = PasswordBox.Password ?? string.Empty;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordTextBox.Focus();
                PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
            }
            else
            {
                PasswordBox.Password = PasswordTextBox.Text ?? string.Empty;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                PasswordBox.Focus();
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingPasswordFields || ShowPasswordCheckBox.IsChecked == true) return;
            _syncingPasswordFields = true;
            try
            {
                PasswordTextBox.Text = PasswordBox.Password ?? string.Empty;
            }
            finally
            {
                _syncingPasswordFields = false;
            }
        }

        private void PasswordTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_syncingPasswordFields || ShowPasswordCheckBox.IsChecked != true) return;
            _syncingPasswordFields = true;
            try
            {
                PasswordBox.Password = PasswordTextBox.Text ?? string.Empty;
            }
            finally
            {
                _syncingPasswordFields = false;
            }
        }
    }
}
