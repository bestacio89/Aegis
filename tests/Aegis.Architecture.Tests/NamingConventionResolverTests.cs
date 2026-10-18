using Aegis.Architecture.Evaluators.Naming.Policies;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class NamingConventionResolverTests
{
    private readonly NamingConventionResolver _resolver = new();

    [Theory]
    [InlineData("TypeScript", "Angular", typeof(AngularNamingConventionPolicy))]
    [InlineData("TypeScript", "React", typeof(ReactNamingConventionPolicy))]
    [InlineData("C#", "Blazor", typeof(BlazorNamingConventionPolicy))]
    [InlineData("C#", "Razor", typeof(RazorNamingConventionPolicy))]
    [InlineData("C#", null, typeof(CSharpNamingConventionPolicy))]
    [InlineData("Java", null, typeof(JavaNamingConventionPolicy))]
    [InlineData("Python", null, typeof(PythonNamingConventionPolicy))]
    [InlineData("Rust", null, typeof(DefaultNamingConventionPolicy))]
    [InlineData(null, null, typeof(DefaultNamingConventionPolicy))]
    public void Resolve_selects_the_policy_for_the_detected_language_and_framework(
        string? language,
        string? framework,
        Type expectedPolicyType)
    {
        var context = new ProjectArchitectureContext
        {
            Language = language!,
            Framework = framework
        };

        var policy = _resolver.Resolve(context);

        Assert.Equal(expectedPolicyType, policy.GetType());
    }

    [Fact]
    public void Csharp_policy_accepts_pascal_case_types_and_rejects_snake_case()
    {
        var policy = new CSharpNamingConventionPolicy();

        Assert.True(policy.IsValidTypeName("OrderService"));
        Assert.False(policy.IsValidTypeName("order_service"));
        Assert.True(policy.IsValidInterfaceName("IOrderService"));
        Assert.False(policy.IsValidInterfaceName("OrderService"));
        Assert.True(policy.IsValidMethodName("CalculateTotal"));
        Assert.True(policy.IsValidVariableName("orderTotal"));
        Assert.True(policy.IsValidConstantName("MAX_RETRIES"));
        Assert.True(policy.IsValidFileName("OrderService"));
        Assert.False(policy.IsValidFileName("order_service"));
    }

    [Fact]
    public void Java_policy_distinguishes_type_and_method_casing()
    {
        var policy = new JavaNamingConventionPolicy();

        Assert.True(policy.IsValidTypeName("OrderService"));
        Assert.True(policy.IsValidMethodName("calculateTotal"));
        Assert.False(policy.IsValidMethodName("CalculateTotal"));
        Assert.True(policy.IsValidConstantName("MAX_RETRIES"));
    }

    [Fact]
    public void Python_policy_accepts_snake_case_functions_variables_and_files()
    {
        var policy = new PythonNamingConventionPolicy();

        Assert.True(policy.IsValidTypeName("OrderService"));
        Assert.True(policy.IsValidMethodName("calculate_total"));
        Assert.True(policy.IsValidVariableName("_order_total"));
        Assert.True(policy.IsValidConstantName("MAX_RETRIES"));
        Assert.True(policy.IsValidFileName("order_service"));
        Assert.False(policy.IsValidFileName("OrderService"));
    }
}
