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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    public partial class EventManager
    {
        /// <summary>
        /// Gets whether the built-in publication pipeline can supply the optional
        /// server-wide admission guarantee. Custom subscription implementations
        /// which bypass that pipeline are not supported.
        /// </summary>
        public bool SupportsEventIdentityAdmission =>
            m_server.SubscriptionManager?.GetType() == typeof(SubscriptionManager) &&
            m_server.SubscriptionStore is not ISubscriptionRetransmissionStore;

        /// <summary>
        /// Gets the continuity status of native evidence recorded since server startup.
        /// Legacy native publication remains compatible until preparation or a reservation requests
        /// the guarantee. Earlier unidentifiable or conflicting native output makes
        /// that guarantee unavailable, rather than silently starting a new domain.
        /// </summary>
        public StatusCode EventIdentityAdmissionStatus
        {
            get
            {
                lock (m_identityLock)
                {
                    return m_identityContinuityStatus;
                }
            }
        }

        /// <summary>
        /// Requires strict native occurrence admission before a projection is published,
        /// without reserving an EventId or consuming identity capacity. Once required,
        /// admission remains strict for the server lifetime, including after the
        /// preparing generation is aborted or removed.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The server pipeline or its earlier native output cannot support continuous admission.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The event manager is disposed.</exception>
        public void RequireEventIdentityAdmission()
        {
            lock (m_identityLock)
            {
                ThrowIfIdentityDisposed();
                if (!SupportsEventIdentityAdmission)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The server publication pipeline cannot support server-wide EventId admission.");
                }
                RequireEventIdentityAdmissionCore();
            }
        }

        /// <summary>
        /// Reserves an EventId before publication. The reservation belongs to the
        /// producing generation, not to an action route or a subscription activation.
        /// Attach every published or retained representation before releasing it to
        /// native reporting. Disposing a reservation does not release queued snapshots.
        /// </summary>
        public EventIdentityReservation ReserveEventIdentity(ByteString eventId, IEventIdentitySource source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (eventId.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadEventIdUnknown);
            }
            lock (m_identityLock)
            {
                RequireEventIdentityAdmissionCore();
                IdentityClaim claim = ReserveIdentityCore(eventId, source, native: false);
                claim.Reservations++;
                return new EventIdentityReservation(this, claim);
            }
        }

        /// <summary>
        /// Admits a native event before fanout or queueing. Native publishers retain
        /// their original ids, including exact retransmission and retained Condition
        /// refresh; ReceiveTime and non-state properties do not create new occurrences.
        /// </summary>
        public void AdmitEvent(ISystemContext context, IFilterTarget occurrence)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (occurrence is null)
            {
                throw new ArgumentNullException(nameof(occurrence));
            }
            lock (m_identityLock)
            {
                ThrowIfIdentityDisposed();
                try
                {
                    IdentitySnapshot snapshot = CaptureIdentity(context, occurrence);
                    if (TryGetIdentityStamp(occurrence, snapshot, out IdentityStamp? stamp))
                    {
                        SetIdentityStamp(occurrence, stamp);
                        return;
                    }
                    IdentityClaim claim = ReserveIdentityCore(
                        snapshot.EventId, new NativeIdentitySource(snapshot), native: true);
                    SetIdentityStamp(occurrence, new IdentityStamp(claim, snapshot));
                }
                catch (ServiceResultException exception) when (!m_identityRequired)
                {
                    MarkIdentityContinuityLost(exception.StatusCode);
                }
            }
        }

        internal void TransferEventIdentity(IFilterTarget original, IFilterTarget snapshot)
        {
            lock (m_identityLock)
            {
                if (m_eventIdentityTargets.TryGetValue(original, out IdentityStamp? stamp))
                {
                    SetIdentityStamp(snapshot, stamp);
                }
            }
        }

        internal void AdmitEventFields(EventFieldList fields)
        {
            if (fields is null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            lock (m_identityLock)
            {
                ThrowIfIdentityDisposed();
                if (m_eventFieldIdentities.TryGetValue(fields, out EventFieldsStamp? stamp))
                {
                    if (!stamp.Fields.Equals(fields.EventFields))
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                            "Queued event fields changed after occurrence admission.");
                    }
                    return;
                }
                if (m_identityRequired)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "Preselected event fields must originate from admitted MonitoredItem event selection.");
                }
                MarkIdentityContinuityLost(StatusCodes.BadNotSupported);
            }
        }

        internal void RegisterEventFields(EventFieldList fields, IFilterTarget occurrence)
        {
            AdmitEvent(m_server.DefaultSystemContext, occurrence);
            lock (m_identityLock)
            {
                if (m_eventIdentityTargets.TryGetValue(occurrence, out IdentityStamp? stamp))
                {
                    m_eventFieldIdentities.Add(fields, new EventFieldsStamp(stamp, fields.EventFields));
                }
            }
        }

        internal void ReleaseEventFields(EventFieldList fields)
        {
            lock (m_identityLock)
            {
                if (m_eventFieldIdentities.Remove(fields))
                {
                    fields.Handle = null;
                }
            }
        }

        private void RequireEventIdentityAdmissionCore()
        {
            ThrowIfIdentityDisposed();
            if (StatusCode.IsBad(m_identityContinuityStatus))
            {
                throw new ServiceResultException(m_identityContinuityStatus,
                    "Earlier native publication cannot support server-wide EventId admission.");
            }
            m_identityRequired = true;
        }

        private void AttachIdentity(IdentityClaim claim, ISystemContext context, IFilterTarget occurrence)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (occurrence is null)
            {
                throw new ArgumentNullException(nameof(occurrence));
            }
            lock (m_identityLock)
            {
                ThrowIfIdentityDisposed();
                IdentitySnapshot snapshot = CaptureIdentity(context, occurrence);
                if (snapshot.EventId != claim.EventId)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                        "The published EventId differs from its reserved occurrence.");
                }
                claim.Attached = true;
                SetIdentityStamp(occurrence, new IdentityStamp(claim, snapshot));
            }
        }

        private void ReleaseIdentity(IdentityClaim claim)
        {
            lock (m_identityLock)
            {
                claim.Reservations--;
                if (claim.Reservations == 0 && !claim.Attached &&
                    m_eventIdentities.TryGetValue(claim.EventId, out IdentityEntry? entry) &&
                    entry.NativeOwner is null &&
                    entry.Claim.TryGetTarget(out IdentityClaim? current) && ReferenceEquals(current, claim))
                {
                    m_eventIdentities.Remove(claim.EventId);
                }
            }
        }

        private bool TryGetIdentityStamp(
            IFilterTarget occurrence, IdentitySnapshot snapshot, [NotNullWhen(true)] out IdentityStamp? stamp)
        {
            if (m_eventIdentityTargets.TryGetValue(occurrence, out stamp) && stamp.Snapshot.Equals(snapshot))
            {
                return true;
            }
            if (occurrence is InstanceStateSnapshot { Handle: IFilterTarget owner } &&
                m_eventIdentityTargets.TryGetValue(owner, out stamp) && stamp.Snapshot.Equals(snapshot))
            {
                return true;
            }
            stamp = null;
            return false;
        }

        private void SetIdentityStamp(IFilterTarget occurrence, IdentityStamp stamp)
        {
            m_eventIdentityTargets.Remove(occurrence);
            m_eventIdentityTargets.Add(occurrence, stamp);
        }

        private IdentityClaim ReserveIdentityCore(ByteString eventId, IEventIdentitySource source, bool native)
        {
            if (m_eventIdentities.TryGetValue(eventId, out IdentityEntry? entry))
            {
                if (entry.Claim.TryGetTarget(out IdentityClaim? existing))
                {
                    if (!existing.Source.IsSameOccurrence(source) || !source.IsSameOccurrence(existing.Source))
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                            "The EventId is already owned by a different server occurrence or source role.");
                    }
                    return existing;
                }
                m_eventIdentities.Remove(eventId);
            }
            if (m_eventIdentities.Count >= m_maxEventIdentities)
            {
                ByteString[] drained = m_eventIdentities
                    .Where(static pair => !pair.Value.Claim.TryGetTarget(out _))
                    .Select(static pair => pair.Key).ToArray();
                foreach (ByteString key in drained)
                {
                    m_eventIdentities.Remove(key);
                }
                if (m_eventIdentities.Count >= m_maxEventIdentities)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The server's owned event identity budget is exhausted; live evidence cannot be evicted.");
                }
            }
            var claim = new IdentityClaim(eventId, source);
            m_eventIdentities.Add(eventId, new IdentityEntry(claim, native));
            return claim;
        }

        private static IdentitySnapshot CaptureIdentity(ISystemContext context, IFilterTarget occurrence)
        {
            var filter = new FilterContext(
                context.NamespaceUris, context.TypeTable, context.PreferredLocales, context.Telemetry);
            Variant id = ReadIdentityField(occurrence, filter, Ua.BrowseNames.EventId);
            Variant time = ReadIdentityField(occurrence, filter, Ua.BrowseNames.Time);
            if (!id.TryGetValue(out ByteString eventId) || eventId.IsEmpty ||
                !time.TryGetValue(out DateTimeUtc occurrenceTime) || occurrenceTime.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "A native occurrence must expose its original EventId and Time for identity admission.");
            }
            var facts = new List<Variant>(32);
            foreach (string name in s_identityFields)
            {
                Variant value = ReadIdentityField(occurrence, filter, name);
                if (value.TryGetValue(out NodeId nodeId))
                {
                    if (nodeId.NamespaceIndex >= context.NamespaceUris.Count)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
                    }
                    value = NodeId.ToExpandedNodeId(nodeId, context.NamespaceUris);
                }
                facts.Add(value);
            }
            if (occurrence.IsTypeOf(filter, Ua.ObjectTypeIds.ConditionType))
            {
                Variant conditionId = occurrence.GetAttributeValue(
                    filter, NodeId.Null, [], Attributes.NodeId, default);
                if (conditionId.TryGetValue(out NodeId nodeId))
                {
                    conditionId = NodeId.ToExpandedNodeId(nodeId, context.NamespaceUris);
                }
                facts.Add(conditionId);
                foreach (ArrayOf<QualifiedName> path in s_conditionIdentityFields)
                {
                    Variant value = occurrence.GetAttributeValue(
                        filter, NodeId.Null, path, Attributes.Value, default);
                    if (value.TryGetValue(out NodeId branchId))
                    {
                        value = NodeId.ToExpandedNodeId(branchId, context.NamespaceUris);
                    }
                    facts.Add(value);
                }
            }
            return new IdentitySnapshot(eventId, facts.ToArrayOf());
        }

        private static Variant ReadIdentityField(IFilterTarget occurrence, IFilterContext context, string name)
        {
            return occurrence.GetAttributeValue(
                context, NodeId.Null, [QualifiedName.From(name)], Attributes.Value, default);
        }

        private void MarkIdentityContinuityLost(StatusCode reason)
        {
            if (StatusCode.IsGood(m_identityContinuityStatus))
            {
                m_identityContinuityStatus = StatusCodes.BadNotSupported;
                m_server.Telemetry.CreateLogger<EventManager>().EventIdentityContinuityUnavailable(reason);
            }
        }

        private void ThrowIfIdentityDisposed()
        {
            if (m_identityDisposed)
            {
                throw new ObjectDisposedException(nameof(EventManager));
            }
        }

        private void DisposeIdentityAdmission()
        {
            lock (m_identityLock)
            {
                m_identityDisposed = true;
                m_eventIdentities.Clear();
                m_eventIdentityTargets = new();
                m_eventFieldIdentities = new();
            }
        }

        /// <summary>
        /// A producing generation's reference to an admitted occurrence. Notification
        /// targets hold independent references through normal event and Publish queues.
        /// </summary>
        public sealed class EventIdentityReservation : IDisposable
        {
            internal EventIdentityReservation(EventManager manager, IdentityClaim claim)
            {
                m_manager = manager;
                m_claim = claim;
            }

            /// <summary>
            /// Attaches the reserved identity to an output or retained representation.
            /// Native reporting can then distinguish redistribution from a new native
            /// publisher claiming the same bytes.
            /// </summary>
            public void Attach(ISystemContext context, IFilterTarget occurrence)
            {
                IdentityClaim claim = Volatile.Read(ref m_claim) ??
                    throw new ObjectDisposedException(nameof(EventIdentityReservation));
                m_manager.AttachIdentity(claim, context, occurrence);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                IdentityClaim? claim = Interlocked.Exchange(ref m_claim, null);
                if (claim is not null)
                {
                    m_manager.ReleaseIdentity(claim);
                }
            }

            private readonly EventManager m_manager;
            private IdentityClaim? m_claim;
        }

        internal sealed record IdentityClaim(ByteString EventId, IEventIdentitySource Source)
        {
            public int Reservations { get; set; }
            public bool Attached { get; set; }
        }

        private sealed class IdentityEntry(IdentityClaim claim, bool native)
        {
            public WeakReference<IdentityClaim> Claim { get; } = new(claim);

            // Native APIs do not expose a producing-generation release boundary.
            public IdentityClaim? NativeOwner { get; } = native ? claim : null;
        }

        private sealed record IdentitySnapshot(ByteString EventId, ArrayOf<Variant> Facts);

        private sealed record IdentityStamp(IdentityClaim Claim, IdentitySnapshot Snapshot);

        private sealed record EventFieldsStamp(IdentityStamp Identity, ArrayOf<Variant> Fields);

        private sealed class NativeIdentitySource(IdentitySnapshot snapshot) : IEventIdentitySource
        {
            private IdentitySnapshot Snapshot { get; } = snapshot;

            public bool IsSameOccurrence(IEventIdentitySource other)
            {
                return other is NativeIdentitySource native && Snapshot.Equals(native.Snapshot);
            }
        }

        private readonly int m_maxEventIdentities;
        private readonly Lock m_identityLock = new();
        private readonly Dictionary<ByteString, IdentityEntry> m_eventIdentities = [];
        private ConditionalWeakTable<IFilterTarget, IdentityStamp> m_eventIdentityTargets = new();
        private ConditionalWeakTable<EventFieldList, EventFieldsStamp> m_eventFieldIdentities = new();
        private StatusCode m_identityContinuityStatus = StatusCodes.Good;
        private bool m_identityRequired;
        private bool m_identityDisposed;
        private static readonly ArrayOf<string> s_identityFields =
        [
            Ua.BrowseNames.EventType, Ua.BrowseNames.SourceNode, Ua.BrowseNames.SourceName,
            Ua.BrowseNames.Time, Ua.BrowseNames.Message, Ua.BrowseNames.Severity
        ];
        private static readonly ArrayOf<ArrayOf<QualifiedName>> s_conditionIdentityFields =
        [
            [QualifiedName.From(Ua.BrowseNames.BranchId)],
            [QualifiedName.From(Ua.BrowseNames.Retain)],
            [QualifiedName.From(Ua.BrowseNames.ConditionClassId)],
            [QualifiedName.From(Ua.BrowseNames.ConditionClassName)],
            [QualifiedName.From(Ua.BrowseNames.ConditionName)],
            [QualifiedName.From(Ua.BrowseNames.EnabledState), QualifiedName.From(Ua.BrowseNames.Id)],
            [QualifiedName.From(Ua.BrowseNames.Quality)],
            [QualifiedName.From(Ua.BrowseNames.LastSeverity)],
            [QualifiedName.From(Ua.BrowseNames.Comment)],
            [QualifiedName.From(Ua.BrowseNames.ClientUserId)],
            [QualifiedName.From(Ua.BrowseNames.AckedState), QualifiedName.From(Ua.BrowseNames.Id)],
            [QualifiedName.From(Ua.BrowseNames.ConfirmedState), QualifiedName.From(Ua.BrowseNames.Id)],
            [QualifiedName.From(Ua.BrowseNames.ActiveState), QualifiedName.From(Ua.BrowseNames.Id)],
            [QualifiedName.From(Ua.BrowseNames.SuppressedState), QualifiedName.From(Ua.BrowseNames.Id)],
            [QualifiedName.From(Ua.BrowseNames.SuppressedOrShelved)],
            [QualifiedName.From(Ua.BrowseNames.AudibleEnabled)]
        ];
    }

    /// <summary>
    /// Source-generated event admission diagnostics.
    /// </summary>
    internal static partial class EventManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.EventManager, Level = LogLevel.Warning,
            Message = "Native event identity continuity is unavailable ({Reason}); " +
                "server-wide occurrence admission cannot be enabled for this server lifetime.")]
        public static partial void EventIdentityContinuityUnavailable(this ILogger logger, StatusCode reason);
    }
}
