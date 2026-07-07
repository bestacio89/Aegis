using Aegis.Shared.Architecture.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.App.Wpf.Services.Abstractions
{
    public interface IDashboardDataMapper
    {
        DashboardSnapshot Build(
            AegisArchitectureReport report,
            ProjectArchitectureContext context);
    }
}
