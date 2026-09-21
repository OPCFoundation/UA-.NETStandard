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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Wot;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class WotProjectionViewBuilder
    {
        internal async ValueTask<WotViewProjectionResult> BuildCanonicalAsync(
            WotDocument document, WotRegistrySnapshot snapshot, CancellationToken cancellationToken)
        {
            WotViewProjectionResult resolved = await BuildAsync(document, null, cancellationToken)
                .ConfigureAwait(false);
            if (!resolved.Success)
            {
                return resolved;
            }
            var diagnostics = resolved.Diagnostics.ToList();
            var projection = WotProjection.Parse(document, diagnostics, m_options.ProjectionCompatibilityMode);
            if (projection is null)
            {
                return new WotViewProjectionResult(null, diagnostics);
            }
            var links = new List<WotCanonicalViewLink>();
            foreach (WotOrganizingLink link in projection.OrganizingLinks)
            {
                WotResource? child = WotDependencyGraph.Resolve(snapshot, link.Href);
                if (child is null)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ValidationError,
                        $"Organizing link '{link.Href}' has no captured canonical Resource."));
                    return new WotViewProjectionResult(null, diagnostics);
                }
                links.Add(new WotCanonicalViewLink(child.Xid, link.RefName));
            }
            WotViewProjectionPlan plan = resolved.Plan!;
            return new WotViewProjectionResult(WotViewProjectionPlan.CreateCanonical(
                plan.Scenario, plan.DocumentKind, plan.OrganizedNodeIds, links.ToArrayOf(), plan.Omissions),
                diagnostics);
        }

        /// <summary>
        /// Creates an unpublished shared View-image factory for an existing lifecycle or aggregate preparation.
        /// </summary>
        public static IAsyncNodeManagerFactory CreateCanonicalNodeManagerFactory(
            WotCanonicalViewState state, NodeManagerRegistration? previous = null)
        {
            return CreateCanonicalNodeManagerFactory(state, previous, null);
        }

        internal static IAsyncNodeManagerFactory CreateCanonicalNodeManagerFactory(
            WotCanonicalViewState state, NodeManagerRegistration? previous, WotPreparedSourceImage? sources)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (previous is not null)
            {
                if (previous.NodeManager is not WotProjectionViewNodeManager manager)
                {
                    throw new ArgumentException(
                        "The previous registration is not a canonical View owner.", nameof(previous));
                }
                manager.ValidateCanonicalSuccessor(state);
            }
            return new CanonicalNodeManagerFactory(
                WotCanonicalViewState.Parse(state.ToByteString()), previous, sources);
        }

        /// <summary>
        /// Prepares one complete canonical graph from captured inputs without publishing or consulting live state.
        /// </summary>
        public static WotCanonicalViewPreparation PrepareCanonicalGraph(
            WotCanonicalViewGraphContext context,
            WotCanonicalViewState? expected,
            ArrayOf<WotViewProjectionRequest> updates,
            ArrayOf<string> removals)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (expected is not null &&
                (!string.Equals(expected.LogicalServerUri, context.LogicalServerUri, StringComparison.Ordinal) ||
                !string.Equals(expected.AllocationNamespaceUri, context.AllocationNamespaceUri, StringComparison.Ordinal)))
            {
                throw new ArgumentException("The expected canonical graph belongs to another logical server or namespace.");
            }
            var definitions = new Dictionary<string, CanonicalViewDefinition>(StringComparer.Ordinal);
            if (expected is not null)
            {
                foreach (CanonicalViewDefinition definition in expected.Definitions)
                {
                    definitions.Add(definition.ResourceXid, definition);
                }
            }
            var updated = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotViewProjectionRequest request in updates)
            {
                if (request is null || string.IsNullOrEmpty(request.ResourceXid) || !request.Plan.IsCanonical)
                {
                    throw new ArgumentException("A canonical request with a Resource identity is required.", nameof(updates));
                }
                WotCanonicalViewGraphContext.RequireUri(request.Plan.Scenario, nameof(updates));
                if (request.Plan.DocumentKind is not (WotDocumentKind.ThingDescription or WotDocumentKind.ThingModel))
                {
                    throw new ArgumentException("A canonical View must describe a TD or TM.", nameof(updates));
                }
                string resourceId = context.Portable(request.ResourceNodeId);
                definitions.TryGetValue(request.ResourceXid, out CanonicalViewDefinition? previous);
                string viewId = request.ViewNodeId.IsNull
                    ? previous?.ViewNodeId ?? WotPortableIdentity.GenerateNodeId(context.AllocationNamespaceUri,
                        [
                            new WotBrowsePathElement(Namespaces.WotCon, "View"),
                            new WotBrowsePathElement(Namespaces.WotCon, request.ResourceXid)
                        ])
                    : context.Portable(request.ViewNodeId);
                if (previous is not null &&
                    (!string.Equals(previous.ResourceNodeId, resourceId, StringComparison.Ordinal) ||
                    !string.Equals(previous.ViewNodeId, viewId, StringComparison.Ordinal)))
                {
                    throw new ArgumentException("A retained canonical Resource or View identity cannot be reassigned.");
                }
                var members = new List<string>(request.Plan.OrganizedNodeIds.Count);
                foreach (NodeId nodeId in request.Plan.OrganizedNodeIds)
                {
                    members.Add(context.Portable(nodeId));
                }
                var links = new List<WotCanonicalViewLink>(request.Plan.CanonicalLinks.Count);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (WotCanonicalViewLink link in request.Plan.CanonicalLinks)
                {
                    if (string.IsNullOrEmpty(link.ResourceXid) || string.IsNullOrEmpty(link.Key) || !keys.Add(link.Key))
                    {
                        throw new ArgumentException("Organizing links require distinct nonempty keys and target Xids.");
                    }
                    links.Add(link);
                }
                var omissions = new List<string>(request.Plan.Omissions.Count);
                foreach (string omission in request.Plan.Omissions)
                {
                    omissions.Add(omission);
                }
                if (!updated.Add(request.ResourceXid))
                {
                    throw new ArgumentException("A canonical Resource cannot be updated twice in one preparation.");
                }
                definitions[request.ResourceXid] = new CanonicalViewDefinition(
                    request.ResourceXid, resourceId, viewId, (int)request.Plan.DocumentKind,
                    request.Plan.Scenario, true, members.Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    links.OrderBy(link => link.Key, StringComparer.Ordinal).ToArray(),
                    omissions.Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal).ToArray());
            }
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string resource in removals)
            {
                if (!removed.Add(resource) || updated.Contains(resource) ||
                    !definitions.TryGetValue(resource, out CanonicalViewDefinition? previous))
                {
                    throw new ArgumentException("A removal must name one known Resource without a simultaneous update.");
                }
                definitions[resource] = previous with { Requested = false };
            }
            var sources = new Dictionary<string, CanonicalViewSource>(StringComparer.Ordinal);
            foreach (WotCanonicalViewSource source in context.Sources)
            {
                string nodeId = context.Portable(source.NodeId);
                if (source.NodeClass is not (NodeClass.Object or NodeClass.Variable or NodeClass.Method or
                    NodeClass.ObjectType or NodeClass.VariableType or NodeClass.DataType or
                    NodeClass.ReferenceType or NodeClass.View) ||
                    !sources.TryAdd(nodeId, new CanonicalViewSource(nodeId, source.NodeClass, source.Available)))
                {
                    throw new ArgumentException("The captured source catalogue contains an invalid or duplicate Node.");
                }
            }
            if (expected is not null)
            {
                foreach (CanonicalViewSource previous in expected.SourceFacts)
                {
                    sources.TryAdd(previous.NodeId, previous with { Available = false });
                }
            }
            WotCanonicalViewState state = BuildCanonicalImage(
                context.LogicalServerUri, context.AllocationNamespaceUri, definitions, sources, expected);
            var affected = new List<string>();
            foreach (WotCanonicalViewPublication view in state.Views)
            {
                WotCanonicalViewPublication? previousView = FindCanonicalView(expected, view.ResourceXid);
                CanonicalViewDefinition? previousDefinition = FindCanonicalDefinition(expected, view.ResourceXid);
                if (previousView is null || previousDefinition is null ||
                    !SameCanonicalPublication(previousView, view) ||
                    !SameCanonicalDefinition(previousDefinition, definitions[view.ResourceXid]))
                {
                    affected.Add(view.ResourceXid);
                }
            }
            bool changed = expected is null || affected.Count > 0 ||
                sources.Count != expected.SourceFacts.Count;
            if (!changed && expected is not null)
            {
                foreach (CanonicalViewSource previous in expected.SourceFacts)
                {
                    if (!sources.TryGetValue(previous.NodeId, out CanonicalViewSource? current) ||
                        current != previous)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            return new WotCanonicalViewPreparation(
                state, affected.OrderBy(value => value, StringComparer.Ordinal).ToArrayOf(), changed);
        }

        private static WotCanonicalViewState BuildCanonicalImage(
            string logicalServerUri,
            string allocationNamespaceUri,
            Dictionary<string, CanonicalViewDefinition> definitions,
            Dictionary<string, CanonicalViewSource> sources,
            WotCanonicalViewState? expected)
        {
            if (definitions.Count > 10_000 || sources.Count > 1_000_000)
            {
                throw new ArgumentException("The canonical graph exceeds its bounded catalogue size.");
            }
            var roles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CanonicalViewSource source in sources.Values)
            {
                ClaimCanonicalRole(roles, source.NodeId, "source");
            }
            foreach (CanonicalViewDefinition definition in definitions.Values)
            {
                ClaimCanonicalRole(roles, definition.ResourceNodeId, "resource:" + definition.ResourceXid);
                ClaimCanonicalRole(roles, definition.ViewNodeId, "view:" + definition.ResourceXid);
            }
            foreach (CanonicalViewDefinition definition in definitions.Values)
            {
                ClaimCanonicalRole(roles,
                    CanonicalRoleId(allocationNamespaceUri, "view-version", definition.ViewNodeId),
                    "version:" + definition.ResourceXid);
                foreach (WotCanonicalViewLink link in definition.Links)
                {
                    if (!definitions.TryGetValue(link.ResourceXid, out CanonicalViewDefinition? child))
                    {
                        throw new ArgumentException("A retained organizing link has no canonical target.");
                    }
                    ClaimCanonicalRole(roles,
                        CanonicalRoleId(
                            allocationNamespaceUri, "group", child.ViewNodeId, definition.ViewNodeId, link.Key),
                        "group:" + definition.ResourceXid + ":" + link.Key);
                    ClaimCanonicalRole(roles,
                        CanonicalRoleId(
                            allocationNamespaceUri, "projection-root", child.ViewNodeId, definition.ViewNodeId, link.Key),
                        "root:" + definition.ResourceXid + ":" + link.Key);
                }
            }
            var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var omissions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (CanonicalViewDefinition definition in definitions.Values)
            {
                if (definition.Requested)
                {
                    CollectCanonicalMembership(definition.ResourceXid, allocationNamespaceUri, definitions, sources,
                        members, omissions, new HashSet<string>(StringComparer.Ordinal));
                }
            }
            var nodes = new List<WotCanonicalViewNode>();
            var references = new HashSet<WotCanonicalViewReference>();
            var views = new List<WotCanonicalViewPublication>();
            ExpandedNodeId organizes = new(Ua.ReferenceTypeIds.Organizes);
            ExpandedNodeId hasProperty = new(Ua.ReferenceTypeIds.HasProperty);
            ExpandedNodeId hasTypeDefinition = new(Ua.ReferenceTypeIds.HasTypeDefinition);
            ExpandedNodeId hasProjection = ReferenceTypeIds.HasWoTProjection;
            foreach (CanonicalViewDefinition definition in definitions.Values.OrderBy(
                definition => definition.ResourceXid, StringComparer.Ordinal))
            {
                ExpandedNodeId viewId = ExpandedNodeId.Parse(definition.ViewNodeId);
                ExpandedNodeId resourceId = ExpandedNodeId.Parse(definition.ResourceNodeId);
                WotCanonicalViewPublication? previousView = FindCanonicalView(expected, definition.ResourceXid);
                if (!members.TryGetValue(definition.ResourceXid, out HashSet<string>? membership))
                {
                    if (previousView is null)
                    {
                        throw new ArgumentException("An inactive canonical identity has no retained publication history.");
                    }
                    views.Add(new WotCanonicalViewPublication(
                        definition.ResourceXid, resourceId, viewId, definition.Requested, false,
                        previousView.ViewVersion, previousView.MembershipDigest, previousView.Membership,
                        previousView.Omissions, 0));
                    continue;
                }
                ByteString digest = WotPortableIdentity.ProjectionMembershipDigest(
                    membership.ToArrayOf());
                uint version = previousView is null ? 1 : previousView.ViewVersion;
                if (previousView is not null && !SameCanonicalMembership(previousView.Membership, membership))
                {
                    version = version == uint.MaxValue ? 1 : version + 1;
                }
                views.Add(new WotCanonicalViewPublication(
                    definition.ResourceXid, resourceId, viewId, definition.Requested, true, version, digest,
                    membership.OrderBy(value => value, StringComparer.Ordinal)
                        .Select(ExpandedNodeId.Parse).ToArrayOf(),
                    omissions[definition.ResourceXid].OrderBy(value => value, StringComparer.Ordinal).ToArrayOf(),
                    1 + (2 * definition.Links.Length)));
                nodes.Add(new WotCanonicalViewNode(
                    viewId, definition.ResourceXid, WotCanonicalViewNodeRole.View, NodeClass.View,
                    new WotBrowsePathElement(viewId.NamespaceUri, "View"), default, default, default, 0));
                references.Add(new WotCanonicalViewReference(
                    new ExpandedNodeId(Ua.ObjectIds.ViewsFolder), organizes, viewId, false));
                references.Add(new WotCanonicalViewReference(
                    viewId, organizes, new ExpandedNodeId(Ua.ObjectIds.ViewsFolder), true));
                references.Add(new WotCanonicalViewReference(resourceId, hasProjection, viewId, false));
                references.Add(new WotCanonicalViewReference(viewId, hasProjection, resourceId, true));
                string versionNode = CanonicalRoleId(allocationNamespaceUri, "view-version", definition.ViewNodeId);
                ClaimCanonicalRole(roles, versionNode, "version:" + definition.ResourceXid);
                ExpandedNodeId versionId = ExpandedNodeId.Parse(versionNode);
                nodes.Add(new WotCanonicalViewNode(
                    versionId, definition.ResourceXid, WotCanonicalViewNodeRole.ViewVersion, NodeClass.Variable,
                    new WotBrowsePathElement(null, "ViewVersion"), new ExpandedNodeId(Ua.VariableTypeIds.PropertyType),
                    new ExpandedNodeId(Ua.DataTypeIds.UInt32), default, version));
                references.Add(new WotCanonicalViewReference(viewId, hasProperty, versionId, false));
                references.Add(new WotCanonicalViewReference(versionId, hasProperty, viewId, true));
                references.Add(new WotCanonicalViewReference(
                    versionId, hasTypeDefinition, new ExpandedNodeId(Ua.VariableTypeIds.PropertyType), false));
                AddCanonicalDirectReferences(viewId, definition, definitions, sources, allocationNamespaceUri, references);
                foreach (WotCanonicalViewLink link in definition.Links)
                {
                    CanonicalViewDefinition child = definitions[link.ResourceXid];
                    string wrapperNode = CanonicalRoleId(
                        allocationNamespaceUri, "group", child.ViewNodeId, definition.ViewNodeId, link.Key);
                    string propertyNode = CanonicalRoleId(
                        allocationNamespaceUri, "projection-root", child.ViewNodeId, definition.ViewNodeId, link.Key);
                    ClaimCanonicalRole(roles, wrapperNode, "group:" + definition.ResourceXid + ":" + link.Key);
                    ClaimCanonicalRole(roles, propertyNode, "root:" + definition.ResourceXid + ":" + link.Key);
                    ExpandedNodeId wrapperId = ExpandedNodeId.Parse(wrapperNode);
                    ExpandedNodeId propertyId = ExpandedNodeId.Parse(propertyNode);
                    nodes.Add(new WotCanonicalViewNode(
                        wrapperId, definition.ResourceXid, WotCanonicalViewNodeRole.Group, NodeClass.Object,
                        new WotBrowsePathElement(allocationNamespaceUri, link.Key),
                        ObjectTypeIds.WoTProjectionGroupType, default, default, 0));
                    nodes.Add(new WotCanonicalViewNode(
                        propertyId, definition.ResourceXid, WotCanonicalViewNodeRole.ProjectionRoot, NodeClass.Variable,
                        new WotBrowsePathElement(Namespaces.WotCon, "ProjectionRoot"),
                        new ExpandedNodeId(Ua.VariableTypeIds.PropertyType), new ExpandedNodeId(Ua.DataTypeIds.NodeId),
                        ExpandedNodeId.Parse(child.ViewNodeId), 0));
                    references.Add(new WotCanonicalViewReference(
                        wrapperId, hasTypeDefinition, ObjectTypeIds.WoTProjectionGroupType, false));
                    references.Add(new WotCanonicalViewReference(wrapperId, hasProperty, propertyId, false));
                    references.Add(new WotCanonicalViewReference(propertyId, hasProperty, wrapperId, true));
                    references.Add(new WotCanonicalViewReference(
                        propertyId, hasTypeDefinition, new ExpandedNodeId(Ua.VariableTypeIds.PropertyType), false));
                    AddCanonicalDirectReferences(
                        wrapperId, child, definitions, sources, allocationNamespaceUri, references);
                }
            }
            return new WotCanonicalViewState(
                logicalServerUri, allocationNamespaceUri, views.ToArrayOf(), nodes.ToArrayOf(),
                references.OrderBy(reference => reference.SourceId.ToString(), StringComparer.Ordinal)
                    .ThenBy(reference => reference.ReferenceTypeId.ToString(), StringComparer.Ordinal)
                    .ThenBy(reference => reference.IsInverse)
                    .ThenBy(reference => reference.TargetId.ToString(), StringComparer.Ordinal).ToArrayOf(),
                definitions.Values.OrderBy(definition => definition.ResourceXid, StringComparer.Ordinal).ToArrayOf(),
                sources.Values.OrderBy(source => source.NodeId, StringComparer.Ordinal).ToArrayOf());
        }

        internal static WotCanonicalViewState RestoreCanonicalGraph(WotCanonicalViewState captured)
        {
            var definitions = new Dictionary<string, CanonicalViewDefinition>(StringComparer.Ordinal);
            foreach (CanonicalViewDefinition definition in captured.Definitions)
            {
                definitions.Add(definition.ResourceXid, definition);
            }
            var sources = new Dictionary<string, CanonicalViewSource>(StringComparer.Ordinal);
            foreach (CanonicalViewSource source in captured.SourceFacts)
            {
                sources.Add(source.NodeId, source);
            }
            var memberships = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var omissions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string resource in definitions.Keys)
            {
                CollectCanonicalMembership(resource, captured.AllocationNamespaceUri, definitions, sources,
                    memberships, omissions, new HashSet<string>(StringComparer.Ordinal));
            }
            return BuildCanonicalImage(
                captured.LogicalServerUri, captured.AllocationNamespaceUri, definitions, sources, captured);
        }

        private static void CollectCanonicalMembership(
            string resource,
            string allocationNamespaceUri,
            Dictionary<string, CanonicalViewDefinition> definitions,
            Dictionary<string, CanonicalViewSource> sources,
            Dictionary<string, HashSet<string>> memberships,
            Dictionary<string, HashSet<string>> omissions,
            HashSet<string> active)
        {
            if (memberships.ContainsKey(resource))
            {
                return;
            }
            if (active.Count >= 128 || !active.Add(resource))
            {
                throw new ArgumentException("The canonical organizing graph contains a cycle or exceeds its depth bound.");
            }
            if (!definitions.TryGetValue(resource, out CanonicalViewDefinition? definition))
            {
                throw new ArgumentException("An organizing link refers to an unknown canonical Resource.");
            }
            var members = new HashSet<string>(StringComparer.Ordinal);
            var omitted = new HashSet<string>(definition.Omissions, StringComparer.Ordinal);
            foreach (string member in definition.Members)
            {
                if (string.Equals(member, Ua.ObjectIds.Server.ToString(), StringComparison.Ordinal))
                {
                    throw new ArgumentException("A canonical View cannot include the Server Object.");
                }
                if (!sources.TryGetValue(member, out CanonicalViewSource? source))
                {
                    throw new ArgumentException("A selected member is not in the captured source catalogue.");
                }
                if (source.Available)
                {
                    members.Add(member);
                }
                else
                {
                    omitted.Add($"Source Node '{member}' is not materialized in this image.");
                }
            }
            foreach (WotCanonicalViewLink link in definition.Links)
            {
                CollectCanonicalMembership(
                    link.ResourceXid, allocationNamespaceUri, definitions, sources, memberships, omissions, active);
                CanonicalViewDefinition child = definitions[link.ResourceXid];
                members.Add(CanonicalRoleId(
                    allocationNamespaceUri, "group", child.ViewNodeId, definition.ViewNodeId, link.Key));
                members.Add(CanonicalRoleId(
                    allocationNamespaceUri, "projection-root", child.ViewNodeId, definition.ViewNodeId, link.Key));
                members.UnionWith(memberships[link.ResourceXid]);
                omitted.UnionWith(omissions[link.ResourceXid]);
            }
            active.Remove(resource);
            memberships.Add(resource, members);
            omissions.Add(resource, omitted);
        }

        private static void AddCanonicalDirectReferences(
            ExpandedNodeId parent,
            CanonicalViewDefinition definition,
            Dictionary<string, CanonicalViewDefinition> definitions,
            Dictionary<string, CanonicalViewSource> sources,
            string allocationNamespaceUri,
            HashSet<WotCanonicalViewReference> references)
        {
            ExpandedNodeId organizes = new(Ua.ReferenceTypeIds.Organizes);
            foreach (string member in definition.Members)
            {
                if (sources[member].Available)
                {
                    references.Add(new WotCanonicalViewReference(parent, organizes, ExpandedNodeId.Parse(member), false));
                }
            }
            foreach (WotCanonicalViewLink link in definition.Links)
            {
                CanonicalViewDefinition child = definitions[link.ResourceXid];
                references.Add(new WotCanonicalViewReference(parent, organizes, ExpandedNodeId.Parse(
                    CanonicalRoleId(allocationNamespaceUri, "group", child.ViewNodeId, definition.ViewNodeId, link.Key)),
                    false));
            }
        }

        internal static string CanonicalRoleId(string namespaceUri, string role, params string[] elements)
        {
            return "nsu=" + CoreUtils.EscapeUri(namespaceUri) + ";s=WoT/" + role + "/" +
                string.Join(".", elements.Select(element =>
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(element))
                        .TrimEnd('=').Replace('+', '-').Replace('/', '_')));
        }

        private static void ClaimCanonicalRole(Dictionary<string, string> roles, string nodeId, string role)
        {
            if (!roles.TryAdd(nodeId, role) &&
                !string.Equals(roles[nodeId], role, StringComparison.Ordinal))
            {
                throw new ArgumentException("A canonical identity collides with another owner or allocation role.");
            }
        }

        private static WotCanonicalViewPublication? FindCanonicalView(WotCanonicalViewState? state, string resource)
        {
            if (state is not null)
            {
                foreach (WotCanonicalViewPublication view in state.Views)
                {
                    if (string.Equals(view.ResourceXid, resource, StringComparison.Ordinal))
                    {
                        return view;
                    }
                }
            }
            return null;
        }

        private static CanonicalViewDefinition? FindCanonicalDefinition(WotCanonicalViewState? state, string resource)
        {
            if (state is not null)
            {
                foreach (CanonicalViewDefinition definition in state.Definitions)
                {
                    if (string.Equals(definition.ResourceXid, resource, StringComparison.Ordinal))
                    {
                        return definition;
                    }
                }
            }
            return null;
        }

        private static bool SameCanonicalMembership(
            ArrayOf<ExpandedNodeId> previous, HashSet<string> current)
        {
            if (previous.Count != current.Count)
            {
                return false;
            }
            foreach (ExpandedNodeId nodeId in previous)
            {
                if (!current.Contains(nodeId.ToString()))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameCanonicalPublication(WotCanonicalViewPublication left, WotCanonicalViewPublication right)
        {
            if (left.Active != right.Active || left.Requested != right.Requested ||
                left.ViewVersion != right.ViewVersion || left.MaterializedNodeCount != right.MaterializedNodeCount ||
                left.ViewNodeId != right.ViewNodeId || left.ResourceNodeId != right.ResourceNodeId ||
                left.MembershipDigest != right.MembershipDigest || left.Omissions.Count != right.Omissions.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Omissions.Count; i++)
            {
                if (!string.Equals(left.Omissions[i], right.Omissions[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameCanonicalDefinition(CanonicalViewDefinition left, CanonicalViewDefinition right)
        {
            return left.ResourceNodeId == right.ResourceNodeId && left.ViewNodeId == right.ViewNodeId &&
                left.DocumentKind == right.DocumentKind && left.Scenario == right.Scenario &&
                left.Requested == right.Requested &&
                left.Members.SequenceEqual(right.Members, StringComparer.Ordinal) &&
                left.Links.SequenceEqual(right.Links) &&
                left.Omissions.SequenceEqual(right.Omissions, StringComparer.Ordinal);
        }

        private sealed class CanonicalNodeManagerFactory :
            IAsyncNodeManagerFactory, IRequestCallbackSafeNodeManagerFactory
        {
            public CanonicalNodeManagerFactory(
                WotCanonicalViewState state, NodeManagerRegistration? previous, WotPreparedSourceImage? sources)
            {
                m_state = state;
                m_previous = previous;
                m_sources = sources;
            }

            public ArrayOf<string> NamespacesUris =>
                WotProjectionViewNodeManager.CanonicalNamespaces(m_state).ToArrayOf();
            public bool AllowLifecycleFromRequestCallback => true;

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_previous?.NodeManager is WotProjectionViewNodeManager previous)
                {
                    previous.ValidateCanonicalServer(server);
                }
                // Ownership transfers to the lifecycle through the returned factory result.
                // TODO: Remove when CA2000 recognizes ValueTask factory ownership transfer.
#pragma warning disable CA2000
                var manager = new WotProjectionViewNodeManager(
                    server, configuration, server.Telemetry.CreateLogger<WotProjectionViewNodeManager>(),
                    m_state, m_previous?.NodeManager, m_sources);
#pragma warning restore CA2000
                return new ValueTask<IAsyncNodeManager>(manager);
            }

            private readonly WotCanonicalViewState m_state;
            private readonly NodeManagerRegistration? m_previous;
            private readonly WotPreparedSourceImage? m_sources;
        }
    }
}
