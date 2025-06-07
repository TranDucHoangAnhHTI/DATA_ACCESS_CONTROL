using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic; // Cho List
using System.Collections.ObjectModel;
using System.Linq; // Cho OrderByDescending
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading; // Cho Dispatcher
using UsbDlpAgent.SharedModels; // Sử dụng model chung
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Controls; // Cho TextChangedEventArgs và SelectionChangedEventArgs
using System.Globalization;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Forms; // For NotifyIcon
using System.Drawing; // For SystemIcons
using System.Windows.Interop; // For window handle
using WinForms = System.Windows.Forms;
using WinFormsRectangle = System.Drawing.Rectangle;

namespace UsbDlpAgent.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private HubConnection? _hubConnection;
        private readonly ObservableCollection<FileActivity> _eventLog;
        private ICollectionView? _eventLogView;
        private bool _isConnected;
        private bool _isReconnecting;
        private bool _isInitialized;
        private readonly CultureInfo _viCulture = new CultureInfo("vi-VN");
        private readonly TimeZoneInfo _vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        private const string HubUrl = "http://localhost:5121/dlphub"; // Cập nhật URL hub
        private bool _showNotifications = true;
        private bool _autoScroll = true;

        public ObservableCollection<FileActivity> EventLog { get; }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged(nameof(IsConnected));
                }
            }
        }

        public bool IsReconnecting
        {
            get => _isReconnecting;
            set
            {
                if (_isReconnecting != value)
                {
                    _isReconnecting = value;
                    OnPropertyChanged(nameof(IsReconnecting));
                }
            }
        }

        public bool ShowNotifications
        {
            get => _showNotifications;
            set
            {
                if (_showNotifications != value)
                {
                    _showNotifications = value;
                    OnPropertyChanged(nameof(ShowNotifications));
                }
            }
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set
            {
                if (_autoScroll != value)
                {
                    _autoScroll = value;
                    OnPropertyChanged(nameof(AutoScroll));
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _eventLog = new ObservableCollection<FileActivity>();
            EventLog = _eventLog;
            DataContext = this;

            // Khởi tạo các thành phần UI
            InitializeFilters();
            InitializeEventLogView();

            _isInitialized = true;
        }

        private void InitializeEventLogView()
        {
            _eventLogView = CollectionViewSource.GetDefaultView(_eventLog);
            EventListView.ItemsSource = _eventLogView;
        }

        private void InitializeFilters()
        {
            // Đảm bảo ComboBox trống trước khi thiết lập ItemsSource
            TypeFilterCombo.Items.Clear();

            var filterItems = new[]
            {
                new FilterItem { Value = (ActivityType?)null, Display = "Tất cả loại" },
                new FilterItem { Value = ActivityType.Created, Display = "Tạo mới" },
                new FilterItem { Value = ActivityType.Modified, Display = "Sửa đổi" },
                new FilterItem { Value = ActivityType.Deleted, Display = "Xóa" },
                new FilterItem { Value = ActivityType.Renamed, Display = "Đổi tên" },
                new FilterItem { Value = ActivityType.MovedToUsb, Display = "Di chuyển vào USB" }
            };

            TypeFilterCombo.ItemsSource = filterItems;
            TypeFilterCombo.DisplayMemberPath = "Display";
            TypeFilterCombo.SelectedValuePath = "Value";
            TypeFilterCombo.SelectedIndex = 0;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await ConnectToServer();
        }

        private async Task ConnectToServer()
        {
            try
            {
                if (_hubConnection == null)
                {
                    _hubConnection = new HubConnectionBuilder()
                        .WithUrl(HubUrl)
                        .WithAutomaticReconnect()
                        .Build();

                    if (_hubConnection != null)
                    {
                        // Xử lý sự kiện nhận lịch sử
                        _hubConnection.On<List<FileActivity>>("ReceiveHistory", history =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                EventLog.Clear();
                                foreach (var activity in history.OrderByDescending(a => a.Timestamp))
                                {
                                    EventLog.Add(activity);
                                }
                                UpdateEventCount();
                            });
                        });

                        // Xử lý sự kiện nhận hoạt động mới
                        _hubConnection.On<FileActivity>("ReceiveFileActivity", activity =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                EventLog.Insert(0, activity);
                                if (EventLog.Count > 200)
                                {
                                    EventLog.RemoveAt(EventLog.Count - 1);
                                }
                                UpdateEventCount();

                                // Hiển thị thông báo và cuộn lên đầu
                                var activityType = activity.Type switch
                                {
                                    ActivityType.Created => "Tạo mới",
                                    ActivityType.Modified => "Sửa đổi",
                                    ActivityType.Deleted => "Xóa",
                                    ActivityType.Renamed => "Đổi tên",
                                    ActivityType.MovedToUsb => "Di chuyển vào USB",
                                    _ => "Không xác định"
                                };

                                ShowNotification(
                                    "Sự kiện mới",
                                    $"{activityType}: {activity.FilePath}\nỔ đĩa: {activity.Drive}"
                                );
                                ScrollToTop();
                            });
                        });

                        // Đăng ký các sự kiện kết nối
                        _hubConnection.Closed += HubConnection_Closed;
                        _hubConnection.Reconnecting += HubConnection_Reconnecting;
                        _hubConnection.Reconnected += HubConnection_Reconnected;

                        await _hubConnection.StartAsync();
                        UpdateConnectionStatus(true);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi kết nối đến máy chủ: {ex.Message}", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateConnectionStatus(false);
            }
        }

        private async Task RequestHistory(int count = 50)
        {
            try
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("RequestHistory", count);
                }
                else
                {
                    System.Windows.MessageBox.Show("Không thể kết nối đến máy chủ. Vui lòng kiểm tra lại kết nối.",
                                  "Lỗi kết nối",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi khi yêu cầu lịch sử: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitialized)
            {
                ApplyFilters();
            }
        }

        private void TypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitialized)
            {
                ApplyFilters();
            }
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                SearchBox.Clear();
                TypeFilterCombo.SelectedIndex = 0;
            }
        }

        private void ApplyFilters()
        {
            if (!_isInitialized || _eventLogView == null)
            {
                return;
            }

            try
            {
                _eventLogView.Filter = obj =>
                {
                    if (obj is not FileActivity activity) return false;

                    // Lọc theo loại hoạt động
                    if (TypeFilterCombo.SelectedValue is ActivityType selectedType)
                    {
                        if (activity.Type != selectedType) return false;
                    }

                    var searchText = SearchBox.Text.ToLower();
                    var matchesSearch = string.IsNullOrEmpty(searchText) ||
                                      activity.FilePath?.ToLower().Contains(searchText) == true ||
                                      activity.OldFilePath?.ToLower().Contains(searchText) == true;

                    return matchesSearch;
                };

                UpdateEventCount();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi khi áp dụng bộ lọc: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateEventCount()
        {
            if (!_isInitialized || _eventLogView == null)
            {
                return;
            }

            try
            {
                var count = _eventLogView.Cast<object>().Count();
                EventCountText.Text = $"{count} sự kiện";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi khi cập nhật số lượng sự kiện: {ex.Message}", "Lỗi cập nhật", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Vô hiệu hóa nút trong khi đang cập nhật
                RefreshButton.IsEnabled = false;

                // Bắt đầu animation xoay
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(1),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                RefreshIconRotation.BeginAnimation(RotateTransform.AngleProperty, animation);

                // Xóa dữ liệu cũ và yêu cầu dữ liệu mới từ server
                await Dispatcher.InvokeAsync(async () =>
                {
                    EventLog.Clear();
                    UpdateEventCount();
                    // Reset các bộ lọc
                    TypeFilterCombo.SelectedIndex = 0;
                    SearchBox.Clear();
                    
                    // Yêu cầu dữ liệu mới từ server
                    await RequestHistory();
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi khi cập nhật dữ liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Dừng animation và kích hoạt lại nút
                RefreshIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                RefreshButton.IsEnabled = true;
            }
        }

        private string FormatDateTime(DateTime utcTime)
        {
            try
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, _vietnamTimeZone);
                return localTime.ToString("dd/MM/yyyy HH:mm:ss.fff", _viCulture);
            }
            catch
            {
                // Fallback to UTC if conversion fails
                return utcTime.ToString("dd/MM/yyyy HH:mm:ss.fff", _viCulture) + " UTC";
            }
        }

        private void EventListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (EventListView.SelectedItem is FileActivity activity)
            {
                var details = $"Thời gian: {FormatDateTime(activity.Timestamp)}\n" +
                            $"Loại: {GetVietnameseEventType(activity.Type)}\n" +
                            $"Đường dẫn: {activity.FilePath}\n" +
                            (activity.OldFilePath != null ? $"Đường dẫn cũ: {activity.OldFilePath}\n" : "") +
                            $"Ổ đĩa: {activity.Drive}";

                System.Windows.MessageBox.Show(details, "Chi tiết sự kiện", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string GetVietnameseEventType(ActivityType type)
        {
            return type switch
            {
                ActivityType.Created => "Tạo mới",
                ActivityType.Modified => "Sửa đổi",
                ActivityType.Deleted => "Xóa",
                ActivityType.Renamed => "Đổi tên",
                ActivityType.MovedToUsb => "Di chuyển vào USB",
                _ => type.ToString()
            };
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            IsConnected = isConnected;
            ConnectionStatusText.Text = isConnected ? "Trạng thái: Đã kết nối" : "Trạng thái: Kết nối thất bại";
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_hubConnection != null)
            {
                try
                {
                    // Hủy đăng ký các sự kiện
                    _hubConnection.Closed -= HubConnection_Closed;
                    _hubConnection.Reconnecting -= HubConnection_Reconnecting;
                    _hubConnection.Reconnected -= HubConnection_Reconnected;

                    // Đóng kết nối
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                    _hubConnection = null;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi khi đóng kết nối: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task HubConnection_Closed(Exception? error)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                IsConnected = false;
                IsReconnecting = false;
                ConnectionStatusText.Text = $"Trạng thái: Ngắt kết nối. {error?.Message}";
            });
        }

        private Task HubConnection_Reconnecting(Exception? error)
        {
            Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                IsReconnecting = true;
                ConnectionStatusText.Text = $"Trạng thái: Đang kết nối lại... ({error?.Message})";
            });
            return Task.CompletedTask;
        }

        private async Task HubConnection_Reconnected(string? connectionId)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                IsConnected = true;
                IsReconnecting = false;
                ConnectionStatusText.Text = "Trạng thái: Đã kết nối";
                // Yêu cầu lịch sử sự kiện khi kết nối lại
                _ = RequestHistory();
            });
        }

        private void ShowNotification(string title, string message)
        {
            if (!ShowNotifications) return;

            // Tạo một cửa sổ thông báo đơn giản
            var notification = new Window
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    Margin = new Thickness(10),
                    TextWrapping = TextWrapping.Wrap
                },
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1)
            };

            // Đặt vị trí thông báo ở góc phải dưới màn hình
            var screen = WinForms.Screen.PrimaryScreen?.WorkingArea;
            if (screen != null)
            {
                var workingArea = (WinFormsRectangle)screen;
                notification.Left = workingArea.Right - notification.Width - 10;
                notification.Top = workingArea.Bottom - notification.Height - 10;
            }

            // Hiển thị thông báo
            notification.Show();

            // Tự động đóng sau 3 giây
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                notification.Close();
            };
            timer.Start();
        }

        private void ScrollToTop()
        {
            if (!AutoScroll) return;
            
            if (EventListView.Items.Count > 0)
            {
                EventListView.ScrollIntoView(EventListView.Items[0]);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Thêm class FilterItem để định nghĩa kiểu dữ liệu cho các item trong ComboBox
        private class FilterItem
        {
            public ActivityType? Value { get; set; }
            public string Display { get; set; } = string.Empty;
        }
    }
}