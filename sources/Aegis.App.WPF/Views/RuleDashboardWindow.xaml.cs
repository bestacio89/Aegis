using System.Windows;
using Aegis.App.Wpf.ViewModels;

namespace Aegis.App.Wpf.Views;

public partial class RuleDashboardWindow : Window
{
    public RuleDashboardWindow(RuleDashboardViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}