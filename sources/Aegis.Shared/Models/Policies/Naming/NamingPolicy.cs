namespace Aegis.Shared.Models.Policies.Naming;

/// <summary>
/// 🧭 Defines organizational naming conventions and compliance expectations.
/// </summary>
public sealed class NamingPolicy
{
    /// <summary>
    /// Enables or disables all naming analysis.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // ==========================================================
    // 🏷️ Class & Interface Naming Rules
    // ==========================================================

    /// <summary>
    /// Require PascalCase for all class and interface names.
    /// </summary>
    public bool EnforcePascalCase { get; set; } = true;

    /// <summary>
    /// Require that interfaces start with 'I' (e.g., IRepository, ICacheProvider).
    /// </summary>
    public bool RequireInterfacePrefix { get; set; } = true;

    /// <summary>
    /// If true, all DTOs and ViewModels must end with 'Dto' or 'ViewModel'.
    /// </summary>
    public bool EnforceSuffixForModels { get; set; } = true;

    /// <summary>
    /// Valid suffixes allowed for data transfer or view models.
    /// </summary>
    public string[] AllowedModelSuffixes { get; set; } = ["Dto", "ViewModel", "Model"];

    /// <summary>
    /// If true, enforces that controllers or services follow proper suffix conventions.
    /// </summary>
    public bool EnforceComponentSuffix { get; set; } = true;

    /// <summary>
    /// Valid suffixes for core components (Controller, Service, Repository, Manager, etc.).
    /// </summary>
    public string[] AllowedComponentSuffixes { get; set; } = ["Controller", "Service", "Repository", "Manager", "Handler"];

    // ==========================================================
    // 🧩 Method Naming Rules
    // ==========================================================

    /// <summary>
    /// Require camelCase for method names.
    /// </summary>
    public bool EnforceCamelCaseForMethods { get; set; } = true;

    /// <summary>
    /// Allowed method prefixes (e.g., 'Get', 'Set', 'Load', 'Create', 'Update', 'Delete').
    /// </summary>
    public string[] AllowedMethodPrefixes { get; set; } = ["Get", "Set", "Load", "Create", "Update", "Delete", "Add", "Remove", "Find"];

    /// <summary>
    /// If true, static methods must start with an uppercase letter (PascalCase).
    /// </summary>
    public bool StaticMethodsPascalCase { get; set; } = true;

    // ==========================================================
    // 🧮 Variable Naming Rules
    // ==========================================================

    /// <summary>
    /// Allow variables using snake_case (useful for Python projects).
    /// </summary>
    public bool AllowSnakeCase { get; set; } = false;

    /// <summary>
    /// Allow variables or constants in ALL_CAPS (for constants only).
    /// </summary>
    public bool AllowAllCapsConstants { get; set; } = true;

    /// <summary>
    /// Require camelCase for local variables and fields.
    /// </summary>
    public bool EnforceCamelCaseForVariables { get; set; } = true;

    /// <summary>
    /// Require PascalCase for public fields or properties.
    /// </summary>
    public bool EnforcePascalCaseForProperties { get; set; } = true;

    // ==========================================================
    // 📂 File & Namespace Naming Rules
    // ==========================================================

    /// <summary>
    /// If true, file names should match their main class or interface name.
    /// </summary>
    public bool EnforceFileNameMatch { get; set; } = true;

    /// <summary>
    /// If true, enforces pluralization rules for services/controllers.
    /// </summary>
    public bool EnforcePluralization { get; set; } = true;

    /// <summary>
    /// If true, enforces that namespaces or package names align with directory structure.
    /// </summary>
    public bool EnforceNamespaceStructure { get; set; } = true;

    /// <summary>
    /// Allow lowercase namespaces (Python/JavaScript), otherwise enforce PascalCase (C#).
    /// </summary>
    public bool AllowLowercaseNamespaces { get; set; } = true;

    // ==========================================================
    // 📊 Compliance Thresholds
    // ==========================================================

    /// <summary>
    /// Minimum acceptable overall naming compliance percentage before triggering warnings.
    /// </summary>
    public double MinOverallCompliance { get; set; } = 85;

    /// <summary>
    /// Minimum acceptable compliance for each individual dimension (class, method, variable).
    /// </summary>
    public double MinCategoryCompliance { get; set; } = 90;

    // ==========================================================
    // 🧠 Advanced Linguistic Rules
    // ==========================================================

    /// <summary>
    /// Detect acronyms (e.g., HTTP, XML) that break casing standards.
    /// </summary>
    public bool DetectAcronymViolations { get; set; } = true;

    /// <summary>
    /// Normalize acronyms to PascalCase style (e.g., HttpClient instead of HTTPClient).
    /// </summary>
    public bool EnforceAcronymNormalization { get; set; } = true;

    /// <summary>
    /// Optional: block reserved prefixes (e.g., “Tmp”, “Test”, “Foo”, “Bar”) in production code.
    /// </summary>
    public bool DisallowTestOrTempPrefixes { get; set; } = true;

    /// <summary>
    /// List of forbidden prefixes that indicate placeholder or temporary names.
    /// </summary>
    public string[] ForbiddenPrefixes { get; set; } = ["Tmp", "Temp", "Foo", "Bar", "Test", "Dummy"];

    /// <summary>
    /// If true, detects ambiguous names like “ManagerManager” or “ServiceService”.
    /// </summary>
    public bool DetectRedundantSuffixes { get; set; } = true;
}
