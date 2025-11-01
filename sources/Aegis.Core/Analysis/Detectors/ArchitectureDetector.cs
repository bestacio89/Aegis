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

        if (hasMediator && hasEfCore) ctx.ArchitectureStyle = "CQRS";
        else if (hasEfCore && ctx.Layer == "Infrastructure") ctx.ArchitectureStyle = "Clean";
        else if (hasMassTr || hasKafka || hasRabbit || hasAzureSB || hasNServiceBus) ctx.ArchitectureStyle = "Hexagonal / Microservices";
        else if (ctx.IsMultiModule && ctx.Nature == "Service") ctx.ArchitectureStyle = "Modular Monolith";
        else if (ctx.DomainType == "Backend" && ctx.Layer == "Api") ctx.ArchitectureStyle = "MVC";
        else ctx.ArchitectureStyle = "Layered";
    }
}
