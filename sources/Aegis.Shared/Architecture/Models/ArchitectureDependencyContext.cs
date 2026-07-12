using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Shared.Architecture.Models
{
    public sealed class ArchitectureDependencyContext
    {
        public string Source { get; set; } = string.Empty;

        public string SourceLayer { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public string TargetLayer { get; set; } = string.Empty;

        public string DependencyType { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;
    }
}
