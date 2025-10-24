using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Architecture.Models.Policies.FrontEnd
{
    public class FrontendPolicy
    {
        public bool EnforceSelectorNaming { get; set; } = true;
        public string[] AllowedSelectorPrefixes { get; set; } = ["app", "lib"];
        public bool EnforceComponentPascalCase { get; set; } = true;
        public int MaxComponentComplexity { get; set; } = 50;
        public int MaxComponentsPerModule { get; set; } = 20;
        public bool CheckHooksRules { get; set; } = true;
        public int MaxHooksPerComponent { get; set; } = 10;
    }

}
