// UsbDlpAgent.SharedModels/FileActivity.cs
namespace UsbDlpAgent.SharedModels
{
    public enum ActivityType
    {
        Created,
        Modified,
        Deleted,
        Renamed,
        MovedToUsb,
        UsbDeviceArrived,
        UsbDeviceRemoved
        // Bạn có thể thêm các loại khác nếu cần
    }

    public class FileActivity
    {
        public DateTime Timestamp { get; set; }
        public ActivityType Type { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? OldFilePath { get; set; }
        public string Drive { get; set; } = string.Empty;

        public override string ToString() // Cập nhật để hiển thị enum Type
        {
            string typeStr = Enum.GetName(typeof(ActivityType), Type) ?? Type.ToString();
            string action;
            if (Type == ActivityType.MovedToUsb && OldFilePath != null)
            {
                action = $"{typeStr}: '{OldFilePath}' moved to '{FilePath}' on {Drive}";
            }
            else if (Type == ActivityType.Renamed && OldFilePath != null)
            {
                action = $"{typeStr}: '{OldFilePath}' to '{FilePath}' on {Drive}";
            }
            else
            {
                action = $"{typeStr}: {FilePath} on {Drive}";
            }
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {action}";
        }
    }
}