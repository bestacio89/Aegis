using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;
using Aegis.Shared.Architecture.Models.Policies.Naming;
using Aegis.Shared.Architecture.Models.Policies.Performance;
using Aegis.Shared.Architecture.Models.Policies.Persistence;
using Aegis.Shared.Enums;

namespace Aegis.Shared.Architecture.Models.Policies
{
    public class AegisArchitecturePolicy
    {
        // 🧭 Metadata
        public string? Name { get; set; } = "Default Aegis Policy";
        public string? Version { get; set; } = "1.0";
        public ReportDetailLevel ReportDetailLevel { get; set; } = ReportDetailLevel.SummaryOnly;
        public bool EnableGlobalWeighting { get; set; } = true;

        // 🔧 Domain-specific policy groups
        public CohesionPolicy Cohesion { get; set; } = new();
        public ErrorHandlingPolicy ErrorHandling { get; set; } = new();
        public ApiConsistencyPolicy ApiConsistency { get; set; } = new();
        public DependencyGraphPolicy DependencyGraph { get; set; } = new();
        public ComplexityPolicy Complexity { get; set; } = new();
        public MaintainabilityPolicy Maintainability { get; set; } = new();
        public SecurityPolicy Security { get; set; } = new();
        public CouplingPolicy Coupling { get; set; } = new();
        public RepositoryPolicy Repository { get; set; } = new();
        public ArchitecturePolicy Architecture { get; set; } = new();
        public NodePolicy Node { get; set; } = new();
        public FrontendPolicy Frontend { get; set; } = new();
        public CircularDependencyPolicy CircularDependency { get; set; } = new();
        public ConfigurationPolicy Configuration { get; set; } = new();
        public LoggingPolicy Logging { get; set; } = new();
        public TransactionPolicy Transaction { get; set; } = new();
        public PerformancePolicy Performance { get; set; } = new();
        public DesignPatternPolicy DesignPatterns => Architecture.DesignPatterns;
        public DependencyPolicy Dependency { get; set; } = new();
        public NamingPolicy Naming { get; set; } = new();
    }
}
