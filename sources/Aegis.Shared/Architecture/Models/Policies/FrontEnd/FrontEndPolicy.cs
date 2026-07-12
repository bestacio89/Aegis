namespace Aegis.Shared.Architecture.Models.Policies.FrontEnd;

/// <summary>
/// 🎨 Global frontend architecture governance policy.
/// Controls SPA, server-rendered, and component-based UI frameworks.
///
/// Supported:
/// - Angular
/// - React
/// - Razor MVC / Razor Pages
/// - Blazor
/// </summary>
public sealed class FrontendPolicy
{
    public bool Enabled { get; set; } = true;


    // ==========================================================
    // 🅰️ Angular
    // ==========================================================

    public bool EnforceSelectorNaming { get; set; } = true;

    public string[] AllowedSelectorPrefixes { get; set; } =
    [
        "app",
        "lib",
        "shared"
    ];

    public bool EnforceComponentPascalCase { get; set; } = true;

    public int MaxComponentsPerModule { get; set; } = 20;



    // ==========================================================
    // ⚛️ React
    // ==========================================================

    public bool CheckHooksRules { get; set; } = true;

    public int MaxHooksPerComponent { get; set; } = 10;

    public bool RequireReactPropTyping { get; set; } = true;

    public bool AllowClassComponents { get; set; } = false;



    // ==========================================================
    // 🟦 Razor MVC / Razor Pages
    // ==========================================================

    public bool EnforceRazorViewModels { get; set; } = true;

    public bool PreventEntityExposure { get; set; } = true;

    public bool DetectRazorBusinessLogic { get; set; } = true;

    public bool PreventDatabaseAccessInViews { get; set; } = true;

    public int MaxRazorViewComplexity { get; set; } = 50;

    public int MaxInlineCodeLines { get; set; } = 15;



    // ==========================================================
    // 🟣 Blazor
    // ==========================================================

    public bool EnforceBlazorCodeBehind { get; set; } = true;

    public bool RequireBlazorDependencyInjection { get; set; } = true;

    public bool DetectBlazorLifecycleMisuse { get; set; } = true;

    public int MaxBlazorComponentComplexity { get; set; } = 50;

    public int MaxBlazorCodeBlockLines { get; set; } = 30;

    public int MaxEventCallbacksPerComponent { get; set; } = 15;



    // ==========================================================
    // 🧩 Cross Framework Rules
    // ==========================================================

    public int MaxComponentComplexity { get; set; } = 50;

    public bool EnforceComponentSeparation { get; set; } = true;

    public bool EnforceDependencyInjection { get; set; } = true;

    public bool DetectBusinessLogicInUI { get; set; } = true;
}