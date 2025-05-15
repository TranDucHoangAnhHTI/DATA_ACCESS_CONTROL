using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UsbDlpAgent.Services
{
    public class LocalDriveMonitorService : ILocalDriveMonitorService
    {
        private readonly ILogger<LocalDriveMonitorService> _logger;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _localWatchers = new();
        private readonly List<string> _monitoredPaths = new List<string>();

        public event EventHandler<FileDeletedEventArgs>? LocalFileDeleted;
        public event EventHandler<FileCreatedEventArgs>? LocalFileCreated;
        public event EventHandler<FileChangedEventArgs>? LocalFileChanged;
        public event EventHandler<FileRenamedEventArgs>? LocalFileRenamed; // MỚI

        public LocalDriveMonitorService(ILogger<LocalDriveMonitorService> logger)
        {
            _logger = logger;
        }

        public void StartMonitoringLocalDrives(IEnumerable<string> driveLettersOrPaths)
        {
            _logger.LogInformation("Attempting to start local drive monitoring for specified paths.");
            foreach (var path in driveLettersOrPaths)
            {
                string fullPath = "";
                try
                {
                    if (string.IsNullOrEmpty(path)) { _logger.LogWarning("Null or empty local path provided. Skipping."); continue; }
                    fullPath = Path.GetFullPath(path);
                    if (!Directory.Exists(fullPath)) { _logger.LogWarning("Cannot monitor non-existent local path: {Path}", fullPath); continue; }
                    if (_localWatchers.ContainsKey(fullPath.ToUpperInvariant())) { _logger.LogInformation("Already monitoring local path: {Path}", fullPath); continue; }

                    _logger.LogDebug("Setting up FileSystemWatcher for local path: {Path}", fullPath);
                    var watcher = new FileSystemWatcher(fullPath)
                    {
                        NotifyFilter = NotifyFilters.FileName
                                     | NotifyFilters.DirectoryName
                                     | NotifyFilters.LastWrite
                                     | NotifyFilters.Size,
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = false,
                        InternalBufferSize = 65536
                    };

                    watcher.Created += OnLocalFileOrDirectoryCreated;
                    watcher.Deleted += OnLocalFileOrDirectoryDeleted;
                    watcher.Changed += OnLocalFileOrDirectoryChanged;
                    watcher.Renamed += OnLocalFileOrDirectoryRenamed; // Sử dụng handler riêng
                    watcher.Error += OnWatcherError;

                    if (_localWatchers.TryAdd(fullPath.ToUpperInvariant(), watcher))
                    {
                        watcher.EnableRaisingEvents = true;
                        _monitoredPaths.Add(fullPath);
                        _logger.LogInformation("Successfully started monitoring local path {Path} for file/directory events.", fullPath);
                    }
                    else
                    {
                        watcher.Dispose();
                        _logger.LogWarning("Failed to add local watcher for {Path}, possibly due to a concurrent add operation.", fullPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error starting FileSystemWatcher for local path {Path}", string.IsNullOrEmpty(fullPath) ? path : fullPath);
                }
            }
        }

        private void OnLocalFileOrDirectoryCreated(object sender, FileSystemEventArgs e)
        {
            _logger.LogDebug("Local created: Name: {Name}, Path: {FullPath}", e.Name, e.FullPath);
            LocalFileCreated?.Invoke(this, new FileCreatedEventArgs(e.FullPath, DateTime.UtcNow));
        }

        private void OnLocalFileOrDirectoryDeleted(object sender, FileSystemEventArgs e)
        {
            _logger.LogDebug("Local deleted: Name: {Name}, Path: {FullPath}", e.Name, e.FullPath);
            LocalFileDeleted?.Invoke(this, new FileDeletedEventArgs(e.FullPath, -1, DateTime.UtcNow));
        }

        private void OnLocalFileOrDirectoryChanged(object sender, FileSystemEventArgs e)
        {
            // FileSystemWatcher.Changed có thể kích hoạt cho thư mục khi nội dung bên trong thay đổi.
            // Chúng ta thường chỉ quan tâm đến thay đổi file.
            if (Directory.Exists(e.FullPath))
            {
                _logger.LogTrace("Directory attribute/timestamp changed (or content changed causing parent dir change event): {DirectoryPath}. Ignoring for direct file modification event.", e.FullPath);
                return; // Bỏ qua nếu là thư mục, trừ khi bạn muốn theo dõi thay đổi thuộc tính thư mục
            }
            _logger.LogDebug("Local changed: Name: {Name}, Path: {FullPath}, Type: {ChangeType}", e.Name, e.FullPath, e.ChangeType);
            LocalFileChanged?.Invoke(this, new FileChangedEventArgs(e.FullPath, DateTime.UtcNow, e.ChangeType));
        }

        private void OnLocalFileOrDirectoryRenamed(object sender, RenamedEventArgs e) // MỚI
        {
            _logger.LogDebug("Local renamed: Old: {OldFullPath} -> New: {FullPath}", e.OldFullPath, e.FullPath);
            LocalFileRenamed?.Invoke(this, new FileRenamedEventArgs(e.OldFullPath, e.FullPath, DateTime.UtcNow));
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.GetException(), "Local FileSystemWatcher encountered an error.");
            if (sender is FileSystemWatcher erroredWatcher)
            {
                _logger.LogWarning("Watcher for path {Path} encountered an error. It might stop monitoring.", erroredWatcher.Path);
            }
        }

        public void StopMonitoringLocalDrives()
        {
            _logger.LogInformation("Stopping all local drive monitoring.");
            var pathsToStop = _localWatchers.Keys.ToList();
            foreach (var pathKey in pathsToStop)
            {
                if (_localWatchers.TryRemove(pathKey, out var watcher))
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= OnLocalFileOrDirectoryCreated;
                    watcher.Deleted -= OnLocalFileOrDirectoryDeleted;
                    watcher.Changed -= OnLocalFileOrDirectoryChanged;
                    watcher.Renamed -= OnLocalFileOrDirectoryRenamed;
                    watcher.Error -= OnWatcherError;
                    watcher.Dispose();
                    _logger.LogInformation("Stopped and disposed local watcher for path {Path}", pathKey);
                }
            }
            _monitoredPaths.Clear();
            _localWatchers.Clear();
        }

        public void Dispose()
        {
            StopMonitoringLocalDrives();
            GC.SuppressFinalize(this);
        }
    }
}