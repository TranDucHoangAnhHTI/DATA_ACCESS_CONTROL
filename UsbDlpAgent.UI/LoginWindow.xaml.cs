using System.Windows;
using Serilog;

namespace UsbDlpAgent.UI
{
    public partial class LoginWindow : Window
    {
        // Thông tin đăng nhập cố định
        private const string FixedUsername = "admin";
        private const string FixedPassword = "password123";

        public LoginWindow()
        {
            try
            {
                Log.Information("Initializing LoginWindow...");
                InitializeComponent();
                Log.Debug("LoginWindow components initialized");
                
                UsernameTextBox.Focus();
                Log.Debug("UsernameTextBox focused");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error initializing LoginWindow");
                MessageBox.Show($"Error initializing login window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Information("Login attempt...");
                string username = UsernameTextBox.Text;
                string password = PasswordBox.Password;

                Log.Debug("Validating credentials...");
                if (username == FixedUsername && password == FixedPassword)
                {
                    Log.Information("Login successful, opening MainWindow");
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.Show();
                    this.Close();
                }
                else
                {
                    Log.Warning("Login failed: Invalid credentials");
                    ErrorMessageTextBlock.Text = "Tên đăng nhập hoặc mật khẩu không đúng.";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during login process");
                MessageBox.Show($"Error during login: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            try
            {
                Log.Debug("LoginWindow source initialized");
                base.OnSourceInitialized(e);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in OnSourceInitialized");
                MessageBox.Show($"Error initializing window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}