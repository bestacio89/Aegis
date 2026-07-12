using System.Windows;
using Aegis.Wpf.ViewModels;

namespace Aegis.Wpf
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

   
    }
}