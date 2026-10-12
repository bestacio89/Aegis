using Aegis.Shared.Security.Models;
using Aegis.Wpf.Models.Security;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Wpf.Services.Security
{
    public sealed record SecurityScanOutcome(
      bool Success,
      AegisSecurityReport? Report,
      SecurityComplianceResult? Compliance,
      SecurityScanResult? View,
      string? Error);
}
