using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Models.Policies.BackEnd
{
    /// <summary> Governs acceptable complexity thresholds. </summary>
    public class ComplexityPolicy
    {
        public int MaxCyclomaticComplexity { get; set; } = 10;
        public int MaxLinesPerMethod { get; set; } = 50;
    }
}
