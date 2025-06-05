// App.xaml.cs
using System.Diagnostics;
using System.Windows;
using Serilog;
using Serilog.Events;
using System.IO;

namespace UsbDlpAgent.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                // Đảm bảo thư mục logs tồn tại
                Directory.CreateDirectory("logs");

                // Cấu hình Serilog với nhiều thông tin hơn
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .WriteTo.File("logs/app.log", 
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.Debug(outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                Log.Information("Starting application...");
                Log.Debug("Application startup arguments: {Args}", string.Join(" ", e.Args));
                
                // Xử lý unhandled exceptions
                AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                {
                    var ex = (Exception)args.ExceptionObject;
                    Log.Fatal(ex, "Unhandled exception occurred");
                    MessageBox.Show($"An unhandled exception occurred: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                };

                // Xử lý unhandled exceptions trong UI thread
                DispatcherUnhandledException += (s, args) =>
                {
                    Log.Error(args.Exception, "Unhandled UI exception occurred");
                    MessageBox.Show($"An error occurred: {args.Exception.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    args.Handled = true;
                };

                base.OnStartup(e);

                Log.Information("Creating LoginWindow...");
                var loginWindow = new LoginWindow();
                Log.Debug("LoginWindow created successfully");

                Log.Information("Showing LoginWindow...");
                loginWindow.Show();
                Log.Information("LoginWindow.Show() called successfully");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to start");
                MessageBox.Show($"Application failed to start: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application is shutting down with exit code: {ExitCode}", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}