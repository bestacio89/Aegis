using Aegis.Shared.Models.Policies.Architecture;
using Aegis.Shared.Models.Policies.BackEnd;
using Aegis.Shared.Models.Policies.Dependency;
using Aegis.Shared.Models.Policies.FrontEnd;
using Aegis.Shared.Models.Policies.Infrastructure;
using Aegis.Shared.Models.Policies.Naming;
using Aegis.Shared.Models.Policies.Performance;
using Aegis.Shared.Models.Policies.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Models.Policies
{

    /// <summary>
    /// Root configuration object that defines thresholds and behavioral toggles
    /// for all Aegis evaluators.
    /// </summary>
    public class AegisPolicy
    {
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
