using Aegis.App.Wpf.ViewModels;
using Aegis.App.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Aegis.App.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OpenLayerDashboard(object sender, RoutedEventArgs e)
        {
            var window = new LayerDashboardWindow
            {
                DataContext = App.Host.Services.GetRequiredService<LayerDashboardViewModel>()
            };
            window.Show();
        }
        private void OpenSectionDashboard(object sender, RoutedEventArgs e)
        {
            var window = new LayerDashboardWindow
            {
                DataContext = App.Host.Services.GetRequiredService<SectionDashboardViewModel>()
            };
            window.Show();
        }

        private void OpenRuleDashboard(object sender, RoutedEventArgs e)
        {
            var window = new LayerDashboardWindow
            {
                DataContext = App.Host.Services.GetRequiredService<RuleDashboardViewModel>()
            };
            window.Show();
        }

    }
}