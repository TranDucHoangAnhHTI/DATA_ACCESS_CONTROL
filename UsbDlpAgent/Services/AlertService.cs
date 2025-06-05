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
        private readonly IEventHistoryService _eventHistoryService;
        private readonly ILogger<DlpNotificationHub> _hubStaticLogger;


        public AlertService(
            ILogger<AlertService> logger,
            IHubContext<DlpNotificationHub> hubContext,
            IEventHistoryService eventHistoryService,
            ILogger<DlpNotificationHub> hubStaticLogger)
        {
            _logger = logger;
            _hubContext = hubContext;
            _eventHistoryService = eventHistoryService;
            _hubStaticLogger = hubStaticLogger;
        }

        public async void TriggerAlert(FileActivity activity)
        {
            _logger.LogWarning("ALERT! Activity: Type={ActivityType}, Path='{FilePath}', OldPath='{OldPath}', Drive='{Drive}'",
                               activity.Type, activity.FilePath, activity.OldFilePath, activity.Drive);

            await _eventHistoryService.AddEventAsync(activity);


            _hubContext.Clients.All.SendAsync("ReceiveNewEvent", activity);

        }
    }
}