using System;
using System.Collections.Generic;
using System.IO; // Cho WatcherChangeTypes và RenamedEventArgs

namespace UsbDlpAgent.Services
{
    public class FileDeletedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public long FileSize { get; }
        public DateTime Timestamp { get; }

        public FileDeletedEventArgs(string filePath, long fileSize, DateTime timestamp)
        {
            FilePath = filePath;
            FileSize = fileSize;
            Timestamp = timestamp;
        }
    }

    public class FileCreatedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public DateTime Timestamp { get; }

        public FileCreatedEventArgs(string filePath, DateTime timestamp)
        {
            FilePath = filePath;
            Timestamp = timestamp;
        }
    }

    public class FileChangedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public DateTime Timestamp { get; }
        public WatcherChangeTypes ChangeType { get; }

        public FileChangedEventArgs(string filePath, DateTime timestamp, WatcherChangeTypes changeType)
        {
            FilePath = filePath;
            Timestamp = timestamp;
            ChangeType = changeType;
        }
    }

    public class FileRenamedEventArgs : EventArgs // MỚI
    {
        public string OldFullPath { get; }
        public string FullPath { get; } // New FullPath
        public DateTime Timestamp { get; }

        public FileRenamedEventArgs(string oldFullPath, string fullPath, DateTime timestamp)
        {
            OldFullPath = oldFullPath;
            FullPath = fullPath;
            Timestamp = timestamp;
        }
    }

    public interface ILocalDriveMonitorService : IDisposable
    {
        event EventHandler<FileDeletedEventArgs>? LocalFileDeleted;
        event EventHandler<FileCreatedEventArgs>? LocalFileCreated;
        event EventHandler<FileChangedEventArgs>? LocalFileChanged;
        event EventHandler<FileRenamedEventArgs>? LocalFileRenamed; // MỚI

        void StartMonitoringLocalDrives(IEnumerable<string> driveLettersOrPaths);
        void StopMonitoringLocalDrives();
    }
}