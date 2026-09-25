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
        public int TrackedOwnerCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_owners.Count;
                }
            }
        }

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
            else if (secure && channelContext.ClientChannelCertificate is { Length: > 0 } certificate)
            {
                key = "application:" + Hash(certificate);
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

            bool secure = original.Channel.SecurityMode is
                MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt &&
                original.Channel.SecurityPolicyUri != SecurityPolicies.None;
            SecureChannelContext verifiedContext = secure ? channelContext : new SecureChannelContext(
                channelContext.SecureChannelId, channelContext.EndpointDescription, channelContext.MessageEncoding,
                peerAddress: channelContext.PeerAddress)
            { UpstreamIdentity = channelContext.UpstreamIdentity };
            if (m_classifier?.TryClassify(verifiedContext, current, out ResourceIsolationIdentity identity) == true ||
                m_classifier?.TryClassifyIngress(
                    channelContext.PeerAddress == null ? null : new IPEndPoint(channelContext.PeerAddress, 0),
                    out identity) == true)
            {
                string key = identity.Class == ResourceIsolationClass.Trusted
                    ? GetTrustedOwnerKey(identity.Key) : "mapped:" + identity.Key;
                return identity.Class == owner.Class && key == owner.Key
                    ? StatusCodes.Good : StatusCodes.BadServerTooBusy;
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
            ResourceIsolationFailureReason reason;
            lock (m_lock)
            {
                ThrowIfDisposed();
                reason = AcquireUnderLock(stage, owner, amount, out lease);
            }
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
            lock (m_lock)
            {
                return m_stages[(int)stage].Used;
            }
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
            lock (m_lock)
            {
                for (int ii = 0; ii < values.Length; ii++)
                {
                    values[ii] = new Measurement<long>(
                        m_stages[ii].Used,
                        new KeyValuePair<string, object?>("stage", ((ResourceIsolationStage)ii).ToString()));
                }
            }
            return values;
        }

        private ResourceIsolationFailureReason AcquireUnderLock(
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
            m_owners.TryGetValue(owner.Key, out OwnerState? accounting);
            long ownerUsed = accounting?.Usage[index] ?? 0;
            if (amount > owner.GetHardLimit(stage) - ownerUsed)
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
            long reservedUsed = owner.Class switch
            {
                ResourceIsolationClass.Bootstrap => state.BootstrapUsed,
                ResourceIsolationClass.Reconnect => state.ReconnectUsed,
                ResourceIsolationClass.Control => state.ControlUsed,
                ResourceIsolationClass.Trusted => accounting?.TrustedUsage[index] ?? 0,
                _ => 0
            };
            long fromReserved = Math.Min(amount, floor - reservedUsed);
            long fromShared = amount - fromReserved;
            if (amount > state.Plan.Capacity - state.Used ||
                fromShared > state.Plan.SharedCapacity - state.SharedUsed)
            {
                return ResourceIsolationFailureReason.Capacity;
            }
            ResourceIsolationClass? nextExclusiveClass =
                accounting == null || accounting.ExclusiveClass == owner.Class ? owner.Class : null;
            // A mixed-class key cannot fill a protected table slot: its ordinary leases
            // may outlive every protected lease without releasing the entry.
            int protectedSlots = GetReservedOwnerSlots(accounting?.ExclusiveClass, nextExclusiveClass);
            long projectedOwnerCount = (long)m_owners.Count + (accounting == null ? 1 : 0);
            if (projectedOwnerCount > Plan.MaxTrackedOwners - protectedSlots)
            {
                return ResourceIsolationFailureReason.OwnerTableFull;
            }
            if (accounting == null)
            {
                accounting = new OwnerState(owner.Key);
                m_owners.Add(owner.Key, accounting);
            }
            var acquired = new Lease(this, accounting, stage, owner.Class, amount, fromReserved);
            accounting.Usage[index] += amount;
            accounting.ActiveLeases++;
            UpdateOwnerClasses(accounting, owner.Class, 1);
            state.Used += amount;
            state.SharedUsed += fromShared;
            switch (owner.Class)
            {
                case ResourceIsolationClass.Bootstrap:
                    state.BootstrapUsed += fromReserved;
                    break;
                case ResourceIsolationClass.Reconnect:
                    state.ReconnectUsed += fromReserved;
                    break;
                case ResourceIsolationClass.Trusted:
                    accounting.TrustedUsage[index] += fromReserved;
                    break;
                case ResourceIsolationClass.Control:
                    state.ControlUsed += fromReserved;
                    break;
            }
            lease = acquired;
            return ResourceIsolationFailureReason.None;
        }

        private void Release(Lease lease)
        {
            lock (m_lock)
            {
                int index = (int)lease.Stage;
                StageState state = m_stages[index];
                OwnerState owner = lease.Owner;
                owner.Usage[index] -= lease.Amount;
                state.Used -= lease.Amount;
                state.SharedUsed -= lease.Amount - lease.Reserved;
                switch (lease.Class)
                {
                    case ResourceIsolationClass.Bootstrap:
                        state.BootstrapUsed -= lease.Reserved;
                        break;
                    case ResourceIsolationClass.Reconnect:
                        state.ReconnectUsed -= lease.Reserved;
                        break;
                    case ResourceIsolationClass.Trusted:
                        owner.TrustedUsage[index] -= lease.Reserved;
                        break;
                    case ResourceIsolationClass.Control:
                        state.ControlUsed -= lease.Reserved;
                        break;
                }
                UpdateOwnerClasses(owner, lease.Class, -1);
                if (--owner.ActiveLeases == 0)
                {
                    m_owners.Remove(owner.Key);
                }
            }
            CapacityAvailable?.Invoke(lease.Stage);
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

        private void UpdateOwnerClasses(OwnerState owner, ResourceIsolationClass ownerClass, int change)
        {
            if (owner.ExclusiveClass is { } previous)
            {
                m_exclusiveOwners[(int)previous]--;
            }
            owner.ClassLeases[(int)ownerClass] += change;
            ResourceIsolationClass? exclusive = null;
            for (int ii = 0; ii < owner.ClassLeases.Length; ii++)
            {
                if (owner.ClassLeases[ii] == 0)
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
            public long Used { get; set; }

            /// <summary>
            /// Leased capacity drawn from the unreserved pool.
            /// </summary>
            public long SharedUsed { get; set; }

            /// <summary>
            /// Leased bootstrap floor.
            /// </summary>
            public long BootstrapUsed { get; set; }

            /// <summary>
            /// Leased reconnect floor.
            /// </summary>
            public long ReconnectUsed { get; set; }

            /// <summary>
            /// Leased control-work floor.
            /// </summary>
            public long ControlUsed { get; set; }
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
            /// The sole active class, or null when leases span classes.
            /// </summary>
            public ResourceIsolationClass? ExclusiveClass { get; set; }

            /// <summary>
            /// Counts outstanding leases by class for protected tracking-table capacity.
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

            /// <summary>
            /// Keeps the owner entry alive until its last reservation is released.
            /// </summary>
            public long ActiveLeases { get; set; }
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
        /// Keeps a fixed number of classifications per live channel-id object, without a permanent identity dictionary.
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
                lock (m_lock)
                {
                    Entry? entry = m_entries[onlyStage.HasValue ? kClassCount : (int)ownerClass];
                    if (entry != null && entry.Owner.Class == ownerClass &&
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
                lock (m_lock)
                {
                    if (m_snapshot == null || !m_snapshot.Matches(context))
                    {
                        m_snapshot = new ChannelSnapshot(context);
                    }
                    return m_snapshot;
                }
            }

            /// <summary>
            /// Replaces one fixed slot; older classifications remain valid only while their leases retain them.
            /// </summary>
            public void Set(
                ResourceIsolationOwner owner, SessionBindingContext? binding, ChannelSnapshot snapshot,
                ResourceIsolationStage? stage, bool mapped)
            {
                lock (m_lock)
                {
                    m_entries[stage.HasValue ? kClassCount : (int)owner.Class] =
                        new Entry(owner, binding, snapshot, stage, mapped);
                }
            }

            /// <summary>
            /// Records the validated mapping inputs associated with one issued owner.
            /// </summary>
            private sealed record Entry(
                ResourceIsolationOwner Owner, SessionBindingContext? Binding, ChannelSnapshot Snapshot,
                ResourceIsolationStage? Stage, bool Mapped);

            private readonly Lock m_lock = new();
            private readonly Entry?[] m_entries = new Entry?[kClassCount + 1];
            private ChannelSnapshot? m_snapshot;
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
                m_certificate = SecurityPolicyUri != SecurityPolicies.None
                    ? context.ClientChannelCertificate?.AsSpan().ToArray() : null;
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
                byte[]? peer = context.PeerAddress?.GetAddressBytes();
                return m_channelId == context.SecureChannelId &&
                    SecurityPolicyUri == context.EndpointDescription?.SecurityPolicyUri &&
                    SecurityMode == (context.EndpointDescription?.SecurityMode ?? MessageSecurityMode.Invalid) &&
                    (SecurityPolicyUri == SecurityPolicies.None ||
                        m_certificate.AsSpan().SequenceEqual(context.ClientChannelCertificate.AsSpan())) &&
                    m_peer.AsSpan().SequenceEqual(peer.AsSpan()) &&
                    ReferenceEquals(m_upstreamIdentity, context.UpstreamIdentity);
            }

            private readonly string m_channelId;
            private readonly byte[]? m_certificate;
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

        private readonly Lock m_lock = new();
        private const int kStageCount = (int)ResourceIsolationStage.ParkedRequest + 1;
        private const int kClassCount = (int)ResourceIsolationClass.Control + 1;
        private readonly StageState[] m_stages = new StageState[kStageCount];
        private readonly Dictionary<string, OwnerState> m_owners = new(StringComparer.Ordinal);
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
    }
}
