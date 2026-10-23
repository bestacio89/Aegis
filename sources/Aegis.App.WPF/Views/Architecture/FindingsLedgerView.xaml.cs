using System.Windows;
using System.Windows.Controls;
using Aegis.Wpf.ViewModels.Architecture;

namespace Aegis.Wpf.Views.Architecture;

public partial class FindingsLedgerView : UserControl
{
    public FindingsLedgerView()
    {
        InitializeComponent();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FindingsLedgerViewModel viewModel)
            viewModel.ClearFilter();
    }
}
