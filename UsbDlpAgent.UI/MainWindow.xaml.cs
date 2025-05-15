using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic; // Cho List
using System.Collections.ObjectModel;
using System.Linq; // Cho OrderByDescending
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading; // Cho Dispatcher
using UsbDlpAgent.SharedModels; // Sử dụng model chung

namespace UsbDlpAgent.UI
{
    public partial class MainWindow : Window
    {
        private HubConnection? _hubConnection;
        public ObservableCollection<FileActivity> EventLog { get; set; }

        private const string HubUrl = "http://localhost:5124/dlphub"; // Phải khớp với service

        public MainWindow()
        {
            InitializeComponent();
            EventLog = new ObservableCollection<FileActivity>();
            EventListView.ItemsSource = EventLog;
            ConnectionStatusText.Text = "Status: Disconnected";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(HubUrl)
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            _hubConnection.Closed += async (error) =>
            {
                await Dispatcher.InvokeAsync(() => ConnectionStatusText.Text = $"Status: Disconnected. {error?.Message}");
            };

            _hubConnection.Reconnecting += error =>
            {
                Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Status: Reconnecting... ({error?.Message})");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += connectionId =>
            {
                Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Status: Reconnected (ID: {connectionId})");
                // Yêu cầu lại lịch sử sau khi kết nối lại
                _hubConnection?.InvokeAsync("RequestHistory", 50); // Lấy 50 mục
                return Task.CompletedTask;
            };

            _hubConnection.On<FileActivity>("ReceiveNewEvent", (activity) =>
            {
                Dispatcher.Invoke(() =>
                {
                    EventLog.Insert(0, activity); // Thêm vào đầu
                    if (EventLog.Count > 200) // Giới hạn số lượng trên UI
                    {
                        EventLog.RemoveAt(EventLog.Count - 1);
                    }
                });
            });

            _hubConnection.On<List<FileActivity>>("ReceiveHistory", (history) =>
            {
                Dispatcher.Invoke(() =>
                {
                    EventLog.Clear();
                    // Lịch sử từ Hub có thể đã được sắp xếp, hoặc sắp xếp lại ở đây
                    foreach (var activity in history.OrderByDescending(a => a.Timestamp))
                    {
                        EventLog.Add(activity);
                    }
                });
            });

            await ConnectWithRetryAsync();
        }

        private async Task ConnectWithRetryAsync()
        {
            if (_hubConnection == null) return;

            Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Status: Connecting to {HubUrl}...");
            while (_hubConnection.State != HubConnectionState.Connected)
            {
                try
                {
                    await _hubConnection.StartAsync();
                    Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Status: Connected to {HubUrl}");
                    // Không cần gọi RequestHistory ở đây nữa vì OnConnectedAsync của Hub sẽ làm
                    return;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ConnectionStatusText.Text = "Status: Connection failed. Retrying in 5s...");
                    Console.WriteLine($"SignalR Connection failed: {ex.Message}");
                    await Task.Delay(5000);
                }
            }
        }

        private async void RequestHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                try
                {
                    // Yêu cầu 100 mục lịch sử khi nhấn nút
                    await _hubConnection.InvokeAsync("RequestHistory", 100);
                    MessageBox.Show("Requested history. Check the list.", "History Request", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error requesting history: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Not connected to the service.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_hubConnection != null)
            {
                // Hủy đăng ký sự kiện để tránh lỗi nếu DisposeAsync mất thời gian
                _hubConnection.Closed -= async (error) => { /* ... */ };
                _hubConnection.Reconnecting -= error => { /* ... */ return Task.CompletedTask; };
                _hubConnection.Reconnected -= connectionId => { /* ... */ return Task.CompletedTask; };

                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }
    }
}