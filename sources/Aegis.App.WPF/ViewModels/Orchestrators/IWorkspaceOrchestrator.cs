using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace Aegis.Wpf.ViewModels.Orchestrators
{
    public interface IWorkspaceOrchestrator
    {
        string ModeName { get; }
        IReadOnlyList<WorkspaceTabDescriptor> Tabs { get; }
        object ActiveWorkspaceViewModel { get; }
        ICommand SelectTabCommand { get; }

        Task RunAnalysisAsync(string repositoryPath, CancellationToken token);
        Task ExportAsync(string format, string outputPath, CancellationToken token);
    }

}
