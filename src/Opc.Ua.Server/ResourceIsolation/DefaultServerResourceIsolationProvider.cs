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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Bounded, server-scoped accounting with non-borrowable protected floors.
    /// </summary>
    /// <remarks>
    /// Admission never waits or revokes existing leases. Unreserved capacity is work-conserving
    /// within configured hard ceilings. Fair ordering of decoded work is the scheduler's job.
    /// Explicit ingress classification is necessary for strict pre-authentication guarantees.
    /// </remarks>
    public sealed class DefaultServerResourceIsolationProvider :
        IServerResourceIsolationProvider, IResourceIsolationRevalidationProvider,
        IResourceIsolationReassemblyProvider, IDisposable
    {
        /// <summary>
        /// Creates a provider from an immutable runtime plan.
        /// Injected collaborators remain owned by the caller.
        /// </summary>
        public DefaultServerResourceIsolationProvider(
            ServerResourceIsolationPlan plan,
            ITelemetryContext telemetry,
            ISessionBindingProvider? sessionBindings = null,
            IResourceIsolationClassifier? classifier = null)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (plan.Mode == ServerResourceIsolationMode.TrustedReservations && classifier == null)
            {
                throw new ArgumentException("TrustedReservations requires an explicit classifier.", nameof(classifier));
            }
            for (int ii = 0; ii < m_stages.Length; ii++)
            {
                ResourceIsolationStagePlan stage = plan.GetStage((ResourceIsolationStage)ii);
                m_stages[ii] = new StageState(stage);
                m_hasBootstrapReservation |= stage.BootstrapReserved > 0;
                m_hasReconnectReservation |= stage.ReconnectReserved > 0;
                m_hasControlReservation |= stage.ControlReserved > 0;
            }
            m_sessionBindings = sessionBindings;
            m_classifier = classifier;
            m_meter = telemetry.CreateMeter();
            m_rejections = m_meter.CreateCounter<long>("opcua.server.isolation.rejections");
            m_meter.CreateObservableGauge("opcua.server.isolation.usage", ObserveUsage);
            m_meter.CreateObservableGauge("opcua.server.isolation.owners", () => TrackedOwnerCount);
            m_logger = telemetry.CreateLogger<DefaultServerResourceIsolationProvider>();
            m_logger.IsolationPolicyStarted();
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                for (int ii = 0; ii < m_stages.Length; ii++)
                {
                    ResourceIsolationStagePlan stage = m_stages[ii].Plan;
                    m_logger.IsolationStageLimits(
                        stage.Stage, stage.Capacity, stage.SharedCapacity,
                        stage.BootstrapReserved, stage.ReconnectReserved, stage.ControlReserved,
                        stage.TrustedReserved, stage.OwnerHardLimit);
                }
            }
        }

        /// <summary>
        /// The immutable policy used by this provider.
        /// </summary>
        public ServerResourceIsolationPlan Plan { get; }

        /// <inheritdoc/>
        public bool UseFairScheduling => Plan.Mode != ServerResourceIsolationMode.SharedOnly;

        /// <inheritdoc/>
        public event Action<ResourceIsolationStage>? CapacityAvailable;

        /// <summary>
        /// Number of keys with active leases. Released keys are removed immediately.
        /// </summary>
        public int TrackedOwnerCount => Volatile.Read(ref m_ownerCount);

        /// <inheritdoc/>
        public ResourceIsolationOwner ClassifyConnection(IPEndPoint? remoteEndpoint)
        {
            ThrowIfDisposed();
            if (m_classifier?.TryClassifyIngress(remoteEndpoint, out ResourceIsolationIdentity identity) == true)
            {
                return CreateMappedOwner(identity);
            }
            return CreateOwner(PeerKey(remoteEndpoint?.Address), ResourceIsolationClass.Established);
        }

        /// <inheritdoc/>
        public ResourceIsolationOwner Classify(
            SecureChannelContext channelContext,
            NodeId authenticationToken = default,
            bool sessionEstablishment = false,
            bool controlRequest = false)
        {
            if (channelContext == null)
            {
                throw new ArgumentNullException(nameof(channelContext));
            }
            if (string.IsNullOrEmpty(channelContext.SecureChannelId))
            {
                throw new ArgumentException("A transport-established channel identifier is required.",
                    nameof(channelContext));
            }
            ThrowIfDisposed();
            SessionBindingContext? binding = null;
            if (!authenticationToken.IsNull &&
                m_sessionBindings?.TryGetSessionContext(authenticationToken, channelContext, out binding) != true)
            {
                binding = null;
            }
            bool secure = channelContext.EndpointDescription is
            {
                SecurityMode: MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt
            } endpoint && endpoint.SecurityPolicyUri != SecurityPolicies.None;
            if (m_classifier != null)
            {
                SecureChannelContext verifiedContext = secure ? channelContext : new SecureChannelContext(
                    channelContext.SecureChannelId, channelContext.EndpointDescription, channelContext.MessageEncoding,
                    peerAddress: channelContext.PeerAddress)
                { UpstreamIdentity = channelContext.UpstreamIdentity };
                if (m_classifier.TryClassify(verifiedContext, binding, out ResourceIsolationIdentity identity) ||
                    m_classifier.TryClassifyIngress(
                        channelContext.PeerAddress == null ? null : new IPEndPoint(channelContext.PeerAddress, 0),
                        out identity))
                {
                    return CreateMappedOwner(identity, binding, channelContext);
                }
            }
            ResourceIsolationClass ownerClass = binding != null && controlRequest
                ? ResourceIsolationClass.Control
                : binding != null && sessionEstablishment
                    ? ResourceIsolationClass.Reconnect : ResourceIsolationClass.Established;
            ClassificationCache cache = m_channelClassifications.GetValue(
                channelContext.SecureChannelId, static _ => new ClassificationCache());
            if (cache.TryGet(ownerClass, binding, channelContext, out ResourceIsolationOwner? cached))
            {
                return cached;
            }
            string key;
            if (binding?.ClientUserId is { } continuityKey)
            {
                key = m_bindingKeys.GetValue(binding,
                    value => new DerivedKey("user:" + Hash(Encoding.UTF8.GetBytes(value.ClientUserId!)))).Value;
            }
            else if (secure && channelContext.ClientChannelCertificate.Length > 0)
            {
                // classification-cache miss only
                key = "application:" + Hash(channelContext.ClientChannelCertificate.ToArray());
            }
            else
            {
                key = PeerKey(channelContext.PeerAddress);
            }
            return CreateOwner(key, ownerClass, binding: binding, channelContext: channelContext);
        }

        /// <inheritdoc/>
        public ResourceIsolationOwner ClassifyReassembly(SecureChannelContext channelContext)
        {
            ResourceIsolationOwner owner = Classify(channelContext);
            if (!UseFairScheduling || owner.Class != ResourceIsolationClass.Established ||
                owner.Key.StartsWith("mapped:", StringComparison.Ordinal) ||
                m_sessionBindings?.HasSession(channelContext.SecureChannelId) != true)
            {
                return owner;
            }
            return CreateOwner(
                "continuity:" + owner.Key,
                ResourceIsolationClass.Reconnect,
                channelContext: channelContext,
                onlyStage: ResourceIsolationStage.ReassemblyBytes);
        }

        /// <inheritdoc/>
        public bool IsCurrent(
            ResourceIsolationOwner owner,
            SecureChannelContext channelContext,
            NodeId authenticationToken = default,
            bool sessionEstablishment = false,
            bool controlRequest = false)
        {
            return StatusCode.IsGood(GetRevalidationStatus(
                owner, channelContext, authenticationToken, sessionEstablishment, controlRequest));
        }

        /// <inheritdoc/>
        public StatusCode GetRevalidationStatus(
            ResourceIsolationOwner owner,
            SecureChannelContext channelContext,
            NodeId authenticationToken = default,
            bool sessionEstablishment = false,
            bool controlRequest = false)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (!m_classifications.TryGetValue(owner, out OwnerIdentity? original))
            {
                return StatusCodes.BadServerTooBusy;
            }
            if (channelContext == null)
            {
                throw new ArgumentNullException(nameof(channelContext));
            }
            ThrowIfDisposed();
            if (original.OnlyStage.HasValue)
            {
                return StatusCodes.BadServerTooBusy;
            }
            if (original.Channel == null || !original.Channel.Matches(channelContext))
            {
                return StatusCodes.BadSecureChannelIdInvalid;
            }
            SessionBindingContext? current = null;
            if (!authenticationToken.IsNull)
            {
                m_sessionBindings?.TryGetSessionContext(authenticationToken, channelContext, out current);
            }
            if (!ReferenceEquals(original.Binding, current))
            {
                return current == null && original.Binding != null
                    ? StatusCodes.BadSessionIdInvalid : StatusCodes.BadServerTooBusy;
            }

            if (m_classifier != null)
            {
                bool secure = original.Channel.SecurityMode is
                    MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt &&
                    original.Channel.SecurityPolicyUri != SecurityPolicies.None;
                SecureChannelContext verifiedContext = secure ? channelContext : new SecureChannelContext(
                    channelContext.SecureChannelId, channelContext.EndpointDescription, channelContext.MessageEncoding,
                    peerAddress: channelContext.PeerAddress)
                { UpstreamIdentity = channelContext.UpstreamIdentity };
                if (m_classifier.TryClassify(verifiedContext, current, out ResourceIsolationIdentity identity) ||
                    m_classifier.TryClassifyIngress(
                        channelContext.PeerAddress == null ? null : new IPEndPoint(channelContext.PeerAddress, 0),
                        out identity))
                {
                    string key = identity.Class == ResourceIsolationClass.Trusted
                        ? GetTrustedOwnerKey(identity.Key) : "mapped:" + identity.Key;
                    return identity.Class == owner.Class && key == owner.Key
                        ? StatusCodes.Good : StatusCodes.BadServerTooBusy;
                }
            }
            ResourceIsolationClass expected = current != null && controlRequest
                ? ResourceIsolationClass.Control
                : current != null && sessionEstablishment
                    ? ResourceIsolationClass.Reconnect : ResourceIsolationClass.Established;
            return owner.Class == expected && !owner.Key.StartsWith("mapped:", StringComparison.Ordinal) &&
                !owner.Key.StartsWith("trusted:", StringComparison.Ordinal)
                ? StatusCodes.Good : StatusCodes.BadServerTooBusy;
        }

        /// <inheritdoc/>
        public bool TryAcquire(
            ResourceIsolationStage stage,
            ResourceIsolationOwner owner,
            long amount,
            [NotNullWhen(true)] out IDisposable? lease,
            out ResourceIsolationFailure failure)
        {
            if (stage is < ResourceIsolationStage.Connection or > ResourceIsolationStage.ParkedRequest)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }
            ThrowIfDisposed();
            ResourceIsolationFailureReason reason = Acquire(stage, owner, amount, out lease);
            failure = new ResourceIsolationFailure(
                reason, reason == ResourceIsolationFailureReason.None ? TimeSpan.Zero : TimeSpan.FromSeconds(1));
            if (reason != ResourceIsolationFailureReason.None)
            {
                // Only bounded enum values enter metric dimensions; no address, token or owner key.
                m_rejections.Add(1,
                    new KeyValuePair<string, object?>("stage", stage.ToString()),
                    new KeyValuePair<string, object?>("reason", reason.ToString()));
                return false;
            }
            if (lease == null)
            {
                throw new InvalidOperationException("Successful admission did not produce a lease.");
            }
            return true;
        }

        /// <summary>
        /// Returns current aggregate usage of a resource pool.
        /// </summary>
        public long GetUsage(ResourceIsolationStage stage)
        {
            if (stage is < ResourceIsolationStage.Connection or > ResourceIsolationStage.ParkedRequest)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }
            return Interlocked.Read(ref m_stages[(int)stage].Used);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_meter.Dispose();
            }
        }

        /// <summary>
        /// Recognizes classifications issued by this provider without accepting fabricated protected owners.
        /// </summary>
        internal bool IsIssuedOwner(ResourceIsolationOwner owner)
        {
            ThrowIfDisposed();
            return m_classifications.TryGetValue(owner, out OwnerIdentity? identity) && !identity.OnlyStage.HasValue;
        }

        /// <summary>
        /// Separates provisioned owner keys from ordinary and mapped identity keys.
        /// </summary>
        internal static string GetTrustedOwnerKey(string key)
        {
            return "trusted:" + key;
        }

        private IEnumerable<Measurement<long>> ObserveUsage()
        {
            var values = new Measurement<long>[kStageCount];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = new Measurement<long>(
                    Interlocked.Read(ref m_stages[ii].Used),
                    new KeyValuePair<string, object?>("stage", ((ResourceIsolationStage)ii).ToString()));
            }
            return values;
        }

        /// <summary>
        /// Admits a lease without a server-wide lock when the owner already holds a lease of the
        /// same accounting class. Owner-table changes (a new owner, a class appearing or
        /// disappearing) take the table lock.
        /// </summary>
        /// <remarks>
        /// Each counter is reserved with a compare-and-swap that never passes its limit, and the
        /// counters already reserved are returned when a later one is full. Two acquisitions
        /// racing for the last units may therefore both be rejected where a serialized order would
        /// admit one; no limit is ever exceeded.
        /// A class count of an owner only crosses zero under the table lock, so the owner's
        /// exclusive class and the protected table slots stay consistent with its leases.
        /// </remarks>
        private ResourceIsolationFailureReason Acquire(
            ResourceIsolationStage stage,
            ResourceIsolationOwner owner,
            long amount,
            out IDisposable? lease)
        {
            lease = null;
            if (!m_classifications.TryGetValue(owner, out OwnerIdentity? identity))
            {
                return ResourceIsolationFailureReason.InvalidOwner;
            }
            if (identity.OnlyStage is { } onlyStage && onlyStage != stage)
            {
                return ResourceIsolationFailureReason.InvalidOwner;
            }
            int index = (int)stage;
            StageState state = m_stages[index];
            ResourceIsolationClass accountingClass = identity.OnlyStage.HasValue
                ? ResourceIsolationClass.Established : owner.Class;
            ResourceIsolationFailureReason reason;
            long fromReserved;

            if (m_owners.TryGetValue(owner.Key, out OwnerState? accounting) &&
                TryAddClassLease(accounting, accountingClass))
            {
                // The owner's set of active classes does not change, so neither does its
                // exclusive class nor the number of protected table slots.
                reason = Reserve(state, accounting, owner, identity, stage, amount, out fromReserved);
                if (reason != ResourceIsolationFailureReason.None)
                {
                    ReleaseClassLease(accounting, accountingClass);
                    return reason;
                }
                lease = new Lease(this, accounting, stage, owner.Class, accountingClass, amount, fromReserved);
                return ResourceIsolationFailureReason.None;
            }

            lock (m_tableLock)
            {
                m_owners.TryGetValue(owner.Key, out accounting);
                OwnerState target = accounting ?? new OwnerState(owner.Key);
                reason = Reserve(state, target, owner, identity, stage, amount, out fromReserved);
                if (reason != ResourceIsolationFailureReason.None)
                {
                    return reason;
                }
                ResourceIsolationClass? nextExclusiveClass =
                    accounting == null || accounting.ExclusiveClass == accountingClass ? accountingClass : null;
                // A mixed-class key cannot fill a protected table slot: its ordinary leases
                // may outlive every protected lease without releasing the entry.
                int protectedSlots = GetReservedOwnerSlots(accounting?.ExclusiveClass, nextExclusiveClass);
                long projectedOwnerCount = (long)m_ownerCount + (accounting == null ? 1 : 0);
                if (projectedOwnerCount > Plan.MaxTrackedOwners - protectedSlots)
                {
                    Unreserve(state, target, owner.Class, stage, amount, fromReserved);
                    return ResourceIsolationFailureReason.OwnerTableFull;
                }
                if (accounting == null)
                {
                    m_owners[owner.Key] = target;
                    Volatile.Write(ref m_ownerCount, m_ownerCount + 1);
                }
                if (Interlocked.Increment(ref target.ClassLeases[(int)accountingClass]) == 1)
                {
                    UpdateExclusiveClassLocked(target);
                }
                lease = new Lease(this, target, stage, owner.Class, accountingClass, amount, fromReserved);
                return ResourceIsolationFailureReason.None;
            }
        }

        /// <summary>
        /// Reserves the owner's ceiling, its protected floor and the stage capacity,
        /// in the order the rejection reasons are reported.
        /// </summary>
        private static ResourceIsolationFailureReason Reserve(
            StageState state,
            OwnerState accounting,
            ResourceIsolationOwner owner,
            OwnerIdentity identity,
            ResourceIsolationStage stage,
            long amount,
            out long fromReserved)
        {
            fromReserved = 0;
            int index = (int)stage;
            if (!TryAddBounded(ref accounting.Usage[index], amount, owner.GetHardLimit(stage)))
            {
                return ResourceIsolationFailureReason.OwnerLimit;
            }
            long floor = owner.Class switch
            {
                ResourceIsolationClass.Bootstrap => state.Plan.BootstrapReserved,
                ResourceIsolationClass.Reconnect => state.Plan.ReconnectReserved,
                ResourceIsolationClass.Control => state.Plan.ControlReserved,
                ResourceIsolationClass.Trusted => identity.Trusted!.Floors[index],
                _ => 0
            };
            if (floor > 0)
            {
                ref long reservedUsed = ref GetReservedCounter(state, accounting, owner.Class, index);
                while (true)
                {
                    long used = Interlocked.Read(ref reservedUsed);
                    long available = Math.Min(amount, floor - used);
                    if (available <= 0 ||
                        Interlocked.CompareExchange(ref reservedUsed, used + available, used) == used)
                    {
                        fromReserved = Math.Max(available, 0);
                        break;
                    }
                }
            }
            long fromShared = amount - fromReserved;
            bool admitted = TryAddBounded(ref state.Used, amount, state.Plan.Capacity);
            if (admitted && fromShared > 0 &&
                !TryAddBounded(ref state.SharedUsed, fromShared, state.Plan.SharedCapacity))
            {
                Interlocked.Add(ref state.Used, -amount);
                admitted = false;
            }
            if (!admitted)
            {
                if (fromReserved > 0)
                {
                    Interlocked.Add(
                        ref GetReservedCounter(state, accounting, owner.Class, index), -fromReserved);
                }
                Interlocked.Add(ref accounting.Usage[index], -amount);
                fromReserved = 0;
                return ResourceIsolationFailureReason.Capacity;
            }
            return ResourceIsolationFailureReason.None;
        }

        /// <summary>
        /// Adds <paramref name="amount"/> unless the counter would exceed <paramref name="limit"/>;
        /// the counter never shows more than the limit, not even transiently.
        /// </summary>
        private static bool TryAddBounded(ref long counter, long amount, long limit)
        {
            while (true)
            {
                long current = Interlocked.Read(ref counter);
                if (amount > limit - current)
                {
                    return false;
                }
                if (Interlocked.CompareExchange(ref counter, current + amount, current) == current)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Returns the counters of a successful reservation.
        /// </summary>
        private static void Unreserve(
            StageState state,
            OwnerState accounting,
            ResourceIsolationClass ownerClass,
            ResourceIsolationStage stage,
            long amount,
            long fromReserved)
        {
            int index = (int)stage;
            if (amount > fromReserved)
            {
                Interlocked.Add(ref state.SharedUsed, fromReserved - amount);
            }
            Interlocked.Add(ref state.Used, -amount);
            if (fromReserved > 0)
            {
                Interlocked.Add(ref GetReservedCounter(state, accounting, ownerClass, index), -fromReserved);
            }
            Interlocked.Add(ref accounting.Usage[index], -amount);
        }

        /// <summary>
        /// The protected-floor counter a class draws from; only called for classes with a floor.
        /// </summary>
        private static ref long GetReservedCounter(
            StageState state,
            OwnerState accounting,
            ResourceIsolationClass ownerClass,
            int index)
        {
            switch (ownerClass)
            {
                case ResourceIsolationClass.Bootstrap:
                    return ref state.BootstrapUsed;
                case ResourceIsolationClass.Reconnect:
                    return ref state.ReconnectUsed;
                case ResourceIsolationClass.Control:
                    return ref state.ControlUsed;
                case ResourceIsolationClass.Trusted:
                    return ref accounting.TrustedUsage[index];
                default:
                    throw new ArgumentOutOfRangeException(nameof(ownerClass));
            }
        }

        private void Release(Lease lease)
        {
            Unreserve(
                m_stages[(int)lease.Stage], lease.Owner, lease.Class, lease.Stage, lease.Amount, lease.Reserved);
            ReleaseClassLease(lease.Owner, lease.AccountingClass);
            CapacityAvailable?.Invoke(lease.Stage);
        }

        /// <summary>
        /// Adds a lease to a class the owner already holds, without changing its set of classes.
        /// </summary>
        private static bool TryAddClassLease(OwnerState owner, ResourceIsolationClass ownerClass)
        {
            ref long count = ref owner.ClassLeases[(int)ownerClass];
            while (true)
            {
                long current = Interlocked.Read(ref count);
                if (current <= 0)
                {
                    return false;
                }
                if (Interlocked.CompareExchange(ref count, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Removes a lease from an owner class; the last lease of a class is removed under the
        /// table lock, which also drops the owner entry once no lease remains.
        /// </summary>
        private void ReleaseClassLease(OwnerState owner, ResourceIsolationClass ownerClass)
        {
            ref long count = ref owner.ClassLeases[(int)ownerClass];
            while (true)
            {
                long current = Interlocked.Read(ref count);
                if (current <= 1)
                {
                    break;
                }
                if (Interlocked.CompareExchange(ref count, current - 1, current) == current)
                {
                    return;
                }
            }
            lock (m_tableLock)
            {
                if (Interlocked.Decrement(ref count) != 0)
                {
                    return;
                }
                UpdateExclusiveClassLocked(owner);
                for (int ii = 0; ii < owner.ClassLeases.Length; ii++)
                {
                    if (Interlocked.Read(ref owner.ClassLeases[ii]) != 0)
                    {
                        return;
                    }
                }
                m_owners.TryRemove(owner.Key, out _);
                Volatile.Write(ref m_ownerCount, m_ownerCount - 1);
            }
        }

        private int GetReservedOwnerSlots(
            ResourceIsolationClass? previous,
            ResourceIsolationClass? next)
        {
            int slots = Plan.TrustedOwners.Count -
                GetProjectedExclusiveOwnerCount(ResourceIsolationClass.Trusted, previous, next);
            if (m_hasBootstrapReservation &&
                GetProjectedExclusiveOwnerCount(ResourceIsolationClass.Bootstrap, previous, next) == 0)
            {
                slots++;
            }
            if (m_hasReconnectReservation &&
                GetProjectedExclusiveOwnerCount(ResourceIsolationClass.Reconnect, previous, next) == 0)
            {
                slots++;
            }
            if (m_hasControlReservation &&
                GetProjectedExclusiveOwnerCount(ResourceIsolationClass.Control, previous, next) == 0)
            {
                slots++;
            }
            return slots;
        }

        private int GetProjectedExclusiveOwnerCount(
            ResourceIsolationClass ownerClass,
            ResourceIsolationClass? previous,
            ResourceIsolationClass? next)
        {
            return m_exclusiveOwners[(int)ownerClass] - (previous == ownerClass ? 1 : 0) +
                (next == ownerClass ? 1 : 0);
        }

        /// <summary>
        /// Recomputes the owner's sole active class after one of its class counts crossed zero.
        /// Concurrent lock-free changes never cross zero, so the result is stable under the lock.
        /// </summary>
        private void UpdateExclusiveClassLocked(OwnerState owner)
        {
            if (owner.ExclusiveClass is { } previous)
            {
                m_exclusiveOwners[(int)previous]--;
            }
            ResourceIsolationClass? exclusive = null;
            for (int ii = 0; ii < owner.ClassLeases.Length; ii++)
            {
                if (Interlocked.Read(ref owner.ClassLeases[ii]) == 0)
                {
                    continue;
                }
                if (exclusive.HasValue)
                {
                    exclusive = null;
                    break;
                }
                exclusive = (ResourceIsolationClass)ii;
            }
            owner.ExclusiveClass = exclusive;
            if (exclusive is { } current)
            {
                m_exclusiveOwners[(int)current]++;
            }
        }

        private ResourceIsolationOwner CreateMappedOwner(
            ResourceIsolationIdentity identity,
            SessionBindingContext? binding = null,
            SecureChannelContext? channelContext = null)
        {
            if (string.IsNullOrEmpty(identity.Key) || identity.Key.Length > Plan.MaxOwnerKeyLength ||
                identity.Class is < ResourceIsolationClass.Bootstrap or > ResourceIsolationClass.Control)
            {
                throw new InvalidOperationException("The configured classifier returned an invalid owner.");
            }
            if (identity.Class == ResourceIsolationClass.Trusted)
            {
                if (!Plan.TrustedOwners.TryGetValue(identity.Key, out TrustedOwnerPlan? provisioned))
                {
                    throw new InvalidOperationException("The classifier returned an unprovisioned trusted owner.");
                }
                return CreateOwner(
                    GetTrustedOwnerKey(identity.Key), identity.Class, provisioned, binding, channelContext,
                    mapped: true);
            }
            return CreateOwner(
                "mapped:" + identity.Key, identity.Class, binding: binding, channelContext: channelContext,
                mapped: true);
        }

        private ResourceIsolationOwner CreateOwner(
            string key,
            ResourceIsolationClass ownerClass,
            TrustedOwnerPlan? provisioned = null,
            SessionBindingContext? binding = null,
            SecureChannelContext? channelContext = null,
            ResourceIsolationStage? onlyStage = null,
            bool mapped = false)
        {
            ClassificationCache? cache = channelContext == null ? null :
                m_channelClassifications.GetValue(
                    channelContext.SecureChannelId, static _ => new ClassificationCache());
            if (cache != null && cache.TryGet(
                ownerClass, binding, channelContext!, out ResourceIsolationOwner? cached, key, onlyStage, mapped))
            {
                return cached;
            }
            var ceilings = new long[kStageCount];
            for (int ii = 0; ii < ceilings.Length; ii++)
            {
                ceilings[ii] = provisioned?.HardLimits[ii] ?? m_stages[ii].Plan.OwnerHardLimit;
            }
            var owner = new ResourceIsolationOwner(
                key, ownerClass, provisioned?.Weight ?? Plan.DefaultWeight, ceilings);
            ChannelSnapshot? snapshot = channelContext == null ? null : cache!.GetSnapshot(channelContext);
            m_classifications.Add(owner, new OwnerIdentity(provisioned, binding, snapshot, onlyStage));
            cache?.Set(owner, binding, snapshot!, onlyStage, mapped);
            return owner;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(DefaultServerResourceIsolationProvider));
            }
        }

        private static string PeerKey(IPAddress? address)
        {
            if (address?.IsIPv4MappedToIPv6 == true)
            {
                address = address.MapToIPv4();
            }
            return address == null ? "peer:unknown" : "peer:" + Hash(address.GetAddressBytes());
        }

        private static string Hash(byte[] value)
        {
#if NET8_0_OR_GREATER
            byte[] hash = SHA256.HashData(value);
#else
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(value);
#endif
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Tracks total, shared and protected usage for one resource stage.
        /// The counters are fields so they can be updated with <see cref="Interlocked"/>.
        /// </summary>
        private sealed class StageState(ResourceIsolationStagePlan plan)
        {
            /// <summary>
            /// The immutable limits applied to these counters.
            /// </summary>
            public ResourceIsolationStagePlan Plan { get; } = plan;

            /// <summary>
            /// All currently leased capacity.
            /// </summary>
            public long Used;

            /// <summary>
            /// Leased capacity drawn from the unreserved pool.
            /// </summary>
            public long SharedUsed;

            /// <summary>
            /// Leased bootstrap floor.
            /// </summary>
            public long BootstrapUsed;

            /// <summary>
            /// Leased reconnect floor.
            /// </summary>
            public long ReconnectUsed;

            /// <summary>
            /// Leased control-work floor.
            /// </summary>
            public long ControlUsed;
        }

        /// <summary>
        /// Retains accounting for an owner only while at least one lease remains active.
        /// </summary>
        private sealed class OwnerState(string key)
        {
            /// <summary>
            /// The bounded canonical key identifying these counters.
            /// </summary>
            public string Key { get; } = key;

            /// <summary>
            /// The sole active class, or null when leases span classes; changed under the table lock.
            /// </summary>
            public ResourceIsolationClass? ExclusiveClass { get; set; }

            /// <summary>
            /// Counts outstanding leases by class for protected tracking-table capacity. A count
            /// crosses zero only under the table lock; the entry is dropped when all are zero.
            /// </summary>
            public long[] ClassLeases { get; } = new long[kClassCount];

            /// <summary>
            /// Outstanding amounts by resource stage.
            /// </summary>
            public long[] Usage { get; } = new long[kStageCount];

            /// <summary>
            /// Amounts drawn from this owner's provisioned floors.
            /// </summary>
            public long[] TrustedUsage { get; } = new long[kStageCount];
        }

        /// <summary>
        /// Associates an issued classification with its provisioned limits and captured validation evidence.
        /// </summary>
        private sealed record OwnerIdentity(
            TrustedOwnerPlan? Trusted, SessionBindingContext? Binding, ChannelSnapshot? Channel,
            ResourceIsolationStage? OnlyStage);

        /// <summary>
        /// Caches a derived key only for the lifetime of its immutable session activation.
        /// </summary>
        private sealed record DerivedKey(string Value);

        /// <summary>
        /// Keeps a bounded set of classifications per live channel-id object, without a permanent identity dictionary.
        /// Snapshot comparisons retain mutation detection; changing session instances invalidates cached ownership.
        /// </summary>
        private sealed class ClassificationCache
        {
            /// <summary>
            /// Reuses a classification only after current Session and classifier checks have run.
            /// </summary>
            public bool TryGet(
                ResourceIsolationClass ownerClass,
                SessionBindingContext? binding,
                SecureChannelContext context,
                [NotNullWhen(true)] out ResourceIsolationOwner? owner,
                string? key = null,
                ResourceIsolationStage? onlyStage = null,
                bool mapped = false)
            {
                Entry[] entries = Volatile.Read(ref m_entries);
                foreach (Entry entry in entries)
                {
                    if (entry.Owner.Class == ownerClass &&
                        entry.Mapped == mapped && entry.Stage == onlyStage &&
                        ReferenceEquals(entry.Binding, binding) && (key == null || key == entry.Owner.Key) &&
                        entry.Snapshot.Matches(context))
                    {
                        owner = entry.Owner;
                        return true;
                    }
                }
                owner = null;
                return false;
            }

            /// <summary>
            /// Reuses unchanged certificate evidence even when another Session uses the same channel.
            /// </summary>
            public ChannelSnapshot GetSnapshot(SecureChannelContext context)
            {
                foreach (Entry entry in Volatile.Read(ref m_entries))
                {
                    if (entry.Snapshot.Matches(context))
                    {
                        return entry.Snapshot;
                    }
                }
                return new ChannelSnapshot(context);
            }

            /// <summary>
            /// Publishes a bounded immutable snapshot; older classifications remain valid while leases retain them.
            /// </summary>
            public void Set(
                ResourceIsolationOwner owner, SessionBindingContext? binding, ChannelSnapshot snapshot,
                ResourceIsolationStage? stage, bool mapped)
            {
                lock (m_lock)
                {
                    Entry[] current = m_entries;
                    int retained = Math.Min(current.Length, kMaximumEntries - 1);
                    var updated = new Entry[retained + 1];
                    updated[0] = new Entry(owner, binding, snapshot, stage, mapped);
                    Array.Copy(current, 0, updated, 1, retained);
                    Volatile.Write(ref m_entries, updated);
                }
            }

            /// <summary>
            /// Records the validated mapping inputs associated with one issued owner.
            /// </summary>
            private sealed record Entry(
                ResourceIsolationOwner Owner, SessionBindingContext? Binding, ChannelSnapshot Snapshot,
                ResourceIsolationStage? Stage, bool Mapped);

            private readonly Lock m_lock = new();
            private const int kMaximumEntries = 8;
            private Entry[] m_entries = [];
        }

        /// <summary>
        /// Captures classification inputs so revalidation can compare them without hashing or issuing another owner.
        /// </summary>
        private sealed class ChannelSnapshot
        {
            /// <summary>
            /// Copies classification inputs that callers could otherwise mutate after enqueue.
            /// </summary>
            public ChannelSnapshot(SecureChannelContext context)
            {
                m_channelId = context.SecureChannelId;
                SecurityPolicyUri = context.EndpointDescription?.SecurityPolicyUri;
                SecurityMode = context.EndpointDescription?.SecurityMode ?? MessageSecurityMode.Invalid;
                // Copied: the context wraps a buffer the transport owns, and revalidation
                // must detect evidence that changed after the owner was issued.
                m_certificate = SecurityPolicyUri != SecurityPolicies.None
                    ? ByteString.From(context.ClientChannelCertificate.Span) : default;
                m_peer = context.PeerAddress?.GetAddressBytes();
                m_upstreamIdentity = context.UpstreamIdentity;
            }

            /// <summary>
            /// The policy at the time classification was issued.
            /// </summary>
            public string? SecurityPolicyUri { get; }

            /// <summary>
            /// The channel security mode captured with the policy.
            /// </summary>
            public MessageSecurityMode SecurityMode { get; }

            /// <summary>
            /// Checks captured channel and identity evidence without creating a new classification.
            /// </summary>
            public bool Matches(SecureChannelContext context)
            {
                return m_channelId == context.SecureChannelId &&
                    SecurityPolicyUri == context.EndpointDescription?.SecurityPolicyUri &&
                    SecurityMode == (context.EndpointDescription?.SecurityMode ?? MessageSecurityMode.Invalid) &&
                    (SecurityPolicyUri == SecurityPolicies.None ||
                        m_certificate.Span.SequenceEqual(context.ClientChannelCertificate.Span)) &&
                    PeerMatches(context.PeerAddress) &&
                    ReferenceEquals(m_upstreamIdentity, context.UpstreamIdentity);
            }

            /// <summary>
            /// Compares the captured peer address bytes; runs for every request, so the
            /// current address is written to the stack rather than copied to a new array.
            /// </summary>
            private bool PeerMatches(IPAddress? peer)
            {
                if (peer == null)
                {
                    return m_peer == null || m_peer.Length == 0;
                }
#if NET
                // 16 bytes hold an IPv6 address, so the write always succeeds.
                Span<byte> bytes = stackalloc byte[16];
                return peer.TryWriteBytes(bytes, out int written) &&
                    m_peer.AsSpan().SequenceEqual(bytes[..written]);
#else
                return m_peer.AsSpan().SequenceEqual(peer.GetAddressBytes());
#endif
            }

            private readonly string m_channelId;
            private readonly ByteString m_certificate;
            private readonly byte[]? m_peer;
            private readonly IUserIdentity? m_upstreamIdentity;
        }

        /// <summary>
        /// Returns a reservation exactly once while leaving the borrowed provider alive.
        /// </summary>
        private sealed class Lease(
            DefaultServerResourceIsolationProvider provider,
            OwnerState owner,
            ResourceIsolationStage stage,
            ResourceIsolationClass ownerClass,
            ResourceIsolationClass accountingClass,
            long amount,
            long reserved) : IDisposable
        {
            /// <summary>
            /// The owner whose counters include this reservation.
            /// </summary>
            public OwnerState Owner { get; } = owner;

            /// <summary>
            /// The resource pool charged by this lease.
            /// </summary>
            public ResourceIsolationStage Stage { get; } = stage;

            /// <summary>
            /// The classification used when the capacity was reserved.
            /// </summary>
            public ResourceIsolationClass Class { get; } = ownerClass;

            /// <summary>
            /// The owner-table class; byte-only continuity must not consume a reconnect identity reservation.
            /// </summary>
            public ResourceIsolationClass AccountingClass { get; } = accountingClass;

            /// <summary>
            /// Total capacity held by the lease.
            /// </summary>
            public long Amount { get; } = amount;

            /// <summary>
            /// Portion of the amount taken from a protected floor.
            /// </summary>
            public long Reserved { get; } = reserved;

            /// <summary>
            /// Releases accounting once, even when disposal races.
            /// </summary>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_provider, null)?.Release(this);
            }

            [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
                Justification = "The provider is borrowed; leases release accounting but must not dispose it. " +
                    "TODO: remove when the analyzer recognizes borrowed provider ownership.")]
            private DefaultServerResourceIsolationProvider? m_provider = provider;
        }

        /// <summary>
        /// Serializes owner-table changes: owner entries, exclusive classes and protected slots.
        /// </summary>
        private readonly Lock m_tableLock = new();
        private const int kStageCount = (int)ResourceIsolationStage.ParkedRequest + 1;
        private const int kClassCount = (int)ResourceIsolationClass.Control + 1;
        private readonly StageState[] m_stages = new StageState[kStageCount];
        private readonly ConcurrentDictionary<string, OwnerState> m_owners = new(StringComparer.Ordinal);
        private int m_ownerCount;
        private readonly ConditionalWeakTable<ResourceIsolationOwner, OwnerIdentity> m_classifications = new();
        private readonly ISessionBindingProvider? m_sessionBindings;
        private readonly IResourceIsolationClassifier? m_classifier;
        private readonly Meter m_meter;
        private readonly Counter<long> m_rejections;
        private readonly ILogger m_logger;
        private readonly ConditionalWeakTable<SessionBindingContext, DerivedKey> m_bindingKeys = new();
        private readonly ConditionalWeakTable<string, ClassificationCache> m_channelClassifications = new();
        private readonly int[] m_exclusiveOwners = new int[kClassCount];
        private readonly bool m_hasBootstrapReservation;
        private readonly bool m_hasReconnectReservation;
        private readonly bool m_hasControlReservation;
        private int m_disposed;
    }

    /// <summary>
    /// Source-generated lifecycle diagnostics for the resource-isolation provider.
    /// </summary>
    internal static partial class DefaultServerResourceIsolationProviderLog
    {
        /// <summary>
        /// Records initialization without logging caller identity, configuration values or credentials.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.ResourceIsolation, Level = LogLevel.Information,
            Message = "Server resource isolation initialized.")]
        public static partial void IsolationPolicyStarted(this ILogger logger);

        /// <summary>
        /// Records one effective resource envelope without exposing owner keys or credentials.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.ResourceIsolation + 1, Level = LogLevel.Information,
            Message = "Resource isolation {Stage}: capacity={Capacity}, shared={SharedCapacity}, " +
                "bootstrapReserved={BootstrapReserved}, reconnectReserved={ReconnectReserved}, " +
                "controlReserved={ControlReserved}, trustedReserved={TrustedReserved}, " +
                "ownerHardLimit={OwnerHardLimit}.")]
        public static partial void IsolationStageLimits(
            this ILogger logger,
            ResourceIsolationStage stage,
            long capacity,
            long sharedCapacity,
            long bootstrapReserved,
            long reconnectReserved,
            long controlReserved,
            long trustedReserved,
            long ownerHardLimit);
    }
}
