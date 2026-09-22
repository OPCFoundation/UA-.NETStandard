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
 *
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class LifecycleWotViewProjectionHost : IWotRecoverableViewProjectionHost
    {
        /// <inheritdoc/>
        public bool SupportsPreparedPublication => m_lifecycle is INodeManagerBatchLifecycle;

        /// <inheritdoc/>
        public async ValueTask<IWotPreparedViewPublication> PrepareAsync(
            ArrayOf<WotViewProjectionRequest> updates,
            ArrayOf<WotViewProjectionHandle> removals,
            WotCommittedPublicationState expectedPublication,
            CancellationToken cancellationToken = default)
        {
            _ = expectedPublication ?? throw new ArgumentNullException(nameof(expectedPublication));
            if (!SupportsPreparedPublication)
            {
                throw new NotSupportedException("The lifecycle cannot prepare a canonical View publication.");
            }
            removals = [.. removals];
            var requested = new HashSet<string>(StringComparer.Ordinal);
            var captured = new List<WotViewProjectionRequest>();
            foreach (WotViewProjectionRequest request in updates)
            {
                if (request is null || string.IsNullOrWhiteSpace(request.ResourceXid) ||
                    !request.Plan.IsCanonical || !requested.Add(request.ResourceXid))
                {
                    throw new ArgumentException("Distinct canonical View requests are required.", nameof(updates));
                }
                WotViewProjectionPlan plan = request.Plan;
                WotCanonicalViewGraphContext.RequireUri(plan.Scenario, nameof(updates));
                if (plan.DocumentKind is not (WotDocumentKind.ThingDescription or WotDocumentKind.ThingModel))
                {
                    throw new ArgumentException("A canonical View must describe a TD or TM.", nameof(updates));
                }
                captured.Add(new WotViewProjectionRequest(
                    request.ClosureKey, request.ResourceXid, request.ResourceNodeId, request.ViewNodeId,
                    WotViewProjectionPlan.CreateCanonical(
                        plan.Scenario, plan.DocumentKind, [.. plan.OrganizedNodeIds],
                        [.. plan.CanonicalLinks], [.. plan.Omissions]))
                {
                    CapturedNamespaceUris = [.. request.CapturedNamespaceUris]
                });
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_manager is not null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "An immediate View image cannot become a prepared canonical owner.");
                }
                CanonicalPublication? previous = m_canonicalPublication;
                ByteString payload = expectedPublication.RegistrySnapshot.CanonicalViewGraphState;
                if (previous is null
                    ? !expectedPublication.Views.IsEmpty || (!payload.IsNull && payload.Length != 0)
                    : previous.Graph.CanonicalViewGraphState != payload ||
                        !previous.Graph.Views.ToList().SequenceEqual(expectedPublication.Views.ToList()) ||
                        !m_lifecycle.Registrations.Contains(registration =>
                            ReferenceEquals(registration, previous.Registration)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The expected graph is not this host's exact committed image.");
                }
                var removed = new HashSet<string>(StringComparer.Ordinal);
                foreach (WotViewProjectionHandle handle in removals)
                {
                    if (handle is null || previous is null ||
                        !previous.Graph.Views.Contains(current => ReferenceEquals(current, handle)) ||
                        !removed.Add(handle.ResourceXid))
                    {
                        throw new ArgumentException(
                            "A removal must belong to this exact View image.", nameof(removals));
                    }
                }
                removed.ExceptWith(requested);
                return new PreparedViewPublication(this, previous, captured.ToArrayOf(), removed.ToArrayOf());
            }
            catch
            {
                m_gate.Release();
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IWotPreparedViewPublication> PrepareRecoveryAsync(
            WotCommittedPublicationState committedPublication,
            CancellationToken cancellationToken = default)
        {
            _ = committedPublication ?? throw new ArgumentNullException(nameof(committedPublication));
            if (!SupportsPreparedPublication)
            {
                throw new NotSupportedException("The lifecycle cannot recover a canonical publication.");
            }
            WotCanonicalViewState recovered = WotCanonicalViewState.Parse(
                committedPublication.RegistrySnapshot.CanonicalViewGraphState);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                CanonicalPublication? previous = m_canonicalPublication;
                if (m_manager is not null || (previous is null
                    ? !committedPublication.Views.IsEmpty
                    : !previous.Graph.Views.ToList().SequenceEqual(committedPublication.Views.ToList()) ||
                        !m_lifecycle.Registrations.Contains(registration =>
                            ReferenceEquals(registration, previous.Registration))))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Recovery does not identify this host's current View owners.");
                }
                return new PreparedViewPublication(this, previous, [], [], recovered);
            }
            catch
            {
                m_gate.Release();
                throw;
            }
        }

        private void EnsureImmediateImage()
        {
            if (m_canonicalPublication is not null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A canonical image must be changed through prepared publication.");
            }
        }

        private sealed record CanonicalPublication(
            WotCanonicalViewState State,
            NodeManagerRegistration Registration,
            WotPreparedViewGraphState Graph);

        private sealed class PreparedViewPublication(
            LifecycleWotViewProjectionHost owner,
            CanonicalPublication? previous,
            ArrayOf<WotViewProjectionRequest> updates,
            ArrayOf<string> removals,
            WotCanonicalViewState? recovered = null)
            : IWotPreparedViewPublication, IAsyncNodeManagerFactory, IRequestCallbackSafeNodeManagerFactory,
                IWotPreparedViewSourceConsumer, IRegistrationBoundNodeManagerFactory
        {
            public ArrayOf<NodeManagerBatchChange> Changes => previous is null
                ? [NodeManagerBatchChange.Add(this)]
                : [NodeManagerBatchChange.Replace(previous.Registration, this,
                    owner.m_retirementPolicy == WotProjectionRetirementPolicy.Immediate)];

            public ArrayOf<string> NamespacesUris => [Namespaces.WotCon];
            public bool AllowLifecycleFromRequestCallback => true;
            public NodeManagerRegistration? ExpectedRegistration => previous?.Registration;

            public void BindSourceImage(WotPreparedSourceImage sources)
            {
                if (m_sources is not null || m_disposed != 0)
                {
                    throw new InvalidOperationException(
                        "The View participant already has a source image or is disposed.");
                }
                m_sources = sources;
            }

            public async ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                if (m_sources is null || m_created is not null || m_disposed != 0)
                {
                    throw new InvalidOperationException("The View factory requires one prepared source image.");
                }
                if (previous?.Registration.NodeManager is WotProjectionViewNodeManager old)
                {
                    old.ValidateCanonicalServer(server);
                }
                NamespaceTable namespaces = server.NamespaceUris;
                if (recovered is not null)
                {
                    foreach (string uri in WotProjectionViewNodeManager.CanonicalNamespaces(recovered))
                    {
                        namespaces.GetIndexOrAppend(uri);
                    }
                    foreach (WotCanonicalViewPublication view in recovered.Views)
                    {
                        if (!string.IsNullOrEmpty(view.ViewNodeId.NamespaceUri))
                        {
                            namespaces.GetIndexOrAppend(view.ViewNodeId.NamespaceUri);
                        }
                    }
                }
                var requests = new List<WotViewProjectionRequest>();
                var sourceIds = new HashSet<NodeId>();
                var retainedFacts = new Dictionary<NodeId, CanonicalViewSource>();
                WotCanonicalViewState? retained = recovered ?? previous?.State;
                if (retained is not null)
                {
                    foreach (CanonicalViewSource source in retained.SourceFacts)
                    {
                        NodeId nodeId = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(source.NodeId), namespaces);
                        if (nodeId.IsNull)
                        {
                            if (recovered is not null && !source.Available)
                            {
                                continue;
                            }
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid, "A retained source namespace is missing.");
                        }
                        sourceIds.Add(nodeId);
                        retainedFacts.Add(nodeId, source);
                    }
                }
                foreach (WotViewProjectionRequest request in updates)
                {
                    NodeId Rebase(NodeId nodeId)
                    {
                        if (request.CapturedNamespaceUris.IsEmpty || nodeId.IsNull)
                        {
                            return nodeId;
                        }
                        if (nodeId.NamespaceIndex >= request.CapturedNamespaceUris.Count)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid, "A captured NodeId is invalid.");
                        }
                        int index = namespaces.GetIndex(request.CapturedNamespaceUris[nodeId.NamespaceIndex]);
                        if (index < 0)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid,
                                "A captured namespace is absent from the prepared source image.");
                        }
                        return nodeId.WithNamespaceIndex((ushort)index);
                    }
                    ArrayOf<NodeId> members = request.Plan.OrganizedNodeIds.ConvertAll(Rebase);
                    sourceIds.UnionWith(members.ToList());
                    requests.Add(new WotViewProjectionRequest(
                        request.ClosureKey, request.ResourceXid,
                        Rebase(request.ResourceNodeId), Rebase(request.ViewNodeId),
                        WotViewProjectionPlan.CreateCanonical(
                            request.Plan.Scenario, request.Plan.DocumentKind, members,
                            request.Plan.CanonicalLinks, request.Plan.Omissions)));
                }
                var facts = new List<WotCanonicalViewSource>();
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                foreach (NodeId nodeId in sourceIds)
                {
                    (object? handle, IAsyncNodeManager? source) = await m_sources
                        .FindAsync(server, nodeId, cancellationToken).ConfigureAwait(false);
                    if (handle is null || source is null)
                    {
                        if (!retainedFacts.TryGetValue(nodeId, out CanonicalViewSource? fact))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdUnknown, "A selected local Node has no prepared source owner.");
                        }
                        facts.Add(new WotCanonicalViewSource(nodeId, fact.NodeClass, false));
                    }
                    else
                    {
                        NodeMetadata metadata = await source.GetNodeMetadataAsync(
                            context, handle, BrowseResultMask.NodeClass, cancellationToken).ConfigureAwait(false);
                        if (metadata is null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdUnknown, "A source has no Node metadata.");
                        }
                        facts.Add(new WotCanonicalViewSource(nodeId, metadata.NodeClass));
                    }
                }
                string logicalServer = server.ServerUris.GetString(0)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The logical server URI is missing.");
                var graphContext = new WotCanonicalViewGraphContext(
                    logicalServer, Namespaces.WotCon, namespaces, facts.ToArrayOf());
                if (recovered is not null)
                {
                    WotCanonicalViewState restored = WotCanonicalViewState.Restore(
                        recovered.ToByteString(), graphContext);
                    m_preparation = new WotCanonicalViewPreparation(
                        restored, restored.Views.ConvertAll(view => view.ResourceXid), false);
                }
                else
                {
                    m_preparation = WotProjectionViewBuilder.PrepareCanonicalGraph(
                        graphContext, previous?.State, requests.ToArrayOf(), removals);
                }
                foreach (string uri in WotProjectionViewNodeManager.CanonicalNamespaces(m_preparation.State))
                {
                    if (namespaces.GetIndex(uri) < 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "Canonical preparation cannot allocate a serving namespace.");
                    }
                }
                m_namespaces = namespaces;
                IAsyncNodeManagerFactory factory = WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(
                    m_preparation.State, previous?.Registration, m_sources);
                m_created = await factory.CreateAsync(server, configuration, cancellationToken).ConfigureAwait(false);
                return m_created;
            }

            public WotPreparedViewGraphState BindPreparedRegistrations(ArrayOf<NodeManagerRegistration> registrations)
            {
                if (registrations.Count != 1 || !ReferenceEquals(registrations[0].NodeManager, m_created) ||
                    m_created is null || m_preparation is null || m_namespaces is null || m_disposed != 0 ||
                    (previous is not null && (registrations[0].Id != previous.Registration.Id ||
                        registrations[0].Generation != previous.Registration.Generation + 1)))
                {
                    throw new ArgumentException(
                        "The registration is not this exact prepared View candidate.", nameof(registrations));
                }
                m_sources!.RequireBound();
                if (m_bound is not null)
                {
                    return m_bound.Graph;
                }
                var handles = new List<WotViewProjectionHandle>();
                foreach (WotCanonicalViewPublication view in m_preparation.State.Views)
                {
                    if (view.Active)
                    {
                        handles.Add(new WotViewProjectionHandle(
                            view.ResourceXid, ExpandedNodeId.ToNodeId(view.ViewNodeId, m_namespaces),
                            view.MaterializedNodeCount, JoinOmissions(view.Omissions), view.Omissions));
                    }
                }
                var graph = new WotPreparedViewGraphState(
                    m_preparation.State.ToByteString(), handles.ToArrayOf(), m_preparation.AffectedResourceXids);
                m_bound = new CanonicalPublication(m_preparation.State, registrations[0], graph);
                return graph;
            }

            public void OnPublished(WotCommittedPublicationState publication)
            {
                Volatile.Write(ref owner.m_canonicalPublication, m_bound);
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    owner.m_gate.Release();
                }
                return default;
            }

            private WotPreparedSourceImage? m_sources;
            private IAsyncNodeManager? m_created;
            private WotCanonicalViewPreparation? m_preparation;
            private NamespaceTable? m_namespaces;
            private CanonicalPublication? m_bound;
            private int m_disposed;
        }

        private CanonicalPublication? m_canonicalPublication;
    }
}
