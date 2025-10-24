using Microsoft.Web.WebView2.Wpf;
using MvvmHelpers;
using System.IO;

namespace Aegis.App.Wpf.ViewModels;

public sealed class ReportViewModel : BaseViewModel
{
    private string _reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aegis", "Reports", "index.html");
    public string ReportPath
    {
        get => _reportPath;
        set => SetProperty(ref _reportPath, value);
    }
}
