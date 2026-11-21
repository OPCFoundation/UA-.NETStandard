// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Diagnostics;

namespace UaLens.Plugins.Continuity;

internal enum ContinuityAvailability
{
    Supported,
    RequiresConfiguration,
    Unsupported,
    Denied,
    Unknown
}

internal sealed record ContinuitySetup(ContinuityAvailability Availability, string Description)
{
    public bool CanStart => Availability == ContinuityAvailability.Supported;
}

internal sealed record ContinuityStepResult(bool WaitingForRestore, string Description);

/// <summary>
/// Owns only scenario resources. Implementations must observe cancellation, finish
/// release before Stop returns, and never replace or dispose the workspace session.
/// </summary>
internal interface IContinuityBackend : IAsyncDisposable
{
    ContinuitySetup CheckSetup(ContinuityConfiguration configuration);

    Task StartAsync(ContinuityConfiguration configuration, CancellationToken ct);

    Task<ContinuityStepResult> StepAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    ArrayOf<DiagnosticMetric> CaptureDiagnostics();
}

internal interface IContinuityBackendFactory
{
    IContinuityBackend Create(ContinuityTimeline timeline);
}
