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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Generic OPC UA — OpenUSD connector. It discovers an
    /// <c>OpenUsdRepresentation</c> and its live bindings through the Part 1
    /// <c>Server/OpenUSD/Representations</c> registry, subscribes to the bound
    /// source Variables, applies the declared conversion, and writes the target
    /// USD attributes into an <see cref="IUsdSink"/>. It is domain-agnostic — it
    /// knows only the OpenUSD binding model, never "pump".
    /// </summary>
    public sealed partial class OpenUsdConnector : IAsyncDisposable, IDisposable
    {
        private readonly ISession m_session;
        private readonly IUsdSink m_sink;
        private readonly bool m_enableCommands;
        private readonly Func<string, CancellationToken, Task<ISession>>? m_remoteSessionFactory;
        private readonly ushort m_ns;
        private readonly NodeId m_representationTypeId;
        private readonly Dictionary<NodeId, OpenUsdIntentProfile> m_bindingTypeIntents;
        private readonly NodeId m_componentTypeId;
        private readonly NodeId m_assetTypeId;
        private Subscription? m_subscription;
        private readonly List<OpenUsdConnector> m_remoteConnectors = [];
        private readonly bool m_ownsSession;
        private readonly OpenUsdConnectorOptions m_options;
        private readonly ITelemetryContext? m_telemetry;
        private readonly ILogger m_logger;
        private NodeId m_openUsdRootId = NodeId.Null;

        /// <summary>
        /// Creates a connector with default options and no telemetry.
        /// </summary>
        public OpenUsdConnector(ISession session, IUsdSink sink)
            : this(session, sink, new OpenUsdConnectorOptions(), telemetry: null, ownsSession: false)
        {
        }

        /// <summary>
        /// Creates a connector, opting into command actuation (fail-closed by default).
        /// </summary>
        public OpenUsdConnector(ISession session, IUsdSink sink, bool enableCommands)
            : this(session, sink, new OpenUsdConnectorOptions { EnableCommands = enableCommands },
                telemetry: null, ownsSession: false)
        {
        }

        /// <summary>
        /// Creates a connector from an <see cref="OpenUsdConnectorOptions"/>, optionally
        /// threading an <see cref="ITelemetryContext"/> for logging. This is the
        /// recommended entry point for advanced scenarios (cross-server federation,
        /// asset size limits); the DI <c>AddOpenUsdConnector(...)</c> extensions build on
        /// it.
        /// </summary>
        public OpenUsdConnector(ISession session, IUsdSink sink, OpenUsdConnectorOptions options,
            ITelemetryContext? telemetry = null)
            : this(session, sink, options, telemetry, ownsSession: false)
        {
        }

        /// <summary>
        /// ownsSession is set for connector-owned remote sessions (§5.14 cross-server
        /// federation): the connector opened this session via the remote-session factory
        /// and therefore closes it on DisposeAsync (unlike the caller-owned primary session).
        /// </summary>
        /// <param name="session"></param>
        /// <param name="sink"></param>
        /// <param name="options"></param>
        /// <param name="telemetry"></param>
        /// <param name="ownsSession"></param>
        private OpenUsdConnector(ISession session, IUsdSink sink, OpenUsdConnectorOptions options,
            ITelemetryContext? telemetry, bool ownsSession)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_sink = sink ?? throw new ArgumentNullException(nameof(sink));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_enableCommands = options.EnableCommands;
            m_remoteSessionFactory = options.RemoteSessionFactory;
            m_ownsSession = ownsSession;
            m_telemetry = telemetry;
            m_logger = telemetry?.CreateLogger<OpenUsdConnector>()
                ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            m_ns = (ushort)m_session.NamespaceUris.GetIndex(OpenUsdModel.NamespaceUri);
            m_representationTypeId = new NodeId(OpenUsdModel.RepresentationTypeId, m_ns);
            m_bindingTypeIntents = new Dictionary<NodeId, OpenUsdIntentProfile>
            {
                { new NodeId(OpenUsdModel.ValueChangeBindingTypeId, m_ns), OpenUsdIntentProfile.UaToUsdTelemetry },
                { new NodeId(OpenUsdModel.AlarmBindingTypeId, m_ns), OpenUsdIntentProfile.UaAlarmToUsd },
                { new NodeId(OpenUsdModel.HistoryBindingTypeId, m_ns), OpenUsdIntentProfile.UaHistoryToUsd },
                { new NodeId(OpenUsdModel.CommandBindingTypeId, m_ns), OpenUsdIntentProfile.UsdToUaCommand }
            };
            m_componentTypeId = new NodeId(OpenUsdModel.ComponentBindingTypeId, m_ns);
            m_assetTypeId = new NodeId(OpenUsdModel.AssetTypeId, m_ns);
        }

        public sealed class BindingInfo
        {
            public NodeId SourceNodeId { get; set; }
            public string? PrimPath { get; set; }
            public string? PropertyName { get; set; }
            public OpenUsdRenderTargetKind Kind { get; set; }
            public double Scale { get; set; } = 1.0;
            public double Offset { get; set; }
            public OpenUsdIntentProfile Intent { get; set; } = OpenUsdIntentProfile.UaToUsdTelemetry;
            public OpenUsdSignalRole SignalRole { get; set; } = OpenUsdSignalRole.Observable;
            public string? SourceSemanticId { get; set; }
            public OpenUsdAlarmAspect? AlarmAspect { get; set; }
            public bool TimeSampled { get; set; }
            public NodeId CommandTargetNodeId { get; set; }
            public string? CommandTriggerPropertyName { get; set; }

            /// <summary>
            /// §5.4 <c>Enabled</c>: <c>false</c> is a tombstone that suppresses an
            /// inherited binding. Absent means enabled, so the default is true.
            /// </summary>
            public bool Enabled { get; set; } = true;

            /// <summary>
            /// §5.3/§5.4 declaration identity — half of the effective binding key.
            /// </summary>
            public Guid BindingDefinitionId { get; set; }

            /// <summary>
            /// §5.7 instance-portable source path from the represented Object.
            /// </summary>
            public RelativePath? SourceBrowsePath { get; set; }

            /// <summary>
            /// §5.10 optional Method to Call instead of writing the target Variable.
            /// </summary>
            public NodeId CommandMethodId { get; set; }

            /// <summary>
            /// §5.8 step (1) unit assertion of the source value.
            /// </summary>
            public EUInformation? SourceEngineeringUnits { get; set; }

            /// <summary>
            /// §5.8 step (1) unit requested for the target value.
            /// </summary>
            public EUInformation? TargetEngineeringUnits { get; set; }
        }

        public sealed class ComponentInfo
        {
            public NodeId NodeId { get; set; }
            public OpenUsdCardinality Cardinality { get; set; } = OpenUsdCardinality.One;
            public OpenUsdCompositionArc Arc { get; set; } = OpenUsdCompositionArc.Child;
            public NodeId ComponentReferenceType { get; set; }
            public NodeId ComponentTypeDefinition { get; set; }
            public string? TargetPrimPath { get; set; }
            public string? TargetPrimNameSource { get; set; }
            public string? ComponentAssetReference { get; set; }
            public NodeId ComponentRepresentation { get; set; }
            public bool Dynamic { get; set; }
            public NodeId ChangeEventSource { get; set; }
            public string? ComponentServerUri { get; set; }
            public string? ComponentEndpointUrl { get; set; }

            /// <summary>
            /// §5.12 <c>Enabled</c>: <c>false</c> is a tombstone that suppresses an
            /// inherited component binding. Absent means enabled, so the default is true.
            /// </summary>
            public bool Enabled { get; set; } = true;

            /// <summary>
            /// §5.12 declaration identity — half of the effective binding key.
            /// </summary>
            public Guid BindingDefinitionId { get; set; }
        }

        public sealed class RepresentationInfo
        {
            public NodeId NodeId { get; set; }
            public NodeId StageNodeId { get; set; }
            public string? PrimPath { get; set; }
            public string? RootLayerIdentifier { get; set; }
            public ByteString RootLayerDigest { get; set; }
            public OpenUsdDigestAlgorithm DigestAlgorithm { get; set; } = OpenUsdDigestAlgorithm.None;
            public List<BindingInfo> Bindings { get; } = [];
            public List<ComponentInfo> Components { get; } = [];
        }

        /// <summary>
        /// §4.2 fixes the BrowseName (<c>1:OpenUSD</c>) and the parent (the Server
        /// Object) of the OpenUSD facility, never its NodeId — §4.3 leaves instance
        /// NodeIds server-assigned. Discovery therefore browses the Server Object and
        /// matches the BrowseName; the conventional <c>ns=1;s=OpenUSD</c> identifier is
        /// only a fallback for servers that do not expose the Organizes reference.
        /// </summary>
        private async Task<NodeId> FindOpenUsdRootAsync(CancellationToken ct)
        {
            if (!m_openUsdRootId.IsNull)
            {
                return m_openUsdRootId;
            }
            var browseName = new QualifiedName("OpenUSD", m_ns);
            foreach (ReferenceDescription r in await BrowseAsync(ObjectIds.Server, ct)
                .ConfigureAwait(false))
            {
                if (r.BrowseName == browseName)
                {
                    var id = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                    if (!id.IsNull)
                    {
                        m_openUsdRootId = id;
                        return id;
                    }
                }
            }
            return new NodeId("OpenUSD", m_ns);
        }

        /// <summary>
        /// Part 1 discovery: the well-known OpenUSD facility exposes a
        /// Representations registry (Organizes) that lists every
        /// OpenUsdRepresentation in the address space, independent of the
        /// represented object's own hierarchy.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<NodeId> FindFirstRepresentationAsync(CancellationToken ct)
        {
            NodeId rootId = await FindOpenUsdRootAsync(ct).ConfigureAwait(false);
            Dictionary<string, NodeId> rootChildren =
                await ChildrenByNameAsync(rootId, ct).ConfigureAwait(false);
            if (!rootChildren.TryGetValue("Representations", out NodeId registry))
            {
                return NodeId.Null;
            }
            foreach ((NodeId childId, NodeId typeDef) in
                await ChildrenWithTypeAsync(registry, ct).ConfigureAwait(false))
            {
                if (!childId.IsNull && typeDef == m_representationTypeId)
                {
                    return childId;
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Enumerate every representation in the registry (there may be several: the
        /// top asset, plus each component's own representation, plus aggregating
        /// representations such as a production line).
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<List<NodeId>> FindAllRepresentationsAsync(CancellationToken ct)
        {
            var result = new List<NodeId>();
            NodeId rootId = await FindOpenUsdRootAsync(ct).ConfigureAwait(false);
            Dictionary<string, NodeId> rootChildren =
                await ChildrenByNameAsync(rootId, ct).ConfigureAwait(false);
            if (!rootChildren.TryGetValue("Representations", out NodeId registry))
            {
                return result;
            }
            foreach ((NodeId childId, NodeId typeDef) in
                await ChildrenWithTypeAsync(registry, ct).ConfigureAwait(false))
            {
                if (!childId.IsNull && typeDef == m_representationTypeId)
                {
                    result.Add(childId);
                }
            }
            return result;
        }

        public async Task<RepresentationInfo?> DiscoverRepresentationAsync(CancellationToken ct)
        {
            NodeId repNodeId = await FindFirstRepresentationAsync(ct).ConfigureAwait(false);
            return repNodeId.IsNull
                ? null
                : await ReadRepresentationAsync(repNodeId, ct).ConfigureAwait(false);
        }

        public async Task<List<RepresentationInfo>> DiscoverAllRepresentationsAsync(CancellationToken ct)
        {
            var reps = new List<RepresentationInfo>();
            foreach (NodeId repNodeId in await FindAllRepresentationsAsync(ct).ConfigureAwait(false))
            {
                RepresentationInfo? info = await ReadRepresentationAsync(repNodeId, ct).ConfigureAwait(false);
                if (info != null)
                {
                    reps.Add(info);
                }
            }
            return reps;
        }

        private async Task<RepresentationInfo?> ReadRepresentationAsync(NodeId repNodeId, CancellationToken ct)
        {
            var info = new RepresentationInfo { NodeId = repNodeId };
            Dictionary<string, NodeId> repProps = await ChildrenByNameAsync(repNodeId, ct)
                .ConfigureAwait(false);
            info.PrimPath = await ReadStringAsync(repProps, "PrimPath", ct).ConfigureAwait(false);
            info.StageNodeId = await ReadNodeIdAsync(repProps, "Stage", ct).ConfigureAwait(false);
            if (!info.StageNodeId.IsNull)
            {
                Dictionary<string, NodeId> stageProps =
                    await ChildrenByNameAsync(info.StageNodeId, ct).ConfigureAwait(false);
                info.RootLayerIdentifier =
                    await ReadStringAsync(stageProps, "RootLayerIdentifier", ct).ConfigureAwait(false);
                info.RootLayerDigest =
                    await ReadByteStringAsync(stageProps, "RootLayerDigest", ct).ConfigureAwait(false);
                info.DigestAlgorithm = (OpenUsdDigestAlgorithm)await ReadInt32Async(
                    stageProps, "RootLayerDigestAlgorithm", ct).ConfigureAwait(false);
            }

            foreach ((NodeId childId, NodeId typeDef) in await ChildrenWithTypeAsync(repNodeId, ct)
                .ConfigureAwait(false))
            {
                if (childId.IsNull)
                {
                    continue;
                }
                if (!typeDef.IsNull
                    && m_bindingTypeIntents.TryGetValue(typeDef, out OpenUsdIntentProfile intent))
                {
                    Dictionary<string, NodeId> bp = await ChildrenByNameAsync(childId, ct)
                        .ConfigureAwait(false);
                    var b = new BindingInfo
                    {
                        SourceNodeId = await ReadNodeIdAsync(bp, "SourceNodeId", ct).ConfigureAwait(false),
                        PrimPath = await ReadStringAsync(bp, "TargetPrimPath", ct).ConfigureAwait(false),
                        PropertyName = await ReadStringAsync(bp, "TargetPropertyName", ct).ConfigureAwait(false),
                        Kind = (OpenUsdRenderTargetKind)await ReadInt32Async(bp, "RenderTargetKind", ct)
                            .ConfigureAwait(false),
                        Scale = await ReadDoubleAsync(bp, "Scale", 1.0, ct).ConfigureAwait(false),
                        Offset = await ReadDoubleAsync(bp, "Offset", 0.0, ct).ConfigureAwait(false),
                        Intent = intent,
                        SignalRole = (OpenUsdSignalRole)await ReadInt32Async(bp, "SignalRole", ct)
                            .ConfigureAwait(false),
                        SourceSemanticId = await ReadStringAsync(bp, "SourceSemanticId", ct)
                            .ConfigureAwait(false),
                        TimeSampled = await ReadBoolAsync(bp, "TimeSampled", ct).ConfigureAwait(false),
                        CommandTargetNodeId = await ReadNodeIdAsync(bp, "CommandTargetNodeId", ct)
                            .ConfigureAwait(false),
                        CommandTriggerPropertyName = await ReadStringAsync(bp, "CommandTriggerPropertyName", ct)
                            .ConfigureAwait(false),
                        // §5.4: Enabled = false is a tombstone; an absent property means enabled.
                        Enabled = await ReadBoolAsync(bp, "Enabled", true, ct).ConfigureAwait(false),
                        BindingDefinitionId = await ReadGuidAsync(bp, "BindingDefinitionId", ct)
                            .ConfigureAwait(false),
                        SourceBrowsePath = await ReadRelativePathAsync(bp, "SourceBrowsePath", ct)
                            .ConfigureAwait(false),
                        CommandMethodId = await ReadNodeIdAsync(bp, "CommandMethodId", ct)
                            .ConfigureAwait(false),
                        SourceEngineeringUnits = await ReadEuInformationAsync(
                            bp, "SourceEngineeringUnits", ct).ConfigureAwait(false),
                        TargetEngineeringUnits = await ReadEuInformationAsync(
                            bp, "TargetEngineeringUnits", ct).ConfigureAwait(false)
                    };
                    if (bp.ContainsKey("AlarmAspect"))
                    {
                        b.AlarmAspect = (OpenUsdAlarmAspect)await ReadInt32Async(bp, "AlarmAspect", ct)
                            .ConfigureAwait(false);
                    }
                    // §5.7: an absolute TargetPrimPath is used as-is; a relative one is
                    // joined to the representation's PrimPath, never authored at the
                    // layer root; an empty one resolves to the representation's own prim.
                    b.PrimPath = JoinPrimPath(info.PrimPath, b.PrimPath ?? string.Empty);
                    // §5.7 source resolution precedence.
                    await ResolveBindingSourceAsync(info, b, ct).ConfigureAwait(false);
                    info.Bindings.Add(b);
                }
                else if (typeDef == m_componentTypeId)
                {
                    Dictionary<string, NodeId> cp = await ChildrenByNameAsync(childId, ct)
                        .ConfigureAwait(false);
                    var c = new ComponentInfo
                    {
                        NodeId = childId,
                        Cardinality = (OpenUsdCardinality)await ReadInt32Async(cp, "Cardinality", ct)
                            .ConfigureAwait(false),
                        Arc = (OpenUsdCompositionArc)await ReadInt32Async(cp, "CompositionArc", ct)
                            .ConfigureAwait(false),
                        ComponentReferenceType = await ReadNodeIdAsync(cp, "ComponentReferenceType", ct)
                            .ConfigureAwait(false),
                        ComponentTypeDefinition = await ReadNodeIdAsync(cp, "ComponentTypeDefinition", ct)
                            .ConfigureAwait(false),
                        TargetPrimPath = await ReadStringAsync(cp, "TargetPrimPath", ct).ConfigureAwait(false),
                        TargetPrimNameSource = await ReadStringAsync(cp, "TargetPrimNameSource", ct)
                            .ConfigureAwait(false),
                        ComponentAssetReference = await ReadStringAsync(cp, "ComponentAssetReference", ct)
                            .ConfigureAwait(false),
                        ComponentRepresentation = await ReadNodeIdAsync(cp, "ComponentRepresentation", ct)
                            .ConfigureAwait(false),
                        Dynamic = await ReadBoolAsync(cp, "Dynamic", ct).ConfigureAwait(false),
                        ChangeEventSource = await ReadNodeIdAsync(cp, "ChangeEventSource", ct)
                            .ConfigureAwait(false),
                        ComponentServerUri = await ReadStringAsync(cp, "ComponentServerUri", ct)
                            .ConfigureAwait(false),
                        ComponentEndpointUrl = await ReadStringAsync(cp, "ComponentEndpointUrl", ct)
                            .ConfigureAwait(false),
                        // §5.4/§5.12: Enabled = false is a tombstone; absent means enabled.
                        Enabled = await ReadBoolAsync(cp, "Enabled", true, ct).ConfigureAwait(false),
                        BindingDefinitionId = await ReadGuidAsync(cp, "BindingDefinitionId", ct)
                            .ConfigureAwait(false)
                    };
                    info.Components.Add(c);
                }
            }
            return info;
        }

        /// <summary>
        /// §5.7 source resolution precedence: if <c>SourceNodeId</c> is present use it;
        /// else if <c>SourceSemanticId</c> is present resolve it against the represented
        /// Object's subtree by matching the source Variable's semantic annotation
        /// (<c>HasDictionaryEntry</c> target); else resolve <c>SourceBrowsePath</c> from
        /// the represented Object. Zero matches leaves the binding unresolved (no update,
        /// no exception); more than one match raises <c>Bad_TooManyMatches</c>.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task ResolveBindingSourceAsync(
            RepresentationInfo info, BindingInfo b, CancellationToken ct)
        {
            if (!b.SourceNodeId.IsNull)
            {
                return;
            }
            if (string.IsNullOrEmpty(b.SourceSemanticId) && b.SourceBrowsePath == null)
            {
                return;
            }
            NodeId representedObject = await ParentAsync(info.NodeId, ct).ConfigureAwait(false);
            if (representedObject.IsNull)
            {
                return;
            }
            List<NodeId> matches = !string.IsNullOrEmpty(b.SourceSemanticId)
                ? await ResolveBySemanticIdAsync(representedObject, b.SourceSemanticId!, ct)
                    .ConfigureAwait(false)
                : await ResolveByBrowsePathAsync(representedObject, b.SourceBrowsePath!, ct)
                    .ConfigureAwait(false);
            if (matches.Count == 0)
            {
                // Unresolved: the binding simply does not update. Not an error (§5.7).
                return;
            }
            if (matches.Count > 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyMatches,
                    "OpenUSD binding source resolved to more than one Variable.");
            }
            b.SourceNodeId = matches[0];
        }

        /// <summary>
        /// Resolves a binding source by matching the semantic annotation
        /// (HasDictionaryEntry target IRDI or equivalent) of the Variables in the
        /// represented Object's subtree.
        /// </summary>
        private async Task<List<NodeId>> ResolveBySemanticIdAsync(
            NodeId representedObject, string semanticId, CancellationToken ct)
        {
            var matches = new List<NodeId>();
            foreach (NodeId candidate in await VariablesInSubtreeAsync(representedObject, ct)
                .ConfigureAwait(false))
            {
                (ArrayOf<ArrayOf<ReferenceDescription>> results, _) = await m_session.ManagedBrowseAsync(
                    null, null, [candidate], 0, BrowseDirection.Forward,
                    ReferenceTypeIds.HasDictionaryEntry, includeSubtypes: true, 0, ct)
                    .ConfigureAwait(false);
                if (results.Count == 0)
                {
                    continue;
                }
                ArrayOf<ReferenceDescription> refs = results[0];
                for (int i = 0; i < refs.Count; i++)
                {
                    string? name = refs[i].BrowseName.Name;
                    string target = ExpandedNodeId.ToNodeId(refs[i].NodeId, m_session.NamespaceUris)
                        .ToString() ?? string.Empty;
                    if (string.Equals(name, semanticId, StringComparison.Ordinal) ||
                        string.Equals(target, semanticId, StringComparison.Ordinal) ||
                        string.Equals(refs[i].NodeId.ToString(), semanticId, StringComparison.Ordinal))
                    {
                        matches.Add(candidate);
                        break;
                    }
                }
            }
            return matches;
        }

        /// <summary>
        /// Resolves an instance-portable <c>SourceBrowsePath</c> from the represented
        /// Object using TranslateBrowsePathsToNodeIds, keeping only Variable targets.
        /// </summary>
        private async Task<List<NodeId>> ResolveByBrowsePathAsync(
            NodeId representedObject, RelativePath path, CancellationToken ct)
        {
            var matches = new List<NodeId>();
            var toTranslate = new BrowsePath[]
            {
                new() { StartingNode = representedObject, RelativePath = path }
            };
            TranslateBrowsePathsToNodeIdsResponse resp;
            try
            {
                resp = await m_session.TranslateBrowsePathsToNodeIdsAsync(null, toTranslate, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return matches;
            }
            if (resp.Results.Count == 0 || StatusCode.IsNotGood(resp.Results[0].StatusCode))
            {
                return matches;
            }
            ArrayOf<BrowsePathTarget> targets = resp.Results[0].Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                BrowsePathTarget t = targets[i];
                // A remaining path index other than uint.MaxValue means the server could
                // not complete the translation locally: not a usable match.
                if (t.RemainingPathIndex != uint.MaxValue)
                {
                    continue;
                }
                var id = ExpandedNodeId.ToNodeId(t.TargetId, m_session.NamespaceUris);
                if (!id.IsNull && await IsVariableAsync(id, ct).ConfigureAwait(false))
                {
                    matches.Add(id);
                }
            }
            return matches;
        }

        private async Task<List<NodeId>> VariablesInSubtreeAsync(NodeId root, CancellationToken ct)
        {
            var found = new List<NodeId>();
            var seen = new HashSet<NodeId>();
            var queue = new Queue<NodeId>();
            queue.Enqueue(root);
            seen.Add(root);
            // The represented Object's subtree is bounded; two levels of nesting cover the
            // Object -> (Folder) -> Variable shapes the binding model uses.
            int depth = 0;
            while (queue.Count > 0 && depth < 4)
            {
                int level = queue.Count;
                for (int i = 0; i < level; i++)
                {
                    NodeId node = queue.Dequeue();
                    foreach (ReferenceDescription r in await BrowseAsync(node, ct).ConfigureAwait(false))
                    {
                        var id = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                        if (id.IsNull || !seen.Add(id))
                        {
                            continue;
                        }
                        if (r.NodeClass == NodeClass.Variable)
                        {
                            found.Add(id);
                        }
                        else if (r.NodeClass == NodeClass.Object)
                        {
                            queue.Enqueue(id);
                        }
                    }
                }
                depth++;
            }
            return found;
        }

        private async Task<bool> IsVariableAsync(NodeId nodeId, CancellationToken ct)
        {
            var toRead = new ReadValueId[]
            {
                new() { NodeId = nodeId, AttributeId = Attributes.NodeClass }
            };
            ReadResponse response = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, toRead, ct).ConfigureAwait(false);
            if (response.Results.Count == 0 || StatusCode.IsNotGood(response.Results[0].StatusCode))
            {
                return false;
            }
            return VariantConversions.TryGetInt64(response.Results[0].WrappedValue, out long v) &&
                v == (long)NodeClass.Variable;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            List<RepresentationInfo> reps = await DiscoverAllRepresentationsAsync(ct).ConfigureAwait(false);
            if (reps.Count == 0)
            {
                throw new InvalidOperationException("No OpenUSD representation discovered.");
            }
            m_allReps = reps;

            // Twin-BOM integrity (§5.2): if a stage advertises a content digest, verify it
            // against the resolved root-layer content before authoring any opinions into
            // it. A mismatch — or content that cannot be obtained to verify — is
            // fail-closed.
            foreach (RepresentationInfo rep in reps)
            {
                if (rep.RootLayerDigest is { IsNull: false, Length: > 0 } &&
                    rep.DigestAlgorithm != OpenUsdDigestAlgorithm.None &&
                    !await VerifyStageDigestAsync(rep, ct).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "OpenUSD stage RootLayerDigest verification failed — refusing to compose.");
                }
            }

            var subscription = new Subscription(m_session.DefaultSubscription)
            {
                DisplayName = "OpenUsdConnector",
                PublishingInterval = m_options.PublishingIntervalMilliseconds,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true
            };
            m_subscription = subscription;
            m_session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            int bindingCount = 0;
            int monitoredCount = 0;

            foreach (RepresentationInfo rep in reps)
            {
                foreach (BindingInfo b in rep.Bindings)
                {
                    bindingCount++;

                    // Command bindings are actuated on demand (IssueCommandAsync), and
                    // history bindings are replayed via ReplayHistoryAsync — neither is a
                    // live MonitoredItem. Telemetry and alarm bindings subscribe here.
                    // §5.4: Enabled = false is a tombstone — a suppressed binding is not
                    // subscribed at all.
                    if (!b.Enabled
                        || b.SourceNodeId.IsNull
                        || b.Intent == OpenUsdIntentProfile.UsdToUaCommand
                        || b.Intent == OpenUsdIntentProfile.UaHistoryToUsd)
                    {
                        continue;
                    }
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        DisplayName = b.PropertyName ?? "binding",
                        StartNodeId = b.SourceNodeId,
                        AttributeId = Attributes.Value,
                        SamplingInterval = m_options.SamplingIntervalMilliseconds,
                        QueueSize = 5,
                        Handle = b
                    };
                    item.Notification += OnNotification;
                    subscription.AddItem(item);
                    monitoredCount++;
                }
            }
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            m_logger.LiveBindingsSubscribed(bindingCount, reps.Count, monitoredCount);

            // Compose each representation's components into the USD prim tree (§5.12):
            // author child/reference/instance prims and federate to remote servers
            // (§5.14). If any component is Dynamic, watch model-change events (§5.13).
            bool anyDynamic = false;
            NodeId eventSource = ObjectIds.Server;
            foreach (RepresentationInfo rep in reps)
            {
                foreach (ComponentInfo c in rep.Components)
                {
                    await ComposeComponentAsync(rep, c, ct).ConfigureAwait(false);
                    if (c.Dynamic)
                    {
                        anyDynamic = true;
                        if (!c.ChangeEventSource.IsNull)
                        {
                            eventSource = c.ChangeEventSource;
                        }
                    }
                }
            }
            if (anyDynamic)
            {
                await SubscribeModelChangesAsync(eventSource, ct).ConfigureAwait(false);

                // The components above were composed from the address space as it stood
                // *before* this subscription existed, so a change in between was seen by
                // neither. Re-resolve once now that both are in place. The resolve is
                // idempotent and serialized with the event-driven ones.
                await RunRecomposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops streaming: deletes this connector's subscriptions and stops every
        /// connector-owned remote connector. Does not close sessions; use
        /// <see cref="DisposeAsync"/> to also release connector-owned remote sessions.
        /// </summary>
        public Task StopAsync()
        {
            return StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// Stops streaming with a cancellation token. See <see cref="StopAsync()"/>.
        /// </summary>
        public async Task StopAsync(CancellationToken ct)
        {
            foreach (OpenUsdConnector remote in m_remoteConnectors)
            {
                await remote.StopAsync(ct).ConfigureAwait(false);
            }
            if (m_eventSubscription != null)
            {
                await m_eventSubscription.DeleteAsync(true, ct).ConfigureAwait(false);
                m_eventSubscription = null;
            }
            if (m_subscription != null)
            {
                await m_subscription.DeleteAsync(true, ct).ConfigureAwait(false);
                m_subscription = null;
            }
        }

        /// <summary>
        /// Stops streaming and releases owned resources: disposes every connector-owned
        /// remote connector (closing the remote sessions the connector opened), closes
        /// this connector's session when it owns it, and disposes internal primitives.
        /// The caller-provided primary session is never closed.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (OpenUsdConnector remote in m_remoteConnectors)
            {
                await remote.DisposeAsync().ConfigureAwait(false);
            }
            m_remoteConnectors.Clear();
            if (m_ownsSession)
            {
                try
                {
                    await m_session.CloseAsync(10000, closeChannel: true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Best-effort close of the connector-owned (remote) session.
                    m_logger.RemoteSessionCloseFailed(ex);
                }
            }
            Dispose();
        }

        /// <summary>
        /// Synchronous fallback that disposes sync-only primitives. Prefer
        /// <see cref="DisposeAsync"/>, which also stops subscriptions and closes any
        /// connector-owned remote session.
        /// </summary>
        public void Dispose()
        {
            m_subscription?.Dispose();
            m_subscription = null;
            m_eventSubscription?.Dispose();
            m_eventSubscription = null;
            m_recomposeGate.Dispose();
        }

        private void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            if (item.Handle is not BindingInfo b)
            {
                return;
            }
            // One batch per notification: a queued item can deliver several values, and a
            // file-backed sink would otherwise rewrite its whole layer once per value.
            using IDisposable batch = m_sink.BeginBatch();
            foreach (DataValue dv in item.DequeueValues())
            {
                if (StatusCode.IsNotGood(dv.StatusCode))
                {
                    continue;
                }
                Variant usdValue = Convert(b, dv.WrappedValue);
                // §5.8 fail-closed: Convert returns a null Variant whenever it cannot
                // faithfully produce the target value (unsupported RenderTargetKind,
                // undecodable source, unhonourable unit/CRS/datum). The target is then
                // left *unresolved* — no opinion is authored at all — instead of
                // fabricating a substitute such as Offset.
                if (!usdValue.IsNull)
                {
                    m_sink.SetAttribute(b.PrimPath!, b.PropertyName!, usdValue);
                    m_logger.LiveUpdateApplied(
                        b.PrimPath ?? string.Empty, b.PropertyName ?? string.Empty,
                        b.SourceNodeId.ToString());
                }
                else
                {
                    // Silence here is what makes an unresolved target so hard to find: the
                    // prim simply never moves, while every subscription counter says the
                    // data is arriving. Say so instead.
                    m_logger.LiveUpdateUnresolved(
                        b.PrimPath ?? string.Empty, b.PropertyName ?? string.Empty,
                        b.SourceNodeId.ToString(), b.Kind.ToString());
                }
            }
        }

        /// <summary>
        /// Applies the binding's declared <see cref="OpenUsdRenderTargetKind"/> to a
        /// raw source value, returning the USD-side value as a <see cref="Variant"/>
        /// (a <c>double</c> for scalars, a three-element <c>double</c> array for a
        /// structured Translation/Rotation source, a three-element <c>float</c> array
        /// for colours, a token <c>string</c> for visibility).
        /// <para>
        /// Conversion follows the §5.8 fixed order: (1) engineering-unit conversion,
        /// (2) <c>Scale</c> then <c>Offset</c>, (3) the transform/geospatial profile.
        /// </para>
        /// <para>
        /// Returns a null <see cref="Variant"/> (see <see cref="Variant.IsNull"/>)
        /// whenever the target value cannot be produced faithfully — a null source, an
        /// unrecognised or unimplemented <see cref="OpenUsdRenderTargetKind"/>, a source
        /// whose value cannot be decoded, or a declared engineering unit the connector
        /// cannot honour. §5.8 requires such a target to be left *unresolved* (no update)
        /// rather than authored with an unconverted or fabricated value.
        /// </para>
        /// </summary>
        public static Variant Convert(BindingInfo b, Variant raw)
        {
            if (raw.IsNull)
            {
                return default;
            }
            switch (b.Kind)
            {
                case OpenUsdRenderTargetKind.Translation:
                    return ConvertTranslation(b, raw);
                case OpenUsdRenderTargetKind.Rotation:
                    return ConvertRotation(b, raw);
                case OpenUsdRenderTargetKind.Georeference:
                    return ConvertGeoreference(b, raw);
                case OpenUsdRenderTargetKind.Scale:
                case OpenUsdRenderTargetKind.Opacity:
                case OpenUsdRenderTargetKind.Custom:
                    return TryScalar(b, raw, out double s) ? new Variant(s) : default;
                case OpenUsdRenderTargetKind.DisplayColor:
                    if (!TryScalar(b, raw, out double dc))
                    {
                        return default;
                    }
                    // Temperature: blue (cool) -> red (hot).
                    double t = Math.Max(0.0, Math.Min(1.0, (dc - 20.0) / 80.0));
                    return new Variant([(float)t, 0f, (float)(1.0 - t)]);
                case OpenUsdRenderTargetKind.EmissiveColor:
                    if (!TryScalar(b, raw, out double ec))
                    {
                        return default;
                    }
                    // Pressure: dark -> bright green-white glow.
                    double e = Math.Max(0.0, Math.Min(1.0, ec / 6.0));
                    return new Variant([(float)(0.1 * e), (float)e, (float)(0.2 * e)]);
                case OpenUsdRenderTargetKind.Visibility:
                    return TryToDouble(raw, out double v)
                        ? new Variant(v != 0.0 ? "inherited" : "invisible")
                        : default;
                case OpenUsdRenderTargetKind.Transform:
                // A matrix4d/quaternion target requires the full §5.8 matrix profile
                // (row-major, row-vector, translation in the 4th row; quaternions
                // reordered (x,y,z,w) -> (w,x,y,z) and normalised). That profile is not
                // implemented here, so the target is left unresolved rather than
                // authored with a scalar stand-in.
                default:
                    // An unrecognised RenderTargetKind (a kind added by a later revision
                    // of the companion specification) is never guessed at.
                    return default;
            }
        }

        /// <summary>
        /// §5.8 transform profile — translation. Accepts a structured 3D source or a
        /// scalar driving a single component. Fails closed on anything else.
        /// </summary>
        private static Variant ConvertTranslation(BindingInfo b, Variant raw)
        {
            double factor = LengthFactor(b);
            if (double.IsNaN(factor))
            {
                return default;
            }
            if (TryGetTranslation(raw, out double x, out double y, out double z))
            {
                return new Variant(
                [
                    (x * factor * b.Scale) + b.Offset,
                    (y * factor * b.Scale) + b.Offset,
                    (z * factor * b.Scale) + b.Offset
                ]);
            }
            return TryToDouble(raw, out double d)
                ? new Variant((d * factor * b.Scale) + b.Offset)
                : default;
        }

        /// <summary>
        /// §5.8 transform profile — rotation. USD rotation ops are in degrees, so a
        /// declared source <c>AngleUnit</c> is converted to degrees as step (1), before
        /// <c>Scale</c>/<c>Offset</c>. An undeclared unit is left untouched; a declared
        /// unit the connector cannot honour fails closed.
        /// </summary>
        private static Variant ConvertRotation(BindingInfo b, Variant raw)
        {
            double factor = AngleFactorToDegrees(b);
            if (double.IsNaN(factor))
            {
                return default;
            }
            if (TryGetRotation(raw, out double a, out double bAngle, out double c))
            {
                return new Variant(
                [
                    (a * factor * b.Scale) + b.Offset,
                    (bAngle * factor * b.Scale) + b.Offset,
                    (c * factor * b.Scale) + b.Offset
                ]);
            }
            return TryToDouble(raw, out double d)
                ? new Variant((d * factor * b.Scale) + b.Offset)
                : default;
        }

        /// <summary>
        /// §5.8 geospatial profile. The target is a georeference origin or globe-anchor
        /// attribute, so latitude/longitude are authored as decimal degrees and
        /// elevation/height in metres — never as a raw <c>xformOp</c>. The
        /// domain-agnostic connector handles the component (scalar) form, selecting the
        /// component from the target attribute name; a structured GPOS
        /// <c>GlobalPositionType</c> value carries a CRS and an elevation datum this
        /// connector cannot interpret, so it is left unresolved per §5.8 ("an unmapped or
        /// unsupported CRS shall leave the target unresolved … rather than author an
        /// unprojected value" / "a connector that cannot honour the stated datum shall
        /// treat the height as unresolved").
        /// </summary>
        private static Variant ConvertGeoreference(BindingInfo b, Variant raw)
        {
            GeoComponent component = GeoComponentOf(b.PropertyName);
            if (component == GeoComponent.Unknown)
            {
                return default;
            }
            double factor = component == GeoComponent.Height
                ? LengthFactor(b)
                : AngleFactorToDegrees(b);
            if (double.IsNaN(factor))
            {
                return default;
            }
            // Only the plain scalar component form is interpretable without a CRS and
            // elevation-datum model; a structured source is unresolved (fail closed).
            return TryToDouble(raw, out double d)
                ? new Variant((d * factor * b.Scale) + b.Offset)
                : default;
        }

        private enum GeoComponent
        {
            Unknown,
            Angular,
            Height
        }

        /// <summary>
        /// Selects the georeference component driven by a binding from its target
        /// attribute name (e.g. <c>cesium:anchor:latitude</c>, <c>anchor:height</c>).
        /// </summary>
        private static GeoComponent GeoComponentOf(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return GeoComponent.Unknown;
            }
            string name = propertyName!;
            int sep = name.LastIndexOfAny([':', '.', '/']);
            string leaf = (sep >= 0 ? name.Substring(sep + 1) : name).ToLowerInvariant();
            switch (leaf)
            {
                case "latitude":
                case "longitude":
                case "lat":
                case "lon":
                case "long":
                    return GeoComponent.Angular;
                case "height":
                case "elevation":
                case "altitude":
                case "alt":
                    return GeoComponent.Height;
                default:
                    return GeoComponent.Unknown;
            }
        }

        /// <summary>
        /// Applies §5.8 steps (1) and (2) to a scalar source, returning false when the
        /// source cannot be decoded or a declared unit cannot be honoured.
        /// </summary>
        private static bool TryScalar(BindingInfo b, Variant raw, out double result)
        {
            result = 0.0;
            double factor = UnitFactor(b);
            if (double.IsNaN(factor) || !TryToDouble(raw, out double d))
            {
                return false;
            }
            result = (d * factor * b.Scale) + b.Offset;
            return true;
        }

        /// <summary>
        /// Extracts X/Y/Z from a core OPC UA ThreeDCartesianCoordinates value, or from
        /// the CartesianCoordinates field of a ThreeDFrame value. Domain-agnostic: only
        /// the core Part 5 3D data types are used, never any Positioning-specific type.
        /// </summary>
        /// <param name="raw"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        private static bool TryGetTranslation(Variant raw, out double x, out double y, out double z)
        {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
            if (raw.TryGetStructure(out ThreeDCartesianCoordinates coordinates))
            {
                x = coordinates.X;
                y = coordinates.Y;
                z = coordinates.Z;
                return true;
            }
            if (raw.TryGetStructure(out ThreeDFrame frame) &&
                frame.CartesianCoordinates is { } frameCoordinates)
#pragma warning restore CS8600
            {
                x = frameCoordinates.X;
                y = frameCoordinates.Y;
                z = frameCoordinates.Z;
                return true;
            }
            x = 0.0;
            y = 0.0;
            z = 0.0;
            return false;
        }

        /// <summary>
        /// Extracts A/B/C from a core OPC UA ThreeDOrientation value, or from the
        /// Orientation field of a ThreeDFrame value.
        /// </summary>
        /// <param name="raw"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <returns></returns>
        private static bool TryGetRotation(Variant raw, out double a, out double b, out double c)
        {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
            if (raw.TryGetStructure(out ThreeDOrientation orientation))
            {
                a = orientation.A;
                b = orientation.B;
                c = orientation.C;
                return true;
            }
            if (raw.TryGetStructure(out ThreeDFrame frame) &&
                frame.Orientation is { } frameOrientation)
#pragma warning restore CS8600
            {
                a = frameOrientation.A;
                b = frameOrientation.B;
                c = frameOrientation.C;
                return true;
            }
            a = 0.0;
            b = 0.0;
            c = 0.0;
            return false;
        }

        /// <summary>
        /// Coerces a source <see cref="Variant"/> to a <c>double</c>, returning false
        /// (rather than a fabricated 0.0) when the value cannot be decoded — for example
        /// a structured value that carries no scalar meaning. §5.8 requires such a source
        /// to leave the target unresolved.
        /// </summary>
        private static bool TryToDouble(Variant v, out double result)
        {
            // Widen any numeric source (int/float/short/…) without boxing. A structured,
            // array or otherwise non-numeric source degrades to false.
            return VariantConversions.TryGetDouble(v, out result);
        }

        // UNECE common codes used by the §5.8 unit profiles.
        private const string kUneceRadian = "C81";
        private const string kUneceDegree = "DD";

        /// <summary>
        /// §5.8 step (1) for a target whose USD unit is degrees (rotation ops, and the
        /// angular components of a georeference). Returns 1.0 when no source unit is
        /// declared (no conversion — the declared value is taken at face value), the
        /// conversion factor when the declared unit is a recognised angle unit, and
        /// <see cref="double.NaN"/> when the declared unit cannot be honoured.
        /// </summary>
        private static double AngleFactorToDegrees(BindingInfo b)
        {
            double source = AngleToDegrees(b.SourceEngineeringUnits);
            double target = AngleToDegrees(b.TargetEngineeringUnits);
            if (double.IsNaN(source) || double.IsNaN(target))
            {
                return double.NaN;
            }
            // The USD attribute is authored in degrees, so a declared TargetEngineeringUnits
            // that is not degrees would require authoring in a non-USD unit: fail closed.
            return target == 1.0 ? source : double.NaN;
        }

        /// <summary>
        /// Factor converting the declared angle unit to degrees. 1.0 when nothing is
        /// declared or degrees are declared, 180/pi for radians, NaN for anything else.
        /// </summary>
        private static double AngleToDegrees(EUInformation? units)
        {
            string code = UnitCode(units);
            if (code.Length == 0)
            {
                return 1.0;
            }
            switch (code)
            {
                case kUneceDegree:
                case "deg":
                case "°":
                    return 1.0;
                case kUneceRadian:
                case "rad":
                    return 180.0 / Math.PI;
                default:
                    return double.NaN;
            }
        }

        /// <summary>
        /// §5.8 step (1) for a length-valued target (translation components, georeference
        /// height). Only a source/target pair that is either undeclared or identical is
        /// honoured without a conversion table; a declared mismatch fails closed.
        /// </summary>
        private static double LengthFactor(BindingInfo b)
        {
            return UnitFactor(b);
        }

        /// <summary>
        /// §5.8 step (1) for a scalar target. When neither side declares a unit, or both
        /// declare the same unit, no conversion is applied. A declared mismatch the
        /// connector has no factor for fails closed (NaN) rather than authoring an
        /// unconverted value.
        /// </summary>
        private static double UnitFactor(BindingInfo b)
        {
            string source = UnitCode(b.SourceEngineeringUnits);
            string target = UnitCode(b.TargetEngineeringUnits);
            if (source.Length == 0 || target.Length == 0 ||
                string.Equals(source, target, StringComparison.Ordinal))
            {
                return 1.0;
            }
            if (source == kUneceRadian && target == kUneceDegree)
            {
                return 180.0 / Math.PI;
            }
            if (source == kUneceDegree && target == kUneceRadian)
            {
                return Math.PI / 180.0;
            }
            return double.NaN;
        }

        /// <summary>
        /// Decodes the UNECE common code carried by an <see cref="EUInformation"/>
        /// <c>UnitId</c> (the code's ASCII characters packed big-endian, e.g. 4404273 -&gt;
        /// "C81", 17476 -&gt; "DD"). Falls back to the display name when no UnitId is set.
        /// </summary>
        internal static string UnitCode(EUInformation? units)
        {
            if (units == null)
            {
                return string.Empty;
            }
            int id = units.UnitId;
            if (id > 0)
            {
                var chars = new char[4];
                int n = 0;
                for (int shift = 24; shift >= 0; shift -= 8)
                {
                    int ch = (id >> shift) & 0xFF;
                    if (ch != 0)
                    {
                        chars[n++] = (char)ch;
                    }
                }
                if (n > 0)
                {
                    return new string(chars, 0, n);
                }
            }
            string display = units.DisplayName.Text ?? string.Empty;
            return display;
        }

        /// <summary>
        /// Verifies the stage's advertised <c>RootLayerDigest</c> (Twin-BOM integrity).
        /// §5.2 requires the digest to be computed over the <b>resolved root-layer
        /// content</b>, not over the identifier string, and a connector <b>shall refuse to
        /// open</b> a layer whose digest does not match. The bytes are therefore obtained
        /// from the stage's served <c>Assets</c> closure (§5.15). When a digest is
        /// advertised but the content cannot be obtained the stage is <b>unverified</b>
        /// and this returns <c>false</c> — fail closed rather than falling back to a
        /// cryptographically vacuous digest of the identifier.
        /// </summary>
        public async Task<bool> VerifyStageDigestAsync(RepresentationInfo rep, CancellationToken ct)
        {
            if (rep.RootLayerDigest.IsNull ||
                rep.RootLayerDigest.Length == 0 ||
                rep.DigestAlgorithm == OpenUsdDigestAlgorithm.None)
            {
                return false;
            }
            byte[]? bytes = await TryReadRootLayerBytesAsync(rep, ct).ConfigureAwait(false);
            return bytes != null && VerifyBytesDigest(bytes, rep.RootLayerDigest, rep.DigestAlgorithm);
        }

        /// <summary>
        /// Verifies a stage's advertised digest against root-layer bytes the caller
        /// already holds (§5.2). Exposed so a host that resolves the root layer itself
        /// can apply the same fail-closed rule.
        /// </summary>
        public static bool VerifyStageDigest(RepresentationInfo rep, byte[] rootLayerBytes)
        {
            if (rootLayerBytes == null ||
                rep.RootLayerDigest.IsNull ||
                rep.RootLayerDigest.Length == 0 ||
                rep.DigestAlgorithm == OpenUsdDigestAlgorithm.None)
            {
                return false;
            }
            return VerifyBytesDigest(rootLayerBytes, rep.RootLayerDigest, rep.DigestAlgorithm);
        }

        /// <summary>
        /// Actuates the single opt-in UsdToUaCommand binding with the supplied USD-side
        /// trigger value. Per §5.10 the value is converted back through the inverse of
        /// §5.8 (<c>Offset</c>/<c>Scale</c>, units); when <c>CommandMethodId</c> is present
        /// the connector <c>Call</c>s that Method with the converted value, otherwise it
        /// <c>Write</c>s the converted value to <c>CommandTargetNodeId</c>.
        /// Fail-closed: throws when commands were not explicitly enabled, refuses a
        /// binding whose <c>Enabled</c> tombstone suppresses it, and refuses to issue
        /// unless the session actually holds the write/Call authorization the target
        /// requires (§5.10/§9 — a Server withholds those rights by default).
        /// Single-writer: uses the first controllable command binding found.
        /// Returns true when the UA write/Call succeeds.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<bool> IssueCommandAsync(double value, CancellationToken ct)
        {
            if (!m_enableCommands)
            {
                throw new InvalidOperationException(
                    "Command bindings are disabled. Construct the connector with enableCommands: true.");
            }
            BindingInfo? cmd = SelectCommandBinding(
                await DiscoverAllRepresentationsAsync(ct).ConfigureAwait(false));
            if (cmd == null)
            {
                return false;
            }
            // §5.8 inverse: undo Offset, then Scale, then the unit conversion.
            if (!TryInvertConversion(cmd, value, out double uaValue))
            {
                return false;
            }
            if (!cmd.CommandMethodId.IsNull)
            {
                // §5.10: when CommandMethodId is present the connector Calls that Method.
                NodeId methodOwner = cmd.CommandTargetNodeId.IsNull
                    ? await ParentAsync(cmd.CommandMethodId, ct).ConfigureAwait(false)
                    : cmd.CommandTargetNodeId;
                if (!await IsCallAuthorizedAsync(cmd.CommandMethodId, ct).ConfigureAwait(false))
                {
                    m_logger.CommandRefusedUnauthorized(cmd.CommandMethodId.ToString() ?? string.Empty);
                    return false;
                }
                try
                {
                    await m_session.CallAsync(methodOwner, cmd.CommandMethodId, ct,
                        new Variant(uaValue)).ConfigureAwait(false);
                    return true;
                }
                catch (ServiceResultException)
                {
                    return false;
                }
            }
            if (cmd.CommandTargetNodeId.IsNull)
            {
                return false;
            }
            if (!await IsWriteAuthorizedAsync(cmd.CommandTargetNodeId, ct).ConfigureAwait(false))
            {
                m_logger.CommandRefusedUnauthorized(cmd.CommandTargetNodeId.ToString() ?? string.Empty);
                return false;
            }
            StatusCode sc = await WriteAsync(cmd.CommandTargetNodeId, uaValue, ct)
                .ConfigureAwait(false);
            return StatusCode.IsGood(sc);
        }

        /// <summary>
        /// §5.10 single-writer selection of the command binding to actuate, and §5.4
        /// tombstone enforcement: a binding whose <c>Enabled</c> property is
        /// <c>false</c> is suppressed and is never actuated. A binding must also be
        /// <c>Controllable</c> (§5.9) and name a write target or a Method to Call.
        /// </summary>
        internal static BindingInfo? SelectCommandBinding(IEnumerable<RepresentationInfo> reps)
        {
            foreach (RepresentationInfo r in reps)
            {
                foreach (BindingInfo b in r.Bindings)
                {
                    if (b.Enabled
                        && b.Intent == OpenUsdIntentProfile.UsdToUaCommand
                        && b.SignalRole == OpenUsdSignalRole.Controllable
                        && (!b.CommandTargetNodeId.IsNull || !b.CommandMethodId.IsNull))
                    {
                        return b;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// §5.10: converts a USD-side trigger value back through the inverse of the §5.8
        /// conversion — <c>(value - Offset) / Scale</c> followed by the inverse unit
        /// factor. Returns false (no command is issued) when the conversion cannot be
        /// inverted faithfully: a zero <c>Scale</c>, or a declared unit the connector
        /// cannot honour.
        /// </summary>
        internal static bool TryInvertConversion(BindingInfo b, double usdValue, out double uaValue)
        {
            uaValue = 0.0;
            double factor = b.Kind == OpenUsdRenderTargetKind.Rotation
                ? AngleFactorToDegrees(b)
                : UnitFactor(b);
            if (double.IsNaN(factor) || factor == 0.0 || b.Scale == 0.0)
            {
                return false;
            }
            uaValue = (usdValue - b.Offset) / b.Scale / factor;
            return true;
        }

        /// <summary>
        /// §5.10/§9: the connector shall hold the write authorization the target requires
        /// before issuing a command. The effective right is read from the target's
        /// <c>UserAccessLevel</c> (and, when exposed, <c>UserRolePermissions</c>), and the
        /// command is refused when the right is absent — the connector does not rely on
        /// the Server's error to fail closed.
        /// </summary>
        private async Task<bool> IsWriteAuthorizedAsync(NodeId nodeId, CancellationToken ct)
        {
            var toRead = new ReadValueId[]
            {
                new() { NodeId = nodeId, AttributeId = Attributes.UserAccessLevel },
                new() { NodeId = nodeId, AttributeId = Attributes.UserRolePermissions }
            };
            ReadResponse response;
            try
            {
                response = await m_session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, toRead, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return false;
            }
            if (response.Results.Count == 0 || StatusCode.IsNotGood(response.Results[0].StatusCode))
            {
                // The target does not expose its effective right: fail closed.
                return false;
            }
            if (!VariantConversions.TryGetInt64(
                    response.Results[0].WrappedValue, out long level))
            {
                return false;
            }
            var accessLevel = unchecked((byte)level);
            if ((accessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return false;
            }
            return response.Results.Count < 2 ||
                HasPermission(response.Results[1], PermissionType.Write);
        }

        /// <summary>
        /// §5.10/§9 counterpart of <see cref="IsWriteAuthorizedAsync"/> for a
        /// <c>CommandMethodId</c>: the Method's <c>UserExecutable</c> attribute (and
        /// <c>UserRolePermissions</c> when exposed) must grant the Call.
        /// </summary>
        private async Task<bool> IsCallAuthorizedAsync(NodeId methodId, CancellationToken ct)
        {
            var toRead = new ReadValueId[]
            {
                new() { NodeId = methodId, AttributeId = Attributes.UserExecutable },
                new() { NodeId = methodId, AttributeId = Attributes.UserRolePermissions }
            };
            ReadResponse response;
            try
            {
                response = await m_session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, toRead, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return false;
            }
            if (response.Results.Count == 0 || StatusCode.IsNotGood(response.Results[0].StatusCode))
            {
                return false;
            }
            if (!VariantConversions.TryGetBoolean(
                    response.Results[0].WrappedValue, out bool executable) ||
                !executable)
            {
                return false;
            }
            return response.Results.Count < 2 ||
                HasPermission(response.Results[1], PermissionType.Call);
        }

        /// <summary>
        /// When a node exposes UserRolePermissions, the required permission must be
        /// granted by at least one of the session's effective Roles. A node that does not
        /// expose the attribute at all falls back to the access-level/executable check.
        /// </summary>
        private static bool HasPermission(DataValue dv, PermissionType required)
        {
            if (StatusCode.IsNotGood(dv.StatusCode) || dv.WrappedValue.IsNull)
            {
                return true;
            }
            if (!dv.WrappedValue.TryGetStructure(out ArrayOf<RolePermissionType> permissions) ||
                permissions.Count == 0)
            {
                return true;
            }
            for (int i = 0; i < permissions.Count; i++)
            {
                if (((PermissionType)permissions[i].Permissions & required) == required)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Replays history (Part 11 HistoryRead) for every UaHistoryToUsd binding,
        /// authoring returned values as USD time samples through the sink. Returns the
        /// number of samples authored. Sources that do not historize yield 0 without
        /// throwing (a documented degrade — history binding requires a historizing source).
        /// </summary>
        public async Task<int> ReplayHistoryAsync(DateTime startTime, DateTime endTime, CancellationToken ct)
        {
            int authored = 0;
            // Author every sample under a single batch so a file-backed sink rewrites the
            // layer once instead of once per sample (avoids O(N^2) replay cost).
            using IDisposable batch = m_sink.BeginBatch();
            foreach (RepresentationInfo rep in await DiscoverAllRepresentationsAsync(ct).ConfigureAwait(false))
            {
                foreach (BindingInfo b in rep.Bindings)
                {
                    // §5.4: Enabled = false is a tombstone — a suppressed history binding
                    // is not replayed.
                    if (!b.Enabled
                        || b.Intent != OpenUsdIntentProfile.UaHistoryToUsd
                        || b.SourceNodeId.IsNull
                        || !b.TimeSampled)
                    {
                        continue;
                    }
                    authored += await ReplayBindingHistoryAsync(b, startTime, endTime, ct)
                        .ConfigureAwait(false);
                }
            }
            return authored;
        }

        private async Task<int> ReplayBindingHistoryAsync(
            BindingInfo b, DateTime startTime, DateTime endTime, CancellationToken ct)
        {
            int authored = 0;
            ByteString continuationPoint = default;
            try
            {
                while (true)
                {
                    var details = new ReadRawModifiedDetails
                    {
                        IsReadModified = false,
                        StartTime = startTime,
                        EndTime = endTime,
                        NumValuesPerNode = 0,
                        ReturnBounds = false
                    };
                    var toRead = new HistoryReadValueId[]
                    {
                        new HistoryReadValueId
                        {
                            NodeId = b.SourceNodeId,
                            ContinuationPoint = continuationPoint
                        }
                    };
                    HistoryReadResponse resp;
                    try
                    {
                        resp = await m_session.HistoryReadAsync(
                            null, new ExtensionObject(details), TimestampsToReturn.Source,
                            false, toRead, ct).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // Source does not support history — documented graceful degrade.
                        return authored;
                    }
                    if (resp.Results.Count == 0)
                    {
                        break;
                    }
                    HistoryReadResult r = resp.Results[0];
                    if (StatusCode.IsNotGood(r.StatusCode))
                    {
                        break;
                    }
                    if (ExtensionObject.ToEncodeable(r.HistoryData) is HistoryData hd)
                    {
                        foreach (DataValue dv in hd.DataValues)
                        {
                            if (StatusCode.IsNotGood(dv.StatusCode))
                            {
                                continue;
                            }
                            Variant usd = Convert(b, dv.WrappedValue);
                            if (!usd.IsNull)
                            {
                                m_sink.SetTimeSample(b.PrimPath!, b.PropertyName!,
                                    dv.SourceTimestamp.ToDateTime(), usd);
                                authored++;
                            }
                        }
                    }
                    continuationPoint = r.ContinuationPoint;
                    if (continuationPoint.IsNull || continuationPoint.Length == 0)
                    {
                        continuationPoint = default;
                        break;
                    }
                }
            }
            finally
            {
                if (!continuationPoint.IsNull && continuationPoint.Length > 0)
                {
                    // Release the outstanding continuation point on early exit so the
                    // server does not retain it until timeout.
                    await ReleaseHistoryContinuationAsync(b.SourceNodeId, continuationPoint, ct)
                        .ConfigureAwait(false);
                }
            }
            return authored;
        }

        private async Task ReleaseHistoryContinuationAsync(
            NodeId nodeId, ByteString continuationPoint, CancellationToken ct)
        {
            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                NumValuesPerNode = 0,
                ReturnBounds = false
            };
            var toRead = new HistoryReadValueId[]
            {
                new() { NodeId = nodeId, ContinuationPoint = continuationPoint }
            };
            try
            {
                await m_session.HistoryReadAsync(
                    null, new ExtensionObject(details), TimestampsToReturn.Source,
                    true, toRead, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // Best-effort release; ignore failures.
            }
        }

        private async Task<StatusCode> WriteAsync(NodeId nodeId, double value, CancellationToken ct)
        {
            var toWrite = new WriteValue[]
            {
                new() {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(value))
                }
            };
            WriteResponse resp = await m_session.WriteAsync(null, toWrite, ct).ConfigureAwait(false);
            return resp.Results.Count > 0
                ? resp.Results[0]
                : StatusCodes.BadUnexpectedError;
        }

        public async Task<string> ReadBrowseNameAsync(NodeId nodeId, CancellationToken ct)
        {
            var toRead = new ReadValueId[]
            {
                new() { NodeId = nodeId, AttributeId = Attributes.BrowseName }
            };
            ReadResponse response = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, toRead, ct).ConfigureAwait(false);
            return response.Results[0].WrappedValue.TryGetValue(out QualifiedName qn)
                ? qn.Name ?? string.Empty
                : string.Empty;
        }

        private async Task<List<ReferenceDescription>> BrowseAsync(NodeId node, CancellationToken ct)
        {
            // ManagedBrowseAsync follows continuation points internally, so a server that
            // caps references per node cannot silently truncate discovery (and no
            // continuation point is leaked on the server).
            (ArrayOf<ArrayOf<ReferenceDescription>> results, _) = await m_session.ManagedBrowseAsync(
                null, null, [node], 0, BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences, includeSubtypes: true, 0, ct)
                .ConfigureAwait(false);
            var list = new List<ReferenceDescription>();
            if (results.Count > 0)
            {
                ArrayOf<ReferenceDescription> refs = results[0];
                for (int i = 0; i < refs.Count; i++)
                {
                    list.Add(refs[i]);
                }
            }
            return list;
        }

        private async Task<Dictionary<string, NodeId>> ChildrenByNameAsync(NodeId parent, CancellationToken ct)
        {
            var map = new Dictionary<string, NodeId>();
            foreach (ReferenceDescription r in await BrowseAsync(parent, ct).ConfigureAwait(false))
            {
                if (r.BrowseName.Name is { Length: > 0 } n && !map.ContainsKey(n))
                {
                    map[n] = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                }
            }
            return map;
        }

        private async Task<List<(NodeId, NodeId)>> ChildrenWithTypeAsync(NodeId parent, CancellationToken ct)
        {
            var list = new List<(NodeId, NodeId)>();
            foreach (ReferenceDescription r in await BrowseAsync(parent, ct).ConfigureAwait(false))
            {
                var id = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                if (id.IsNull)
                {
                    // Skip references whose target NodeId cannot be resolved in the
                    // session namespace table (unresolved external server references).
                    continue;
                }
                NodeId typeDef = ExpandedNodeId.ToNodeId(r.TypeDefinition, m_session.NamespaceUris);
                list.Add((id, typeDef));
            }
            return list;
        }

        private async Task<DataValue> ReadAsync(NodeId nodeId, CancellationToken ct)
        {
            var toRead = new ReadValueId[]
            {
                new() { NodeId = nodeId, AttributeId = Attributes.Value }
            };
            ReadResponse response = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, toRead, ct).ConfigureAwait(false);
            return response.Results[0];
        }

        private async Task<string?> ReadStringAsync(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return null;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
            return dv.WrappedValue.TryGetValue(out string s) ? s : null;
        }

        private async Task<NodeId> ReadNodeIdAsync(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return NodeId.Null;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
            return dv.WrappedValue.TryGetValue(out NodeId n) ? n : NodeId.Null;
        }

        private async Task<int> ReadInt32Async(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return 0;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
            return VariantConversions.TryGetInt64(dv.WrappedValue, out long v)
                ? unchecked((int)v)
                : 0;
        }

        private async Task<double> ReadDoubleAsync(
            Dictionary<string, NodeId> props, string name, double fallback, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return fallback;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
            return VariantConversions.TryGetDouble(dv.WrappedValue, out double v) ? v : fallback;
        }

        private Task<bool> ReadBoolAsync(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            return ReadBoolAsync(props, name, false, ct);
        }

        private async Task<bool> ReadBoolAsync(
            Dictionary<string, NodeId> props, string name, bool fallback, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return fallback;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
            return VariantConversions.TryGetBoolean(dv.WrappedValue, out bool v) ? v : fallback;
        }

        private async Task<Guid> ReadGuidAsync(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return Guid.Empty;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
            if (dv.WrappedValue.TryGetValue(out Uuid uuid))
            {
                return (Guid)uuid;
            }
            return dv.WrappedValue.TryGetValue(out string text) &&
                Guid.TryParse(text, out Guid parsed)
                ? parsed
                : Guid.Empty;
        }

        private async Task<RelativePath?> ReadRelativePathAsync(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return null;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
            return dv.WrappedValue.TryGetStructure(out RelativePath path) && path.Elements.Count > 0
                ? path
                : null;
#pragma warning restore CS8600
        }

        private async Task<EUInformation?> ReadEuInformationAsync(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return null;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
            return dv.WrappedValue.TryGetStructure(out EUInformation eu) ? eu : null;
#pragma warning restore CS8600
        }

        private async Task<ByteString> ReadByteStringAsync(
            Dictionary<string, NodeId> props, string name, CancellationToken ct)
        {
            if (!props.TryGetValue(name, out NodeId id))
            {
                return default;
            }
            DataValue dv = await ReadAsync(id, ct).ConfigureAwait(false);
            return dv.WrappedValue.TryGetValue(out ByteString bs) ? bs : default;
        }
    }
}
