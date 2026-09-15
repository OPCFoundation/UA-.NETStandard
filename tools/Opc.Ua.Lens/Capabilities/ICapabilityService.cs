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
