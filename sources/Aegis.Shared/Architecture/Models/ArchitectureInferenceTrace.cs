using Aegis.Shared.Enums;
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
        public InferenceSource Source { get; set; }
        public double Confidence { get; set; }
    }
}
