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

namespace Opc.Ua.Server
{
    /// <summary>
    /// An immutable capacity snapshot consumed by the runtime isolation provider.
    /// </summary>
    /// <remarks>
    /// CreateRuntimePlan resolves every stage. CreatePlan retains the standalone reassembly sizing API.
    /// Floors are separate, non-borrowable partitions inside the original totals.
    /// </remarks>
    public sealed class ServerResourceIsolationPlan
    {
        internal ServerResourceIsolationPlan(
            ServerResourceIsolationMode mode,
            long maxReassemblyBytes,
            long maxBytesWithoutSession,
            int maxSessionCount,
            int maxChannelCount,
            long maxRetainedMessageBytes,
            long bootstrapReservedBytes,
            long reconnectReservedBytes,
            long trustedReservedBytes = 0,
            ResourceIsolationStagePlan[]? stages = null,
            Dictionary<string, TrustedOwnerPlan>? trustedOwners = null,
            int maxTrackedOwners = 4096,
            int maxOwnerKeyLength = 256,
            int defaultWeight = 1)
        {
            Mode = mode;
            MaxReassemblyBytes = maxReassemblyBytes;
            MaxBytesWithoutSession = maxBytesWithoutSession;
            MaxSessionCount = maxSessionCount;
            MaxChannelCount = maxChannelCount;
            MaxRetainedMessageBytes = maxRetainedMessageBytes;
            BootstrapReservedBytes = bootstrapReservedBytes;
            ReconnectReservedBytes = reconnectReservedBytes;
            TrustedReservedBytes = trustedReservedBytes;
            m_stages = stages;
            TrustedOwners = trustedOwners ?? new Dictionary<string, TrustedOwnerPlan>(StringComparer.Ordinal);
            MaxTrackedOwners = maxTrackedOwners;
            MaxOwnerKeyLength = maxOwnerKeyLength;
            DefaultWeight = defaultWeight;
        }

        /// <summary>
        /// The requested planning profile, not the server's active policy.
        /// </summary>
        public ServerResourceIsolationMode Mode { get; }

        /// <summary>
        /// The unchanged total backing-array byte capacity for incomplete messages.
        /// </summary>
        public long MaxReassemblyBytes { get; }

        /// <summary>
        /// The unchanged legacy sessionless occupancy threshold, not a reserved partition.
        /// </summary>
        public long MaxBytesWithoutSession { get; }

        /// <summary>
        /// The unchanged configured Session maximum.
        /// </summary>
        public int MaxSessionCount { get; }

        /// <summary>
        /// The unchanged configured SecureChannel maximum.
        /// </summary>
        public int MaxChannelCount { get; }

        /// <summary>
        /// SecureChannels above the configured Session maximum. This is headroom, not an enforced reserve.
        /// </summary>
        public int SecureChannelHeadroom => MaxChannelCount - MaxSessionCount;

        /// <summary>
        /// The supplied retained-chunk bound multiplied by the supplied maximum backing-array length.
        /// </summary>
        public long MaxRetainedMessageBytes { get; }

        /// <summary>
        /// The prospective non-borrowable bootstrap byte floor.
        /// </summary>
        public long BootstrapReservedBytes { get; }

        /// <summary>
        /// The prospective non-borrowable reconnect byte floor.
        /// </summary>
        public long ReconnectReservedBytes { get; }

        /// <summary>
        /// The sum of the two separate proposed floors.
        /// </summary>
        public long ReservedReassemblyBytes => BootstrapReservedBytes + ReconnectReservedBytes + TrustedReservedBytes;

        /// <summary>
        /// The remaining shared bytes inside the original total.
        /// </summary>
        public long UnreservedReassemblyBytes =>
            m_stages?[(int)ResourceIsolationStage.ReassemblyBytes].SharedCapacity ??
            MaxReassemblyBytes - ReservedReassemblyBytes;

        /// <summary>
        /// Individually provisioned, non-borrowable trusted-owner bytes.
        /// </summary>
        public long TrustedReservedBytes { get; }

        /// <summary>
        /// Bounded active accounting table size.
        /// </summary>
        public int MaxTrackedOwners { get; }

        /// <summary>
        /// Maximum classifier key length.
        /// </summary>
        public int MaxOwnerKeyLength { get; }

        /// <summary>
        /// Default relative scheduling weight.
        /// </summary>
        public int DefaultWeight { get; }

        internal Dictionary<string, TrustedOwnerPlan> TrustedOwners { get; }

        /// <summary>
        /// Returns an immutable stage plan. Standalone reassembly-only plans cannot run a server.
        /// </summary>
        public ResourceIsolationStagePlan GetStage(ResourceIsolationStage stage)
        {
            if (stage is < ResourceIsolationStage.Connection or > ResourceIsolationStage.ParkedRequest)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }
            if (m_stages == null)
            {
                throw new InvalidOperationException("CreateRuntimePlan is required for runtime admission.");
            }
            return m_stages[(int)stage];
        }

        private readonly ResourceIsolationStagePlan[]? m_stages;
    }

    internal sealed record TrustedOwnerPlan(int Weight, long[] Floors, long[] HardLimits);
}
