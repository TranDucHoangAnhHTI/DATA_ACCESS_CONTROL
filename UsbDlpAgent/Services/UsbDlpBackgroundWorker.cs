using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO; // For Path, FileInfo, Directory
using System.Linq; // For Any()
using System.Threading;
using System.Threading.Tasks;
using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.Services
{
    public class RecentlyDeletedFileInfo
    {
        public string FilePath { get; }
        public string FileName { get; }
        public long FileSize { get; } // Note: Size from FSW delete event is unreliable (-1)
        public DateTime DeletionTimestamp { get; }

        public RecentlyDeletedFileInfo(string filePath, long fileSize, DateTime deletionTimestamp)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            FileSize = fileSize;
            DeletionTimestamp = deletionTimestamp;
        }
    }

    public class UsbDlpBackgroundWorker : BackgroundService
    {
        private readonly ILogger<UsbDlpBackgroundWorker> _logger;
        private readonly IUsbWatcher _usbWatcher;
        private readonly IFsWatcherManager _fsWatcherManager;
        private readonly IAlertService _alertService;
        private readonly ILocalDriveMonitorService _localDriveMonitor;
        private readonly IConfiguration _configuration;

        private readonly ConcurrentQueue<RecentlyDeletedFileInfo> _recentlyDeletedFiles = new();
        private TimeSpan _moveDetectionWindow = TimeSpan.FromSeconds(10); // Default value
        private int _maxDeletedFilesCacheSize = 200; // Default value

        public UsbDlpBackgroundWorker(
            ILogger<UsbDlpBackgroundWorker> logger,
            IUsbWatcher usbWatcher,
            IFsWatcherManager fsWatcherManager,
            IAlertService alertService,
            ILocalDriveMonitorService localDriveMonitor,
            IConfiguration configuration)
        {
            _logger = logger;
            _usbWatcher = usbWatcher;
            _fsWatcherManager = fsWatcherManager;
            _alertService = alertService;
            _localDriveMonitor = localDriveMonitor;
            _configuration = configuration;

            // Read configuration for move detection parameters
            if (int.TryParse(_configuration["DlpSettings:MoveDetectionWindowSeconds"], out int seconds) && seconds > 0)
            {
                _moveDetectionWindow = TimeSpan.FromSeconds(seconds);
            }
            _logger.LogInformation("Move detection window set to {Seconds} seconds.", _moveDetectionWindow.TotalSeconds);

            if (int.TryParse(_configuration["DlpSettings:MaxDeletedFilesCache"], out int cacheSize) && cacheSize > 0)
            {
                _maxDeletedFilesCacheSize = cacheSize;
            }
            _logger.LogInformation("Max deleted files cache size set to {CacheSize}.", _maxDeletedFilesCacheSize);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UsbDlpBackgroundWorker starting at: {Time}", DateTimeOffset.Now);

            // Subscribe to USB device events
            _usbWatcher.UsbDeviceArrived += OnUsbDeviceArrived;
            _usbWatcher.UsbDeviceRemoved += OnUsbDeviceRemoved;

            // Subscribe to file system events on USB drives
            _fsWatcherManager.FileActivityDetected += OnFsFileActivityDetected;

            // Subscribe to local drive file system events
            _localDriveMonitor.LocalFileCreated += OnLocalFileCreatedDetected;
            _localDriveMonitor.LocalFileDeleted += OnLocalFileDeletedDetected;
            _localDriveMonitor.LocalFileChanged += OnLocalFileChangedDetected;
            _localDriveMonitor.LocalFileRenamed += OnLocalFileRenamedDetected;

            _usbWatcher.StartWatching();

            var localPathsToMonitor = _configuration.GetSection("DlpSettings:LocalPathsToMonitor").Get<string[]>();
            if (localPathsToMonitor != null && localPathsToMonitor.Any())
            {
                _logger.LogInformation("Configured local paths to monitor for file operations: {Paths}", string.Join(", ", localPathsToMonitor));
                _localDriveMonitor.StartMonitoringLocalDrives(localPathsToMonitor);
            }
            else
            {
                _logger.LogWarning("No local paths configured for file operation monitoring. Move detection and local activity tracking might be limited.");
            }

            // Task to periodically clean up the deleted files cache
            return Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    CleanUpDeletedFilesCache();
                    try
                    {
                        // Adjust delay as needed, e.g., every minute or 5 minutes
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        // Expected when stoppingToken is cancelled during Task.Delay
                        _logger.LogInformation("Cache cleanup task canceled.");
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Also expected when stoppingToken is cancelled
                        _logger.LogInformation("Cache cleanup task operation canceled.");
                        break;
                    }
                }
                _logger.LogInformation("UsbDlpBackgroundWorker execution loop is stopping.");
            }, stoppingToken);
        }

        private void OnUsbDeviceArrived(object? sender, DeviceEventArgs e)
        {
            _logger.LogInformation("Worker: USB Device Arrived - Drive: {Drive}, Volume: {Volume}", e.DriveLetter, e.VolumeName);
            string pathToMonitor = e.DriveLetter.EndsWith("\\") ? e.DriveLetter : e.DriveLetter + "\\";
            if (Directory.Exists(pathToMonitor)) // Ensure drive is accessible
            {
                _fsWatcherManager.StartMonitoringDrive(pathToMonitor);
            }
            else
            {
                _logger.LogWarning("Worker: USB drive {Drive} arrived but path {Path} is not accessible for monitoring.", e.DriveLetter, pathToMonitor);
            }
        }

        private void OnUsbDeviceRemoved(object? sender, DeviceEventArgs e)
        {
            _logger.LogInformation("Worker: USB Device Removed - Drive: {Drive}, Volume: {Volume}", e.DriveLetter, e.VolumeName);
            string pathToStopMonitoring = e.DriveLetter.EndsWith("\\") ? e.DriveLetter : e.DriveLetter + "\\";
            _fsWatcherManager.StopMonitoringDrive(pathToStopMonitoring);
        }

        private bool IsTemporaryOrSystemFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string? fileName = Path.GetFileName(filePath);
            if (fileName == null) return false;

            return fileName.StartsWith("~") ||
                   fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);
        }

        private void OnLocalFileCreatedDetected(object? sender, FileCreatedEventArgs e)
        {
            if (IsTemporaryOrSystemFile(e.FilePath))
            {
                _logger.LogTrace("Ignoring temporary/system local file creation: {FilePath}", e.FilePath);
                return;
            }
            _logger.LogInformation("Worker: Local CREATED - Path: {FilePath}", e.FilePath);
            var activity = new FileActivity
            {
                Timestamp = e.Timestamp,
                Type = ActivityType.Created,
                FilePath = e.FilePath,
                Drive = Path.GetPathRoot(e.FilePath) ?? "Local",
            };
            _alertService.TriggerAlert(activity);
        }

        private void OnLocalFileDeletedDetected(object? sender, FileDeletedEventArgs e)
        {
            if (IsTemporaryOrSystemFile(e.FilePath))
            {
                _logger.LogTrace("Ignoring temporary/system local file deletion: {FilePath}", e.FilePath);
                return;
            }
            _logger.LogInformation("Worker: Local DELETED - Path: {FilePath}", e.FilePath);
            var deletedInfo = new RecentlyDeletedFileInfo(e.FilePath, e.FileSize, e.Timestamp); // e.FileSize is often -1
            _recentlyDeletedFiles.Enqueue(deletedInfo);
            // Keep cache size in check
            while (_recentlyDeletedFiles.Count > _maxDeletedFilesCacheSize && _recentlyDeletedFiles.TryDequeue(out _)) { }
        }

        private void OnLocalFileChangedDetected(object? sender, FileChangedEventArgs e)
        {
            if (IsTemporaryOrSystemFile(e.FilePath))
            {
                _logger.LogTrace("Ignoring temporary/system local file change: {FilePath}", e.FilePath);
                return;
            }
            // FileSystemWatcher.Changed can also fire for directories when their content changes
            // or their attributes/timestamps change. We typically only care about file modifications.
            if (Directory.Exists(e.FilePath))
            {
                _logger.LogTrace("Ignoring directory change event for: {DirectoryPath} (Type: {ChangeType})", e.FilePath, e.ChangeType);
                return;
            }
            _logger.LogInformation("Worker: Local CHANGED - Path: {FilePath}, Type: {ChangeType}", e.FilePath, e.ChangeType);
            var activity = new FileActivity
            {
                Timestamp = e.Timestamp,
                Type = ActivityType.Modified,
                FilePath = e.FilePath,
                Drive = Path.GetPathRoot(e.FilePath) ?? "Local",
            };
            _alertService.TriggerAlert(activity);
        }

        private void OnLocalFileRenamedDetected(object? sender, FileRenamedEventArgs e)
        {
            if (IsTemporaryOrSystemFile(e.OldFullPath) || IsTemporaryOrSystemFile(e.FullPath))
            {
                _logger.LogTrace("Ignoring temporary/system local file rename: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
                return;
            }
            _logger.LogInformation("Worker: Local RENAMED - Old: {OldPath} -> New: {NewPath}", e.OldFullPath, e.FullPath);
            var activity = new FileActivity
            {
                Timestamp = e.Timestamp,
                Type = ActivityType.Renamed,
                FilePath = e.FullPath, // New path
                OldFilePath = e.OldFullPath, // Old path
                Drive = Path.GetPathRoot(e.FullPath) ?? "Local",
            };
            _alertService.TriggerAlert(activity);
        }

        private void OnFsFileActivityDetected(object? sender, FileActivityEventArgs e)
        {
            _logger.LogDebug("Worker: Activity on USB Drive '{Drive}': Type={Type}, Path='{Path}', OldPath='{OldPath}'",
                e.Activity.Drive, e.Activity.Type, e.Activity.FilePath, e.Activity.OldFilePath);

            if (IsTemporaryOrSystemFile(e.Activity.FilePath) || (e.Activity.OldFilePath != null && IsTemporaryOrSystemFile(e.Activity.OldFilePath)))
            {
                _logger.LogTrace("Ignoring temporary/system file activity on USB: {Type} for {Path}", e.Activity.Type, e.Activity.FilePath);
                return;
            }

            if (e.Activity.Type == ActivityType.Created)
            {
                string usbFileName = Path.GetFileName(e.Activity.FilePath);
                long usbFileSize = -1;
                try
                {
                    FileInfo fi = new FileInfo(e.Activity.FilePath);
                    if (fi.Exists) usbFileSize = fi.Length;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not get size of newly created file on USB: {FilePath}", e.Activity.FilePath);
                }

                _logger.LogInformation("Worker: File CREATED on USB '{FilePath}' (Size: {Size} bytes). Checking for potential move operation.", e.Activity.FilePath, usbFileSize);

                RecentlyDeletedFileInfo? matchedDeletedFile = null;
                // Create a snapshot of the queue to iterate over to avoid issues with concurrent modification if any.
                // Reverse() is used to check most recent deletions first.
                var currentDeletedFilesSnapshot = _recentlyDeletedFiles.Reverse().ToList();

                foreach (var deletedFile in currentDeletedFilesSnapshot)
                {
                    bool nameMatch = string.Equals(deletedFile.FileName, usbFileName, StringComparison.OrdinalIgnoreCase);
                    // Time check: USB creation must be AFTER local deletion and WITHIN the detection window.
                    bool timeMatch = (e.Activity.Timestamp > deletedFile.DeletionTimestamp) &&
                                     ((e.Activity.Timestamp - deletedFile.DeletionTimestamp) <= _moveDetectionWindow);

                    // Size comparison can be unreliable, especially since deletedFile.FileSize is often -1.
                    // If usbFileSize is valid and deletedFile.FileSize was somehow captured, you could add:
                    // bool sizeMatch = (usbFileSize != -1 && deletedFile.FileSize != -1 && Math.Abs(deletedFile.FileSize - usbFileSize) < 1024); // Example: 1KB tolerance

                    if (nameMatch && timeMatch) // Add && sizeMatch if implementing reliable size check
                    {
                        matchedDeletedFile = deletedFile;
                        _logger.LogInformation("Potential MOVE detected: USB File '{UsbFile}' (Size: {UsbSize}) matches deleted local file '{LocalFileName}' (Original Path: '{LocalPath}', Size: {LocalSize}) within {Window} seconds.",
                                             e.Activity.FilePath, usbFileSize, deletedFile.FileName, deletedFile.FilePath, deletedFile.FileSize, _moveDetectionWindow.TotalSeconds);
                        break; // Found a match
                    }
                }

                if (matchedDeletedFile != null)
                {
                    // It's a move operation
                    var moveActivity = new FileActivity
                    {
                        Timestamp = e.Activity.Timestamp,
                        Type = ActivityType.MovedToUsb,
                        FilePath = e.Activity.FilePath,       // Destination on USB
                        OldFilePath = matchedDeletedFile.FilePath, // Source from local
                        Drive = e.Activity.Drive
                    };
                    _alertService.TriggerAlert(moveActivity);
                }
                else
                {
                    // It's a standard file creation on USB
                    _logger.LogInformation("Worker: Standard file creation on USB: {FilePath}", e.Activity.FilePath);
                    _alertService.TriggerAlert(e.Activity);
                }
            }
            else // Handle other USB activities (Modified, Deleted, Renamed on USB)
            {
                _logger.LogInformation("Worker: USB File Activity (Type: {Type}): {FilePath}", e.Activity.Type, e.Activity.FilePath);
                _alertService.TriggerAlert(e.Activity);
            }
        }

        private void CleanUpDeletedFilesCache()
        {
            if (_recentlyDeletedFiles.IsEmpty) return;

            int initialCount = _recentlyDeletedFiles.Count;
            // Remove items older than the detection window plus a small buffer to ensure items are not removed too early.
            DateTime cutoff = DateTime.UtcNow - (_moveDetectionWindow + TimeSpan.FromSeconds(30));

            int removedCount = 0;
            while (_recentlyDeletedFiles.TryPeek(out var oldestItem) && oldestItem.DeletionTimestamp < cutoff)
            {
                if (_recentlyDeletedFiles.TryDequeue(out _))
                {
                    removedCount++;
                }
                else
                {
                    // Should not happen with TryPeek first, but as a safeguard
                    break;
                }
            }

            if (removedCount > 0)
            {
                _logger.LogDebug("Cleaned up {Count} items from deleted files cache. Current cache size: {NewCount}",
                                 removedCount, _recentlyDeletedFiles.Count);
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UsbDlpBackgroundWorker is stopping.");

            // Unsubscribe from events
            _usbWatcher.UsbDeviceArrived -= OnUsbDeviceArrived;
            _usbWatcher.UsbDeviceRemoved -= OnUsbDeviceRemoved;
            _fsWatcherManager.FileActivityDetected -= OnFsFileActivityDetected;
            _localDriveMonitor.LocalFileCreated -= OnLocalFileCreatedDetected;
            _localDriveMonitor.LocalFileDeleted -= OnLocalFileDeletedDetected;
            _localDriveMonitor.LocalFileChanged -= OnLocalFileChangedDetected;
            _localDriveMonitor.LocalFileRenamed -= OnLocalFileRenamedDetected;

            // Stop watchers before disposing
            _usbWatcher.StopWatching();
            _localDriveMonitor.StopMonitoringLocalDrives();
            // FsWatcherManager typically stops individual watchers when a USB is removed,
            // but its Dispose method should handle any remaining.

            await base.StopAsync(stoppingToken); // Allows the host to complete its shutdown
            _logger.LogInformation("UsbDlpBackgroundWorker has stopped.");
        }

        public override void Dispose()
        {
            _logger.LogInformation("Disposing UsbDlpBackgroundWorker resources.");
            _usbWatcher.Dispose();
            _fsWatcherManager.Dispose();
            _localDriveMonitor.Dispose();
            base.Dispose(); // Important for BackgroundService
            GC.SuppressFinalize(this);
        }
    }
}