namespace UsbDlpAgent.Services
{
    public class DeviceEventArgs : EventArgs
    {
        public string DriveLetter { get; }
        public string VolumeName { get; }
        public DeviceEventArgs(string driveLetter, string volumeName)
        {
            DriveLetter = driveLetter;
            VolumeName = volumeName;
        }
    }

    public interface IUsbWatcher : IDisposable
    {
        event EventHandler<DeviceEventArgs>? UsbDeviceArrived;
        event EventHandler<DeviceEventArgs>? UsbDeviceRemoved;
        void StartWatching();
        void StopWatching();
    }
}