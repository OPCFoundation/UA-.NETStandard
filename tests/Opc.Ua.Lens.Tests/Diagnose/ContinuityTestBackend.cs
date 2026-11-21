// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Diagnostics;
using UaLens.Plugins.Continuity;

namespace UaLens.Tests.Diagnose;

internal sealed class ContinuityTestBackend : IContinuityBackend, IContinuityBackendFactory
{
    public ContinuitySetup Setup { get; set; } = new(ContinuityAvailability.Supported, "Test setup.");

    public Func<CancellationToken, Task> Start { get; set; } = _ => Task.CompletedTask;

    public Func<CancellationToken, Task<ContinuityStepResult>> Step { get; set; } =
        _ => Task.FromResult(new ContinuityStepResult(false, "Owned step completed."));

    public Func<CancellationToken, Task> Stop { get; set; } = _ => Task.CompletedTask;

    public int Starts { get; private set; }

    public int Steps { get; private set; }

    public int Stops { get; private set; }

    public int Disposals { get; private set; }

    public IContinuityBackend Create(ContinuityTimeline timeline) => this;

    public ContinuitySetup CheckSetup(ContinuityConfiguration configuration) => Setup;

    public Task StartAsync(ContinuityConfiguration configuration, CancellationToken ct)
    {
        Starts++;
        return Start(ct);
    }

    public Task<ContinuityStepResult> StepAsync(CancellationToken ct)
    {
        Steps++;
        return Step(ct);
    }

    public Task StopAsync(CancellationToken ct)
    {
        Stops++;
        return Stop(ct);
    }

    public ArrayOf<DiagnosticMetric> CaptureDiagnostics() => default;

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return ValueTask.CompletedTask;
    }
}
