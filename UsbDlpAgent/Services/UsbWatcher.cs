using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Management;

namespace UsbDlpAgent.Services
{
    public class UsbWatcher : IUsbWatcher, IDisposable
    {
        private readonly ILogger<UsbWatcher> _logger;
        private readonly ManagementScope _scope;
        private ManagementEventWatcher? _insertWatcher;
        private ManagementEventWatcher? _removeWatcher;

        public event EventHandler<DeviceEventArgs>? UsbDeviceArrived;
        public event EventHandler<DeviceEventArgs>? UsbDeviceRemoved;

        public UsbWatcher(ILogger<UsbWatcher> logger)
        {
            _logger = logger;

            // Set up a WMI scope with privileges enabled
            var connectionOptions = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                EnablePrivileges = true
            };
            _scope = new ManagementScope(@"\\.\root\CIMV2", connectionOptions);
            _scope.Connect();
        }

        public void StartWatching()
        {
            try
            {
                _logger.LogInformation("Starting USB device watcher.");

                // Insertion events (EventType = 2)
                var insertQuery = new WqlEventQuery(
                    "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2");
                _insertWatcher = new ManagementEventWatcher(_scope, insertQuery);
                _insertWatcher.EventArrived += DeviceInsertedEvent;
                _insertWatcher.Start();
                _logger.LogInformation("Watching for USB device insertions.");

                // Removal events (EventType = 3)
                var removeQuery = new WqlEventQuery(
                    "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 3");
                _removeWatcher = new ManagementEventWatcher(_scope, removeQuery);
                _removeWatcher.EventArrived += DeviceRemovedEvent;
                _removeWatcher.Start();
                _logger.LogInformation("Watching for USB device removals.");
            }
            catch (ManagementException mex)
            {
                _logger.LogError(mex, "Failed to start WMI watcher. Ensure WMI service is running and you have sufficient permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while starting USB watcher.");
            }
        }

        private void DeviceInsertedEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                // Win32_VolumeChangeEvent exposes DriveName directly
                string driveLetter = e.NewEvent["DriveName"]?.ToString() ?? string.Empty;
                _logger.LogDebug("Insert Event: DriveName = {DriveLetter}", driveLetter);

                if (driveLetter.Length == 2 && driveLetter[1] == ':')
                {
                    _logger.LogInformation("Potential device arrival: {DriveLetter}", driveLetter);

                    if (IsRemovableDrive(driveLetter))
                    {
                        _logger.LogInformation("USB Device Arrived (removable): {DriveLetter}", driveLetter);
                        UsbDeviceArrived?.Invoke(this, new DeviceEventArgs(driveLetter, "Removable Drive"));
                    }
                    else
                    {
                        _logger.LogWarning("Device {DriveLetter} arrived but is not marked removable.", driveLetter);
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid DriveName on insert event: '{DriveName}'", driveLetter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling DeviceInsertedEvent");
            }
        }

        private void DeviceRemovedEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                string driveLetter = e.NewEvent["DriveName"]?.ToString() ?? string.Empty;
                _logger.LogDebug("Remove Event: DriveName = {DriveLetter}", driveLetter);

                if (driveLetter.Length == 2 && driveLetter[1] == ':')
                {
                    _logger.LogInformation("USB Device Removed: {DriveLetter}", driveLetter);
                    UsbDeviceRemoved?.Invoke(this, new DeviceEventArgs(driveLetter, "Removable Drive"));
                }
                else
                {
                    _logger.LogWarning("Invalid DriveName on remove event: '{DriveName}'", driveLetter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling DeviceRemovedEvent");
            }
        }

        private bool IsRemovableDrive(string driveLetter)
        {
            try
            {
                var drive = new DriveInfo(driveLetter);
                _logger.LogDebug("Drive {DriveLetter}: IsReady={IsReady}, Type={DriveType}",
                    driveLetter, drive.IsReady, drive.DriveType);

                return drive.IsReady && (drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.Fixed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IsRemovableDrive: cannot determine type for {DriveLetter}", driveLetter);
                return false;
            }
        }

        public void StopWatching()
        {
            _logger.LogInformation("Stopping USB device watcher.");

            if (_insertWatcher != null)
            {
                _insertWatcher.Stop();
                _insertWatcher.EventArrived -= DeviceInsertedEvent;
                _insertWatcher.Dispose();
                _insertWatcher = null;
            }

            if (_removeWatcher != null)
            {
                _removeWatcher.Stop();
                _removeWatcher.EventArrived -= DeviceRemovedEvent;
                _removeWatcher.Dispose();
                _removeWatcher = null;
            }
        }

        public void Dispose()
        {
            StopWatching();
            GC.SuppressFinalize(this);
        }
    }
}
