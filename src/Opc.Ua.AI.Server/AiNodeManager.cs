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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.AI;
using Opc.Ua.XRegistry;
using Opc.Ua.Server;

namespace Opc.Ua.AI.Server
{
    /// <summary>
    /// Publishes the AI models this Server exposes, per
    /// <c>OPC UA - AI Model Management and Inference</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The address space this builds is the specification's own: an
    /// <c>AiRootType</c> under the Server Object holding models, deployments,
    /// sources, registries and jobs. A client discovers everything from there,
    /// which is what a well-known entry point is for.
    /// </para>
    /// <para>
    /// The node manager owns the OPC UA surface and nothing else. Reaching an
    /// actual model is <see cref="IInferenceBackend"/>'s business, and the
    /// separation is deliberate: the specification's claim is that where inference
    /// runs does not change how it is called, so the code answering a call should
    /// not be able to tell where it ran.
    /// </para>
    /// </remarks>
    public sealed partial class AiNodeManager : AsyncCustomNodeManager
    {
        private readonly AiOptions m_options;
        private readonly InferenceBackendOptions m_backendOptions;
        private readonly InferenceBackendOptions m_fallbackBackendOptions;
        private readonly InferenceBackends m_backends;
        private readonly ILogger m_logger;
        private readonly Lock m_sync = new();
        private readonly Dictionary<NodeId, TransferEntry> m_transfers = [];
        private readonly List<NodeId> m_jobs = [];
        private readonly HashSet<string> m_learningSampleIds = new(StringComparer.Ordinal);
        private readonly Queue<string> m_learningSampleOrder = new();
        private readonly StreamFileManager m_files;
        private int m_nextId;

        private AiRootState? m_root;
        private ModelState? m_primaryModel;
        private ModelState? m_fallbackModel;
        private DeploymentState? m_primary;
        private DeploymentState? m_fallback;
        private LearningJobState? m_learningJob;

        /// <summary>
        /// Creates the node manager.
        /// </summary>
        /// <param name="server">The server hosting this node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="backends">Reaches the models this Server publishes.</param>
        /// <param name="options">What this Server publishes.</param>
        /// <param name="backendOptions">What the primary deployment reaches.</param>
        /// <param name="fallbackBackendOptions">
        /// What the fallback deployment reaches. Separate from the primary's on
        /// purpose: the fallback has its own site, jurisdiction and egress, and
        /// publishing the primary's would describe a deployment that does not exist.
        /// </param>
        /// <param name="logger">Where diagnostics go.</param>
        public AiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            InferenceBackends backends,
            IOptions<AiOptions>? options = null,
            IOptions<InferenceBackendOptions>? backendOptions = null,
            InferenceBackendOptions? fallbackBackendOptions = null,
            ILogger<AiNodeManager>? logger = null)
            : base(
                server,
                configuration,
                Opc.Ua.AI.Namespaces.AI,
                Opc.Ua.AI.Namespaces.xRegistry)
        {
            m_backends = backends ?? throw new ArgumentNullException(nameof(backends));
            m_options = options?.Value ?? new AiOptions();
            m_backendOptions = backendOptions?.Value ?? new InferenceBackendOptions();
            m_fallbackBackendOptions = fallbackBackendOptions ?? m_backendOptions;
            m_logger = logger ?? (ILogger)NullLogger<AiNodeManager>.Instance;
            m_files = new StreamFileManager(m_options.MaxTransferSize);
            SystemContext.NodeIdFactory = this;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_sync)
                {
                    foreach (TransferEntry entry in m_transfers.Values)
                    {
                        entry.Dispose();
                    }

                    m_transfers.Clear();
                }

                m_files.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// NodeId of the AI root, which is what a client browses to first.
        /// </summary>
        public NodeId RootId => m_root?.NodeId ?? NodeId.Null;

        /// <summary>
        /// NodeId of the primary deployment, for the client sample and the tests.
        /// </summary>
        public NodeId PrimaryDeploymentId => m_primary?.NodeId ?? NodeId.Null;

