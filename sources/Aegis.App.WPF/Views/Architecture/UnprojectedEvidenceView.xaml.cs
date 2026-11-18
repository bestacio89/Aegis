using System.Windows;
using System.Windows.Controls;

namespace Aegis.Wpf.Views.Architecture;

public partial class UnprojectedEvidenceView : UserControl
{
    public UnprojectedEvidenceView()
    {
        InitializeComponent();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.Architecture.UnprojectedEvidenceViewModel viewModel)
            return;

        viewModel.SearchText = string.Empty;
        viewModel.SelectedDomain = "All";
        viewModel.SelectedReason = "All";
    }
}
