using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading; // Cho Dispatcher
using UsbDlpAgent.SharedModels;
using Serilog;

namespace UsbDlpAgent.UI
{
    public partial class MainWindow : Window
    {
        private HubConnection? _hubConnection;
        private const string HubUrl = "http://localhost:5121/dlphub"; // Phải khớp với service
        private bool _historyViewHubInitialized = false;

        // Dummy handlers for unsubscribing to avoid CS1998 warnings
        private Task HubConnection_Closed_DummyHandler(Exception? arg) { return Task.CompletedTask; }
        private Task HubConnection_Reconnecting_DummyHandler(Exception? arg) { return Task.CompletedTask; }
        private Task HubConnection_Reconnected_DummyHandler(string? arg) { return Task.CompletedTask; }

        public MainWindow()
        {
            try
            {
                Log.Information("Initializing MainWindow...");
                InitializeComponent();
                ConnectionStatusText.Text = "Trạng thái: Đã ngắt kết nối";
                Log.Debug("MainWindow initialized successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error initializing MainWindow");
                MessageBox.Show($"Error initializing main window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Information("MainWindow loaded, initializing SignalR connection...");
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(HubUrl)
                    .WithAutomaticReconnect(new[] {
                        TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)
                    })
                    .Build();

                Log.Debug("Setting up SignalR event handlers...");
                _hubConnection.Closed += HubConnection_Closed_Handler;
                _hubConnection.Reconnecting += HubConnection_Reconnecting_Handler;
                _hubConnection.Reconnected += HubConnection_Reconnected_Handler;

                _hubConnection.On<FileActivity>("ReceiveNewEvent", (activity) =>
                {
                    try
                    {
                        Log.Debug("Received new event: {Event}", activity);
                        RealtimeViewInstance?.AddEvent(activity);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error handling new event");
                    }
                });

                _hubConnection.On<List<FileActivity>>("ReceiveInitialRealtimeHistory", (history) =>
                {
                    try
                    {
                        Log.Debug("Received initial history with {Count} events", history?.Count ?? 0);
                        RealtimeViewInstance?.LoadInitialHistory(history);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error handling initial history");
                    }
                });

                _hubConnection.On<List<FileActivity>>("ReceiveFilteredHistoryData", (history) =>
                {
                    try
                    {
                        Log.Debug("Received filtered history with {Count} events", history?.Count ?? 0);
                        HistoryViewInstance?.LoadFilteredHistory(history);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error handling filtered history");
                    }
                });

                await ConnectWithRetryAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in Window_Loaded");
                MessageBox.Show($"Error initializing connection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Task HubConnection_Closed_Handler(Exception? error)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                Log.Warning(error, "SignalR connection closed");
                ConnectionStatusText.Text = $"Trạng thái: Đã ngắt kết nối. {(error == null ? "" : error.Message)}";
                _historyViewHubInitialized = false;
            }).Task;
        }

        private Task HubConnection_Reconnecting_Handler(Exception? error)
        {
            Log.Information(error, "SignalR connection reconnecting");
            Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Trạng thái: Đang kết nối lại... ({(error == null ? "" : error.Message)})");
            return Task.CompletedTask;
        }

        private async Task HubConnection_Reconnected_Handler(string? connectionId)
        {
            try
            {
                Log.Information("SignalR connection reconnected with ID: {ConnectionId}", connectionId);
                Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Trạng thái: Đã kết nối lại (ID: {connectionId})");

                if (!_historyViewHubInitialized && HistoryViewInstance != null && _hubConnection != null)
                {
                    Log.Debug("Initializing history view hub connection");
                    HistoryViewInstance.SetHubConnection(_hubConnection);
                    _historyViewHubInitialized = true;
                }

                if (_hubConnection != null)
                {
                    Log.Debug("Requesting initial events after reconnect");
                    await _hubConnection.InvokeAsync("RequestInitialRealtimeEvents", 50);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in reconnected handler");
                MessageBox.Show($"Error after reconnection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ConnectWithRetryAsync()
        {
            if (_hubConnection == null) return;

            Log.Information("Attempting to connect to SignalR hub at {HubUrl}", HubUrl);
            Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Trạng thái: Đang kết nối đến {HubUrl}...");
            
            int retryCount = 0;
            while (true)
            {
                if (_hubConnection.State == HubConnectionState.Connected) break;
                try
                {
                    Log.Debug("Starting SignalR connection attempt {RetryCount}", retryCount + 1);
                    await _hubConnection.StartAsync();
                    Log.Information("Successfully connected to SignalR hub");
                    Dispatcher.Invoke(() => ConnectionStatusText.Text = $"Trạng thái: Đã kết nối đến {HubUrl}");

                    if (!_historyViewHubInitialized && HistoryViewInstance != null)
                    {
                        Log.Debug("Initializing history view hub connection");
                        HistoryViewInstance.SetHubConnection(_hubConnection);
                        _historyViewHubInitialized = true;
                    }

                    Log.Debug("Requesting initial events");
                    await _hubConnection.InvokeAsync("RequestInitialRealtimeEvents", 50);
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Log.Warning(ex, "SignalR connection attempt {RetryCount} failed", retryCount);
                    Dispatcher.Invoke(() => ConnectionStatusText.Text = "Trạng thái: Kết nối thất bại. Thử lại sau 5 giây...");
                    await Task.Delay(5000);
                }
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Information("User logging out");
                if (_hubConnection != null)
                {
                    Log.Debug("Stopping SignalR connection");
                    await _hubConnection.StopAsync();
                }
                Log.Debug("Opening login window");
                LoginWindow loginWindow = new LoginWindow();
                loginWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during logout");
                MessageBox.Show($"Error during logout: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Log.Information("MainWindow closing, cleaning up SignalR connection");
                if (_hubConnection != null)
                {
                    Log.Debug("Removing SignalR event handlers");
                    _hubConnection.Closed -= HubConnection_Closed_Handler;
                    _hubConnection.Reconnecting -= HubConnection_Reconnecting_Handler;
                    _hubConnection.Reconnected -= HubConnection_Reconnected_Handler;

                    Log.Debug("Removing SignalR message handlers");
                    _hubConnection.Remove("ReceiveNewEvent");
                    _hubConnection.Remove("ReceiveInitialRealtimeHistory");
                    _hubConnection.Remove("ReceiveFilteredHistoryData");

                    Log.Debug("Disposing SignalR connection");
                    await _hubConnection.DisposeAsync();
                    _hubConnection = null;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during window closing");
                MessageBox.Show($"Error during cleanup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}