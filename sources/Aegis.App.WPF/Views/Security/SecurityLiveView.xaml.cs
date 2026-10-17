using Aegis.Wpf.ViewModels.Security;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace Aegis.Wpf.Views.Security;

public partial class SecurityLiveView : UserControl
{
    private INotifyCollectionChanged? _log;

    public SecurityLiveView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_log is not null)
            _log.CollectionChanged -= OnLogChanged;

        _log = (e.NewValue as SecurityLiveViewModel)?.LogEntries;

        if (_log is not null)
            _log.CollectionChanged += OnLogChanged;
    }

    // Keep the newest probe-log line in view while a run is streaming.
    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }
}