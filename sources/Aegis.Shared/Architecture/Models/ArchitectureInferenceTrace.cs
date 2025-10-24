using Aegis.Shared.Architecture.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Architecture.Models
{
    public sealed class ArchitectureInferenceTrace
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public ArchitectureInferenceSource Source { get; set; }
        public double Confidence { get; set; }
    }
}