        /// <summary>
        /// NodeId of the fallback deployment, or a null NodeId when none is published.
        /// </summary>
        public NodeId FallbackDeploymentId => m_fallback?.NodeId ?? NodeId.Null;

        /// <summary>
        /// NodeId of the learning job that accounts for submitted ground-truth samples.
        /// </summary>
        public NodeId LearningJobId => m_learningJob?.NodeId ?? NodeId.Null;

        /// <inheritdoc/>
        /// <remarks>
        /// String identifiers, deliberately. Numeric ones would be drawn from the
        /// same namespace the loaded NodeSet occupies, and this model runs to
        /// ns=2;i=7001 - so a counter starting at 1 walks into the type nodes, and
        /// the predefined-node index overwrites rather than rejects. A Server that
        /// had served a few hundred transfers would quietly have replaced
        /// <c>AiRootType</c> with an inference job's <c>FinishedAt</c> property.
        /// A string identifier cannot collide with a numeric one at all, which is a
        /// stronger guarantee than any seed value.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return new NodeId(
                FormattableString.Invariant($"n{Interlocked.Increment(ref m_nextId)}"),
                NamespaceIndex);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // In dependency order. The AI types subtype xRegistry ones, so loading
            // only the AI model would leave every one of those supertypes dangling
            // and the type table would refuse the model outright.
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaXRegistry(context);
            nodes.AddOpcUaAI(context);
            return new ValueTask<NodeStateCollection>(SortBySupertype(nodes));
        }

        /// <summary>
        /// Orders types so that a supertype always precedes its subtypes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The type table refuses a type whose supertype it has not seen, and a
        /// NodeSet is under no obligation to list them in that order - NodeIds are
        /// assigned in declaration order, and a model that gains an abstract base
        /// after its first concrete subtype will legitimately carry a higher NodeId
        /// for the base. The AI model does exactly that.
        /// </para>
        /// <para>
        /// Sorting here rather than reordering the NodeSet keeps the two concerns
        /// apart: the NodeSet says what the model is, and this says what order this
        /// particular loader needs to hear it in.
        /// </para>
        /// </remarks>
        private static NodeStateCollection SortBySupertype(NodeStateCollection nodes)
        {
            var byId = new Dictionary<NodeId, BaseTypeState>();

            foreach (NodeState node in nodes)
            {
                if (node is BaseTypeState type && !type.NodeId.IsNull)
                {
                    byId[type.NodeId] = type;
                }
            }

            var sorted = new NodeStateCollection();
            var placed = new HashSet<NodeId>();

            foreach (NodeState node in nodes)
            {
                Place(node, byId, placed, sorted);
            }

            return sorted;
        }

        private static void Place(
            NodeState node,
            Dictionary<NodeId, BaseTypeState> byId,
            HashSet<NodeId> placed,
            NodeStateCollection sorted)
        {
            if (!node.NodeId.IsNull && !placed.Add(node.NodeId))
            {
                return;
            }

            // A supertype outside this collection is already in the type table -
            // every core type is - so only the ones declared here need placing
            // first. Marking the node placed before the recursion means a cycle
            // terminates rather than overflowing the stack; a model containing one
            // is broken either way, and it will be rejected with a clear message
            // instead of a StackOverflowException that kills the process.
            if (node is BaseTypeState type &&
                !type.SuperTypeId.IsNull &&
                byId.TryGetValue(type.SuperTypeId, out BaseTypeState? super))
            {
                Place(super, byId, placed, sorted);
            }

            sorted.Add(node);
        }

        /// <inheritdoc/>
        protected override async ValueTask AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await base.AddPredefinedNodeAsync(context, node, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The type table reports a rejected node as a bare status code with
                // no indication of which node it was, which makes a model that fails
                // to load nearly impossible to diagnose. Naming the node costs
                // nothing on the path that works.
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    FormattableString.Invariant(
                        $"Could not add {node.BrowseName} ({node.NodeId}): {ex.Message}"),
                    ex);
            }
        }

        /// <summary>
        /// The node registered under an identifier, or null when none is.
        /// </summary>
        /// <remarks>
        /// A test seam. The distinction between "on the NodeState tree" and "in the
        /// predefined-node index" is exactly what two of this sample's defects
        /// turned on, and it cannot be observed from outside the index.
        /// </remarks>
        internal NodeState? IndexedNode(NodeId nodeId)
        {
            return PredefinedNodes.TryGetValue(nodeId, out NodeState? node) ? node : null;
        }

        /// <summary>
        /// How many nodes of a given type the index holds.
        /// </summary>
        /// <typeparam name="TNode">The node state type to count.</typeparam>
        internal int CountIndexed<TNode>() where TNode : NodeState
        {
            int count = 0;

            foreach (KeyValuePair<NodeId, NodeState> pair in PredefinedNodes)
            {
                if (pair.Value is TNode)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Finds or creates a child declared by the type, and fails loudly if the
        /// type does not declare it.
        /// </summary>
        /// <remarks>
        /// Optional members are not materialised by <c>Create</c>, so every member
        /// this sample publishes beyond the mandatory ones has to be asked for. A
        /// browse name the type does not declare is a coding error rather than a
        /// runtime condition, so this throws rather than returning null and letting
        /// a null reference surface somewhere less informative.
        /// </remarks>
        /// <typeparam name="TChild">The instance state type of the child.</typeparam>
        /// <param name="parent">The node declaring the child.</param>
        /// <param name="browseName">The browse name of the child.</param>
        private TChild Child<TChild>(NodeState parent, string browseName)
            where TChild : BaseInstanceState
        {
            var qualifiedName = new QualifiedName(browseName, NamespaceIndex);

            if (parent.FindChild(SystemContext, qualifiedName) is TChild existing)
            {
                return existing;
            }

            if (parent.CreateChild(SystemContext, qualifiedName) is not TChild typed)
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"{parent.BrowseName} declares no {browseName} of type {typeof(TChild).Name}."));
            }

            // Two things a freshly materialised optional child does not have, and
            // needs before a client can see it or use it.
            //
            // Create runs the child's own initialisation, which is what builds the
            // members ITS type declares - for a Method, that is InputArguments and
            // OutputArguments. Without it the Method browses correctly, accepts a
            // call, and rejects it with BadTooManyArguments no matter what is passed,
            // because as far as the Server is concerned it takes none.
            //
            // ReferenceTypeId is what a Browse names the reference by. A child
            // without one is indexed, readable by NodeId and callable, but no client
            // can navigate to it - so the whole optional half of the model simply
            // is not there, without anything failing.
            typed.Create(
                SystemContext,
                NodeId.Null,
                qualifiedName,
                new LocalizedText(browseName),
                true);

            typed.ReferenceTypeId = typed is PropertyState
                ? Opc.Ua.ReferenceTypeIds.HasProperty
                : Opc.Ua.ReferenceTypeIds.HasComponent;

            return typed;
        }

        /// <summary>
        /// One chunked inference exchange the Server is holding.
        /// </summary>
        /// <remarks>
        /// The buffers live here rather than in the address space because a
        /// NodeState is not where bytes want to live. The node carries the state a
        /// client reads; this carries the payload it reads through.
        /// </remarks>
        private sealed class TransferEntry : IDisposable
        {
            public required InferenceTransferState Node { get; init; }

            public required NodeId DeploymentId { get; init; }

            public System.IO.MemoryStream Request { get; } = new();

            public System.IO.MemoryStream Response { get; } = new();

            public string ContentType { get; set; } = "application/json";

            public DateTime ExpiresAt { get; set; }

            public void Dispose()
            {
                Request.Dispose();
                Response.Dispose();
            }
        }
    }
}
