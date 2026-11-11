using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class ArchitectureDetector
{
    public static void Analyze(ProjectArchitectureContext ctx)
    {
        var deps = ctx.DetectedDependencies;

        bool hasMediator = deps.Any(d => d.Contains("MediatR") || d.Contains("Franz.Common.Mediator"));
        bool hasEfCore = deps.Any(d => d.Contains("EntityFrameworkCore"));
        bool hasMassTr = deps.Any(d => d.Contains("MassTransit"));
        bool hasKafka = deps.Any(d => d.Contains("Confluent.Kafka"));
        bool hasRabbit = deps.Any(d => d.Contains("RabbitMQ.Client"));
        bool hasNServiceBus = deps.Any(d => d.Contains("NServiceBus"));
        bool hasAzureSB = deps.Any(d => d.Contains("Azure.Messaging.ServiceBus"));

        // A repository containing class libraries and integration packages is
        // not thereby a microservices system. Establish the product/project
        // shape before interpreting infrastructure dependencies.
        if (ctx.DomainType == "Library")
        {
            ctx.ArchitectureStyle = ctx.IsMultiModule
                ? "Multi-Project Library"
                : "Library";
            return;
        }

        if (ctx.DomainType == "Mixed")
        {
            ctx.ArchitectureStyle = "Multi-Project / Mixed";
            return;
        }

        var serviceProjectCount = ctx.Modules.Count(module =>
            module.ProjectKind == "WebApplication"
            || module.ProjectKind == "Executable");

        if (hasMediator && hasEfCore) ctx.ArchitectureStyle = "CQRS";
        else if (hasEfCore && ctx.Layer == "Infrastructure") ctx.ArchitectureStyle = "Clean";
        else if (ctx.DomainType == "FullStack" && ctx.Modules.Count > 1)
            ctx.ArchitectureStyle = "Multi-Project Full-Stack";
        else if (serviceProjectCount == 1 && ctx.Modules.Count > 1)
            ctx.ArchitectureStyle = "Modular Monolith";
        else if ((hasMassTr || hasKafka || hasRabbit || hasAzureSB || hasNServiceBus)
            && serviceProjectCount > 1)
            ctx.ArchitectureStyle = "Message-Driven Multi-Service";
        else if (hasMassTr || hasKafka || hasRabbit || hasAzureSB || hasNServiceBus)
            ctx.ArchitectureStyle = "Message-Driven Application";
        else if ((ctx.DomainType is "Backend" or "FullStack") && ctx.Layer == "Api") ctx.ArchitectureStyle = "MVC";
        else ctx.ArchitectureStyle = "Layered";
    }
}
