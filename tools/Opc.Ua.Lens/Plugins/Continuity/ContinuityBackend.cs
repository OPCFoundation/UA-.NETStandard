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
