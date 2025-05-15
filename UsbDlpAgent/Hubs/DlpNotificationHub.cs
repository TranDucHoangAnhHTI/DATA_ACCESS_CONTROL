// UsbDlpAgent/Hubs/DlpNotificationHub.cs
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsbDlpAgent.Services; // Thêm using cho IEventHistoryService
using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.Hubs
{
    public class DlpNotificationHub : Hub
    {
        private readonly ILogger<DlpNotificationHub> _logger;
        private readonly IEventHistoryService _eventHistoryService; // MỚI

        // Bỏ _eventHistory tĩnh và _historyLock
        // private static readonly List<FileActivity> _eventHistory = new List<FileActivity>();
        // private static readonly object _historyLock = new object();
        // private const int MaxHistoryItems = 200;

        public DlpNotificationHub(ILogger<DlpNotificationHub> logger, IEventHistoryService eventHistoryService) // MỚI
        {
            _logger = logger;
            _eventHistoryService = eventHistoryService; // MỚI
        }

        public async Task RequestHistory(int count = 50)
        {
            _logger.LogInformation("Client {ConnectionId} requested event history (count: {Count}).", Context.ConnectionId, count);
            // Lấy lịch sử từ service
            List<FileActivity> historyToSend = await _eventHistoryService.GetRecentEventsAsync(Math.Max(0, count));
            await Clients.Caller.SendAsync("ReceiveHistory", historyToSend);
        }

        // Bỏ phương thức AddEventToHistoryAndBroadcast tĩnh. Việc thêm sự kiện sẽ do AlertService thực hiện.
        // public static void AddEventToHistoryAndBroadcast(...) { ... }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected to DlpNotificationHub: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
            await RequestHistory(20); // Gửi 20 sự kiện gần nhất khi kết nối
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected from DlpNotificationHub: {ConnectionId}, Error: {Error}", Context.ConnectionId, exception?.Message);
            await base.OnDisconnectedAsync(exception);
        }
    }
}