// UsbDlpAgent/Services/IEventHistoryService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.Services
{
    public interface IEventHistoryService
    {
        Task AddEventAsync(FileActivity activity);
        Task<List<FileActivity>> GetRecentEventsAsync(int count);
        // Có thể thêm các phương thức khác: GetEventsByDateRangeAsync, ClearHistoryAsync, etc.
    }
}