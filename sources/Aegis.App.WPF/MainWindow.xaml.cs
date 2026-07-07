using System.Windows;
using Aegis.App.Wpf.ViewModels;

namespace Aegis.App.Wpf
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            
        }
    }
}