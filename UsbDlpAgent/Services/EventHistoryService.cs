// UsbDlpAgent/Services/EventHistoryService.cs
using Microsoft.Extensions.Configuration; // Để đọc đường dẫn file log từ config
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json; // System.Text.Json cho JSON serialization
using System.Threading; // Cho SemaphoreSlim
using System.Threading.Tasks;
using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.Services
{
    public class EventHistoryService : IEventHistoryService
    {
        private readonly ILogger<EventHistoryService> _logger;
        private readonly string _historyFilePath;
        private readonly int _maxHistoryItemsInFile; // Giới hạn số dòng trong file (để xoay vòng đơn giản)
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1); // Lock để ghi file an toàn

        // Cấu hình JsonSerializer
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false, // Ghi không thụt lề để tiết kiệm dung lượng
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase // Tùy chọn: camelCase
        };

        public EventHistoryService(ILogger<EventHistoryService> logger, IConfiguration configuration)
        {
            _logger = logger;

            // Lấy đường dẫn file từ cấu hình, nếu không có thì dùng mặc định
            string? configuredPath = configuration["DlpSettings:EventHistoryFilePath"];
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                // Mặc định lưu trong thư mục của ứng dụng
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appSpecificFolder = Path.Combine(appDataPath, "UsbDlpAgent");
                Directory.CreateDirectory(appSpecificFolder); // Đảm bảo thư mục tồn tại
                _historyFilePath = Path.Combine(appSpecificFolder, "event_history.ndjson");
            }
            else
            {
                _historyFilePath = Path.GetFullPath(configuredPath);
                // Đảm bảo thư mục chứa file log tồn tại
                string? directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            _logger.LogInformation("Event history will be stored at: {FilePath}", _historyFilePath);

            // Giới hạn số dòng trong file (ví dụ: 10000 dòng)
            _maxHistoryItemsInFile = configuration.GetValue<int>("DlpSettings:MaxHistoryFileItems", 10000);
            if (_maxHistoryItemsInFile <= 0) _maxHistoryItemsInFile = 10000; // Giá trị mặc định an toàn
        }

        public async Task AddEventAsync(FileActivity activity)
        {
            await _fileLock.WaitAsync(); // Chờ để chiếm lock
            try
            {
                _logger.LogTrace("Adding event to history file: {ActivityType} on {FilePath}", activity.Type, activity.FilePath);
                // Ghi mỗi sự kiện trên một dòng (Newline Delimited JSON - NDJSON)
                string jsonEvent = JsonSerializer.Serialize(activity, _jsonOptions);
                await File.AppendAllTextAsync(_historyFilePath, jsonEvent + Environment.NewLine);

                // (Tùy chọn) Xoay vòng file log đơn giản nếu quá nhiều dòng
                await TrimLogFileIfNeededAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add event to history file: {FilePath}", _historyFilePath);
            }
            finally
            {
                _fileLock.Release(); // Nhả lock
            }
        }

        public async Task<List<FileActivity>> GetRecentEventsAsync(int count)
        {
            if (count <= 0) return new List<FileActivity>();

            List<FileActivity> events = new List<FileActivity>();
            await _fileLock.WaitAsync();
            try
            {
                if (!File.Exists(_historyFilePath))
                {
                    _logger.LogWarning("History file not found: {FilePath}", _historyFilePath);
                    return events;
                }

                _logger.LogDebug("Reading recent {Count} events from history file: {FilePath}", count, _historyFilePath);
                // Đọc N dòng cuối cùng. Đây là cách đơn giản, có thể không hiệu quả với file rất lớn.
                // Với file NDJSON, đọc từ cuối lên là phức tạp. Đơn giản nhất là đọc hết và lấy N mục cuối.
                var lines = await File.ReadAllLinesAsync(_historyFilePath);
                foreach (var line in lines.Reverse().Take(count).Reverse()) // Lấy N dòng cuối, rồi đảo lại để đúng thứ tự thời gian
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        FileActivity? activity = JsonSerializer.Deserialize<FileActivity>(line, _jsonOptions);
                        if (activity != null)
                        {
                            events.Add(activity);
                        }
                    }
                    catch (JsonException jex)
                    {
                        _logger.LogWarning(jex, "Failed to deserialize line from history file: {Line}", line);
                    }
                }
                _logger.LogDebug("Retrieved {RetrievedCount} events from history.", events.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read events from history file: {FilePath}", _historyFilePath);
            }
            finally
            {
                _fileLock.Release();
            }
            // Sắp xếp lại theo Timestamp giảm dần (mới nhất trước) nếu cần,
            // nhưng logic đọc ở trên đã cố gắng giữ thứ tự gần đúng.
            return events.OrderByDescending(e => e.Timestamp).ToList();
        }

        private async Task TrimLogFileIfNeededAsync()
        {
            // Đây là một cách xoay vòng log rất đơn giản: nếu file quá nhiều dòng, đọc hết, giữ lại N dòng cuối, ghi lại.
            // Không hiệu quả cho file rất lớn. Cân nhắc các thư viện logging chuyên dụng (Serilog, NLog) cho việc này.
            try
            {
                var lines = await File.ReadAllLinesAsync(_historyFilePath);
                if (lines.Length > _maxHistoryItemsInFile)
                {
                    _logger.LogInformation("History file exceeds {MaxItems} items. Trimming to {KeepItems} items.", _maxHistoryItemsInFile, _maxHistoryItemsInFile / 2);
                    var linesToKeep = lines.Skip(lines.Length - (_maxHistoryItemsInFile / 2)).ToList(); // Giữ lại một nửa
                    await File.WriteAllLinesAsync(_historyFilePath, linesToKeep);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trimming log file: {FilePath}", _historyFilePath);
            }
        }
    }
}