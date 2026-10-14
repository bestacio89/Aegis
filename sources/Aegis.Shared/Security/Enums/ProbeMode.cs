namespace Aegis.Shared.Security.Enums;

/// <summary>
/// How a probe or evaluator obtains its evidence.
/// Static = reads what a project declares (files, configuration), deterministic and safe to repeat.
/// Live   = observes a running system or environment (handshakes, ports, host state, cloud APIs);
///          results depend on when it runs and it requires an authorized target.
/// </summary>
public enum ProbeMode
{
    Static = 0,
    Live = 1
}