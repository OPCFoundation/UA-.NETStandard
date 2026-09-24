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
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Opc.Ua
{
    /// <summary>
    /// Server-wide resource pools. Byte accounting charges actual retained backing arrays.
    /// </summary>
    public enum ResourceIsolationStage
    {
        /// <summary>
        /// Admitted transport connections.
        /// </summary>
        Connection,

        /// <summary>
        /// Pending protocol handshakes.
        /// </summary>
        Handshake,

        /// <summary>
        /// Retained, incomplete message bytes.
        /// </summary>
        ReassemblyBytes,

        /// <summary>
        /// Concurrent session creation or activation.
        /// </summary>
        SessionEstablishment,

        /// <summary>
        /// Decoded requests waiting for execution.
        /// </summary>
        RequestQueue,

        /// <summary>
        /// Requests currently executing, including single-chunk requests.
        /// </summary>
        RequestExecution,

        /// <summary>
        /// Conservative decoded-request admission cost, not a managed-heap measurement.
        /// </summary>
        RequestQueueBytes,

        /// <summary>
        /// Long-lived requests parked without an execution worker.
        /// </summary>
        ParkedRequest
    }

    /// <summary>
    /// Policy classification, not an authorization decision.
    /// </summary>
    public enum ResourceIsolationClass
    {
        /// <summary>
        /// Explicitly classified, protected startup traffic.
        /// </summary>
        Bootstrap,

        /// <summary>
        /// Ordinary shared traffic; includes unknown anonymous callers.
        /// </summary>
        Established,

        /// <summary>
        /// Verified session continuity or explicitly classified reconnect ingress.
        /// </summary>
        Reconnect,

        /// <summary>
        /// An explicitly provisioned and verified owner.
        /// </summary>
        Trusted,

        /// <summary>
        /// Recovery/control work backed by a verified session or explicit trusted mapping.
        /// </summary>
        Control
    }

    /// <summary>
    /// Bounded admission rejection categories, suitable for metric labels.
    /// </summary>
    public enum ResourceIsolationFailureReason
    {
        /// <summary>
        /// Admission succeeded.
        /// </summary>
        None,

        /// <summary>
        /// The shared or protected pool is full.
        /// </summary>
        Capacity,

        /// <summary>
        /// The owner's configured hard ceiling is reached.
        /// </summary>
        OwnerLimit,

        /// <summary>
        /// The bounded active owner table is full.
        /// </summary>
        OwnerTableFull,

        /// <summary>
        /// The classification was not issued by this provider.
        /// </summary>
        InvalidOwner
    }

    /// <summary>
    /// An admission result without sensitive or unbounded diagnostic data.
    /// </summary>
    public readonly record struct ResourceIsolationFailure(
        ResourceIsolationFailureReason Reason,
        TimeSpan RetryAfter);

    /// <summary>
    /// Immutable scheduling and accounting classification issued by a provider.
    /// </summary>
    /// <remarks>
    /// Keys are sensitive: do not expose them in metric labels or logs.
    /// A snapshot is not authorization; reclassify decoded requests after queueing.
    /// </remarks>
    public sealed class ResourceIsolationOwner
    {
        /// <summary>
        /// Creates a classification for a provider implementation.
        /// Consumers must obtain classifications from their configured provider.
        /// </summary>
        public ResourceIsolationOwner(
            string key,
            ResourceIsolationClass ownerClass,
            int weight,
            ArrayOf<long> hardLimits)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("An owner key is required.", nameof(key));
            }
            if (ownerClass is < ResourceIsolationClass.Bootstrap or > ResourceIsolationClass.Control)
            {
                throw new ArgumentOutOfRangeException(nameof(ownerClass));
            }
            if (weight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight));
            }
            if (hardLimits.Count != (int)ResourceIsolationStage.ParkedRequest + 1)
            {
                throw new ArgumentException("A hard limit is required for every stage.", nameof(hardLimits));
            }
            m_hardLimits = new long[hardLimits.Count];
            for (int ii = 0; ii < m_hardLimits.Length; ii++)
            {
                if (hardLimits[ii] <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(hardLimits));
                }
                m_hardLimits[ii] = hardLimits[ii];
            }
            Key = key;
            Class = ownerClass;
            Weight = weight;
        }

        /// <summary>
        /// Stable, provider-resolved accounting key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Validated policy class.
        /// </summary>
        public ResourceIsolationClass Class { get; }

        /// <summary>
        /// Configured relative scheduling weight, never supplied by the request.
        /// </summary>
        public int Weight { get; }

        /// <summary>
        /// Gets the provider-resolved hard ceiling for a stage.
        /// </summary>
        public long GetHardLimit(ResourceIsolationStage stage)
        {
            if (stage is < ResourceIsolationStage.Connection or > ResourceIsolationStage.ParkedRequest)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }
            return m_hardLimits[(int)stage];
        }

        private readonly long[] m_hardLimits;
    }

    /// <summary>
    /// Server-scoped, fail-fast resource admission shared by all listeners.
    /// </summary>
    public interface IServerResourceIsolationProvider
    {
        /// <summary>
        /// Whether decoded requests use weighted scheduling instead of compatibility FIFO.
        /// </summary>
        bool UseFairScheduling { get; }

        /// <summary>
        /// Signals released capacity outside provider accounting locks.
        /// Handlers must not block or throw; scheduling should be queued asynchronously.
        /// </summary>
        event Action? CapacityAvailable;

        /// <summary>
        /// Classifies an observed transport endpoint. Protected pre-authentication classes
        /// require an explicitly configured trusted ingress mapper.
        /// </summary>
        ResourceIsolationOwner ClassifyConnection(IPEndPoint? remoteEndpoint);

        /// <summary>
        /// Classifies a transport-established channel and, optionally, a live verified session.
        /// Session establishment and recovery/control intent select separate protected partitions.
        /// Neither flag alone grants privileges without verified continuity or trusted ingress.
        /// </summary>
        ResourceIsolationOwner Classify(
            SecureChannelContext channelContext,
            NodeId authenticationToken = default,
            bool sessionEstablishment = false,
            bool controlRequest = false);

        /// <summary>
        /// Revalidates a classification immediately before dispatch without refreshing session activity.
        /// Full service authorization remains mandatory.
        /// </summary>
        bool IsCurrent(
            ResourceIsolationOwner owner,
            SecureChannelContext channelContext,
            NodeId authenticationToken = default,
            bool sessionEstablishment = false,
            bool controlRequest = false);

        /// <summary>
        /// Attempts immediate admission. Successful leases are idempotently disposable.
        /// Reassembly callers retain one lease per byte increment until releasing the message;
        /// callers never wait for admission while holding transport state.
        /// </summary>
        bool TryAcquire(
            ResourceIsolationStage stage,
            ResourceIsolationOwner owner,
            long amount,
            [NotNullWhen(true)] out IDisposable? lease,
            out ResourceIsolationFailure failure);
    }

    /// <summary>
    /// Optional capability exposed by server transport callbacks.
    /// </summary>
    public interface IResourceIsolationProviderSource
    {
        /// <summary>
        /// Gets the shared runtime admission provider.
        /// </summary>
        IServerResourceIsolationProvider? ResourceIsolationProvider { get; }
    }
}
