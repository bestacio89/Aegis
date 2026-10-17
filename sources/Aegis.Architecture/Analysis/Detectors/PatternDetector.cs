using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class PatternDetector
{
    public static void Analyze(ProjectArchitectureContext ctx)
    {
        ctx.PatternsDetected ??= new List<string>();

        DetectFrameworkPatterns(ctx);
        DetectDependencyPatterns(ctx);
        DetectStructuralPatterns(ctx);
    }


    private static void DetectFrameworkPatterns(
        ProjectArchitectureContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Framework))
            return;


        var framework = ctx.Framework.ToLowerInvariant();


        if (framework.Contains("angular"))
        {
            Add(ctx, "Angular Modular Architecture");
        }


        if (framework.Contains("spring"))
        {
            Add(ctx, "Spring Layered Architecture");
        }


        if (framework.Contains("django"))
        {
            Add(ctx, "Django MTV Architecture");
        }


        if (framework.Contains("asp.net"))
        {
            Add(ctx, "ASP.NET Web Architecture");
        }
    }


    private static void DetectDependencyPatterns(
        ProjectArchitectureContext ctx)
    {
        var dependencies = ctx.DetectedDependencies;


        // CQRS
        if (dependencies.Any(d =>
            d.Contains("MediatR",
                StringComparison.OrdinalIgnoreCase)
            ||
            d.Contains("Mediator",
                StringComparison.OrdinalIgnoreCase)))
        {
            Add(ctx, "CQRS");
        }


        // Persistence abstraction
        if (dependencies.Any(d =>
            d.Contains("EntityFramework",
                StringComparison.OrdinalIgnoreCase)))
        {
            Add(ctx, "Repository Pattern");
        }


        // Messaging / distributed architecture
        if (dependencies.Any(d =>
            d.Contains("Kafka",
                StringComparison.OrdinalIgnoreCase)
            ||
            d.Contains("Rabbit",
                StringComparison.OrdinalIgnoreCase)
            ||
            d.Contains("MassTransit",
                StringComparison.OrdinalIgnoreCase)))
        {
            Add(ctx, "Event Driven Architecture");
        }


        // DI heavy applications
        if (dependencies.Any(d =>
            d.Contains("DependencyInjection",
                StringComparison.OrdinalIgnoreCase)))
        {
            Add(ctx, "Dependency Injection");
        }
    }


    private static void DetectStructuralPatterns(
        ProjectArchitectureContext ctx)
    {
        if (ctx.IsMultiModule)
        {
            Add(ctx, "Modular Architecture");
        }


        if (ctx.Layers.Any(l =>
            l.Name.Equals("Domain",
                StringComparison.OrdinalIgnoreCase)))
        {
            Add(ctx, "Domain Driven Design");
        }
    }


    private static void Add(
        ProjectArchitectureContext ctx,
        string pattern)
    {
        if (!ctx.PatternsDetected.Contains(pattern))
        {
            ctx.PatternsDetected.Add(pattern);
        }
    }
}