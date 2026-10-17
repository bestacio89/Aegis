using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Evaluators.Naming.Policies;

public sealed class NamingConventionResolver
{
    public INamingConventionPolicy Resolve(ProjectArchitectureContext context)
    {
        return context switch
        {
            {
                Language: "TypeScript",
                Framework: "Angular"
            }
                => new AngularNamingConventionPolicy(),


            {
                Language: "TypeScript",
                Framework: "React"
            }
                => new ReactNamingConventionPolicy(),


            {
                Language: "C#",
                Framework: "Blazor"
            }
                => new BlazorNamingConventionPolicy(),


            {
                Language: "C#",
                Framework: "Razor"
            }
                => new RazorNamingConventionPolicy(),


            {
                Language: "C#"
            }
                => new CSharpNamingConventionPolicy(),


            {
                Language: "Java"
            }
                => new JavaNamingConventionPolicy(),


            {
                Language: "Python"
            }
                => new PythonNamingConventionPolicy(),


            _
                => new DefaultNamingConventionPolicy()
        };
    }
}