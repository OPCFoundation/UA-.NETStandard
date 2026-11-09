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

namespace Opc.Ua.CranesHoists
{
    /// <summary>
    /// Supplies a coherent, bounded acquisition for one crane system.
    /// </summary>
    /// <remarks>
    /// Supply every configured telemetry signal, retaining its acquisition timestamp and quality.
    /// Explicit bad/null samples represent unavailable data; missing signals are configuration errors.
    /// Requests and static capabilities are not telemetry and must not be returned.
    /// </remarks>
    public interface ICraneDataProvider
    {
        /// <summary>
        /// Acquires the configured signals without fabricating measurements or executing motion.
        /// </summary>
        ValueTask<ArrayOf<CraneSample>> ReadAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Accepts or rejects standard speed-limit configuration requests for one crane system.
    /// </summary>
    /// <remarks>
    /// Good means the request was accepted, not that a physical speed limit became active.
    /// Active limits and external-control state are reported separately by the data provider.
    /// Implementations must honor cancellation and must never implement motion or safety-bypass commands here.
    /// </remarks>
    public interface ICraneLimitProvider
    {
        /// <summary>
        /// Returns the explicit acceptance status of this standard property write.
        /// </summary>
        ValueTask<StatusCode> RequestAsync(
            CraneLimitRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Authorizes individual limit writes using the actual OPC UA caller context.
    /// </summary>
    public interface ICraneWriteAuthorizer
    {
        /// <summary>
        /// Returns true only when the caller is allowed to request this configuration change.
        /// </summary>
        ValueTask<bool> AuthorizeAsync(
            ISystemContext caller,
            CraneLimitRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Carries one acquired value without discarding its quality or source timestamp.
    /// </summary>
    public sealed class CraneSample
    {
        /// <summary>
        /// Creates an acquisition for a system-local signal.
        /// </summary>
        public CraneSample(CraneSignalAddress address, in DataValue value)
        {
            if (string.IsNullOrWhiteSpace(address.ComponentId))
            {
                throw new ArgumentException("A component identifier is required.", nameof(address));
            }
            Address = address;
            Value = value;
        }

        /// <summary>
        /// Gets the signal identity.
        /// </summary>
        public CraneSignalAddress Address { get; }

        /// <summary>
        /// Gets the original acquisition.
        /// </summary>
        public DataValue Value { get; }
    }

    /// <summary>
    /// Describes a standard limit-property write; it is not a new OPC UA method or motion protocol.
    /// </summary>
    /// <param name="SystemId">The unique system-local root identifier.</param>
    /// <param name="Address">The target component and standard request signal.</param>
    /// <param name="Value">The exact Boolean or Double value requested by the client.</param>
    public sealed record CraneLimitRequest(string SystemId, CraneSignalAddress Address, Variant Value);
}
