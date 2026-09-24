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
using Opc.Ua.Bindings;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Sizes prospective reassembly floors and validates SecureChannel headroom without changing a server.
    /// </summary>
    /// <remarks>
    /// This staged, pure calculator is not consumed by server hosting or transport admission.
    /// A successful plan is not an enforced isolation policy. Other protected stages, trusted-owner
    /// provisioning, hard owner ceilings and weighted scheduling are deliberately not configured here.
    /// </remarks>
    public sealed class ServerResourceIsolationOptions
    {
        /// <summary>
        /// The planning profile, independent of rate-limiter options. Does not select a running server policy.
        /// </summary>
        public ServerResourceIsolationMode Mode { get; set; } = ServerResourceIsolationMode.Balanced;

        /// <summary>
        /// The proposed non-borrowable bootstrap floor in bytes, or null for one maximum retained message.
        /// SharedOnly and FairShare accept only null or zero.
        /// </summary>
        public long? BootstrapReservedBytes { get; set; }

        /// <summary>
        /// The proposed non-borrowable reconnect floor in bytes, or null for one maximum retained message.
        /// SharedOnly and FairShare accept only null or zero.
        /// </summary>
        public long? ReconnectReservedBytes { get; set; }

        /// <summary>
        /// Creates an immutable capacity plan using explicit, finite deployment bounds.
        /// Does not reserve memory, mutate configuration or install enforcement.
        /// </summary>
        /// <param name="budget">The existing reassembly total and sessionless occupancy threshold.</param>
        /// <param name="maxSessionCount">The positive configured maximum number of Sessions.</param>
        /// <param name="maxChannelCount">The configured SecureChannel maximum, at least Sessions plus one.</param>
        /// <param name="maxRetainedChunkCount">
        /// A positive upper bound on intermediate chunks retained for any permitted message across all
        /// relevant negotiations/listeners. Include even an unfinished message at its chunk-count limit.
        /// No finite bound is inferred from payload bytes alone.
        /// </param>
        /// <param name="maxPooledBufferLength">
        /// A positive upper bound on the actual backing-array length of every retained chunk, including
        /// pool rounding and metadata. This is not the wire chunk size or a typical observed rental.
        /// The caller must establish this bound for its buffer manager and pool.
        /// </param>
        /// <exception cref="ArgumentNullException">The budget is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A bound or profile value is invalid.</exception>
        /// <exception cref="ArgumentException">
        /// The totals cannot support the requested floors and message footprint, or the profile
        /// does not permit the supplied overrides.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// TrustedReservations requires a provisioning and classification model not yet implemented.
        /// </exception>
        public ServerResourceIsolationPlan CreatePlan(
            ChunkReassemblyBudget budget,
            int maxSessionCount,
            int maxChannelCount,
            int maxRetainedChunkCount,
            int maxPooledBufferLength)
        {
            if (budget == null)
            {
                throw new ArgumentNullException(nameof(budget));
            }
            ServerResourceIsolationMode mode = Mode;
            ValidateMode(mode);
            if (maxSessionCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSessionCount), "A finite positive limit is required.");
            }
            if (maxChannelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxChannelCount), "A finite positive limit is required.");
            }
            if (maxChannelCount < (long)maxSessionCount + 1)
            {
                throw new ArgumentException(
                    "SecureChannel capacity must support the configured Session count plus one.",
                    nameof(maxChannelCount));
            }
            if (maxRetainedChunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRetainedChunkCount), "A finite positive retained-chunk bound is required.");
            }
            if (maxPooledBufferLength <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPooledBufferLength), "A finite positive backing-array length bound is required.");
            }

            long messageBytes = (long)maxRetainedChunkCount * maxPooledBufferLength;
            if (messageBytes > budget.MaxBytes)
            {
                throw new ArgumentException(
                    "The existing reassembly total cannot hold one maximum retained message.", nameof(budget));
            }

            long bootstrapBytes = ResolveFloor(
                mode, BootstrapReservedBytes, messageBytes, nameof(BootstrapReservedBytes));
            long reconnectBytes = ResolveFloor(
                mode, ReconnectReservedBytes, messageBytes, nameof(ReconnectReservedBytes));
            if (bootstrapBytes > budget.MaxBytes || reconnectBytes > budget.MaxBytes - bootstrapBytes)
            {
                throw new ArgumentException(
                    "The reserved floors exceed the existing reassembly total.", nameof(budget));
            }
            if (bootstrapBytes > budget.MaxBytesWithoutSession)
            {
                throw new ArgumentException(
                    "The bootstrap floor exceeds the existing sessionless occupancy threshold.", nameof(budget));
            }
            if (budget.MaxBytes - bootstrapBytes - reconnectBytes < messageBytes)
            {
                throw new ArgumentException(
                    "The unreserved capacity must still hold one maximum retained message.", nameof(budget));
            }

            return new ServerResourceIsolationPlan(
                mode,
                budget.MaxBytes,
                budget.MaxBytesWithoutSession,
                maxSessionCount,
                maxChannelCount,
                messageBytes,
                bootstrapBytes,
                reconnectBytes);
        }

        private static void ValidateMode(ServerResourceIsolationMode mode)
        {
            if (mode is < ServerResourceIsolationMode.SharedOnly or > ServerResourceIsolationMode.TrustedReservations)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (mode == ServerResourceIsolationMode.TrustedReservations)
            {
                throw new NotSupportedException(
                    "Trusted reservation planning requires explicit owner provisioning and validated classification.");
            }
        }

        private static long ResolveFloor(
            ServerResourceIsolationMode mode,
            long? requestedBytes,
            long messageBytes,
            string parameterName)
        {
            if (requestedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "A reserved floor cannot be negative.");
            }
            if (mode != ServerResourceIsolationMode.Balanced)
            {
                if (requestedBytes.GetValueOrDefault() != 0)
                {
                    throw new ArgumentException("This profile does not have reserved floors.", parameterName);
                }
                return 0;
            }

            long bytes = requestedBytes ?? messageBytes;
            if (bytes < messageBytes)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, "Each protected floor must hold at least one maximum retained message.");
            }
            return bytes;
        }
    }
}
