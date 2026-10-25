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
            return nodeManager is not null && m_nodeManagers.Revision.TryGetGeneration(
                    nodeManager,
                    out NodeManagerGeneration? generation) &&
                generation!.Gate.State == NodeManagerGenerationState.Live;
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

            NodeManagerGeneration? generation;
            if (generationReference is not null)
            {
                if (!generationReference.TryGetGeneration(out generation) ||
                    generation is null ||
                    !ReferenceEquals(generation.NodeManager, nodeManager))
                {
                    return false;
                }
            }
            else if (!master.m_nodeManagers.Revision.TryGetGeneration(nodeManager, out generation))
            {
                return false;
            }
            if (!generation!.Gate.TryEnter(out int slot))
            {
                return false;
            }
            emission = new SourceEmissionAdmission(master, generation, slot);
            return true;
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

            NodeManagerGeneration generation;
            if (generationReference is not null)
            {
                if (!generationReference.TryGetGeneration(out NodeManagerGeneration? boundGeneration) ||
                    boundGeneration is null ||
                    !ReferenceEquals(boundGeneration.NodeManager, nodeManager))
                {
                    return false;
                }
                generation = boundGeneration;
            }
            else
            {
                if (!master.m_nodeManagers.Revision.TryGetGeneration(
                        nodeManager,
                        out NodeManagerGeneration? currentGeneration))
                {
                    return false;
                }
                generation = currentGeneration!;
            }
            if (!generation.Gate.TryEnter(out int slot))
            {
                return false;
            }

            emission = new SourceEmissionAdmission(master, generation, slot);
            return true;
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

            if (!master.m_nodeManagers.Revision.TryGetGeneration(
                    nodeManager,
                    out NodeManagerGeneration? generation))
            {
                return false;
            }
            NodeManagerGeneration currentGeneration = generation!;
            if (!currentGeneration.Gate.TryEnter(out int slot))
            {
                return false;
            }
            admission = new SourceEmissionAdmission(master, currentGeneration, slot);
            return true;
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
                int slot)
            {
                m_owner = owner;
                Generation = generation;
                m_slot = slot;
            }

            /// <summary>
            /// Gets the exact generation admitted by the source emission.
            /// </summary>
            internal NodeManagerGeneration Generation { get; }

            /// <summary>
            /// Transfers this single-owned admission to an explicitly propagated event origin.
            /// </summary>
            /// <returns>The new owner; the original admission must not be disposed afterward.</returns>
            internal SourceEmissionOrigin TransferToOrigin()
            {
                return new SourceEmissionOrigin(m_owner, Generation, m_slot);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Generation.Gate.Exit(m_slot);
            }

            private readonly MasterNodeManager m_owner;
            private readonly int m_slot;
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
            internal SourceEmissionOrigin(MasterNodeManager owner, NodeManagerGeneration generation, int slot)
            {
                m_owner = owner;
                m_generation = generation;
                m_lease = new GenerationGate.Lease(generation.Gate, slot);
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
                if (generation is null ||
                    !ReferenceEquals(Volatile.Read(ref m_owner), owner) ||
                    (!(globalSink || ReferenceEquals(nodeManager, owner.CoreNodeManager)) &&
                        (!ReferenceEquals(nodeManager, generation.NodeManager) ||
                            (reference is not null &&
                                (!reference.TryGetGeneration(out NodeManagerGeneration? bound) ||
                                    !ReferenceEquals(bound, generation))))) ||
                    !m_lease.TryFork(out int slot))
                {
                    return false;
                }
                emission = new SourceEmissionAdmission(owner, generation, slot);
                return true;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_lease.Dispose();
                Volatile.Write(ref m_generation, null);
                Volatile.Write(ref m_owner, null);
            }

            /// <summary>The routing owner, released when the source callback finishes.</summary>
            private MasterNodeManager? m_owner;

            /// <summary>The exact source, released when the source callback finishes.</summary>
            private NodeManagerGeneration? m_generation;

            /// <summary>The existing generation gate admission, not a separate retention counter.</summary>
            private readonly GenerationGate.Lease m_lease;
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
