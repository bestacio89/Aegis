using Aegis.App.Wpf.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.App.Wpf.Services.Abstractions
{
    public interface ILayerAnalysisService
    {
        Task<IReadOnlyList<LayerStat>> GetLayerStatsAsync(CancellationToken ct);
    }
}
