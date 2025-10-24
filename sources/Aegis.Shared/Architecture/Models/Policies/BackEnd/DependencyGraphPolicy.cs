using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Architecture.Models.Policies.BackEnd
{
    /// <summary> Governs coupling and module dependencies. </summary>
    public class DependencyGraphPolicy
    {
        public int MaxDependenciesPerModule { get; set; } = 10;
    }

}
