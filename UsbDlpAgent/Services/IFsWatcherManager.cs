using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.Services
{
    public class FileActivityEventArgs : EventArgs
    {
        public FileActivity Activity { get; }
        public FileActivityEventArgs(FileActivity activity)
        {
            Activity = activity;
        }
    }

    public interface IFsWatcherManager : IDisposable
    {
        event EventHandler<FileActivityEventArgs>? FileActivityDetected;
        void StartMonitoringDrive(string drivePath);
        void StopMonitoringDrive(string drivePath);
    }
}