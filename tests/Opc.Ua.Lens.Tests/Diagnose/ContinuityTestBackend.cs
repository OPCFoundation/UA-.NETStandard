/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
