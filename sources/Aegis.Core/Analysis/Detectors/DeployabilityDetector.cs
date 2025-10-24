using Aegis.Shared.Architecture.Models;

namespace Aegis.Core.Analysis.Detectors;

internal static class DeployabilityDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        bool docker = files.Any(f => Path.GetFileName(f).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase));
        bool compose = files.Any(f => f.Contains("docker-compose", StringComparison.OrdinalIgnoreCase));
        bool k8s = files.Any(f => f.Contains("deployment.yaml") || f.Contains("ingress.yaml") || f.Contains("service.yaml"));

        ctx.MetadataMap["Dockerized"] = docker || compose ? "true" : "false";
        ctx.MetadataMap["Kubernetes"] = k8s ? "true" : "false";

        if (docker || k8s)
            ctx.Nature ??= "Service";
    }
}
