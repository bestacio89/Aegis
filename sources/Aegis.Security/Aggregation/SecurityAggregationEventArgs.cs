// Aegis.Security/Aggregation/SecurityAggregationEventArgs.cs
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Aggregation
{
    public sealed class SecurityAggregationEventArgs : EventArgs
    {
        public SecurityScanSummary Snapshot { get; }
        public SecurityAggregationEventArgs(SecurityScanSummary snapshot)
          => Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }
}
