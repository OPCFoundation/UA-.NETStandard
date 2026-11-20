/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace UaLens.Capabilities;

/// <summary>
/// Stack/test seam for a bounded, read-only check. Implementations must honor cancellation
/// and release server resources.
/// </summary>
internal interface ICapabilityProbe
{
    Task<CapabilityResult> ProbeAsync(
        ISession session,
        CapabilityRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Lazy primary-session evidence. Independent network runtimes own their own configuration and availability.
/// </summary>
internal interface ICapabilityService : IAsyncDisposable
{
    /// <summary>
    /// Reads current evidence without network I/O. Unknown means that a check or a retry is needed.
    /// </summary>
    CapabilityResult GetCached(CapabilityRequest request);

    /// <summary>
    /// Checks on demand with bounded concurrency, duration and caching. Caller cancellation is propagated.
    /// </summary>
    Task<CapabilityResult> ProbeAsync(CapabilityRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates metadata after a model-change notification or explicit refresh, including in-flight results.
    /// Does not attach or release session resources.
    /// </summary>
    void Invalidate();
}
