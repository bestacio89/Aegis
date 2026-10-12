using Aegis.Shared.Security.Enums;
using Aegis.Wpf.Models.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Aegis.Wpf.ViewModels.Security;

public sealed partial class SecurityDomainsViewModel : ObservableObject
{
    public ObservableCollection<SecurityDomainItem> Domains { get; } = new();

    [ObservableProperty] private SecurityDomainItem? selectedDomain;
    [ObservableProperty] private int totalDomains;
    [ObservableProperty] private int compliantDomains;
    [ObservableProperty] private int domainsAtRisk;

    /// <summary>Set by the workspace: jump to the findings tab filtered on a domain.</summary>
    public Action<SecurityCategory>? OpenDomainRequested { get; set; }

    public void Update(SecurityScanResult result)
    {
        Domains.Clear();
        foreach (var d in result.Domains)
            Domains.Add(d);

        SelectedDomain = null;
        TotalDomains = Domains.Count;
        CompliantDomains = Domains.Count(d => d.IsCompliant);
        DomainsAtRisk = TotalDomains - CompliantDomains;
    }

    [RelayCommand]
    private void OpenDomain(SecurityDomainItem? item)
    {
        if (item is not null)
            OpenDomainRequested?.Invoke(item.Domain);
    }
}