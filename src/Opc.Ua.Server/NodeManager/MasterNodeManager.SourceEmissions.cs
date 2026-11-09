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
using System.Threading;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Determines whether a source generation may currently emit notifications.
        /// </summary>
        /// <param name="nodeManager">The source node manager.</param>
        /// <returns><c>true</c> when the source generation is live.</returns>
        internal bool IsSourceEmissionAllowed(IAsyncNodeManager? nodeManager)
        {
            if (nodeManager is null)
            {
                return false;
            }
            NodeManagerRoutingTable.RoutingSnapshot snapshot = m_nodeManagers.Revision;
            return TryResolveSource(
                    snapshot, nodeManager, null, out NodeManagerGeneration? generation, out bool legacyFallback) &&
                generation.Gate.State == NodeManagerGenerationState.Live &&
                (!legacyFallback ||
                    m_nodeManagers.IsLegacySourceAllowed(snapshot, GetReferenceOwnerIdentity(nodeManager)));
        }

        /// <summary>
        /// Admits an item source through its exact generation without consulting execution-context state.
        /// </summary>
        /// <param name="server">The server owning the generation.</param>
        /// <param name="nodeManager">The source node manager.</param>
        /// <param name="generationReference">The non-owning source generation identity, when known.</param>
        /// <param name="emission">The admission owned by the caller until delivery completes.</param>
        /// <returns>Whether the source accepted the operation.</returns>
        internal static bool TryCaptureItemEmission(
            IServerInternal server,
            IAsyncNodeManager? nodeManager,
            NodeManagerGenerationReference? generationReference,
            out SourceEmissionAdmission? emission)
        {
            emission = null;
            if (nodeManager is null)
            {
                return false;
            }
            if (server.NodeManager is not MasterNodeManager master)
            {
                return true;
            }

            NodeManagerGeneration? boundGeneration = null;
            if (generationReference is not null)
            {
                if (!generationReference.TryGetGeneration(out boundGeneration) ||
                    boundGeneration is null)
                {
                    return false;
                }
            }
            return TryAdmitSource(
                master, nodeManager, boundGeneration, false, out emission);
        }

        /// <summary>
        /// Enters the source generation gate for one notification emission.
        /// </summary>
        /// <param name="server">The server internal.</param>
        /// <param name="nodeManager">The source node manager.</param>
        /// <param name="emission">The generation gate admission, when admitted.</param>
        /// <param name="generationReference">The exact bound generation for an item emission, if known.</param>
        /// <param name="isGlobalServerEventSink">
        /// Whether this is the Server object's event callback, which receives events from every node manager.
        /// </param>
        /// <param name="context">The explicit event propagation context, when present.</param>
        /// <returns><c>true</c> when the generation accepted the emission.</returns>
        internal static bool TryCaptureSourceEmission(
            IServerInternal server,
            IAsyncNodeManager? nodeManager,
            out SourceEmissionAdmission? emission,
            NodeManagerGenerationReference? generationReference = null,
            bool isGlobalServerEventSink = false,
            ISystemContext? context = null)
        {
            emission = null;
            if (nodeManager is null)
            {
                return false;
            }
            if (server.NodeManager is not MasterNodeManager master)
            {
                return true;
            }

            SourceEmissionOrigin? origin = context switch
            {
                ServerSystemContext serverContext => serverContext.SourceEmissionOrigin,
                SourceEmissionContext sourceContext => sourceContext.Origin,
                _ => null
            };
            if (origin is not null)
            {
                return origin.TryCapture(
                    master, nodeManager, generationReference, isGlobalServerEventSink, out emission);
            }

            NodeManagerGeneration? boundGeneration = null;
            if (generationReference is not null)
            {
                if (!generationReference.TryGetGeneration(out boundGeneration) ||
                    boundGeneration is null)
                {
                    return false;
                }
            }
            return TryAdmitSource(
                master, nodeManager, boundGeneration, true, out emission);
        }

        /// <summary>
        /// Pins the routing view and admits a source against that same view.
        /// </summary>
        private static bool TryAdmitSource(
            MasterNodeManager master,
            IAsyncNodeManager nodeManager,
            NodeManagerGeneration? boundGeneration,
            bool trackReadinessEvents,
            out SourceEmissionAdmission? emission)
        {
            emission = null;
            while (true)
            {
                (NodeManagerRoutingTable.RoutingSnapshot snapshot, AddressSpaceEpoch viewEpoch, int viewSlot) =
                    master.m_nodeManagers.PinCurrent();
                try
                {
                    if (!TryResolveSource(
                            snapshot,
                            nodeManager,
                            boundGeneration,
                            out NodeManagerGeneration? generation,
                            out bool legacyFallback) ||
                        !generation.Gate.TryEnter(out int generationSlot))
                    {
                        if (ReferenceEquals(snapshot, master.m_nodeManagers.Revision))
                        {
                            return false;
                        }
                        continue;
                    }
                    AddressSpaceEpoch? sourceEpoch = null;
                    int sourceSlot = -1;
                    NodeManagerGeneration? issuer = null;
                    int issuerSlot = -1;
                    bool transferred = false;
                    try
                    {
                        IAsyncNodeManager resource = GetReferenceOwnerIdentity(nodeManager);
                        issuer = boundGeneration ?? generation;
                        if ((boundGeneration is null || ReferenceEquals(boundGeneration, generation)) &&
                            (ReferenceEquals(nodeManager, resource) || nodeManager is AsyncNodeManagerAdapter) &&
                            !ReferenceEquals(generation.NodeManager, resource))
                        {
                            issuer = generation.PrivateSourceGeneration ??
                                generation.ForwardedSourceGeneration ??
                                NodeManagerGenerationRegistry.GetOrCreate(resource);
                        }
                        if (!ReferenceEquals(issuer, generation))
                        {
                            if (!IsPrivateForwardedSource(generation, issuer, nodeManager) &&
                                !IsPrivateFacadeSource(generation, issuer, nodeManager) &&
                                !IsLegacyForwardedSource(generation, issuer, nodeManager))
                            {
                                return false;
                            }
                            if (!ReferenceEquals(issuer.Gate, generation.Gate) &&
                                !issuer.Gate.TryEnter(out issuerSlot))
                            {
                                return false;
                            }
                        }
                        NodeManagerRoutingTable.SourceEpochAdmission sourceAdmission =
                            master.m_nodeManagers.TryPinSourceEpoch(
                                snapshot, resource, legacyFallback, out sourceEpoch, out sourceSlot);
                        if (sourceAdmission == NodeManagerRoutingTable.SourceEpochAdmission.StaleView)
                        {
                            continue;
                        }
                        if (sourceAdmission == NodeManagerRoutingTable.SourceEpochAdmission.RetiredSource)
                        {
                            return false;
                        }
                        if (!IsPrivateFacadeSource(generation, issuer, nodeManager) &&
                            !generation.TryRetainForwardedSource(issuer))
                        {
                            return false;
                        }
                        emission = new SourceEmissionAdmission(
                            master,
                            generation,
                            generationSlot,
                            sourceEpoch,
                            sourceSlot,
                            trackReadinessEvents,
                            issuer,
                            issuerSlot);
                        transferred = true;
                        return true;
                    }
                    finally
                    {
                        if (!transferred)
                        {
                            generation.Gate.Exit(generationSlot);
                            if (issuerSlot >= 0)
                            {
                                issuer!.Gate.Exit(issuerSlot);
                            }
                            sourceEpoch?.Exit(sourceSlot);
                        }
                    }
                }
                finally
                {
                    viewEpoch.Exit(viewSlot);
                }
            }
        }

        /// <summary>
        /// Resolves a source against one captured routing view.
        /// </summary>
        /// <remarks>
        /// A registered emitter must be visible and route its bound generation, or the private source
        /// generation retained by that facade. A raw resource needs exactly one visible facade. An exact
        /// bound owner can emit outside routing only while no facade is registered for its resource.
        /// </remarks>
        private static bool TryResolveSource(
            NodeManagerRoutingTable.RoutingSnapshot routing,
            IAsyncNodeManager nodeManager,
            NodeManagerGeneration? boundGeneration,
            [NotNullWhen(true)] out NodeManagerGeneration? generation,
            out bool legacyFallback)
        {
            generation = null;
            legacyFallback = false;
            if (boundGeneration is not null && !IsCanonicalBoundSource(boundGeneration, nodeManager))
            {
                return false;
            }
            for (int ii = 0; ii < routing.NodeManagers.Length; ii++)
            {
                if (ReferenceEquals(routing.NodeManagers[ii], nodeManager))
                {
                    NodeManagerGeneration exact = routing.Generations[ii];
                    if (IsHidden(routing, nodeManager) ||
                        (boundGeneration is not null &&
                            !ReferenceEquals(exact, boundGeneration) &&
                            !IsPrivateFacadeSource(exact, boundGeneration, nodeManager)))
                    {
                        return false;
                    }
                    if (ReferenceEquals(GetReferenceOwnerIdentity(nodeManager), nodeManager))
                    {
                        for (int jj = 0; jj < routing.NodeManagers.Length; jj++)
                        {
                            IAsyncNodeManager other = routing.NodeManagers[jj];
                            if (jj != ii && !IsHidden(routing, other) &&
                                ReferenceEquals(GetReferenceOwnerIdentity(other), nodeManager))
                            {
                                return false;
                            }
                        }
                    }
                    generation = exact;
                    return true;
                }
            }

            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(nodeManager);
            bool rawResource = ReferenceEquals(resource, nodeManager);
            if (!rawResource && nodeManager is not AsyncNodeManagerAdapter)
            {
                return false;
            }
            NodeManagerGeneration? routed = null;
            bool routedAllowed = false;
            int visibleRoutes = 0;
            bool registeredResource = false;
            for (int ii = 0; ii < routing.NodeManagers.Length; ii++)
            {
                IAsyncNodeManager facade = routing.NodeManagers[ii];
                NodeManagerGeneration candidate = routing.Generations[ii];
                NodeManagerGeneration? privateSource = boundGeneration ?? candidate.PrivateSourceGeneration;
                bool privateAlias = privateSource is not null &&
                    IsPrivateForwardedSource(candidate, privateSource, nodeManager);
                bool identityAlias = ReferenceEquals(NodeManagerGenerationRegistry.GetIdentity(facade), resource);
                bool legacyAlias = boundGeneration is not null &&
                    IsLegacyForwardedSource(candidate, boundGeneration, nodeManager);
                if (!identityAlias && !privateAlias &&
                    !ReferenceEquals(GetReferenceOwnerIdentity(facade), resource))
                {
                    continue;
                }
                registeredResource = true;
                if (IsHidden(routing, facade))
                {
                    continue;
                }
                if (!rawResource &&
                    ((boundGeneration is null && (identityAlias || privateAlias)) ||
                        ReferenceEquals(candidate, boundGeneration) || privateAlias))
                {
                    generation = candidate;
                    return true;
                }
                visibleRoutes++;
                routed = candidate;
                routedAllowed = boundGeneration is null
                    ? true
                    : ReferenceEquals(candidate, boundGeneration) || privateAlias || legacyAlias;
            }
            if (visibleRoutes > 0)
            {
                if (visibleRoutes != 1 || routed is null || !routedAllowed)
                {
                    return false;
                }
                generation = routed;
                return true;
            }
            if (!registeredResource &&
                boundGeneration is not null &&
                boundGeneration.AllowsLegacySource &&
                ReferenceEquals(boundGeneration.NodeManager, nodeManager))
            {
                generation = boundGeneration;
                legacyFallback = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Matches the current live raw issuer of an explicitly forwarding facade.
        /// </summary>
        private static bool IsLegacyForwardedSource(
            NodeManagerGeneration facade,
            NodeManagerGeneration source,
            IAsyncNodeManager nodeManager)
        {
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(nodeManager);
            return source.AllowsLegacySource &&
                source.Gate.State == NodeManagerGenerationState.Live &&
                ReferenceEquals(source.NodeManager, resource) &&
                (ReferenceEquals(resource, nodeManager) || nodeManager is AsyncNodeManagerAdapter) &&
                ReferenceEquals(GetReferenceOwnerIdentity(facade.NodeManager), resource) &&
                NodeManagerGenerationRegistry.TryGet(resource, out NodeManagerGeneration? current) &&
                ReferenceEquals(source, current);
        }

        /// <summary>
        /// Matches the exact raw generation retained by a privately prepared forwarding facade.
        /// </summary>
        private static bool IsPrivateForwardedSource(
            NodeManagerGeneration facade,
            NodeManagerGeneration boundGeneration,
            IAsyncNodeManager nodeManager)
        {
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(nodeManager);
            return ReferenceEquals(facade.PrivateSourceGeneration, boundGeneration) &&
                ReferenceEquals(boundGeneration.NodeManager, resource) &&
                (ReferenceEquals(resource, nodeManager) || nodeManager is AsyncNodeManagerAdapter) &&
                ReferenceEquals(GetReferenceOwnerIdentity(facade.NodeManager), resource);
        }

        /// <summary>
        /// Matches the exact facade binding retained before its replacement slot was assigned.
        /// </summary>
        private static bool IsPrivateFacadeSource(
            NodeManagerGeneration candidate,
            NodeManagerGeneration boundGeneration,
            IAsyncNodeManager nodeManager)
        {
            return ReferenceEquals(candidate.PrivateFacadeGeneration, boundGeneration) &&
                ReferenceEquals(candidate.NodeManager, nodeManager) &&
                ReferenceEquals(boundGeneration.NodeManager, nodeManager) &&
                ReferenceEquals(candidate.Gate, boundGeneration.Gate);
        }

        /// <summary>
        /// Matches the exact owner, a stack adapter alias of the owner's resource, or that raw resource.
        /// </summary>
        /// <remarks>
        /// A distinct custom facade that is neither the resource nor a stack adapter is not accepted
        /// unless it owns the generation.
        /// </remarks>
        private static bool IsCanonicalBoundSource(NodeManagerGeneration generation, IAsyncNodeManager nodeManager)
        {
            if (ReferenceEquals(generation.NodeManager, nodeManager))
            {
                return true;
            }
            if (generation.PrivateSourceGeneration is { } privateSource &&
                IsPrivateForwardedSource(generation, privateSource, nodeManager))
            {
                return true;
            }
            if (generation.ForwardedSourceGeneration is { } forwarded &&
                ReferenceEquals(forwarded.NodeManager, nodeManager) &&
                forwarded.Gate.State == NodeManagerGenerationState.Live &&
                NodeManagerGenerationRegistry.TryGet(nodeManager, out NodeManagerGeneration? current) &&
                ReferenceEquals(forwarded, current))
            {
                return true;
            }
            IAsyncNodeManager resource = NodeManagerGenerationRegistry.GetIdentity(nodeManager);
            if (!ReferenceEquals(NodeManagerGenerationRegistry.GetIdentity(generation.NodeManager), resource))
            {
                return false;
            }
            return ReferenceEquals(resource, nodeManager) ||
                nodeManager is AsyncNodeManagerAdapter;
        }

        /// <summary>
        /// Checks whether a registered facade is unreachable by Clients in a routing view.
        /// </summary>
        private static bool IsHidden(NodeManagerRoutingTable.RoutingSnapshot routing, IAsyncNodeManager facade)
        {
            foreach (IAsyncNodeManager hiddenManager in routing.HiddenNodeManagers)
            {
                if (ReferenceEquals(hiddenManager, facade))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Admits creation of a custom source while its generation remains live.
        /// </summary>
        /// <param name="server">The server internal.</param>
        /// <param name="nodeManager">The source node manager.</param>
        /// <param name="admission">The generation lease, or null when no master gate applies.</param>
        /// <returns><c>true</c> when creation was admitted.</returns>
        internal static bool TryBeginCustomSourceCreation(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            out SourceEmissionAdmission? admission)
        {
            admission = null;
            if (server.NodeManager is not MasterNodeManager master)
            {
                return true;
            }

            return TryAdmitSource(master, nodeManager, null, false, out admission);
        }

        /// <summary>
        /// Owns one source-emission generation gate admission without allocating a dispatch lease.
        /// This value has single-owner semantics and must be disposed exactly once.
        /// </summary>
        internal readonly struct SourceEmissionAdmission : IDisposable
        {
            /// <summary>
            /// Initializes an admitted source emission.
            /// </summary>
            internal SourceEmissionAdmission(
                MasterNodeManager owner,
                NodeManagerGeneration generation,
                int slot,
                AddressSpaceEpoch? epoch = null,
                int epochSlot = -1,
                bool trackReadinessEvents = true,
                NodeManagerGeneration? issuer = null,
                int issuerSlot = -1)
            {
                m_owner = owner;
                Generation = generation;
                IssuerGeneration = issuer ?? generation;
                m_slot = slot;
                m_issuerSlot = issuerSlot;
                m_epoch = epoch;
                m_epochSlot = epochSlot;
                if (trackReadinessEvents)
                {
                    ReadinessEventId = generation.IsDeferringEvents || IssuerGeneration.IsDeferringEvents
                        ? NextReadinessEventId()
                        : 0;
                    m_tracksReadiness = generation.CaptureSourceEmission(
                        ReadinessEventId, out bool readinessPending);
                    bool issuerPending = false;
                    m_tracksIssuerReadiness = !ReferenceEquals(IssuerGeneration, generation) &&
                        IssuerGeneration.CaptureSourceEmission(ReadinessEventId, out issuerPending);
                    ReadinessPending = readinessPending || issuerPending;
                }
                else
                {
                    ReadinessEventId = 0;
                    m_tracksReadiness = false;
                    m_tracksIssuerReadiness = false;
                    ReadinessPending = generation.ReadinessPending || IssuerGeneration.ReadinessPending;
                }
            }

            /// <summary>
            /// Initializes a child admission propagated from an existing source event.
            /// </summary>
            internal SourceEmissionAdmission(
                MasterNodeManager owner,
                NodeManagerGeneration generation,
                int slot,
                long readinessEventId,
                bool readinessPending,
                bool tracksReadiness,
                NodeManagerGeneration issuer,
                int issuerSlot,
                bool tracksIssuerReadiness,
                AddressSpaceEpoch? epoch,
                int epochSlot)
            {
                m_owner = owner;
                Generation = generation;
                IssuerGeneration = issuer;
                m_slot = slot;
                m_issuerSlot = issuerSlot;
                m_epoch = epoch;
                m_epochSlot = epochSlot;
                ReadinessEventId = readinessEventId == 0 &&
                    (tracksReadiness || tracksIssuerReadiness ||
                        generation.IsDeferringEvents || issuer.IsDeferringEvents)
                        ? NextReadinessEventId()
                        : readinessEventId;
                bool tracksGeneration = generation.CaptureSourceEmission(
                    ReadinessEventId, out bool generationReadinessPending);
                if (tracksReadiness && !tracksGeneration)
                {
                    generation.TrackSourceEmission(ReadinessEventId);
                }
                bool issuerReadinessPending = false;
                bool tracksIssuer = !ReferenceEquals(issuer, generation) &&
                    issuer.CaptureSourceEmission(ReadinessEventId, out issuerReadinessPending);
                if (tracksIssuerReadiness && !tracksIssuer)
                {
                    issuer.TrackSourceEmission(ReadinessEventId);
                }
                ReadinessPending = readinessPending || generationReadinessPending || issuerReadinessPending;
                m_tracksReadiness = tracksReadiness || tracksGeneration;
                m_tracksIssuerReadiness = tracksIssuerReadiness || tracksIssuer;
            }

            /// <summary>
            /// Gets the exact generation admitted by the source emission.
            /// </summary>
            internal NodeManagerGeneration Generation { get; }

            /// <summary>
            /// Gets the exact source generation that issued this admission.
            /// </summary>
            internal NodeManagerGeneration IssuerGeneration { get; }

            /// <summary>
            /// Gets whether the generation was still initializing when the emission was admitted.
            /// Delivery happens later, so the state is captured here and not re-read.
            /// </summary>
            internal bool ReadinessPending { get; }

            /// <summary>
            /// Gets the identity shared by every delivery propagated from this event.
            /// </summary>
            internal long ReadinessEventId { get; }

            /// <summary>
            /// Creates an identity used only while a generation retains readiness-time events.
            /// </summary>
            /// <returns>A unique source-event identity.</returns>
            internal static long NextReadinessEventId()
            {
                return Interlocked.Increment(ref s_nextReadinessEventId);
            }

            /// <summary>
            /// Transfers this single-owned admission to an explicitly propagated event origin.
            /// </summary>
            /// <returns>The new owner; the original admission must not be disposed afterward.</returns>
            internal SourceEmissionOrigin TransferToOrigin()
            {
                return new SourceEmissionOrigin(
                    m_owner,
                    Generation,
                    m_slot,
                    ReadinessEventId,
                    ReadinessPending,
                    m_tracksReadiness,
                    IssuerGeneration,
                    m_issuerSlot,
                    m_tracksIssuerReadiness,
                    m_epoch,
                    m_epochSlot);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (m_tracksReadiness)
                {
                    Generation.ReleaseSourceEmission(ReadinessEventId);
                }
                if (m_tracksIssuerReadiness)
                {
                    IssuerGeneration.ReleaseSourceEmission(ReadinessEventId);
                }
                Generation.Gate.Exit(m_slot);
                if (m_issuerSlot >= 0)
                {
                    IssuerGeneration.Gate.Exit(m_issuerSlot);
                }
                m_epoch?.Exit(m_epochSlot);
            }

            /// <summary>The process-wide event identity sequence.</summary>
            private static long s_nextReadinessEventId;

            private readonly MasterNodeManager m_owner;
            private readonly int m_slot;
            private readonly int m_issuerSlot;
            private readonly AddressSpaceEpoch? m_epoch;
            private readonly int m_epochSlot;
            private readonly bool m_tracksReadiness;
            private readonly bool m_tracksIssuerReadiness;
        }

        /// <summary>
        /// Owns a source callback's admission and revokes new fan-out when that callback finishes.
        /// </summary>
        internal sealed class SourceEmissionOrigin : IDisposable
        {
            /// <summary>
            /// Takes ownership of one already admitted striped gate slot.
            /// </summary>
            /// <param name="owner">The routing owner that admitted the source.</param>
            /// <param name="generation">The exact source generation.</param>
            /// <param name="slot">The admitted counter slot transferred by the caller.</param>
            /// <param name="readinessEventId">The source event identity.</param>
            /// <param name="readinessPending">Whether readiness was pending at source admission.</param>
            /// <param name="tracksReadiness">Whether this origin owns a readiness-drain admission.</param>
            /// <param name="issuer">The exact source that issued the admission.</param>
            /// <param name="issuerSlot">The source gate slot, or -1 when it shares the admitted gate.</param>
            /// <param name="tracksIssuerReadiness">Whether the source owns a readiness-drain admission.</param>
            /// <param name="epoch">The resource source epoch pinned by the source, when applicable.</param>
            /// <param name="epochSlot">The counter slot for the resource source epoch.</param>
            internal SourceEmissionOrigin(
                MasterNodeManager owner,
                NodeManagerGeneration generation,
                int slot,
                long readinessEventId,
                bool readinessPending,
                bool tracksReadiness,
                NodeManagerGeneration issuer,
                int issuerSlot,
                bool tracksIssuerReadiness,
                AddressSpaceEpoch? epoch,
                int epochSlot)
            {
                m_owner = owner;
                m_generation = generation;
                m_issuer = issuer;
                m_lease = new GenerationGate.Lease(generation.Gate, slot);
                m_issuerLease = issuerSlot < 0 ? null : new GenerationGate.Lease(issuer.Gate, issuerSlot);
                m_readinessEventId = readinessEventId;
                m_readinessPending = readinessPending;
                m_tracksReadiness = tracksReadiness;
                m_tracksIssuerReadiness = tracksIssuerReadiness;
                m_epochLease = epoch is null ? null : new AddressSpaceEpoch.Lease(epoch, epochSlot);
            }

            /// <summary>
            /// Gets whether the originating callback still accepts child emissions.
            /// </summary>
            internal bool IsActive => m_lease.IsActive;

            /// <summary>
            /// Admits one refresh target without retaining a request context or an ambient source.
            /// </summary>
            /// <param name="item">The original target item.</param>
            /// <param name="emission">The child admission owned by the queue call.</param>
            /// <returns>Whether this source still admits the target.</returns>
            internal bool TryCaptureForItem(IEventMonitoredItem item, out SourceEmissionAdmission? emission)
            {
                emission = null;
                MasterNodeManager? owner = Volatile.Read(ref m_owner);
                IAsyncNodeManager? nodeManager = item.NodeManager;
                return owner is not null && nodeManager is not null &&
                    TryCapture(
                        owner, nodeManager, (item as MonitoredItem)?.Generation,
                        item.MonitoringAllEvents, out emission);
            }

            /// <summary>
            /// Copies event provenance without mutating the producer's shared context.
            /// </summary>
            /// <param name="context">The original event context.</param>
            /// <returns>A context carrying this revocable origin.</returns>
            internal ISystemContext CreateContext(ISystemContext context)
            {
                if (context is ServerSystemContext serverContext)
                {
                    ServerSystemContext copy = serverContext.Copy();
                    copy.SourceEmissionOrigin = this;
                    return copy;
                }
                return new SourceEmissionContext(context, this);
            }

            /// <summary>
            /// Gives a fan-out consumer its own gate slot only while this origin is active.
            /// </summary>
            /// <param name="owner">The receiving server's routing owner.</param>
            /// <param name="nodeManager">The receiving notifier's manager.</param>
            /// <param name="reference">The notifier's exact generation, when bound.</param>
            /// <param name="globalSink">Whether this notifier receives all server events.</param>
            /// <param name="emission">The child admission owned by the consumer.</param>
            /// <returns>Whether this origin admitted the consumer.</returns>
            internal bool TryCapture(
                MasterNodeManager owner,
                IAsyncNodeManager nodeManager,
                NodeManagerGenerationReference? reference,
                bool globalSink,
                out SourceEmissionAdmission? emission)
            {
                emission = null;
                NodeManagerGeneration? generation = Volatile.Read(ref m_generation);
                NodeManagerGeneration? issuer = Volatile.Read(ref m_issuer);
                if (generation is null ||
                    issuer is null ||
                    !ReferenceEquals(Volatile.Read(ref m_owner), owner) ||
                    (!(globalSink || ReferenceEquals(nodeManager, owner.CoreNodeManager)) &&
                        ((!IsCanonicalBoundSource(generation, nodeManager) &&
                                !IsCanonicalBoundSource(issuer, nodeManager)) ||
                            (reference is not null &&
                                (!reference.TryGetGeneration(out NodeManagerGeneration? bound) ||
                                    bound is null ||
                                    (!ReferenceEquals(bound, generation) &&
                                        !ReferenceEquals(bound, issuer) &&
                                        !IsPrivateFacadeSource(generation, bound, nodeManager) &&
                                        !IsPrivateForwardedSource(generation, bound, nodeManager)))))) ||
                    !m_lease.TryFork(out int slot))
                {
                    return false;
                }
                int issuerSlot = -1;
                if (m_issuerLease is not null && !m_issuerLease.TryFork(out issuerSlot))
                {
                    generation.Gate.Exit(slot);
                    return false;
                }
                AddressSpaceEpoch? epoch = null;
                int epochSlot = -1;
                if (m_epochLease is not null && !m_epochLease.TryFork(out epoch, out epochSlot))
                {
                    generation.Gate.Exit(slot);
                    if (issuerSlot >= 0)
                    {
                        issuer.Gate.Exit(issuerSlot);
                    }
                    return false;
                }
                bool transferred = false;
                try
                {
                    emission = new SourceEmissionAdmission(
                        owner,
                        generation,
                        slot,
                        m_readinessEventId,
                        m_readinessPending,
                        m_tracksReadiness,
                        issuer,
                        issuerSlot,
                        m_tracksIssuerReadiness,
                        epoch,
                        epochSlot);
                    transferred = true;
                    return true;
                }
                finally
                {
                    if (!transferred)
                    {
                        generation.Gate.Exit(slot);
                        if (issuerSlot >= 0)
                        {
                            issuer.Gate.Exit(issuerSlot);
                        }
                        epoch?.Exit(epochSlot);
                    }
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                NodeManagerGeneration? generation = Interlocked.Exchange(ref m_generation, null);
                NodeManagerGeneration? issuer = Interlocked.Exchange(ref m_issuer, null);
                if (m_tracksReadiness && generation is not null)
                {
                    generation.ReleaseSourceEmission(m_readinessEventId);
                }
                if (m_tracksIssuerReadiness && issuer is not null)
                {
                    issuer.ReleaseSourceEmission(m_readinessEventId);
                }
                m_lease.Dispose();
                m_issuerLease?.Dispose();
                m_epochLease?.Dispose();
                Volatile.Write(ref m_owner, null);
            }

            /// <summary>The routing owner, released when the source callback finishes.</summary>
            private MasterNodeManager? m_owner;

            /// <summary>The exact source, released when the source callback finishes.</summary>
            private NodeManagerGeneration? m_generation;

            /// <summary>The original issuer, released when the source callback finishes.</summary>
            private NodeManagerGeneration? m_issuer;

            /// <summary>The existing generation gate admission, not a separate retention counter.</summary>
            private readonly GenerationGate.Lease m_lease;

            /// <summary>The issuer gate admission when it differs from the routed gate.</summary>
            private readonly GenerationGate.Lease? m_issuerLease;

            /// <summary>The resource source epoch pinned by the source, when captured.</summary>
            private readonly AddressSpaceEpoch.Lease? m_epochLease;

            /// <summary>The unique source-event identity inherited by child emissions.</summary>
            private readonly long m_readinessEventId;

            /// <summary>Whether the source event was admitted while readiness was pending.</summary>
            private readonly bool m_readinessPending;

            /// <summary>Whether this origin owns one readiness-drain admission.</summary>
            private readonly bool m_tracksReadiness;

            /// <summary>Whether the issuer owns one readiness-drain admission.</summary>
            private readonly bool m_tracksIssuerReadiness;
        }

        /// <summary>
        /// Carries explicit provenance for event producers supplying a non-server system context.
        /// </summary>
        /// <param name="context">The original context whose values remain authoritative.</param>
        /// <param name="origin">The revocable source admission.</param>
        private sealed class SourceEmissionContext(ISystemContext context, SourceEmissionOrigin origin) : ISystemContext
        {
            /// <summary>
            /// Gets the explicit event origin.
            /// </summary>
            internal SourceEmissionOrigin Origin { get; } = origin;

            /// <inheritdoc/>
            public object? SystemHandle => context.SystemHandle;

            /// <inheritdoc/>
            public string? UserId => context.UserId;

            /// <inheritdoc/>
            public ArrayOf<string> PreferredLocales => context.PreferredLocales;

            /// <inheritdoc/>
            public string? AuditEntryId => context.AuditEntryId;

            /// <inheritdoc/>
            public NamespaceTable NamespaceUris => context.NamespaceUris;

            /// <inheritdoc/>
            public StringTable ServerUris => context.ServerUris;

            /// <inheritdoc/>
            public ITypeTable TypeTable => context.TypeTable;

            /// <inheritdoc/>
            public IEncodeableFactory EncodeableFactory => context.EncodeableFactory;

            /// <inheritdoc/>
            public INodeIdFactory? NodeIdFactory => context.NodeIdFactory;

            /// <inheritdoc/>
            public NodeStateFactory NodeStateFactory => context.NodeStateFactory;

            /// <inheritdoc/>
            public ITelemetryContext Telemetry => context.Telemetry;
        }

    }
}
