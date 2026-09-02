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
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Server.Scene
{
    /// <summary>
    /// Options controlling how much of a scene is materialized. Each switch corresponds to a
    /// conformance unit of draft OPC UA — OpenUSD Scene Materialization §12, so a Server
    /// materializes only what it needs; Scene Structure is the always-on baseline.
    /// </summary>
    public sealed class UsdMaterializationOptions
    {
        /// <summary>
        /// Materialize <c>Composition/</c> arcs and <c>VariantSets/</c> — the
        /// Composition Provenance CU (§5.6, §7.4).
        /// </summary>
        public bool MaterializeComposition { get; set; } = true;

        /// <summary>
        /// Materialize applied API schemas as AddIns under <c>AppliedSchemas/</c> — the
        /// Applied Schemas CU (§5.6, §8.2).
        /// </summary>
        public bool MaterializeAppliedSchemas { get; set; } = true;

        /// <summary>
        /// Materialize non-well-known prim metadata under <c>Metadata/</c> (§6.3).
        /// </summary>
        public bool MaterializeMetadata { get; set; } = true;

        /// <summary>
        /// When a vendor georeference or globe-anchor schema is recognised, additionally
        /// author the portable <c>UsdGeoreferenceApiType</c> / <c>UsdGlobeAnchorApiType</c>
        /// carrying the same values, so a generic client obtains the anchor from one
        /// well-known type — the Georeferencing CU (§5.8).
        /// </summary>
        public bool DualAuthorPortableGeoreference { get; set; } = true;

        /// <summary>
        /// Mark attributes flagged as live in the scene document as historizing, so a Server
        /// that retains a value timeline exposes it through HistoricalAccess (§9 Mode A).
        /// </summary>
        public bool HistorizeLiveAttributes { get; set; } = true;

        /// <summary>
        /// The wall-clock UTC instant that USD time code <c>0</c> maps to, used to relate stage
        /// timeline ordinates to OPC UA timestamps when a Server serves time samples through
        /// HistoricalAccess (§9). USD time codes are stage-timeline ordinates, not wall clock;
        /// they relate to UTC only through an explicit epoch together with the stage's
        /// <c>TimeCodesPerSecond</c>. This is deliberately left <c>null</c> by default — no epoch
        /// is invented. When it is <c>null</c> the sample timeline is Server-defined and
        /// <see cref="UsdHistoricalAccess.ResolveUtc"/> returns <c>null</c>.
        /// </summary>
        public DateTime? TimeCodeEpochUtc { get; set; }
    }

    /// <summary>
    /// One time sample of a USD attribute: a value at a stage-timeline time code (§7.1, §9).
    /// The time code is a stage-timeline ordinate (it may be negative or fractional); it relates
    /// to wall-clock time only through an explicit epoch and the stage's <c>TimeCodesPerSecond</c>
    /// (see <see cref="UsdHistoricalAccess"/>).
    /// </summary>
    public readonly struct UsdTimeSample : IEquatable<UsdTimeSample>
    {
        /// <summary>
        /// Creates a time sample.
        /// </summary>
        /// <param name="timeCode">The stage-timeline time code.</param>
        /// <param name="value">The sampled value, in the same shape the reader produces.</param>
        public UsdTimeSample(double timeCode, UsdValue value)
        {
            TimeCode = timeCode;
            Value = value;
        }

        /// <summary>
        /// The stage-timeline time code (may be negative or fractional).
        /// </summary>
        public double TimeCode { get; }

        /// <summary>
        /// The sampled value.
        /// </summary>
        public UsdValue Value { get; }

        /// <inheritdoc/>
        public bool Equals(UsdTimeSample other)
        {
            return TimeCode.Equals(other.TimeCode) && Equals(Value, other.Value);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is UsdTimeSample other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return TimeCode.GetHashCode();
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(UsdTimeSample left, UsdTimeSample right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(UsdTimeSample left, UsdTimeSample right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// The HistoricalAccess surface of one materialized sampled attribute: the node, its composed
    /// scene path, the ordered time samples and the mapping context needed to relate USD time codes
    /// to wall-clock time (§7.1 step 3, §9).
    /// </summary>
    /// <remarks>
    /// The materializer sets <c>Historizing</c> and the HistoryRead access bit on the node and
    /// records the samples here, but this library does not itself answer <c>HistoryRead</c> — in
    /// this stack history is served at the NodeManager layer, and no per-variable read-history
    /// callback exists on <see cref="BaseVariableState"/> to hook. To turn these samples into a
    /// working HistoricalAccess surface a hosting Server must still: (1) advertise the
    /// HistoricalAccess server capability and, if desired, add the HA configuration Object under
    /// each sampled node; (2) load these <see cref="Samples"/> into a historian store keyed by the
    /// node, mapping each sample onto a <c>DataValue</c> whose <c>SourceTimestamp</c> comes from
    /// <see cref="ResolveUtc"/> (or a Server-defined timeline when no epoch was declared, §9); and
    /// (3) answer the <c>HistoryRead</c> <c>RAW</c>/<c>PROCESSED</c> service for those nodes from
    /// that store — for example by overriding <c>HistoryRead</c> on the owning NodeManager or
    /// registering a historian provider for them. <see cref="ResolveUtc"/> is provided so step (2)
    /// maps every sample onto a UTC timestamp consistently.
    /// </remarks>
    public sealed class UsdHistoricalAccess
    {
        internal UsdHistoricalAccess(
            UsdAttributeState node,
            string attributePath,
            IReadOnlyList<UsdTimeSample> samples,
            double? timeCodesPerSecond,
            DateTime? epochUtc)
        {
            Node = node;
            AttributePath = attributePath;
            Samples = samples;
            TimeCodesPerSecond = timeCodesPerSecond;
            EpochUtc = epochUtc;
        }

        /// <summary>
        /// The materialized attribute node whose history these samples are.
        /// </summary>
        public UsdAttributeState Node { get; }

        /// <summary>
        /// The attribute's composed-scene path (<c>&lt;primPath&gt;.&lt;attributeName&gt;</c>).
        /// </summary>
        public string AttributePath { get; }

        /// <summary>
        /// The time samples, ordered by ascending time code.
        /// </summary>
        public IReadOnlyList<UsdTimeSample> Samples { get; }

        /// <summary>
        /// The stage's <c>TimeCodesPerSecond</c>, or <c>null</c> when the stage declares none. Only
        /// meaningful together with <see cref="EpochUtc"/>.
        /// </summary>
        public double? TimeCodesPerSecond { get; }

        /// <summary>
        /// The declared wall-clock UTC instant of time code <c>0</c>, or <c>null</c> when no epoch
        /// was declared. When <c>null</c> the timeline is Server-defined (§9).
        /// </summary>
        public DateTime? EpochUtc { get; }

        /// <summary>
        /// Maps a USD time code to wall-clock UTC using the declared epoch and
        /// <see cref="TimeCodesPerSecond"/>. Returns <c>null</c> when no epoch is declared or
        /// <see cref="TimeCodesPerSecond"/> is absent or non-positive — the mapping is then
        /// Server-defined and this method never invents one (fail closed, §9).
        /// </summary>
        /// <param name="timeCode">The stage-timeline time code to map.</param>
        /// <returns>The UTC instant, or <c>null</c> when no epoch mapping is defined.</returns>
        public DateTime? ResolveUtc(double timeCode)
        {
            if (EpochUtc is not DateTime epoch ||
                TimeCodesPerSecond is not double tcps ||
                tcps <= 0.0 ||
                double.IsNaN(timeCode) ||
                double.IsInfinity(timeCode))
            {
                return null;
            }
            return epoch.AddSeconds(timeCode / tcps);
        }
    }

    /// <summary>
    /// The address-space nodes produced by a materialization, indexed so a caller can bind
    /// live data to them or resolve a Part 1 binding target (§10).
    /// </summary>
    public sealed class UsdMaterializationResult
    {
        internal UsdMaterializationResult(
            UsdStageState stage,
            Dictionary<string, UsdPrimState> primsByPath,
            Dictionary<string, UsdAttributeState> attributesByPath,
            Dictionary<string, UsdHistoricalAccess> historicalAccessByPath,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>> samplesByNode,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<string>> connectionsByNode)
        {
            Stage = stage;
            PrimsByPath = primsByPath;
            AttributesByPath = attributesByPath;
            HistoricalAccessByPath = historicalAccessByPath;
            SamplesByNode = samplesByNode;
            ConnectionsByNode = connectionsByNode;
        }

        /// <summary>
        /// The materialized stage Object.
        /// </summary>
        public UsdStageState Stage { get; }

        /// <summary>
        /// Every materialized prim, keyed by its absolute SdfPath (for example
        /// <c>/Plant/Pumps/P101</c>).
        /// </summary>
        public IReadOnlyDictionary<string, UsdPrimState> PrimsByPath { get; }

        /// <summary>
        /// Every materialized attribute, keyed by <c>&lt;primPath&gt;.&lt;attributeName&gt;</c>
        /// (for example <c>/Plant/Pumps/P101/Pump/Impeller.xformOp:rotateZ</c>). This is the
        /// index a Part 1 binding resolves its target against (§10).
        /// </summary>
        public IReadOnlyDictionary<string, UsdAttributeState> AttributesByPath { get; }

        /// <summary>
        /// Every composed-scene attribute that carries time samples, keyed by the same
        /// <c>&lt;primPath&gt;.&lt;attributeName&gt;</c> path as <see cref="AttributesByPath"/>
        /// (§7.1 step 3). Each entry exposes the sampled node — materialized with its authored
        /// default in <c>Value</c> and <c>Historizing</c> set — together
        /// with the ordered samples and the §9 epoch / <c>TimeCodesPerSecond</c> mapping context.
        /// A hosting Server binds these to its historian to answer <c>HistoryRead</c>; this library
        /// authors the history metadata but does not itself serve the history (see the type
        /// documentation on <see cref="UsdHistoricalAccess"/>). Variant-branch attributes are
        /// authoring provenance and are deliberately absent here.
        /// </summary>
        public IReadOnlyDictionary<string, UsdHistoricalAccess> HistoricalAccessByPath { get; }

        /// <summary>
        /// Samples keyed by attribute node identity, covering every sampled attribute in the whole
        /// materialization — including those inside variant branches — so the exporter can recover
        /// each node's <c>.timeSamples</c> for a lossless <c>.usda</c> round trip (§7.2). Keyed by
        /// node reference because materialized attributes deliberately share the model's placeholder
        /// NodeId, so only object identity distinguishes them.
        /// </summary>
        internal IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>> SamplesByNode { get; }

        /// <summary>
        /// The authored connection paths of every connected attribute in the whole materialization
        /// — including those inside variant branches — in authored order, keyed by attribute node
        /// identity. The exporter replays these so the exported connection sequence is deterministic
        /// and equal to the authored order, and so a <c>.connect</c> whose target lies outside the
        /// materialized subtree (and therefore has no browsable <c>UsdConnection</c> edge) still
        /// survives materialize→export (§5.4, §7.4). This is the connection counterpart of a
        /// relationship's <c>TargetPaths</c> property; it lives on the result rather than on the
        /// node because the attribute model carries no <c>ConnectionPaths</c> member. Keyed by node
        /// reference for the same placeholder-NodeId reason as <see cref="SamplesByNode"/>.
        /// </summary>
        internal IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<string>> ConnectionsByNode { get; }
    }

    /// <summary>
    /// Materializes a composed USD stage into an OPC UA address space per draft
    /// OPC UA — OpenUSD Scene Materialization §7.1, so that the prim hierarchy *is* the node
    /// hierarchy and browsing the server is browsing the scene.
    /// </summary>
    public static partial class UsdSceneMaterializer
    {
        /// <summary>
        /// Materializes a stage as a <c>UsdStageType</c> Object under <paramref name="parent"/>.
        /// </summary>
        /// <param name="context">The server system context.</param>
        /// <param name="parent">The node the stage is attached to — typically the Stages
        /// discovery folder.</param>
        /// <param name="stage">The composed scene to materialize.</param>
        /// <param name="ns">The OpenUSD Scene companion namespace index.</param>
        /// <param name="options">Which conformance units to materialize; defaults to all.</param>
        /// <returns>The materialized stage and its prim/attribute indexes.</returns>
        public static UsdMaterializationResult MaterializeUsdStage(
            this ISystemContext context,
            NodeState parent,
            UsdStage stage,
            ushort ns,
            UsdMaterializationOptions? options = null)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (stage is null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            options ??= new UsdMaterializationOptions();

            UsdStageState stageNode = context.CreateInstanceOfUsdStageType(
                parent, new QualifiedName(stage.StageName, ns));
            // The instance factory leaves ReferenceTypeId = Null; HasComponent makes the
            // stage browsable from the folder it was attached to.
            stageNode.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            parent.AddChild(stageNode);
            stageNode.NodeId = context.RequireNodeIdFactory().New(context, stageNode);

            ApplyStageMetadata(context, stageNode, stage);

            var prims = new Dictionary<string, UsdPrimState>(StringComparer.Ordinal);
            var attributes = new Dictionary<string, UsdAttributeState>(StringComparer.Ordinal);
            var pendingRelationships = new List<(UsdRelationshipState Node, UsdRelationship Source)>();
            var pendingConnections = new List<(UsdAttributeState Node, UsdAttribute Source)>();

            // §9: the epoch is Server-declared (never invented) and the stage carries
            // TimeCodesPerSecond; together they let a Server relate the recorded stage-timeline
            // samples to wall-clock time. Both may be absent, in which case the timeline is
            // Server-defined.
            var recorder = new UsdMaterializationRecorder(options.TimeCodeEpochUtc, stage.TimeCodesPerSecond);

            foreach (UsdPrim root in stage.RootPrims)
            {
                MaterializePrim(
                    context, stageNode, root, "/" + root.Name, ns, options,
                    prims, attributes, pendingRelationships, pendingConnections, recorder);
            }

            // Second pass: targets and connections can point at any prim in the stage, so
            // they are resolved only once every node exists and has a NodeId.
            ResolveRelationshipTargets(context, pendingRelationships, prims, attributes);
            ResolveConnections(context, pendingConnections, attributes);

            // The composed-scene HistoricalAccess surface is derived from the composed attribute
            // index, so only composed attributes are exposed — variant-branch samples stay in the
            // node-keyed map for the exporter alone (they are un-composed provenance, §7.4).
            var historicalAccessByPath =
                new Dictionary<string, UsdHistoricalAccess>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, UsdAttributeState> entry in attributes)
            {
                if (recorder.SamplesByNode.TryGetValue(
                    entry.Value, out IReadOnlyList<UsdTimeSample>? samples))
                {
                    historicalAccessByPath[entry.Key] = new UsdHistoricalAccess(
                        entry.Value, entry.Key, samples,
                        recorder.TimeCodesPerSecond, recorder.EpochUtc);
                }
            }

            return new UsdMaterializationResult(
                stageNode, prims, attributes, historicalAccessByPath,
                recorder.SamplesByNode, recorder.ConnectionsByNode);
        }

        private static void ApplyStageMetadata(
            ISystemContext context, UsdStageState stageNode, UsdStage stage)
        {
            if (!string.IsNullOrEmpty(stage.DefaultPrim))
            {
                stageNode.CreateOrReplaceDefaultPrim(context, null!).Value = stage.DefaultPrim;
            }
            if (!string.IsNullOrEmpty(stage.UpAxis))
            {
                stageNode.CreateOrReplaceUpAxis(context, null!).Value = stage.UpAxis;
            }
            stageNode.CreateOrReplaceMetersPerUnit(context, null!).Value = stage.MetersPerUnit;
            if (stage.KilogramsPerUnit.HasValue)
            {
                stageNode.CreateOrReplaceKilogramsPerUnit(context, null!).Value =
                    stage.KilogramsPerUnit.Value;
            }
            if (stage.TimeCodesPerSecond.HasValue)
            {
                stageNode.CreateOrReplaceTimeCodesPerSecond(context, null!).Value =
                    stage.TimeCodesPerSecond.Value;
            }
            if (stage.StartTimeCode.HasValue)
            {
                stageNode.CreateOrReplaceStartTimeCode(context, null!).Value =
                    stage.StartTimeCode.Value;
            }
            if (stage.EndTimeCode.HasValue)
            {
                stageNode.CreateOrReplaceEndTimeCode(context, null!).Value =
                    stage.EndTimeCode.Value;
            }
            if (!string.IsNullOrEmpty(stage.RootLayerIdentifier))
            {
                stageNode.CreateOrReplaceRootLayerIdentifier(context, null!).Value =
                    stage.RootLayerIdentifier;
            }
            if (!string.IsNullOrEmpty(stage.Documentation))
            {
                stageNode.CreateOrReplaceDocumentation(context, null!).Value = stage.Documentation;
            }
        }

        private static void MaterializePrim(
            ISystemContext context,
            NodeState parent,
            UsdPrim prim,
            string path,
            ushort ns,
            UsdMaterializationOptions options,
            Dictionary<string, UsdPrimState> prims,
            Dictionary<string, UsdAttributeState> attributes,
            List<(UsdRelationshipState, UsdRelationship)> pendingRelationships,
            List<(UsdAttributeState, UsdAttribute)> pendingConnections,
            UsdMaterializationRecorder recorder)
        {
            var browseName = new QualifiedName(prim.Name, ns);

            // A prim of a known typed schema is instantiated as that generated ObjectType
            // subclass, so the node *is* the type it declares and carries the subtype's
            // members (§5.3). An unknown schema returns null here and degrades to a concrete
            // UsdPrimType via AddUsdPrim_Placeholder, still keeping its TypeName token (§8.4).
            UsdPrimState? primNode = CreateTypedPrimInstance(context, parent, prim.TypeName, browseName);
            primNode ??= parent switch
            {
                UsdStageState stageParent => stageParent.AddUsdPrim_Placeholder(context, browseName),
                UsdPrimState primParent => primParent.AddUsdPrim_Placeholder(context, browseName),
                _ => throw new ArgumentException(
                    "A prim can only be materialized under a stage or another prim.",
                    nameof(parent))
            };

            PopulatePrimNode(
                context, primNode, prim, path, ns, options,
                prims, attributes, pendingRelationships, pendingConnections, recorder);
        }

        /// <summary>
        /// Populates an already-created prim node with its properties, attributes, relationships,
        /// composition/schema/metadata folders and child prims (everything after the node itself
        /// exists). Split out from <see cref="MaterializePrim"/> so a variant branch — whose body is
        /// prim-shaped — is materialized with exactly the same fidelity as a top-level prim (§5.6).
        /// </summary>
        private static void PopulatePrimNode(
            ISystemContext context,
            UsdPrimState primNode,
            UsdPrim prim,
            string path,
            ushort ns,
            UsdMaterializationOptions options,
            Dictionary<string, UsdPrimState> prims,
            Dictionary<string, UsdAttributeState> attributes,
            List<(UsdRelationshipState, UsdRelationship)> pendingRelationships,
            List<(UsdAttributeState, UsdAttribute)> pendingConnections,
            UsdMaterializationRecorder recorder)
        {
            primNode.CreateOrReplaceSpecifier(context, null!).Value = prim.Specifier;
            if (!string.IsNullOrEmpty(prim.TypeName))
            {
                primNode.CreateOrReplaceTypeName(context, null!).Value = prim.TypeName;
            }
            primNode.CreateOrReplaceKind(context, null!).Value = prim.Kind;
            primNode.CreateOrReplaceActive(context, null!).Value = prim.Active;
            if (prim.Instanceable)
            {
                primNode.CreateOrReplaceInstanceable(context, null!).Value = true;
            }
            if (!string.IsNullOrEmpty(prim.Documentation))
            {
                primNode.CreateOrReplaceDocumentation(context, null!).Value = prim.Documentation;
            }

            prims[path] = primNode;

            foreach (UsdAttribute attribute in prim.Attributes)
            {
                UsdAttributeState attributeNode = MaterializeAttribute(
                    context, primNode, attribute, ns, options);
                string attributePath = path + "." + attribute.Name;
                attributes[attributePath] = attributeNode;
                if (attribute.Connections.Count > 0)
                {
                    pendingConnections.Add((attributeNode, attribute));
                    // Record the authored connection order (including any target outside the
                    // materialized subtree) so the exporter reproduces the exact authored
                    // sequence and never drops an unresolvable .connect (M-2, M-5; §5.4, §7.4).
                    recorder.RecordConnections(attributeNode, attribute.Connections);
                }
                if (attribute.TimeSamples.Count > 0)
                {
                    recorder.Record(attributeNode, attribute);
                }
            }

            foreach (UsdRelationship relationship in prim.Relationships)
            {
                UsdRelationshipState relationshipNode = MaterializeRelationship(
                    context, primNode, relationship, ns);
                pendingRelationships.Add((relationshipNode, relationship));
            }

            // Applied API schemas materialize as AddIns; additionally, a *typed* Cesium
            // georeference/globe-anchor prim (which carries no applied schema) still needs the
            // AppliedSchemas/ folder so the portable georeference can be dual-authored (§5.8).
            if (options.MaterializeAppliedSchemas &&
                (prim.ApiSchemas.Count > 0 ||
                    (options.DualAuthorPortableGeoreference && IsGeoreferenceTypedPrim(prim))))
            {
                MaterializeAppliedSchemas(context, primNode, prim, ns, options);
            }
            if (options.MaterializeComposition)
            {
                if (prim.Composition.Count > 0)
                {
                    MaterializeComposition(context, primNode, prim, ns);
                }
                if (prim.VariantSets.Count > 0)
                {
                    MaterializeVariantSets(context, primNode, prim, ns, options, recorder);
                }
            }
            if (options.MaterializeMetadata && prim.Metadata.Count > 0)
            {
                MaterializeMetadata(context, primNode, prim, ns);
            }

            foreach (UsdPrim child in prim.Children)
            {
                MaterializePrim(
                    context, primNode, child, path + "/" + child.Name, ns, options,
                    prims, attributes, pendingRelationships, pendingConnections, recorder);
            }
        }

        /// <summary>
        /// Instantiates a prim as the generated State subclass for a known typed schema (§5.3),
        /// attached under <paramref name="parent"/> exactly as <c>AddUsdPrim_Placeholder</c> or the stage
        /// attach idiom would attach a plain prim (<c>HasComponent</c> + a factory NodeId, so it
        /// is browsable and uniquely identified identically). Returns <c>null</c> for an unknown
        /// or untyped schema so the caller applies the §8.4 fallback — a concrete
        /// <c>UsdPrimType</c> that still keeps its <c>TypeName</c> token.
        /// </summary>
        /// <remarks>
        /// The subtype's declared members are all Optional and are deliberately *not* pre-created
        /// here. §7.1 step 3 makes the generic <c>&lt;UsdAttribute&gt;</c> pass authoritative for
        /// every attribute value, so authoring an empty typed member (e.g. an empty
        /// <c>XformOpOrder</c>) would both duplicate that state and publish a plausible-but-wrong
        /// placeholder — the opposite of failing closed. Instantiating the subclass gives the node
        /// its correct type identity (and the typed accessors) while the attribute pass remains the
        /// single source of values.
        /// </remarks>
        private static UsdPrimState? CreateTypedPrimInstance(
            ISystemContext context, NodeState parent, string typeName, QualifiedName browseName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            UsdPrimState? node = typeName switch
            {
                "Xform" => context.CreateInstanceOfUsdGeomXformType(parent, browseName),
                "Scope" => context.CreateInstanceOfUsdGeomScopeType(parent, browseName),
                "Mesh" => context.CreateInstanceOfUsdGeomMeshType(parent, browseName),
                "Cylinder" => context.CreateInstanceOfUsdGeomCylinderType(parent, browseName),
                "Sphere" => context.CreateInstanceOfUsdGeomSphereType(parent, browseName),
                "Cube" => context.CreateInstanceOfUsdGeomCubeType(parent, browseName),
                "Cone" => context.CreateInstanceOfUsdGeomConeType(parent, browseName),
                "Capsule" => context.CreateInstanceOfUsdGeomCapsuleType(parent, browseName),
                "Material" => context.CreateInstanceOfUsdShadeMaterialType(parent, browseName),
                "Shader" => context.CreateInstanceOfUsdShadeShaderType(parent, browseName),
                _ => null
            };
            if (node is null)
            {
                return null;
            }

            // The instance factory leaves ReferenceTypeId = Null and seeds a type NodeId;
            // Attach makes it a HasComponent child with a fresh factory NodeId, matching how
            // AddUsdPrim_Placeholder and MaterializeUsdStage attach every other node (§7.1).
            Attach(context, parent, node, Opc.Ua.ReferenceTypeIds.HasComponent);
            return node;
        }
    }
}
