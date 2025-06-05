using UsbDlpAgent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // For logging configuration
using Microsoft.AspNetCore.Builder;   // For app.UseRouting, app.UseEndpoints
using Microsoft.AspNetCore.Hosting;   // For ConfigureWebHostDefaults, UseUrls
using UsbDlpAgent.Hubs;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "UsbDlpAgentService";
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                if (OperatingSystem.IsWindows())
                {
                    logging.AddEventLog(eventLogSettings =>
                    {
                        eventLogSettings.SourceName = "UsbDlpAgentService";
                    });
                }
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<IEventHistoryService, EventHistoryService>();
                services.AddSingleton<IAlertService, AlertService>();
                services.AddSingleton<IUsbWatcher, UsbWatcher>();
                services.AddSingleton<IFsWatcherManager, FsWatcherManager>();
                services.AddSingleton<ILocalDriveMonitorService, LocalDriveMonitorService>();
                // Thêm ILogger<DlpNotificationHub> để có thể inject vào AlertService
                services.AddLogging(); // Đảm bảo các dịch vụ logging cơ bản được đăng ký

                services.AddSignalR(); // Thêm dịch vụ SignalR

                services.AddHostedService<UsbDlpBackgroundWorker>();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {

                webBuilder.ConfigureKestrel(serverOptions =>
                {
                    // serverOptions.ListenLocalhost(5125); // Lắng nghe trên http://localhost:5123
                    serverOptions.ListenLocalhost(5120, listenOptions => listenOptions.UseHttps()); // Nếu muốn HTTPS
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<DlpNotificationHub>("/dlphub");
                        // Có thể thêm một endpoint GET đơn giản để kiểm tra service có chạy không
                        // endpoints.MapGet("/", async context =>
                        // {
                        //     await context.Response.WriteAsync("UsbDlpAgent Service with SignalR Hub is running.");
                        // });
                    });
                });
            });
}