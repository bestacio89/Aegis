using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Models.Policies.Architecture
{
    public class NodePolicy
    {
        public bool EnforceSafeRequireUsage { get; set; } = true;
        public bool DisallowEval { get; set; } = true;
        public bool DisallowChildProcess { get; set; } = true;
        public bool CheckUnhandledPromises { get; set; } = true;
        public bool ValidatePackageJsonScripts { get; set; } = true;
        public bool EnforceESModules { get; set; } = false;
    }

}
