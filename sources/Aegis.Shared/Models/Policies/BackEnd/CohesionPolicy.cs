using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Models.Policies.BackEnd
{
    /// <summary> Governs class cohesion thresholds. </summary>
    public class CohesionPolicy
    {
        public int MaxMembersPerClass { get; set; } = 40;
        public double MaxMethodFieldRatio { get; set; } = 6.0;
    }
}
