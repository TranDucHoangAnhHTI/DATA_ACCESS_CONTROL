using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UsbDlpAgent.SharedModels;

namespace UsbDlpAgent.UI
{
    public partial class RealtimeEventsView : UserControl
    {
        public ObservableCollection<FileActivity> EventLog { get; set; }

        public RealtimeEventsView()
        {
            InitializeComponent();
            EventLog = new ObservableCollection<FileActivity>();
            EventListView.ItemsSource = EventLog;
        }

        public void AddEvent(FileActivity activity)
        {
            Dispatcher.Invoke(() =>
            {
                EventLog.Insert(0, activity);
                if (EventLog.Count > 200) // Limit items for performance
                {
                    EventLog.RemoveAt(EventLog.Count - 1);
                }
            });
        }

        public void LoadInitialHistory(IEnumerable<FileActivity> history)
        {
            Dispatcher.Invoke(() =>
            {
                EventLog.Clear();
                if (history != null)
                {
                    foreach (var activity in history.OrderByDescending(a => a.Timestamp))
                    {
                        EventLog.Add(activity);
                    }
                }
            });
        }

        private void EventListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // The detail view updates via DataBinding to ListView's SelectedItem.
            // Add any additional logic here if needed when selection changes.
        }
    }
}