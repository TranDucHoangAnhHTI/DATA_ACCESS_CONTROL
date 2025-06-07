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

namespace UsbDlpAgent.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private HubConnection? _hubConnection;
        private ObservableCollection<FileActivity> _eventLog;
        private ICollectionView? _eventLogView;
        private bool _isConnected;
        private bool _isReconnecting;
        private bool _isInitialized;

        public ObservableCollection<FileActivity> EventLog
        {
            get => _eventLog;
            private set
            {
                _eventLog = value;
                OnPropertyChanged(nameof(EventLog));
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }

        public bool IsReconnecting
        {
            get => _isReconnecting;
            set
            {
                _isReconnecting = value;
                OnPropertyChanged(nameof(IsReconnecting));
            }
        }

        private const string HubUrl = "http://localhost:5124/dlphub"; // Phải khớp với service

        public MainWindow()
        {
            InitializeComponent();
            _eventLog = new ObservableCollection<FileActivity>();
            EventListView.ItemsSource = _eventLog;
            _eventLogView = CollectionViewSource.GetDefaultView(_eventLog);
            ConnectionStatusText.Text = "Status: Disconnected";
            DataContext = this;

            // Initialize ComboBox items
            TypeFilterCombo.Items.Clear();
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "All Types" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Created" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Modified" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Deleted" });
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "Renamed" });
            TypeFilterCombo.SelectedIndex = 0;

            _isInitialized = true;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(HubUrl)
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            _hubConnection.Closed += async (error) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    IsConnected = false;
                    IsReconnecting = false;
                    ConnectionStatusText.Text = $"Status: Disconnected. {error?.Message}";
                });
            };

            _hubConnection.Reconnecting += error =>
            {
                Dispatcher.Invoke(() =>
                {
                    IsConnected = false;
                    IsReconnecting = true;
                    ConnectionStatusText.Text = $"Status: Reconnecting... ({error?.Message})";
                });
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async (error) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    IsConnected = true;
                    IsReconnecting = false;
                    ConnectionStatusText.Text = "Status: Connected";
                });
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
                    UpdateEventCount();
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

            try
            {
                await _hubConnection.StartAsync();
                IsConnected = true;
                ConnectionStatusText.Text = "Status: Connected";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error connecting to server: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectionStatusText.Text = "Status: Connection Failed";
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
                _eventLogView.Filter = item =>
                {
                    if (item is not FileActivity activity) return false;

                    var searchText = SearchBox.Text.ToLower();
                    var typeFilter = (TypeFilterCombo.SelectedItem as ComboBoxItem)?.Content.ToString();

                    var matchesSearch = string.IsNullOrEmpty(searchText) ||
                                      activity.FilePath?.ToLower().Contains(searchText) == true ||
                                      activity.OldFilePath?.ToLower().Contains(searchText) == true;

                    var matchesType = typeFilter == "All Types" || 
                                    (typeFilter != null && activity.Type.ToString() == typeFilter);

                    return matchesSearch && matchesType;
                };

                UpdateEventCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying filters: {ex.Message}", "Filter Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                EventCountText.Text = $"{count} event{(count != 1 ? "s" : "")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating event count: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                ApplyFilters();
            }
        }

        private void EventListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (EventListView.SelectedItem is FileActivity activity)
            {
                var details = $"Timestamp: {activity.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\n" +
                            $"Type: {activity.Type}\n" +
                            $"File Path: {activity.FilePath}\n" +
                            (activity.OldFilePath != null ? $"Old File Path: {activity.OldFilePath}\n" : "") +
                            $"Drive: {activity.Drive}";

                MessageBox.Show(details, "Event Details", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}