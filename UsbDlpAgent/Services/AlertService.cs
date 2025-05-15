// UsbDlpAgent/Services/AlertService.cs
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks; // Cho Task
using UsbDlpAgent.Hubs;
using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.Services
{
    public class AlertService : IAlertService
    {
        private readonly ILogger<AlertService> _logger;
        private readonly IHubContext<DlpNotificationHub> _hubContext;
        private readonly IEventHistoryService _eventHistoryService; // MỚI
        private readonly ILogger<DlpNotificationHub> _hubStaticLogger;


        public AlertService(
            ILogger<AlertService> logger,
            IHubContext<DlpNotificationHub> hubContext,
            IEventHistoryService eventHistoryService, // MỚI: Inject
            ILogger<DlpNotificationHub> hubStaticLogger)
        {
            _logger = logger;
            _hubContext = hubContext;
            _eventHistoryService = eventHistoryService; // MỚI
            _hubStaticLogger = hubStaticLogger;
        }

        public async void TriggerAlert(FileActivity activity) // Đổi sang async void hoặc Task
        {
            _logger.LogWarning("ALERT! Activity: Type={ActivityType}, Path='{FilePath}', OldPath='{OldPath}', Drive='{Drive}'",
                               activity.Type, activity.FilePath, activity.OldFilePath, activity.Drive);

            // 1. Ghi sự kiện vào file lịch sử
            await _eventHistoryService.AddEventAsync(activity); // MỚI

            // 2. Phát sóng sự kiện qua SignalR (Không dùng _eventHistory tĩnh của Hub nữa)
            //    Hub sẽ đọc từ EventHistoryService khi client yêu cầu.
            //    Chỉ cần gửi sự kiện mới này cho các client.
            _hubContext.Clients.All.SendAsync("ReceiveNewEvent", activity);

            // Phương thức tĩnh DlpNotificationHub.AddEventToHistoryAndBroadcast không còn cần thiết
            // nếu AlertService tự ghi vào history và Hub tự đọc từ history service.
            // DlpNotificationHub.AddEventToHistoryAndBroadcast(_hubContext, activity, _hubStaticLogger); // XÓA HOẶC THAY ĐỔI
        }
    }
}