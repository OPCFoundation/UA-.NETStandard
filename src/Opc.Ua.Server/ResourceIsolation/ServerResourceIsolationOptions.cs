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
using Opc.Ua.Bindings;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Configures server-scoped, nonblocking resource admission and decoded-request fairness.
    /// </summary>
    /// <remarks>
    /// Reserved floors remain inside existing totals. Unknown anonymous callers receive shared,
    /// best-effort service; guaranteed pre-authentication access requires an explicit ingress classifier.
    /// StandardServer's SharedOnly mode does not install or validate a default runtime provider;
    /// existing unlimited capacities, aggregate limits and FIFO behavior remain in effect.
    /// </remarks>
    public sealed class ServerResourceIsolationOptions
    {
        /// <summary>
        /// The runtime profile, independent of and additional to existing rate limiters.
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
        /// Maximum concurrently accounted owner keys. Idle entries are removed on final release.
        /// Active entries are never evicted to make room for new keys.
        /// </summary>
        public int MaxTrackedOwners { get; set; } = 4096;

        /// <summary>
        /// Maximum classifier-supplied key length. Default identities use bounded SHA-256 digests.
        /// </summary>
        public int MaxOwnerKeyLength { get; set; } = 256;

        /// <summary>
        /// Relative scheduling weight for ordinary owners.
        /// </summary>
        public int DefaultWeight { get; set; } = 1;

        /// <summary>
        /// Maximum lifetime of an admitted, unfinished protocol handshake.
        /// The maximum and default are two minutes; it is independent of renewable channel lifetimes.
        /// </summary>
        public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Stage-specific overrides. Capacity overrides can only lower existing totals.
        /// </summary>
        public ArrayOf<ResourceIsolationStageOptions> Stages { get; set; }

        /// <summary>
        /// Explicitly provisioned trusted owners, required by TrustedReservations.
        /// </summary>
        public ArrayOf<TrustedResourceOwnerOptions> TrustedOwners { get; set; }

        /// <summary>
        /// Explicit maximum retained backing-array footprint for custom buffer pools or transports.
        /// Null uses the default pool's conservative bound across negotiated chunk sizes.
        /// </summary>
        public long? MaxRetainedMessageBytes { get; set; }

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
            ValidateOwners();
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

            long trustedBytes = 0;
            foreach (TrustedResourceOwnerOptions owner in TrustedOwners)
            {
                long reserved = messageBytes;
                foreach (TrustedResourceReservation reservation in owner.Reservations)
                {
                    if (reservation.Stage == ResourceIsolationStage.ReassemblyBytes)
                    {
                        reserved = reservation.Reserved;
                    }
                }
                ValidateFloor(reserved, messageBytes, nameof(TrustedOwners));
                if (reserved > budget.MaxBytes - bootstrapBytes - reconnectBytes - trustedBytes)
                {
                    throw new ArgumentException("Trusted floors exceed the existing reassembly total.");
                }
                trustedBytes += reserved;
            }
            if (budget.MaxBytes - bootstrapBytes - reconnectBytes - trustedBytes < messageBytes)
            {
                throw new ArgumentException("The shared pool must hold one maximum retained message.");
            }
            return new ServerResourceIsolationPlan(
                mode,
                budget.MaxBytes,
                budget.MaxBytesWithoutSession,
                maxSessionCount,
                maxChannelCount,
                messageBytes,
                bootstrapBytes,
                reconnectBytes,
                trustedBytes);
        }

        /// <summary>
        /// Creates a complete runtime plan without changing any declared capacity.
        /// The supplied budget remains a separately enforced, potentially shared additional ceiling.
        /// Shared connection capacity must support the advertised Session maximum plus one
        /// ordinary reconnect channel; reserved ingress capacity is not counted toward this guarantee.
        /// </summary>
        public ServerResourceIsolationPlan CreateRuntimePlan(
            ApplicationConfiguration configuration,
            ServerRateLimitOptions rateLimits,
            ChunkReassemblyBudget? budget = null)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (rateLimits == null)
            {
                throw new ArgumentNullException(nameof(rateLimits));
            }
            ServerConfiguration server = configuration.ServerConfiguration ??
                throw new ArgumentException("Server configuration is required.", nameof(configuration));
            TransportQuotas quotas = configuration.TransportQuotas ??
                throw new ArgumentException("Transport quotas are required.", nameof(configuration));
            if (quotas.MaxMessageSize <= 0 || quotas.MaxBufferSize <= 0)
            {
                throw new ArgumentException("Isolation requires finite positive message and buffer limits.");
            }
            long footprint = MaxRetainedMessageBytes ??
                checked(4L * (quotas.MaxMessageSize + (long)Math.Max(
                    TcpMessageLimits.MinBufferSize, Math.Min(quotas.MaxBufferSize, TcpMessageLimits.MaxBufferSize))));
            if (footprint <= 0)
            {
                throw new ArgumentException("MaxRetainedMessageBytes must be positive.");
            }
            budget ??= ChunkReassemblyBudget.CreateDefault(EndpointConfiguration.Create(configuration));
            ValidateMode(Mode);
            long bootstrap = ResolveFloor(Mode, BootstrapReservedBytes, footprint, nameof(BootstrapReservedBytes));
            long reconnect = ResolveFloor(Mode, ReconnectReservedBytes, footprint, nameof(ReconnectReservedBytes));
            ValidateOwners();
            if (server.MaxSessionCount <= 0 || server.MaxChannelCount < (long)server.MaxSessionCount + 1)
            {
                throw new ArgumentException("SecureChannel capacity must support the Session maximum plus one.");
            }
            if (MaxTrackedOwners <= 0 || MaxOwnerKeyLength <= 0 || DefaultWeight <= 0)
            {
                throw new ArgumentException("Owner table, key length and weight limits must be positive.");
            }
            if (HandshakeTimeout <= TimeSpan.Zero || HandshakeTimeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentException("HandshakeTimeout must be positive and no greater than two minutes.");
            }
            if (TrustedOwners.Count > MaxTrackedOwners)
            {
                throw new ArgumentException("The owner table cannot hold every provisioned trusted owner.");
            }
            int sessionCapacity = rateLimits.MaxConcurrentSessionEstablishment > 0
                ? rateLimits.MaxConcurrentSessionEstablishment
                : ServerRateLimitOptions.DefaultMaxConcurrentSessionEstablishment;
            int queuedCapacity = Math.Max(100, server.MaxQueuedRequestCount);
            int executionCapacity = Math.Max(100, Math.Max(server.MinRequestThreadCount, server.MaxRequestThreadCount));
            long retainedRequestSlots = 2L * queuedCapacity + executionCapacity;
            if (retainedRequestSlots > long.MaxValue / quotas.MaxMessageSize)
            {
                throw new ArgumentException("The declared queued, executing and parked request cost overflows Int64.");
            }
            long requestCostCapacity = retainedRequestSlots * quotas.MaxMessageSize;
            long[] totals =
            [
                server.MaxChannelCount, server.MaxChannelCount, budget.MaxBytes, sessionCapacity,
                queuedCapacity, executionCapacity, requestCostCapacity, queuedCapacity
            ];
            var overrides = new ResourceIsolationStageOptions?[totals.Length];
            foreach (ResourceIsolationStageOptions item in Stages)
            {
                if (item == null)
                {
                    throw new ArgumentException("Stage options cannot contain null entries.");
                }
                ValidateStage(item.Stage);
                if (overrides[(int)item.Stage] != null)
                {
                    throw new ArgumentException("Duplicate resource stage.");
                }
                overrides[(int)item.Stage] = item;
            }
            var trusted = new Dictionary<string, TrustedOwnerPlan>(StringComparer.Ordinal);
            foreach (TrustedResourceOwnerOptions owner in TrustedOwners)
            {
                var floors = new long[totals.Length];
                var ceilings = new long[totals.Length];
                for (int ii = 0; ii < floors.Length; ii++)
                {
                    floors[ii] = GetStageUnit((ResourceIsolationStage)ii, footprint, quotas.MaxMessageSize);
                    ceilings[ii] = overrides[ii]?.OwnerHardLimit ?? overrides[ii]?.Capacity ?? totals[ii];
                }
                foreach (TrustedResourceReservation item in owner.Reservations)
                {
                    floors[(int)item.Stage] = item.Reserved;
                    ceilings[(int)item.Stage] = item.HardLimit ?? ceilings[(int)item.Stage];
                }
                trusted.Add(owner.Key, new TrustedOwnerPlan(owner.Weight, floors, ceilings));
            }
            var stages = new ResourceIsolationStagePlan[totals.Length];
            bool hasBootstrap = false;
            bool hasReconnect = false;
            bool hasControl = false;
            for (int ii = 0; ii < stages.Length; ii++)
            {
                ResourceIsolationStageOptions? item = overrides[ii];
                long unit = GetStageUnit((ResourceIsolationStage)ii, footprint, quotas.MaxMessageSize);
                long capacity = item?.Capacity ?? totals[ii];
                long floorDefault = Mode is ServerResourceIsolationMode.Balanced or
                    ServerResourceIsolationMode.TrustedReservations ? unit : 0;
                long bootstrapFloor = item?.BootstrapReserved ??
                    (ii == (int)ResourceIsolationStage.ReassemblyBytes ? bootstrap : floorDefault);
                long reconnectFloor = item?.ReconnectReserved ??
                    (ii == (int)ResourceIsolationStage.ReassemblyBytes ? reconnect : floorDefault);
                long controlFloor = item?.ControlReserved ??
                    (ii >= (int)ResourceIsolationStage.RequestQueue ? floorDefault : 0);
                long hardLimit = item?.OwnerHardLimit ?? capacity;
                if (capacity <= 0 || capacity > totals[ii] || hardLimit < unit || hardLimit > capacity)
                {
                    throw new ArgumentException($"Invalid capacity or owner ceiling for {(ResourceIsolationStage)ii}.");
                }
                ValidateFloor(bootstrapFloor, unit, nameof(Stages));
                ValidateFloor(reconnectFloor, unit, nameof(Stages));
                ValidateFloor(controlFloor, unit, nameof(Stages));
                if (floorDefault == 0 && (bootstrapFloor != 0 || reconnectFloor != 0 || controlFloor != 0))
                {
                    throw new ArgumentException("This profile does not permit reserved floors.");
                }
                long remaining = capacity;
                SubtractFloor(ref remaining, bootstrapFloor);
                SubtractFloor(ref remaining, reconnectFloor);
                SubtractFloor(ref remaining, controlFloor);
                long trustedTotal = 0;
                foreach (TrustedOwnerPlan owner in trusted.Values)
                {
                    ValidateFloor(owner.Floors[ii], unit, nameof(TrustedOwners));
                    if (owner.HardLimits[ii] < Math.Max(unit, owner.Floors[ii]) ||
                        owner.HardLimits[ii] > capacity)
                    {
                        throw new ArgumentException("A trusted owner's ceiling cannot satisfy its floor.");
                    }
                    SubtractFloor(ref remaining, owner.Floors[ii]);
                    trustedTotal += owner.Floors[ii];
                }
                if (remaining < unit || hardLimit < Math.Max(Math.Max(bootstrapFloor, reconnectFloor), controlFloor))
                {
                    throw new ArgumentException($"The {(ResourceIsolationStage)ii} pools cannot hold an operation.");
                }
                stages[ii] = new ResourceIsolationStagePlan(
                    (ResourceIsolationStage)ii, capacity, bootstrapFloor, reconnectFloor, trustedTotal, hardLimit,
                    controlFloor);
                hasBootstrap |= bootstrapFloor > 0;
                hasReconnect |= reconnectFloor > 0;
                hasControl |= controlFloor > 0;
            }
            long requiredOwners = (long)TrustedOwners.Count + 1 + (hasBootstrap ? 1 : 0) +
                (hasReconnect ? 1 : 0) + (hasControl ? 1 : 0);
            if (requiredOwners > MaxTrackedOwners)
            {
                throw new ArgumentException("The owner table needs slots for shared and each protected class.");
            }
            if (stages[(int)ResourceIsolationStage.Connection].SharedCapacity < (long)server.MaxSessionCount + 1)
            {
                throw new ArgumentException(
                    "The shared Connection capacity must support MaxSessionCount plus one ordinary reconnect " +
                    "channel. Reduce connection reservations or explicitly increase MaxChannelCount.");
            }
            ResourceIsolationStagePlan reassembly = stages[(int)ResourceIsolationStage.ReassemblyBytes];
            return new ServerResourceIsolationPlan(
                Mode, budget.MaxBytes, budget.MaxBytesWithoutSession, server.MaxSessionCount, server.MaxChannelCount,
                footprint, reassembly.BootstrapReserved, reassembly.ReconnectReserved, reassembly.TrustedReserved,
                stages, trusted, MaxTrackedOwners, MaxOwnerKeyLength, DefaultWeight);
        }

        private void ValidateOwners()
        {
            if (Mode == ServerResourceIsolationMode.TrustedReservations && TrustedOwners.Count == 0)
            {
                throw new ArgumentException("TrustedReservations requires explicit trusted-owner provisioning.");
            }
            if (Mode != ServerResourceIsolationMode.TrustedReservations && TrustedOwners.Count != 0)
            {
                throw new ArgumentException("Trusted owners require the TrustedReservations profile.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TrustedResourceOwnerOptions owner in TrustedOwners)
            {
                if (owner == null || string.IsNullOrEmpty(owner.Key) || owner.Key.Length > MaxOwnerKeyLength ||
                    owner.Weight <= 0 || !keys.Add(owner.Key))
                {
                    throw new ArgumentException("Trusted owners require unique bounded keys and positive weights.");
                }
                var stages = new HashSet<ResourceIsolationStage>();
                foreach (TrustedResourceReservation reservation in owner.Reservations)
                {
                    ValidateStage(reservation.Stage);
                    if (!stages.Add(reservation.Stage) || reservation.Reserved < 0 || reservation.HardLimit <= 0)
                    {
                        throw new ArgumentException("Invalid or duplicate trusted stage reservation.");
                    }
                }
            }
        }

        private static void ValidateStage(ResourceIsolationStage stage)
        {
            if (stage is < ResourceIsolationStage.Connection or > ResourceIsolationStage.ParkedRequest)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        private static long GetStageUnit(ResourceIsolationStage stage, long footprint, int maxMessageSize)
        {
            return stage switch
            {
                ResourceIsolationStage.ReassemblyBytes => footprint,
                ResourceIsolationStage.RequestQueueBytes => maxMessageSize,
                _ => 1
            };
        }

        private static void ValidateFloor(long floor, long unit, string parameterName)
        {
            if (floor < 0 || (floor != 0 && floor < unit))
            {
                throw new ArgumentOutOfRangeException(parameterName, "A nonzero floor must hold one operation.");
            }
        }

        private static void SubtractFloor(ref long remaining, long floor)
        {
            if (floor > remaining)
            {
                throw new ArgumentException("Reserved floors exceed the existing capacity.");
            }
            remaining -= floor;
        }

        private static void ValidateMode(ServerResourceIsolationMode mode)
        {
            if (mode is < ServerResourceIsolationMode.SharedOnly or > ServerResourceIsolationMode.TrustedReservations)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
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
            if (mode is not ServerResourceIsolationMode.Balanced and
                not ServerResourceIsolationMode.TrustedReservations)
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
