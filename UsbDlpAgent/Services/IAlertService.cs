using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.Services
{
    public interface IAlertService
    {
        void TriggerAlert(FileActivity activity);
    }
}