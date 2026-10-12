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
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// An organizing link to a canonical projection Resource.
    /// </summary>
    /// <param name="ResourceXid">The resource xid.</param>
    public readonly record struct WotCanonicalViewLink(string ResourceXid, string Key);

    /// <summary>
    /// A known source Node in the captured image; unavailable Nodes are reported as omissions.
    /// </summary>
    /// <param name="NodeId">The node identifier.</param>
    /// <param name="NodeClass">The node class.</param>
    /// <param name="Available">The available flag supplied by the caller.</param>
    public readonly record struct WotCanonicalViewSource(NodeId NodeId, NodeClass NodeClass, bool Available = true);

    /// <summary>
    /// Captured, immutable inputs to pure canonical View planning.
    /// </summary>
    public sealed class WotCanonicalViewGraphContext
    {
        /// <summary>
        /// Captures the logical server, allocation namespace, namespace table and complete source catalogue.
        /// </summary>
        /// <param name="logicalServerUri">The logical server URI used to initialize the instance.</param>
        /// <param name="allocationNamespaceUri">The allocation namespace URI used to initialize the instance.</param>
        /// <param name="namespaceUris">The namespace URIs used to initialize the instance.</param>
        /// <param name="sources">The sources used to initialize the instance.</param>
        public WotCanonicalViewGraphContext(
            string logicalServerUri,
            string allocationNamespaceUri,
            NamespaceTable namespaceUris,
            ArrayOf<WotCanonicalViewSource> sources)
        {
            RequireUri(logicalServerUri, nameof(logicalServerUri));
            RequireUri(allocationNamespaceUri, nameof(allocationNamespaceUri));
            if (string.Equals(allocationNamespaceUri, global::Opc.Ua.Namespaces.OpcUa, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Canonical allocations require a non-Core namespace.", nameof(allocationNamespaceUri));
            }
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            LogicalServerUri = logicalServerUri;
            AllocationNamespaceUri = allocationNamespaceUri;
            m_namespaces = new string[namespaceUris.Count];
            for (int i = 0; i < m_namespaces.Length; i++)
            {
                m_namespaces[i] = namespaceUris.GetString((uint)i)
                    ?? throw new ArgumentException("The namespace table is incomplete.", nameof(namespaceUris));
            }
            var copy = new WotCanonicalViewSource[sources.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = sources[i];
            }
            Sources = copy.ToArrayOf();
        }

        /// <summary>
        /// Gets the immutable logical server identity, independently of store/refresh epochs.
        /// </summary>
        public string LogicalServerUri { get; }

        /// <summary>
        /// Gets the host-owned namespace for role-separated generated identities.
        /// </summary>
        public string AllocationNamespaceUri { get; }

        /// <summary>
        /// Gets the captured source facts, not executing source Nodes.
        /// </summary>
        public ArrayOf<WotCanonicalViewSource> Sources { get; }

        /// <param name="nodeId">The node identifier.</param>
        internal string Portable(NodeId nodeId)
        {
            if (nodeId.IsNull || nodeId.NamespaceIndex >= m_namespaces.Length)
            {
                throw new ArgumentException("A canonical graph identity must resolve in the captured namespace table.");
            }
            string identifier = nodeId.WithNamespaceIndex(0).ToString();
            string portable = nodeId.NamespaceIndex == 0
                ? identifier
                : "nsu=" + CoreUtils.EscapeUri(m_namespaces[nodeId.NamespaceIndex]) + ";" + identifier;
            return WotPortableIdentity.CanonicalNodeId(portable);
        }

        internal static void RequireUri(string value, string parameter)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !uri.IsWellFormedOriginalString())
            {
                throw new ArgumentException("An absolute URI is required.", parameter);
            }
        }

        private readonly string[] m_namespaces;
    }

    /// <summary>
    /// The role of an owned organizational Node, never an executable source affordance.
    /// </summary>
    public enum WotCanonicalViewNodeRole
    {
        /// <summary>
        /// The canonical View.
        /// </summary>
        View,

        /// <summary>
        /// An organizational occurrence Object.
        /// </summary>
        Group,

        /// <summary>
        /// Its mandatory canonical-root Property.
        /// </summary>
        ProjectionRoot,

        /// <summary>
        /// A View's standard version Property.
        /// </summary>
        ViewVersion
    }

    /// <summary>
    /// An immutable owned-Node descriptor without a live NodeState reference.
    /// </summary>
    public sealed class WotCanonicalViewNode
    {
        /// <summary>
        /// Initializes a new <c>WotCanonicalViewNode</c> instance.
        /// </summary>
        /// <param name="nodeId">The node ID used to initialize the instance.</param>
        /// <param name="resourceXid">The resource xid used to initialize the instance.</param>
        /// <param name="role">The role used to initialize the instance.</param>
        /// <param name="nodeClass">The node class used to initialize the instance.</param>
        /// <param name="browseName">The browse name used to initialize the instance.</param>
        /// <param name="typeDefinition">The type definition used to initialize the instance.</param>
        /// <param name="dataType">The data type used to initialize the instance.</param>
        /// <param name="nodeIdValue">The node ID value used to initialize the instance.</param>
        /// <param name="versionValue">The version value used to initialize the instance.</param>
        internal WotCanonicalViewNode(
            ExpandedNodeId nodeId,
            string resourceXid,
            WotCanonicalViewNodeRole role,
            NodeClass nodeClass,
            WotBrowsePathElement browseName,
            ExpandedNodeId typeDefinition,
            ExpandedNodeId dataType,
            ExpandedNodeId nodeIdValue,
            uint versionValue)
        {
            NodeId = nodeId;
            ResourceXid = resourceXid;
            Role = role;
            NodeClass = nodeClass;
            BrowseName = browseName;
            TypeDefinition = typeDefinition;
            DataType = dataType;
            NodeIdValue = nodeIdValue;
            VersionValue = versionValue;
        }

        /// <summary>
        /// Gets the portable Node identity.
        /// </summary>
        public ExpandedNodeId NodeId { get; }

        /// <summary>
        /// Gets the owning canonical Resource.
        /// </summary>
        public string ResourceXid { get; }

        /// <summary>
        /// Gets the allocation role.
        /// </summary>
        public WotCanonicalViewNodeRole Role { get; }

        /// <summary>
        /// Gets the NodeClass.
        /// </summary>
        public NodeClass NodeClass { get; }

        /// <summary>
        /// Gets the namespace-qualified BrowseName without a session index.
        /// </summary>
        public WotBrowsePathElement BrowseName { get; }

        /// <summary>
        /// Gets the type definition, or Null for a View.
        /// </summary>
        public ExpandedNodeId TypeDefinition { get; }

        /// <summary>
        /// Gets the Property data type, or Null for non-Variables.
        /// </summary>
        public ExpandedNodeId DataType { get; }

        /// <summary>
        /// Gets the canonical child View named by a ProjectionRoot Property.
        /// </summary>
        public ExpandedNodeId NodeIdValue { get; }

        /// <summary>
        /// Gets the committed-candidate version for a ViewVersion Property.
        /// </summary>
        public uint VersionValue { get; }

        /// <summary>
        /// Gets the scalar rank of organizational Properties.
        /// </summary>
        public int ValueRank => NodeClass == NodeClass.Variable ? ValueRanks.Scalar : ValueRanks.Any;

        /// <summary>
        /// Gets read-only access for organizational Properties.
        /// </summary>
        public byte AccessLevel => NodeClass == NodeClass.Variable ? AccessLevels.CurrentRead : AccessLevels.None;
    }

    /// <summary>
    /// A portable Reference in the complete organizational publication.
    /// </summary>
    /// <param name="SourceId">The source identifier.</param>
    /// <param name="ReferenceTypeId">The reference type identifier.</param>
    /// <param name="TargetId">The target identifier.</param>
    /// <param name="IsInverse">The inverse flag supplied by the caller.</param>
    public readonly record struct WotCanonicalViewReference(
        ExpandedNodeId SourceId, ExpandedNodeId ReferenceTypeId, ExpandedNodeId TargetId, bool IsInverse);

    /// <summary>
    /// The immutable candidate metadata and retained history of one canonical Resource.
    /// </summary>
    public sealed class WotCanonicalViewPublication
    {
        /// <summary>
        /// Initializes a new <c>WotCanonicalViewPublication</c> instance.
        /// </summary>
        /// <param name="resourceXid">The resource xid used to initialize the instance.</param>
        /// <param name="resourceNodeId">The resource node ID used to initialize the instance.</param>
        /// <param name="viewNodeId">The view node ID used to initialize the instance.</param>
        /// <param name="requested">The requested flag used to initialize <c>WotCanonicalViewPublication</c>.</param>
        /// <param name="active">The active flag used to initialize <c>WotCanonicalViewPublication</c>.</param>
        /// <param name="version">The version used to initialize the instance.</param>
        /// <param name="digest">The digest used to initialize the instance.</param>
        /// <param name="membership">The membership used to initialize the instance.</param>
        /// <param name="omissions">The omissions used to initialize the instance.</param>
        /// <param name="count">The count used to initialize <c>WotCanonicalViewPublication</c>.</param>
        internal WotCanonicalViewPublication(
            string resourceXid,
            ExpandedNodeId resourceNodeId,
            ExpandedNodeId viewNodeId,
            bool requested,
            bool active,
            uint version,
            ByteString digest,
            ArrayOf<ExpandedNodeId> membership,
            ArrayOf<string> omissions,
            int count)
        {
            ResourceXid = resourceXid;
            ResourceNodeId = resourceNodeId;
            ViewNodeId = viewNodeId;
            Requested = requested;
            Active = active;
            ViewVersion = version;
            MembershipDigest = digest;
            Membership = membership;
            Omissions = omissions;
            MaterializedNodeCount = count;
        }

        /// <summary>
        /// Gets the logical Resource Xid.
        /// </summary>
        public string ResourceXid { get; }

        /// <summary>
        /// Gets the portable logical Resource NodeId.
        /// </summary>
        public ExpandedNodeId ResourceNodeId { get; }

        /// <summary>
        /// Gets its retained canonical View identity.
        /// </summary>
        public ExpandedNodeId ViewNodeId { get; }

        /// <summary>
        /// Gets whether this Resource is an active root request.
        /// </summary>
        public bool Requested { get; }

        /// <summary>
        /// Gets whether the remaining request closure reaches this View.
        /// </summary>
        public bool Active { get; }

        /// <summary>
        /// Gets the positive candidate publication token, retained on retirement.
        /// </summary>
        public uint ViewVersion { get; }

        /// <summary>
        /// Gets the full semantic-membership SHA-256.
        /// </summary>
        public ByteString MembershipDigest { get; }

        /// <summary>
        /// Gets the exact effective membership, including organizational occurrences.
        /// </summary>
        public ArrayOf<ExpandedNodeId> Membership { get; }

        /// <summary>
        /// Gets explicitly reported omissions.
        /// </summary>
        public ArrayOf<string> Omissions { get; }

        /// <summary>
        /// Gets the canonical View plus its owned wrappers and ProjectionRoot Properties.
        /// </summary>
        public int MaterializedNodeCount { get; }
    }

    /// <summary>
    /// An immutable complete canonical View graph, independent of operational publication.
    /// </summary>
    public sealed partial class WotCanonicalViewState
    {
        /// <summary>
        /// Initializes a new <c>WotCanonicalViewState</c> instance.
        /// </summary>
        /// <param name="logicalServerUri">The logical server URI used to initialize the instance.</param>
        /// <param name="allocationNamespaceUri">The allocation namespace URI used to initialize the instance.</param>
        /// <param name="views">The views used to initialize the instance.</param>
        /// <param name="nodes">The nodes used to initialize the instance.</param>
        /// <param name="references">The references used to initialize the instance.</param>
        /// <param name="definitions">The definitions used to initialize the instance.</param>
        /// <param name="sources">The sources used to initialize the instance.</param>
        internal WotCanonicalViewState(
            string logicalServerUri,
            string allocationNamespaceUri,
            ArrayOf<WotCanonicalViewPublication> views,
            ArrayOf<WotCanonicalViewNode> nodes,
            ArrayOf<WotCanonicalViewReference> references,
            ArrayOf<CanonicalViewDefinition> definitions,
            ArrayOf<CanonicalViewSource> sources)
        {
            LogicalServerUri = logicalServerUri;
            AllocationNamespaceUri = allocationNamespaceUri;
            Views = views;
            Nodes = nodes;
            References = references;
            Definitions = definitions;
            SourceFacts = sources;
        }

        /// <summary>
        /// Gets the logical server owning the publication.
        /// </summary>
        public string LogicalServerUri { get; }

        /// <summary>
        /// Gets its non-Core allocation namespace.
        /// </summary>
        public string AllocationNamespaceUri { get; }

        /// <summary>
        /// Gets active Views and retained inactive identity/token history.
        /// </summary>
        public ArrayOf<WotCanonicalViewPublication> Views { get; }

        /// <summary>
        /// Gets only owned organizational Nodes, never copies of source affordances.
        /// </summary>
        public ArrayOf<WotCanonicalViewNode> Nodes { get; }

        /// <summary>
        /// Gets the complete emitted Reference map.
        /// </summary>
        public ArrayOf<WotCanonicalViewReference> References { get; }

        /// <summary>
        /// Gets the definitions for <c>WotCanonicalViewState</c>.
        /// </summary>
        internal ArrayOf<CanonicalViewDefinition> Definitions { get; }

        /// <summary>
        /// Gets the source facts for <c>WotCanonicalViewState</c>.
        /// </summary>
        internal ArrayOf<CanonicalViewSource> SourceFacts { get; }

        /// <summary>
        /// Serializes a complete portable publication, retaining inactive identity and token history.
        /// </summary>
        public ByteString ToByteString()
        {
            return SerializeCanonicalState(this);
        }

        /// <summary>
        /// Validates and restores a complete canonical graph; absence is not a serialized empty graph.
        /// </summary>
        /// <param name="payload">The byte string.</param>
        public static WotCanonicalViewState Parse(ByteString payload)
        {
            return ParseCanonicalState(payload);
        }

        /// <summary>
        /// Restores a publication only when its logical identity and known Node facts match the captured source image.
        /// </summary>
        /// <param name="payload">The byte string.</param>
        /// <param name="context">The WoT canonical view graph context.</param>
        public static WotCanonicalViewState Restore(ByteString payload, WotCanonicalViewGraphContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            WotCanonicalViewState state = Parse(payload);
            if (!string.Equals(state.LogicalServerUri, context.LogicalServerUri, StringComparison.Ordinal) ||
                !string.Equals(state.AllocationNamespaceUri, context.AllocationNamespaceUri, StringComparison.Ordinal))
            {
                throw new FormatException("The canonical publication belongs to another logical server or namespace.");
            }
            try
            {
                var sources = new Dictionary<string, WotCanonicalViewSource>(StringComparer.Ordinal);
                foreach (WotCanonicalViewSource source in context.Sources)
                {
                    if (!sources.TryAdd(context.Portable(source.NodeId), source))
                    {
                        throw new FormatException("The captured image has duplicate source identities.");
                    }
                }
                foreach (CanonicalViewSource source in state.SourceFacts)
                {
                    bool found = sources.TryGetValue(source.NodeId, out WotCanonicalViewSource current);
                    if ((source.Available && !found) ||
                        (found && (current.Available != source.Available || current.NodeClass != source.NodeClass)))
                    {
                        throw new FormatException("The captured source image disagrees with canonical publication facts.");
                    }
                }
                _ = WotProjectionViewBuilder.PrepareCanonicalGraph(context, state, [], []);
                return state;
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("The captured image violates canonical publication identities.", exception);
            }
        }

    }

    /// <summary>
    /// A pure candidate and its complete affected-resource footprint, not a commit decision.
    /// </summary>
    public sealed class WotCanonicalViewPreparation
    {
        /// <summary>
        /// Initializes a new <c>WotCanonicalViewPreparation</c> instance.
        /// </summary>
        /// <param name="state">The state used to initialize the instance.</param>
        /// <param name="affectedResourceXids">The affected resource xids used to initialize the instance.</param>
        /// <param name="hasChanges">The has-changes flag used to initialize <c>WotCanonicalViewPreparation</c>.</param>
        internal WotCanonicalViewPreparation(
            WotCanonicalViewState state, ArrayOf<string> affectedResourceXids, bool hasChanges)
        {
            State = state;
            AffectedResourceXids = affectedResourceXids;
            HasChanges = hasChanges;
        }

        /// <summary>
        /// Gets the complete resulting graph and retained history.
        /// </summary>
        public WotCanonicalViewState State { get; }

        /// <summary>
        /// Gets every changed ancestor, canonical child, updated root and retired root.
        /// </summary>
        public ArrayOf<string> AffectedResourceXids { get; }

        /// <summary>
        /// Gets whether the complete candidate differs from the expected graph.
        /// </summary>
        public bool HasChanges { get; }
    }

    /// <summary>
    /// Carries the canonical view definition data.
    /// </summary>
    /// <param name="ResourceXid">The resource xid.</param>
    /// <param name="ResourceNodeId">The resource node ID.</param>
    /// <param name="ViewNodeId">The view node ID.</param>
    /// <param name="DocumentKind">The document kind.</param>
    /// <param name="Requested">The requested flag supplied by the caller.</param>
    internal sealed record CanonicalViewDefinition(
        string ResourceXid,
        string ResourceNodeId,
        string ViewNodeId,
        int DocumentKind,
        string Scenario,
        bool Requested,
        string[] Members,
        WotCanonicalViewLink[] Links,
        string[] Omissions);

    /// <summary>
    /// Carries the canonical view source data.
    /// </summary>
    /// <param name="NodeId">The node ID.</param>
    /// <param name="NodeClass">The node class.</param>
    /// <param name="Available">The available flag supplied by the caller.</param>
    internal sealed record CanonicalViewSource(string NodeId, NodeClass NodeClass, bool Available);
}
