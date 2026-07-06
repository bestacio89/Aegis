using System.Windows;
using Aegis.App.Wpf.ViewModels;

namespace Aegis.App.Wpf.Views;

public partial class SectionDashboardWindow : Window
{
    public SectionDashboardWindow(SectionDashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

}