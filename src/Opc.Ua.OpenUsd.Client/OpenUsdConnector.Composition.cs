/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Composition/aggregation support (spec §5.12–5.14): assemble the represented
    /// Object's components into the USD prim tree — 1:1 or 1..n, static or dynamic
    /// (reconciled from model-change events), including components on other servers.
    /// </summary>
    public sealed partial class OpenUsdConnector
    {
        private Subscription? m_eventSubscription;
        private List<RepresentationInfo> m_allReps = new();
        private readonly HashSet<string> m_composedInstancePrims = new(StringComparer.Ordinal);
        private readonly Lock m_composeGate = new();
        private readonly SemaphoreSlim m_recomposeGate = new(1, 1);
        private int m_recomposePending;

        private async Task ComposeComponentAsync(RepresentationInfo rep, ComponentInfo c, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(c.TargetPrimPath))
            {
                return;
            }
            // Cross-server component (§5.14): author the reference prim, and federate to
            // the remote server to drive its bindings when a session factory is available.
            if (!string.IsNullOrEmpty(c.ComponentServerUri) || !string.IsNullOrEmpty(c.ComponentEndpointUrl))
            {
                await ComposeCrossServerAsync(rep, c, ct).ConfigureAwait(false);
                return;
            }

            // Precise 1:1 (§5.12): a One binding that names the specific component's own
            // representation composes exactly that prim (arc=Child nested prim).
            if (c.Cardinality == OpenUsdCardinality.One && c.ComponentRepresentation != null)
            {
                string onePrim = ResolveTarget(rep, c);
                m_sink.ComposePrim(onePrim, c.Arc, c.ComponentAssetReference, active: true);
                lock (m_composeGate)
                {
                    m_composedInstancePrims.Add(onePrim);
                }
                return;
            }

            NodeId? representedObj = await ParentAsync(rep.NodeId!.Value, ct).ConfigureAwait(false);
            if (representedObj == null)
            {
                return;
            }
            List<(NodeId Obj, NodeId Rep, string Name)> comps =
                await ResolveComponentRepsAsync(representedObj.Value, c, ct).ConfigureAwait(false);

            var live = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            if (c.Cardinality == OpenUsdCardinality.Many)
            {
                m_logger.ComponentsResolved(comps.Count, c.TargetPrimPath ?? string.Empty);
            }
            foreach ((NodeId _, NodeId _, string name) in comps)
            {
                if (c.Cardinality == OpenUsdCardinality.One && index >= 1)
                {
                    break;
                }
                string primPath = ComponentPrimPath(rep, c, name);
                m_sink.ComposePrim(primPath, c.Arc, c.ComponentAssetReference, active: true);
                live.Add(primPath);
                lock (m_composeGate)
                {
                    m_composedInstancePrims.Add(primPath);
                }
                index++;
            }

            // Dynamic reconciliation (§5.13): deactivate previously-composed instance
            // prims under this component's scope that no longer resolve.
            if (c.Dynamic && c.Cardinality == OpenUsdCardinality.Many)
            {
                string prefix = ResolveTarget(rep, c) + "/";
                List<string> stale;
                lock (m_composeGate)
                {
                    stale = m_composedInstancePrims
                        .Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && !live.Contains(p))
                        .ToList();
                    foreach (string p in stale)
                    {
                        m_composedInstancePrims.Remove(p);
                    }
                }
                foreach (string p in stale)
                {
                    m_sink.ComposePrim(p, c.Arc, c.ComponentAssetReference, active: false);
                }
            }
        }

        private async Task ComposeCrossServerAsync(RepresentationInfo rep, ComponentInfo c, CancellationToken ct)
        {
            string primPath = ResolveTarget(rep, c);
            OpenUsdCompositionArc arc = c.Arc == OpenUsdCompositionArc.Child
                ? OpenUsdCompositionArc.Reference
                : c.Arc;
            m_sink.ComposePrim(primPath, arc, c.ComponentAssetReference, active: true);
            lock (m_composeGate)
            {
                m_composedInstancePrims.Add(primPath);
            }
            if (m_remoteSessionFactory != null && !string.IsNullOrEmpty(c.ComponentEndpointUrl))
            {
                ISession remote = await m_remoteSessionFactory(c.ComponentEndpointUrl!, ct)
                    .ConfigureAwait(false);
                var remoteConn = new OpenUsdConnector(remote, m_sink, m_options, m_telemetry, ownsSession: true);
                m_remoteConnectors.Add(remoteConn);
                await remoteConn.StartAsync(ct).ConfigureAwait(false);
                m_logger.CrossServerFederated(c.ComponentEndpointUrl!);
            }
        }

        // Resolve the component Objects (with their own OpenUsdRepresentation AddIn)
        // reachable from the represented Object: its direct children plus one level of
        // Folder children, filtered by ComponentTypeDefinition when supplied.
        private async Task<List<(NodeId, NodeId, string)>> ResolveComponentRepsAsync(
            NodeId parent, ComponentInfo c, CancellationToken ct)
        {
            var result = new List<(NodeId, NodeId, string)>();
            var candidates = new List<(NodeId Id, string Name, NodeId? TypeDef)>();
            foreach ((string name, NodeId? id, NodeId? typeDef) in await ChildrenFullAsync(parent, ct)
                .ConfigureAwait(false))
            {
                if (id == null)
                {
                    continue;
                }
                if (typeDef == ObjectTypeIds.FolderType)
                {
                    foreach ((string gName, NodeId? gId, NodeId? gType) in
                        await ChildrenFullAsync(id.Value, ct).ConfigureAwait(false))
                    {
                        if (gId != null)
                        {
                            candidates.Add((gId.Value, gName, gType));
                        }
                    }
                }
                else
                {
                    candidates.Add((id.Value, name, typeDef));
                }
            }
            foreach ((NodeId id, string name, NodeId? typeDef) in candidates)
            {
                if (c.ComponentTypeDefinition != null && typeDef != c.ComponentTypeDefinition)
                {
                    continue;
                }
                NodeId? repNode = await FirstChildOfTypeAsync(id, m_representationTypeId, ct)
                    .ConfigureAwait(false);
                if (repNode != null)
                {
                    result.Add((id, repNode.Value, name));
                }
            }
            return result;
        }

        private static string ResolveTarget(RepresentationInfo rep, ComponentInfo c)
        {
            string t = c.TargetPrimPath!;
            if (t.StartsWith('/'))
            {
                return t.TrimEnd('/');
            }
            string basePath = (rep.PrimPath ?? string.Empty).TrimEnd('/');
            return basePath + "/" + t.Trim('/');
        }

        private static string ComponentPrimPath(RepresentationInfo rep, ComponentInfo c, string name)
        {
            string target = ResolveTarget(rep, c);
            if (c.Cardinality == OpenUsdCardinality.One)
            {
                return target;
            }
            return target + "/" + SanitizeName(name);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "_";
            }
            var sb = new System.Text.StringBuilder(name.Length);
            char c0 = name[0];
            sb.Append(char.IsLetter(c0) || c0 == '_' ? c0 : '_');
            for (int i = 1; i < name.Length; i++)
            {
                char ch = name[i];
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }
            return sb.ToString();
        }

        private async Task SubscribeModelChangesAsync(NodeId eventSource, CancellationToken ct)
        {
            var subscription = new Subscription(m_session.DefaultSubscription)
            {
                DisplayName = "OpenUsdConnector.ModelChange",
                PublishingInterval = 500,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true
            };
            m_eventSubscription = subscription;
            m_session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);

            // OfType(BaseModelChangeEventType) matches both GeneralModelChangeEventType
            // and SemanticChangeEventType (both derive from it) — §5.13.
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
            filter.WhereClause.Push(FilterOperator.OfType,
                Variant.From(ObjectTypeIds.BaseModelChangeEventType));

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                DisplayName = "modelchange",
                StartNodeId = eventSource,
                AttributeId = Attributes.EventNotifier,
                MonitoringMode = MonitoringMode.Reporting,
                QueueSize = 100,
                Filter = filter
            };
            item.Notification += OnModelChangeEvent;
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            m_logger.ModelChangeSubscribed(eventSource);
        }

        private void OnModelChangeEvent(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            // Fail-safe (§5.13): any model-change event triggers a full re-resolve rather
            // than a partial delta. Serialized + coalesced (see RunRecomposeAsync) so
            // overlapping events cannot race the reconciliation; the resolve is idempotent.
            _ = RunRecomposeAsync();
        }

        // Serializes model-change reconciliation and coalesces overlapping requests into a
        // single trailing run, so two events can never run RecomposeAsync concurrently and
        // no event is lost.
        private async Task RunRecomposeAsync()
        {
            Interlocked.Exchange(ref m_recomposePending, 1);
            while (true)
            {
                if (!await m_recomposeGate.WaitAsync(0).ConfigureAwait(false))
                {
                    // Another pass holds the gate; it will observe our pending request.
                    return;
                }
                try
                {
                    while (Interlocked.Exchange(ref m_recomposePending, 0) == 1)
                    {
                        try
                        {
                            await RecomposeAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // A transient recompose failure must not crash the connector;
                            // the next model-change event re-runs the fail-safe resolve.
                            m_logger.RecomposeFailed(ex);
                        }
                    }
                }
                finally
                {
                    m_recomposeGate.Release();
                }
                // If a request landed between clearing the flag and releasing the gate,
                // loop to acquire again and service it.
                if (Volatile.Read(ref m_recomposePending) == 0)
                {
                    return;
                }
            }
        }

        private async Task RecomposeAsync(CancellationToken ct)
        {
            try
            {
                foreach (RepresentationInfo rep in m_allReps)
                {
                    foreach (ComponentInfo c in rep.Components)
                    {
                        if (c.Dynamic
                            && string.IsNullOrEmpty(c.ComponentServerUri)
                            && string.IsNullOrEmpty(c.ComponentEndpointUrl))
                        {
                            await ComposeComponentAsync(rep, c, ct).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Swallow: a transient recompose failure must not crash the connector;
                // the next model-change event re-runs the fail-safe full resolve.
            }
        }

        // ---- browse helpers -------------------------------------------------

        private async Task<NodeId?> ParentAsync(NodeId node, CancellationToken ct)
        {
            // Browse the AGGREGATING parent (HasComponent/HasAddIn), not any hierarchical
            // reference — otherwise the Organizes link from the discovery registry would
            // be returned instead of the represented Object.
            (ArrayOf<ArrayOf<ReferenceDescription>> results, _) = await m_session.ManagedBrowseAsync(
                null, null, [node], 0, BrowseDirection.Inverse,
                ReferenceTypeIds.Aggregates, includeSubtypes: true, 0, ct).ConfigureAwait(false);
            if (results.Count == 0 || results[0].Count == 0)
            {
                return null;
            }
            NodeId parent = ExpandedNodeId.ToNodeId(results[0][0].NodeId, m_session.NamespaceUris);
            return parent.IsNull ? null : parent;
        }

        private async Task<List<(string, NodeId?, NodeId?)>> ChildrenFullAsync(NodeId parent, CancellationToken ct)
        {
            var list = new List<(string, NodeId?, NodeId?)>();
            (ArrayOf<ArrayOf<ReferenceDescription>> results, _) = await m_session.ManagedBrowseAsync(
                null, null, [parent], 0, BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences, includeSubtypes: true, 0, ct).ConfigureAwait(false);
            if (results.Count == 0)
            {
                return list;
            }
            ArrayOf<ReferenceDescription> refs = results[0];
            for (int i = 0; i < refs.Count; i++)
            {
                NodeId id = ExpandedNodeId.ToNodeId(refs[i].NodeId, m_session.NamespaceUris);
                if (id.IsNull)
                {
                    continue;
                }
                NodeId typeDef = ExpandedNodeId.ToNodeId(refs[i].TypeDefinition, m_session.NamespaceUris);
                list.Add((refs[i].BrowseName.Name ?? string.Empty, id,
                    typeDef.IsNull ? (NodeId?)null : typeDef));
            }
            return list;
        }

        private async Task<NodeId?> FirstChildOfTypeAsync(NodeId parent, NodeId typeDefinition, CancellationToken ct)
        {
            foreach ((string _, NodeId? id, NodeId? typeDef) in await ChildrenFullAsync(parent, ct)
                .ConfigureAwait(false))
            {
                if (id != null && typeDef == typeDefinition)
                {
                    return id;
                }
            }
            return null;
        }
    }
}
