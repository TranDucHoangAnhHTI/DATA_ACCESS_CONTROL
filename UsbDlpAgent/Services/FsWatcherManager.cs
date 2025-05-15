using Microsoft.Extensions.Logging;
using UsbDlpAgent.SharedModels;
using System.Collections.Concurrent;

namespace UsbDlpAgent.Services
{
    public class FsWatcherManager : IFsWatcherManager
    {
        private readonly ILogger<FsWatcherManager> _logger;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();

        public event EventHandler<FileActivityEventArgs>? FileActivityDetected;

        public FsWatcherManager(ILogger<FsWatcherManager> logger)
        {
            _logger = logger;
        }

        public void StartMonitoringDrive(string drivePath)
        {
            if (string.IsNullOrEmpty(drivePath) || !Directory.Exists(drivePath))
            {
                _logger.LogWarning("Cannot monitor invalid or non-existent drive path: {Path}", drivePath);
                return;
            }

            if (_watchers.ContainsKey(drivePath.ToUpperInvariant()))
            {
                _logger.LogInformation("Already monitoring drive: {Path}", drivePath);
                return;
            }

            try
            {
                _logger.LogInformation("Starting file system monitoring on: {Path}", drivePath);
                var watcher = new FileSystemWatcher(drivePath)
                {
                    NotifyFilter = NotifyFilters.LastWrite
                                 | NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.CreationTime // Added for creation
                                 | NotifyFilters.Size,       // Added for size changes (copying)
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    InternalBufferSize = 65536 // Increase buffer for potentially many events
                };

                watcher.Created += (s, e) => OnFileSystemEvent(e, ActivityType.Created, drivePath);
                watcher.Changed += (s, e) => OnFileSystemEvent(e, ActivityType.Modified, drivePath);
                watcher.Deleted += (s, e) => OnFileSystemEvent(e, ActivityType.Deleted, drivePath);
                watcher.Renamed += (s, e) => OnRenamedEvent(e, drivePath);
                watcher.Error += OnWatcherError;


                if (_watchers.TryAdd(drivePath.ToUpperInvariant(), watcher))
                {
                    _logger.LogInformation("Successfully started monitoring {Path}", drivePath);
                }
                else
                {
                    watcher.Dispose(); // Clean up if add failed (e.g. race condition)
                    _logger.LogWarning("Failed to add watcher for {Path}, possibly already added by another thread.", drivePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting FileSystemWatcher for {Path}", drivePath);
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.GetException(), "FileSystemWatcher encountered an error.");
            // Potentially try to restart the watcher, or stop monitoring the affected path.
            if (sender is FileSystemWatcher watcher)
            {
                _logger.LogWarning("Attempting to re-enable watcher for path: {Path}", watcher.Path);
                try
                {
                    watcher.EnableRaisingEvents = false;
                    // Add a small delay or check if path is still accessible before re-enabling
                    System.Threading.Thread.Sleep(1000);
                    if (Directory.Exists(watcher.Path))
                    {
                        watcher.EnableRaisingEvents = true;
                        _logger.LogInformation("Watcher re-enabled for path: {Path}", watcher.Path);
                    }
                    else
                    {
                        _logger.LogWarning("Path {Path} no longer exists. Stopping watcher.", watcher.Path);
                        StopMonitoringDrive(watcher.Path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to re-enable watcher for {Path}", watcher.Path);
                    StopMonitoringDrive(watcher.Path); // Stop if re-enable fails
                }
            }
        }


        private void OnFileSystemEvent(FileSystemEventArgs e, ActivityType type, string drive)
        {
            // Filter out temporary files or system files if needed
            if (e.Name != null && (e.Name.StartsWith("~") || e.Name.EndsWith(".tmp")))
            {
                // _logger.LogTrace("Ignoring temporary file event: {FileName}", e.Name);
                return;
            }

            var activity = new FileActivity
            {
                Timestamp = DateTime.UtcNow,
                Type = type,
                FilePath = e.FullPath,
                Drive = drive.Substring(0, Math.Min(3, drive.Length)) // e.g. E:\
            };
            _logger.LogInformation("File Activity: {Activity}", activity.ToString());
            FileActivityDetected?.Invoke(this, new FileActivityEventArgs(activity));
        }

        private void OnRenamedEvent(RenamedEventArgs e, string drive)
        {
            var activity = new FileActivity
            {
                Timestamp = DateTime.UtcNow,
                Type = ActivityType.Renamed,
                FilePath = e.FullPath,
                OldFilePath = e.OldFullPath,
                Drive = drive.Substring(0, Math.Min(3, drive.Length))
            };
            _logger.LogInformation("File Activity: {Activity}", activity.ToString());
            FileActivityDetected?.Invoke(this, new FileActivityEventArgs(activity));
        }

        public void StopMonitoringDrive(string drivePath)
        {
            _logger.LogInformation("Stopping file system monitoring on: {Path}", drivePath);
            if (_watchers.TryRemove(drivePath.ToUpperInvariant(), out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }

        public void Dispose()
        {
            _logger.LogInformation("Disposing FsWatcherManager and all active watchers.");
            foreach (var key in _watchers.Keys.ToList()) // ToList to avoid modification during iteration
            {
                StopMonitoringDrive(key);
            }
            _watchers.Clear();
            GC.SuppressFinalize(this);
        }
    }
}