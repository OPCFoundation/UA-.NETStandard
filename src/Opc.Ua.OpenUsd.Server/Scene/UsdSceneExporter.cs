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
    /// Reads a materialized address space back into a scene document, the inverse of
    /// <see cref="UsdSceneMaterializer"/> (draft OPC UA — OpenUSD Scene Materialization §7.2).
    /// Combined with a <c>.usda</c> writer this closes the §7.4 round trip.
    /// </summary>
    public static class UsdSceneExporter
    {
        /// <summary>
        /// Exports a materialized scene, recovering time samples from the materialization result so
        /// sampled attributes round-trip (§7.2). This is the recommended overload after a
        /// materialization, since the samples are held by the result rather than readable from the
        /// nodes.
        /// </summary>
        /// <param name="context">The server system context.</param>
        /// <param name="result">The materialization result to export.</param>
        /// <returns>The reconstructed composed scene.</returns>
        public static UsdStage ExportUsdStage(
            this ISystemContext context, UsdMaterializationResult result)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            return ExportUsdStageCore(
                context, result.Stage, result.SamplesByNode, result.ConnectionsByNode);
        }

        /// <summary>
        /// Exports a materialized <c>UsdStageType</c> Object into a scene document.
        /// </summary>
        /// <param name="context">The server system context.</param>
        /// <param name="stageNode">The materialized stage.</param>
        /// <param name="timeSamples">
        /// The time samples recovered per attribute node (from
        /// <see cref="UsdMaterializationResult.SamplesByNode"/>), or <c>null</c> when the caller has
        /// none — sampled attributes then export their default only. Keyed by node identity because
        /// materialized attributes deliberately share the model's placeholder NodeId (§7.2).
        /// </param>
        /// <remarks>
        /// Connections are recovered from each attribute's <c>ConnectionPaths</c> member, which the
        /// materializer authors on the node itself, so this overload is now lossless for
        /// connections too: authored order, multiplicity, and targets outside the materialized
        /// subtree all survive. The browsable <c>UsdConnection</c> edges are used only as a
        /// fallback for a stage that was not materialized by this library, where they cannot
        /// recover authored order or an out-of-subtree target.
        /// </remarks>
        /// <returns>The reconstructed composed scene.</returns>
        public static UsdStage ExportUsdStage(
            this ISystemContext context,
            UsdStageState stageNode,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>>? timeSamples = null)
        {
            return ExportUsdStageCore(context, stageNode, timeSamples, null);
        }

        private static UsdStage ExportUsdStageCore(
            ISystemContext context,
            UsdStageState stageNode,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>>? timeSamples,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<string>>? connectionPaths)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (stageNode is null)
            {
                throw new ArgumentNullException(nameof(stageNode));
            }

            var stage = new UsdStage(stageNode.BrowseName.Name ?? string.Empty)
            {
                DefaultPrim = ReadString(stageNode.DefaultPrim) ?? string.Empty,
                UpAxis = ReadString(stageNode.UpAxis) ?? "Z",
                MetersPerUnit = ReadDouble(stageNode.MetersPerUnit) ?? 1.0,
                KilogramsPerUnit = ReadDouble(stageNode.KilogramsPerUnit),
                TimeCodesPerSecond = ReadDouble(stageNode.TimeCodesPerSecond),
                StartTimeCode = ReadDouble(stageNode.StartTimeCode),
                EndTimeCode = ReadDouble(stageNode.EndTimeCode),
                RootLayerIdentifier = ReadString(stageNode.RootLayerIdentifier) ?? string.Empty,
                Documentation = ReadString(stageNode.Documentation) ?? string.Empty
            };

            // Two-pass export: an attribute connection can point at any attribute in the
            // stage (§5.4), so the whole prim tree and a NodeId -> SdfPath index are built
            // first, then connections are resolved once every attribute's path is known —
            // mirroring how the materializer resolves references in a second pass.
            var attributePaths = new Dictionary<NodeId, string>();
            var pendingConnections =
                new List<(UsdAttribute Attribute, UsdAttributeState Node)>();

            foreach (UsdPrimState child in ChildrenOfType<UsdPrimState>(context, stageNode))
            {
                string childPath = "/" + (child.BrowseName.Name ?? string.Empty);
                stage.AddRootPrim(ExportPrim(
                    context, child, childPath, attributePaths, pendingConnections,
                    timeSamples, connectionPaths));
            }

            ResolveExportedConnections(context, pendingConnections, attributePaths, connectionPaths);
            return stage;
        }

        private static UsdPrim ExportPrim(
            ISystemContext context,
            UsdPrimState primNode,
            string primPath,
            Dictionary<NodeId, string> attributePaths,
            List<(UsdAttribute Attribute, UsdAttributeState Node)> pendingConnections,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>>? timeSamples,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<string>>? connectionPaths)
        {
            string name = primNode.BrowseName.Name ?? string.Empty;
            var prim = new UsdPrim(name, ReadString(primNode.TypeName) ?? string.Empty)
            {
                Specifier = ReadEnum(primNode.Specifier, UsdSpecifierEnum.Def),
                Kind = ReadEnum(primNode.Kind, UsdPrimKindEnum.Unspecified),
                Active = ReadBool(primNode.Active) ?? true,
                Instanceable = ReadBool(primNode.Instanceable) ?? false,
                Documentation = ReadString(primNode.Documentation) ?? string.Empty
            };

            foreach (UsdAttributeState attributeNode in
                ChildrenOfType<UsdAttributeState>(context, primNode))
            {
                UsdAttribute attribute = ExportAttribute(attributeNode, timeSamples);
                prim.Attributes.Add(attribute);
                // Index the attribute by NodeId so any connection targeting it can be
                // mapped back to <primPath>.<attributeName>, the same key the materializer
                // resolved connections against.
                string attributePath =
                    primPath + "." + (attributeNode.BrowseName.Name ?? string.Empty);
                attributePaths[attributeNode.NodeId] = attributePath;
                pendingConnections.Add((attribute, attributeNode));
            }
            foreach (UsdRelationshipState relationshipNode in
                ChildrenOfType<UsdRelationshipState>(context, primNode))
            {
                prim.Relationships.Add(ExportRelationship(relationshipNode));
            }

            ExportComposition(context, primNode, prim);
            ExportVariantSets(context, primNode, prim, timeSamples, connectionPaths);
            ExportAppliedSchemas(context, primNode, prim);
            ExportMetadata(context, primNode, prim);

            foreach (UsdPrimState childNode in ChildrenOfType<UsdPrimState>(context, primNode))
            {
                string childPath = primPath + "/" + (childNode.BrowseName.Name ?? string.Empty);
                prim.AddChild(ExportPrim(
                    context, childNode, childPath, attributePaths, pendingConnections,
                    timeSamples, connectionPaths));
            }
            return prim;
        }

        private static UsdAttribute ExportAttribute(
            UsdAttributeState attributeNode,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>>? timeSamples)
        {
            string name = attributeNode.BrowseName.Name ?? string.Empty;
            // The UsdTypeName annotation is what makes the §6.2 mapping reversible: it records
            // the exact SdfValueTypeName even where several USD types share one DataType.
            string typeName = ReadString(attributeNode.UsdTypeName) ?? string.Empty;
            var attribute = new UsdAttribute(name, typeName)
            {
                Variability = ReadEnum(attributeNode.Variability, UsdVariabilityEnum.Varying),
                Custom = ReadBool(attributeNode.Custom) ?? false,
                Interpolation = ReadString(attributeNode.Interpolation),
                // §9 Mode A is a per-attribute property independent of whether the Server
                // retains history: the materializer grants CurrentWrite to every live
                // attribute but only sets Historizing when it also keeps the timeline.
                // Infer liveness from the write access, so a Mode A attribute that is not
                // historized still exports as live.
                Live = (attributeNode.AccessLevel & Opc.Ua.AccessLevels.CurrentWrite) != 0,
                // Decoerce reads the Variant through its typed accessors, so an ArrayOf<T> or
                // MatrixOf<T> keeps its shape and array, tuple and matrix values survive the
                // round trip without a boxing accessor (§7.2).
                Value = UsdValueCoercion.Decoerce(attributeNode.Value)
            };

            // §7.2: time samples are held by the materialization result, not the node, so recover
            // them from the caller's node-keyed map. The authored default stays in Value above;
            // the two are independent (an attribute may have either, both or neither).
            if (timeSamples != null &&
                timeSamples.TryGetValue(attributeNode, out IReadOnlyList<UsdTimeSample>? samples))
            {
                foreach (UsdTimeSample sample in samples)
                {
                    attribute.TimeSamples[sample.TimeCode] = sample.Value;
                }
            }
            return attribute;
        }

        private static void ResolveExportedConnections(
            ISystemContext context,
            List<(UsdAttribute Attribute, UsdAttributeState Node)> pending,
            Dictionary<NodeId, string> attributePaths,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<string>>? connectionPaths)
        {
            NodeId connectionTypeId = ExpandedNodeId.ToNodeId(
                Opc.Ua.OpenUsd.Scene.ReferenceTypeIds.UsdConnection, context.NamespaceUris);
            var references = new List<IReference>();
            foreach ((UsdAttribute attribute, UsdAttributeState node) in pending)
            {
                // Authoritative: ConnectionPaths is authored on the node itself and carries every
                // connection in order, including a target outside the materialized subtree that
                // has no browsable edge. It is the connection counterpart of a relationship's
                // TargetPaths (§5.4, §5.5, §7.4), so — unlike the side channel below — it works
                // for a bare stage node too.
                if (node.ConnectionPaths?.Value is { } declared && declared.Count > 0)
                {
                    foreach (string path in declared)
                    {
                        attribute.Connections.Add(path);
                    }
                    continue;
                }

                // Compatibility: a materialization result may still carry the recorded order for
                // a stage materialized before ConnectionPaths existed.
                if (connectionPaths != null &&
                    connectionPaths.TryGetValue(node, out IReadOnlyList<string>? authored))
                {
                    foreach (string path in authored)
                    {
                        attribute.Connections.Add(path);
                    }
                    continue;
                }

                // Fallback (a bare stage node with no recorded side channel): rebuild from the
                // browsable UsdConnection edges. Reference enumeration order is not the authored
                // order, so sort by target SdfPath to make the exported sequence deterministic;
                // targets outside the subtree cannot be recovered on this path.
                references.Clear();
                node.GetReferences(context, references, connectionTypeId, false);
                var resolved = new List<string>();
                foreach (IReference reference in references)
                {
                    NodeId targetId =
                        ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                    if (!targetId.IsNull &&
                        attributePaths.TryGetValue(targetId, out string? path))
                    {
                        resolved.Add(path);
                    }
                }
                resolved.Sort(StringComparer.Ordinal);
                foreach (string path in resolved)
                {
                    attribute.Connections.Add(path);
                }
            }
        }

        private static UsdRelationship ExportRelationship(UsdRelationshipState relationshipNode)
        {
            var relationship = new UsdRelationship(relationshipNode.BrowseName.Name ?? string.Empty)
            {
                Custom = ReadBool(relationshipNode.Custom) ?? false
            };
            // TargetPaths is authoritative: it also covers targets that lie outside the
            // materialized subtree and therefore have no browsable edge (§5.5).
            if (relationshipNode.TargetPaths?.Value is { } paths)
            {
                foreach (string path in paths)
                {
                    relationship.Targets.Add(path);
                }
            }
            return relationship;
        }

        private static void ExportComposition(
            ISystemContext context, UsdPrimState primNode, UsdPrim prim)
        {
            if (primNode.Composition == null)
            {
                return;
            }
            foreach (UsdCompositionArcState arcNode in
                ChildrenOfType<UsdCompositionArcState>(context, primNode.Composition))
            {
                var arc = new UsdCompositionArc(ReadEnum(arcNode.ArcKind, UsdArcKindEnum.Reference))
                {
                    AssetPath = ReadString(arcNode.AssetPath) ?? string.Empty,
                    PrimPath = ReadString(arcNode.PrimPath) ?? string.Empty,
                    ListPosition = ReadEnum(arcNode.ListPosition, UsdListOpTypeEnum.Explicit),
                    VariantSet = ReadString(arcNode.VariantSet) ?? string.Empty,
                    VariantSelection = ReadString(arcNode.VariantSelection) ?? string.Empty
                };
                prim.Composition.Add(arc);
            }
        }

        private static void ExportVariantSets(
            ISystemContext context, UsdPrimState primNode, UsdPrim prim,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>>? timeSamples,
            IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<string>>? connectionPaths)
        {
            if (primNode.VariantSets == null)
            {
                return;
            }
            foreach (UsdVariantSetState setNode in
                ChildrenOfType<UsdVariantSetState>(context, primNode.VariantSets))
            {
                var variantSet = new UsdVariantSet(
                    ReadString(setNode.SetName) ?? setNode.BrowseName.Name ?? string.Empty,
                    ReadString(setNode.Selection) ?? string.Empty);

                // §5.6: recover every authored <Variant> branch (a UsdPrimType child of the set)
                // as a prim-shaped branch body, exporting each with the same machinery as a
                // top-level prim. A branch is self-contained, so its attribute index and
                // connections resolve within the branch alone.
                foreach (UsdPrimState branchNode in ChildrenOfType<UsdPrimState>(context, setNode))
                {
                    var branchPaths = new Dictionary<NodeId, string>();
                    var branchPending =
                        new List<(UsdAttribute Attribute, UsdAttributeState Node)>();
                    string branchPath = "/" + (branchNode.BrowseName.Name ?? string.Empty);
                    UsdPrim branch = ExportPrim(
                        context, branchNode, branchPath, branchPaths, branchPending,
                        timeSamples, connectionPaths);
                    ResolveExportedConnections(
                        context, branchPending, branchPaths, connectionPaths);
                    variantSet.Variants.Add(branch);
                }

                prim.VariantSets.Add(variantSet);
            }
        }

        private static void ExportAppliedSchemas(
            ISystemContext context, UsdPrimState primNode, UsdPrim prim)
        {
            if (primNode.AppliedSchemas == null)
            {
                return;
            }
            foreach (UsdApiSchemaState schemaNode in
                ChildrenOfType<UsdApiSchemaState>(context, primNode.AppliedSchemas))
            {
                // The portable georeference AddIns are additive provenance dual-authored by
                // the materializer; they carry no opinion the vendor schema does not, so they
                // are not re-emitted (Annex B.4).
                if (schemaNode is UsdGeoreferenceApiState or UsdGlobeAnchorApiState)
                {
                    continue;
                }
                string schemaName = ReadString(schemaNode.SchemaName)
                    ?? schemaNode.BrowseName.Name
                    ?? string.Empty;
                var schema = new UsdApiSchema(schemaName);
                if (schemaNode is UsdCollectionAPIState collection)
                {
                    schema.ExpansionRule = ReadString(collection.ExpansionRule) ?? string.Empty;
                }
                prim.ApiSchemas.Add(schema);
            }
        }

        private static void ExportMetadata(
            ISystemContext context, UsdPrimState primNode, UsdPrim prim)
        {
            if (primNode.Metadata == null)
            {
                return;
            }
            ReadMetadataFolder(context, primNode.Metadata, prim.Metadata);
        }

        /// <summary>
        /// Recovers a <c>Metadata/</c> folder into a metadata dictionary (§6.3), the inverse of the
        /// materializer's typed authoring. A leaf Property is read back in its own type through
        /// <c>Decoerce</c>, which reverses the §6.2 coercion off the Variant's typed accessors, so
        /// a value round-trips as itself rather than an opaque box. A nested Metadata folder is
        /// recovered as a nested dictionary, so structured <c>customData</c> keeps its authored
        /// nesting to any depth.
        /// </summary>
        private static void ReadMetadataFolder(
            ISystemContext context, NodeState folder, IDictionary<string, UsdValue> into)
        {
            var children = new List<BaseInstanceState>();
            folder.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                string key = child.BrowseName.Name ?? string.Empty;
                if (key.Length == 0)
                {
                    continue;
                }
                switch (child)
                {
                    case BaseVariableState property:
                        into[key] = UsdValueCoercion.Decoerce(property.Value);
                        break;
                    case FolderState subFolder:
                        var nested = new Dictionary<string, UsdValue>(StringComparer.Ordinal);
                        ReadMetadataFolder(context, subFolder, nested);
                        into[key] = UsdValue.FromDictionary(nested);
                        break;
                }
            }
        }

        private static IEnumerable<T> ChildrenOfType<T>(ISystemContext context, NodeState parent)
            where T : NodeState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is T typed)
                {
                    yield return typed;
                }
            }
        }

        private static string? ReadString(BaseVariableState? node)
        {
            if (node == null)
            {
                return null;
            }
            return node.Value.TryGetValue(out string value) ? value : null;
        }

        private static double? ReadDouble(BaseVariableState? node)
        {
            if (node == null)
            {
                return null;
            }
            return node.Value.TryGetValue(out double value) ? value : null;
        }

        private static bool? ReadBool(BaseVariableState? node)
        {
            if (node == null)
            {
                return null;
            }
            return node.Value.TryGetValue(out bool value) ? value : null;
        }

        private static TEnum ReadEnum<TEnum>(BaseVariableState? node, TEnum fallback)
            where TEnum : struct, Enum
        {
            if (node == null)
            {
                return fallback;
            }
            if (node.Value.TryGetValue(out int raw) &&
                Enum.IsDefined(typeof(TEnum), raw))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), raw);
            }
            return node.Value.TryGetValue(out TEnum typed) ? typed : fallback;
        }
    }
}
