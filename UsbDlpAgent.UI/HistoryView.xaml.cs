using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.UI
{
    public partial class HistoryView : UserControl
    {
        public ObservableCollection<FileActivity> HistoryLog { get; set; }
        private HubConnection? _hubConnection;

        public HistoryView()
        {
            InitializeComponent();
            HistoryLog = new ObservableCollection<FileActivity>();
            HistoryListView.ItemsSource = HistoryLog;
        }

        public void SetHubConnection(HubConnection hubConnection)
        {
            _hubConnection = hubConnection;
        }

        public void LoadFilteredHistory(IEnumerable<FileActivity> history)
        {
            Dispatcher.Invoke(() =>
            {
                HistoryLog.Clear();
                if (history != null)
                {
                    foreach (var activity in history.OrderByDescending(a => a.Timestamp))
                    {
                        HistoryLog.Add(activity);
                    }
                }
            });
        }

        private async void LoadHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                try
                {
                    // Điều chỉnh EndDatePicker để bao gồm toàn bộ ngày được chọn
                    DateTime? endDate = EndDatePicker.SelectedDate;
                    if (endDate.HasValue)
                    {
                        endDate = endDate.Value.Date.AddDays(1).AddTicks(-1); // Cuối ngày
                    }
                    DateTime? startDate = StartDatePicker.SelectedDate?.Date; // Đầu ngày


                    await _hubConnection.InvokeAsync("RequestHistoryByFilter",
                        startDate,
                        endDate,
                        KeywordTextBox.Text,
                        200); // Giới hạn số lượng item
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi yêu cầu lịch sử: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Chưa kết nối tới dịch vụ.", "Lỗi Kết nối", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HistoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // The detail view updates via DataBinding to ListView's SelectedItem.
        }
    }
}