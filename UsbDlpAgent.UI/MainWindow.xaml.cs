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
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Mouse = System.Windows.Input.Mouse;
using PdfFont = iTextSharp.text.Font;
using System.Text;

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
        private const string HubUrl = "http://localhost:5124/dlphub"; // Cập nhật URL hub
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
            DriveFilterCombo.Items.Clear();

            // Khởi tạo bộ lọc loại sự kiện
            var filterItems = new[]
            {
                new FilterItem { Value = (ActivityType?)null, Display = "Tất cả loại" },
                new FilterItem { Value = ActivityType.Created, Display = "Tạo mới" },
                new FilterItem { Value = ActivityType.Modified, Display = "Sửa đổi" },
                new FilterItem { Value = ActivityType.Deleted, Display = "Xóa" },
                new FilterItem { Value = ActivityType.Renamed, Display = "Đổi tên" },
                new FilterItem { Value = ActivityType.MovedToUsb, Display = "Di chuyển vào USB" },
                new FilterItem { Value = ActivityType.UsbDeviceArrived, Display = "USB được cắm vào" },
                new FilterItem { Value = ActivityType.UsbDeviceRemoved, Display = "USB được rút ra" }
            };

            TypeFilterCombo.ItemsSource = filterItems;
            TypeFilterCombo.DisplayMemberPath = "Display";
            TypeFilterCombo.SelectedValuePath = "Value";
            TypeFilterCombo.SelectedIndex = 0;

            // Khởi tạo bộ lọc ổ đĩa
            var driveItems = new List<FilterItem<string>>
            {
                new FilterItem<string> { Value = null, Display = "Tất cả ổ đĩa" }
            };
            DriveFilterCombo.ItemsSource = driveItems;
            DriveFilterCombo.DisplayMemberPath = "Display";
            DriveFilterCombo.SelectedValuePath = "Value";
            DriveFilterCombo.SelectedIndex = 0;

            // Khởi tạo DatePicker
            StartDatePicker.SelectedDate = null;
            EndDatePicker.SelectedDate = null;
        }

        private void UpdateDriveFilter()
        {
            if (!_isInitialized || DriveFilterCombo.ItemsSource is not List<FilterItem<string>> driveItems)
                return;

            // Lấy danh sách ổ đĩa duy nhất từ EventLog
            var uniqueDrives = EventLog
                .Select(a => a.Drive)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            // Cập nhật danh sách ổ đĩa trong ComboBox
            driveItems.Clear();
            driveItems.Add(new FilterItem<string> { Value = null, Display = "Tất cả ổ đĩa" });
            driveItems.AddRange(uniqueDrives.Select(d => new FilterItem<string> { Value = d, Display = d }));

            // Refresh ComboBox
            DriveFilterCombo.Items.Refresh();
        }

        private void DriveFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitialized)
            {
                ApplyFilters();
            }
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            TypeFilterCombo.SelectedIndex = 0;
            DriveFilterCombo.SelectedIndex = 0;
            StartDatePicker.SelectedDate = null;
            EndDatePicker.SelectedDate = null;
            ApplyFilters();
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

                    // Lọc theo ổ đĩa
                    if (DriveFilterCombo.SelectedValue is string selectedDrive && !string.IsNullOrEmpty(selectedDrive))
                    {
                        if (activity.Drive != selectedDrive) return false;
                    }

                    // Lọc theo thời gian
                    if (StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue)
                    {
                        var startDate = StartDatePicker.SelectedDate.Value.Date;
                        var endDate = EndDatePicker.SelectedDate.Value.Date.AddDays(1).AddTicks(-1);
                        if (activity.Timestamp < startDate || activity.Timestamp > endDate) return false;
                    }

                    // Tìm kiếm theo từ khóa
                    var searchText = SearchBox.Text.ToLower();
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        // Tìm kiếm trong tên file, đường dẫn, ổ đĩa
                        var matchesSearch = 
                            (activity.FilePath?.ToLower().Contains(searchText) == true) ||
                            (activity.OldFilePath?.ToLower().Contains(searchText) == true) ||
                            (activity.Drive?.ToLower().Contains(searchText) == true) ||
                            (GetVietnameseEventType(activity.Type).ToLower().Contains(searchText));

                        if (!matchesSearch) return false;
                    }

                    return true;
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
                // Không cần animation nữa vì đã thay đổi UI
                await RequestHistory();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi làm mới: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
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
                ActivityType.UsbDeviceArrived => "USB được cắm vào",
                ActivityType.UsbDeviceRemoved => "USB được rút ra",
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

        // Thêm class FilterItem generic để hỗ trợ các kiểu dữ liệu khác nhau
        private class FilterItem<T>
        {
            public T? Value { get; set; }
            public string Display { get; set; } = string.Empty;
        }

        // Alias cho FilterItem<ActivityType?> để giữ tương thích với code cũ
        private class FilterItem : FilterItem<ActivityType?>
        {
        }

        private async void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "PDF files (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName = $"USB_DLP_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    ExportPdfButton.IsEnabled = false;
                    Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                    await Task.Run(() =>
                    {
                        using (var document = new Document(PageSize.A4.Rotate()))
                        {
                            using (var writer = PdfWriter.GetInstance(document, new FileStream(saveFileDialog.FileName, FileMode.Create, FileAccess.Write)))
                            {
                                document.Open();

                                // Add title
                                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                                var title = new Paragraph("USB DLP Activity Report", titleFont);
                                title.Alignment = Element.ALIGN_CENTER;
                                title.SpacingAfter = 20f;
                                document.Add(title);

                                // Add timestamp
                                var dateFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                                var timestamp = new Paragraph($"Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", dateFont);
                                timestamp.Alignment = Element.ALIGN_CENTER;
                                timestamp.SpacingAfter = 20f;
                                document.Add(timestamp);

                                // Create table
                                var table = new PdfPTable(5);
                                table.WidthPercentage = 100;
                                table.SetWidths(new float[] { 2f, 2f, 1.5f, 3f, 3f });

                                // Add headers
                                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                                var headers = new[] { "Time", "Type", "Drive", "File Path", "Details" };
                                foreach (var header in headers)
                                {
                                    table.AddCell(new PdfPCell(new Phrase(header, headerFont))
                                    {
                                        BackgroundColor = new BaseColor(240, 240, 240),
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                        Padding = 5f
                                    });
                                }

                                // Add data rows
                                var dataFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);
                                foreach (var activity in EventLog)
                                {
                                    table.AddCell(new PdfPCell(new Phrase(activity.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), dataFont)) { Padding = 5f });
                                    table.AddCell(new PdfPCell(new Phrase(GetActivityTypeText(activity.Type), dataFont)) { Padding = 5f });
                                    table.AddCell(new PdfPCell(new Phrase(activity.Drive ?? "N/A", dataFont)) { Padding = 5f });
                                    table.AddCell(new PdfPCell(new Phrase(activity.FilePath ?? "N/A", dataFont)) { Padding = 5f });

                                    var details = new StringBuilder();
                                    if (!string.IsNullOrEmpty(activity.OldFilePath))
                                    {
                                        details.AppendLine($"Old Path: {activity.OldFilePath}");
                                    }
                                    table.AddCell(new PdfPCell(new Phrase(details.ToString().TrimEnd(), dataFont)) { Padding = 5f });
                                }

                                document.Add(table);

                                // Add summary
                                var summaryFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                                var summary = new Paragraph($"Total Events: {EventLog.Count}", summaryFont);
                                summary.Alignment = Element.ALIGN_RIGHT;
                                summary.SpacingBefore = 20f;
                                document.Add(summary);
                            }
                        }
                    });

                    System.Windows.MessageBox.Show("Report exported successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exporting PDF: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ExportPdfButton.IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }

        private string GetActivityTypeText(ActivityType type)
        {
            return type switch
            {
                ActivityType.Created => "Created",
                ActivityType.Modified => "Modified",
                ActivityType.Deleted => "Deleted",
                ActivityType.Renamed => "Renamed",
                ActivityType.MovedToUsb => "Moved to USB",
                ActivityType.UsbDeviceArrived => "USB Device Connected",
                ActivityType.UsbDeviceRemoved => "USB Device Disconnected",
                _ => "Unknown"
            };
        }

        private void OnReceiveHistory(List<FileActivity> history)
        {
            Dispatcher.Invoke(() =>
            {
                EventLog.Clear();
                foreach (var activity in history.OrderByDescending(a => a.Timestamp))
                {
                    EventLog.Add(activity);
                }
                UpdateEventCount();
                UpdateDriveFilter();
            });
        }

        private void OnReceiveNewEvent(FileActivity activity)
        {
            Dispatcher.Invoke(() =>
            {
                EventLog.Insert(0, activity);
                if (EventLog.Count > 200)
                {
                    EventLog.RemoveAt(EventLog.Count - 1);
                }
                UpdateEventCount();
                UpdateDriveFilter();

                var activityType = GetVietnameseEventType(activity.Type);

                ShowNotification(
                    "Sự kiện mới",
                    $"{activityType}: {activity.FilePath}\nỔ đĩa: {activity.Drive}"
                );
                ScrollToTop();
            });
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
                        _hubConnection.On<List<FileActivity>>("ReceiveHistory", OnReceiveHistory);

                        // Xử lý sự kiện nhận hoạt động mới
                        _hubConnection.On<FileActivity>("ReceiveNewEvent", OnReceiveNewEvent);

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

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitialized)
            {
                ApplyFilters();
            }
        }

        private void UpdateStatusBar()
        {
            // Implementation of UpdateStatusBar method
        }
    }
}