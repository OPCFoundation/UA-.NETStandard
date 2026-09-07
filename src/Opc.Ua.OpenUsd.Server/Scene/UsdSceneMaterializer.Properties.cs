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
using System.Globalization;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Server.Scene
{
    public static partial class UsdSceneMaterializer
    {
        private const string kCesiumLatitude = "cesium:anchor:latitude";
        private const string kCesiumLongitude = "cesium:anchor:longitude";
        private const string kCesiumHeight = "cesium:anchor:height";
        private const string kCesiumGeoreference = "CesiumGeoreferencePrim";
        private const string kCesiumGlobeAnchor = "CesiumGlobeAnchorAPI";

        /// <summary>
        /// WGS84 — the CRS Cesium anchors are expressed in.
        /// </summary>
        private const int kEpsgWgs84 = 4326;

        /// <summary>
        /// Whether a prim is a *typed* Cesium georeference or globe-anchor prim
        /// (<c>def CesiumGeoreferencePrim …</c> / <c>def CesiumGlobeAnchorAPI …</c>; §5.8,
        /// Annex B.1). Such a prim needs its <c>AppliedSchemas/</c> folder created — and the
        /// portable anchor dual-authored — even when it carries no applied API schema.
        /// </summary>
        private static bool IsGeoreferenceTypedPrim(UsdPrim prim)
        {
            return string.Equals(prim.TypeName, kCesiumGeoreference, StringComparison.Ordinal) ||
                string.Equals(prim.TypeName, kCesiumGlobeAnchor, StringComparison.Ordinal);
        }

        private static UsdAttributeState MaterializeAttribute(
            ISystemContext context,
            UsdPrimState primNode,
            UsdAttribute attribute,
            ushort ns,
            UsdMaterializationOptions options)
        {
            UsdAttributeState node = primNode.AddUsdAttribute_Placeholder(
                context, new QualifiedName(attribute.Name, ns));

            UsdValueTypeMapping mapping = UsdValueTypeMap.Map(
                attribute.TypeName, context.NamespaceUris);
            uint components = UsdValueTypeMap.ComponentCount(attribute.TypeName);

            node.DataType = mapping.DataTypeId;
            node.ValueRank = mapping.ValueRank;
            if (mapping.ArrayDimensions != null)
            {
                node.ArrayDimensions = mapping.ArrayDimensions;
            }

            if (UsdValueCoercion.TryCoerce(attribute.Value, mapping, components, out Variant variant))
            {
                node.Value = variant;
            }

            // The exact SdfValueTypeName is always retained, which is what makes the §6.2
            // mapping reversible even where several USD types share one OPC UA DataType.
            node.CreateOrReplaceUsdTypeName(context, null!).Value = attribute.TypeName;
            node.CreateOrReplaceVariability(context, null!).Value = attribute.Variability;
            if (attribute.Custom)
            {
                node.CreateOrReplaceCustom(context, null!).Value = true;
            }
            if (!string.IsNullOrEmpty(attribute.Namespace))
            {
                node.CreateOrReplaceNamespace(context, null!).Value = attribute.Namespace;
            }
            if (!string.IsNullOrEmpty(attribute.Interpolation))
            {
                node.CreateOrReplaceInterpolation(context, null!).Value = attribute.Interpolation;
            }

            if (attribute.Live)
            {
                // Mode A (§9): the Value is server-maintained and time-varying, so a
                // Subscription delivers changes and — where the Server retains it —
                // HistoryRead exposes the value timeline.
                node.AccessLevel = AccessLevels.CurrentReadOrWrite;
                node.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                if (options.HistorizeLiveAttributes)
                {
                    node.Historizing = true;
                    node.AccessLevel |= AccessLevels.HistoryRead;
                    node.UserAccessLevel |= AccessLevels.HistoryRead;
                }
            }

            // §7.1 step 3: an attribute that carries time samples materializes its authored
            // default as Value (already set above) and exposes the samples through
            // HistoricalAccess. Time samples make the node historizing independently of Live —
            // the samples ARE the retained timeline — so mark it read + HistoryRead. The samples
            // themselves are recorded by the caller into the materialization result, from where a
            // hosting Server binds them to its historian and the exporter recovers them (§7.2).
            if (attribute.TimeSamples.Count > 0)
            {
                node.Historizing = true;
                node.AccessLevel |= AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                node.UserAccessLevel |= AccessLevels.CurrentRead | AccessLevels.HistoryRead;
            }

            return node;
        }

        private static UsdRelationshipState MaterializeRelationship(
            ISystemContext context,
            UsdPrimState primNode,
            UsdRelationship relationship,
            ushort ns)
        {
            UsdRelationshipState node = primNode.AddUsdRelationship_Placeholder(
                context, new QualifiedName(relationship.Name, ns));

            var paths = new string[relationship.Targets.Count];
            relationship.Targets.CopyTo(paths, 0);
            node.CreateOrReplaceTargetPaths(context, null!).Value = paths;
            if (relationship.Custom)
            {
                node.CreateOrReplaceCustom(context, null!).Value = true;
            }
            return node;
        }

        private static void ResolveRelationshipTargets(
            ISystemContext context,
            List<(UsdRelationshipState Node, UsdRelationship Source)> pending,
            Dictionary<string, UsdPrimState> prims,
            Dictionary<string, UsdAttributeState> attributes)
        {
            foreach ((UsdRelationshipState node, UsdRelationship source) in pending)
            {
                var resolved = new List<NodeId>(source.Targets.Count);
                foreach (string path in source.Targets)
                {
                    NodeState? target = null;
                    if (prims.TryGetValue(path, out UsdPrimState? prim))
                    {
                        target = prim;
                    }
                    else if (attributes.TryGetValue(path, out UsdAttributeState? attribute))
                    {
                        target = attribute;
                    }
                    if (target == null)
                    {
                        // §5.5 / §7.1 step 4 require the targets to stay ordered, so Targets
                        // must correspond positionally to TargetPaths. A target outside the
                        // materialized subtree keeps full fidelity through TargetPaths; here it
                        // takes a null-NodeId placeholder so the two arrays remain index-aligned
                        // (entry i of one describes the same target as entry i of the other) and
                        // only resolvable targets additionally become browsable edges.
                        resolved.Add(NodeId.Null);
                        continue;
                    }
                    resolved.Add(target.NodeId);
                    node.AddReference(
                        ExpandedNodeId.ToNodeId(
                            Opc.Ua.OpenUsd.Scene.ReferenceTypeIds.UsdRelationshipTarget,
                            context.NamespaceUris),
                        false,
                        target.NodeId);
                }
                node.CreateOrReplaceTargets(context, null!).Value = resolved.ToArray();
            }
        }

        private static void ResolveConnections(
            ISystemContext context,
            List<(UsdAttributeState Node, UsdAttribute Source)> pending,
            Dictionary<string, UsdAttributeState> attributes)
        {
            NodeId connectionTypeId = ExpandedNodeId.ToNodeId(
                Opc.Ua.OpenUsd.Scene.ReferenceTypeIds.UsdConnection, context.NamespaceUris);
            foreach ((UsdAttributeState node, UsdAttribute source) in pending)
            {
                // ConnectionPaths is the connection counterpart of UsdRelationshipType's
                // TargetPaths (§5.4/§5.5): it carries every authored SdfPath in order, so a
                // connection whose target lies outside the materialized subtree — and therefore
                // has no browsable edge — still survives an export, and authored order and
                // multiplicity are preserved on the node itself rather than out of band.
                var authored = new string[source.Connections.Count];
                source.Connections.CopyTo(authored, 0);
                node.CreateOrReplaceConnectionPaths(context, null!).Value = authored;

                // Materialized attributes deliberately share the model's placeholder NodeId
                // (xUsdAttribute_, i=6023), so two connections authored on one attribute can
                // resolve to the same target NodeId. A forward reference is keyed by
                // (type, isInverse, target), so adding that target twice would throw; dedupe the
                // browsable edges by target NodeId. These edges exist only for §5.4
                // browsability — ConnectionPaths above is what an export relies on.
                var linked = new HashSet<NodeId>();
                foreach (string path in source.Connections)
                {
                    if (attributes.TryGetValue(path, out UsdAttributeState? target) &&
                        linked.Add(target.NodeId))
                    {
                        node.AddReference(connectionTypeId, false, target.NodeId);
                    }
                }
            }
        }

        private static void MaterializeComposition(
            ISystemContext context, UsdPrimState primNode, UsdPrim prim, ushort ns)
        {
            FolderState folder = primNode.CreateOrReplaceComposition(context, null!);
            EnsureNodeId(context, folder);

            int index = 0;
            foreach (UsdCompositionArc arc in prim.Composition)
            {
                // string.Create(IFormatProvider, ...) is .NET 6+; concatenation with an
                // invariant-culture index keeps the Server compiling on net472/net48/
                // netstandard2.1 while staying culture-independent.
                string name = arc.ArcKind.ToString() + "_" +
                    index.ToString(CultureInfo.InvariantCulture);
                index++;
                UsdCompositionArcState node = context.CreateInstanceOfUsdCompositionArcType(
                    folder, new QualifiedName(name, ns));
                Attach(context, folder, node, Opc.Ua.ReferenceTypeIds.HasComponent);

                node.CreateOrReplaceArcKind(context, null!).Value = arc.ArcKind;
                if (!string.IsNullOrEmpty(arc.AssetPath))
                {
                    node.CreateOrReplaceAssetPath(context, null!).Value = arc.AssetPath;
                }
                if (!string.IsNullOrEmpty(arc.PrimPath))
                {
                    node.CreateOrReplacePrimPath(context, null!).Value = arc.PrimPath;
                }
                node.CreateOrReplaceListPosition(context, null!).Value = arc.ListPosition;
                if (!string.IsNullOrEmpty(arc.VariantSet))
                {
                    node.CreateOrReplaceVariantSet(context, null!).Value = arc.VariantSet;
                }
                if (!string.IsNullOrEmpty(arc.VariantSelection))
                {
                    node.CreateOrReplaceVariantSelection(context, null!).Value = arc.VariantSelection;
                }
            }
        }

        private static void MaterializeVariantSets(
            ISystemContext context, UsdPrimState primNode, UsdPrim prim, ushort ns,
            UsdMaterializationOptions options, UsdMaterializationRecorder recorder)
        {
            FolderState folder = primNode.CreateOrReplaceVariantSets(context, null!);
            EnsureNodeId(context, folder);

            foreach (UsdVariantSet variantSet in prim.VariantSets)
            {
                UsdVariantSetState node = context.CreateInstanceOfUsdVariantSetType(
                    folder, new QualifiedName(variantSet.SetName, ns));
                Attach(context, folder, node, Opc.Ua.ReferenceTypeIds.HasComponent);
                node.CreateOrReplaceSetName(context, null!).Value = variantSet.SetName;
                node.CreateOrReplaceSelection(context, null!).Value = variantSet.Selection;

                // §5.6: a UsdVariantSetType exposes its variant *branches* as <Variant>
                // OptionalPlaceholder children (UsdPrimType). Every captured branch is authored —
                // not only the selection — so the full branch structure the model defines is
                // materialized. A set that recorded only a resolved Selection (no captured
                // branches) authors no branch rather than inventing one from the selection name
                // (fail closed); its selection is still carried by the Selection property above.
                foreach (UsdPrim branch in variantSet.Variants)
                {
                    UsdPrimState branchNode = node.AddVariant_Placeholder(
                        context, new QualifiedName(branch.Name, ns));
                    // AddVariant_Placeholder leaves the type's placeholder NodeId (i=6055); force a fresh
                    // per-instance NodeId so branches on different sets never collide, matching
                    // how the CreateInstanceOf* factories mint instance ids.
                    context.AssignInstanceNodeId(branchNode);

                    // A branch body is prim-shaped and self-contained: its relationships and
                    // connections address paths *within the branch*, so it is populated and
                    // resolved against an isolated local index. That keeps branch-internal edges
                    // working while never leaking branch paths into the composed-scene index the
                    // §7.4 round trip and Part 1 bindings resolve against. Sampled and connected
                    // branch attributes are still recorded into the shared recorder (node-keyed)
                    // so they round-trip through the exporter.
                    var branchPrims = new Dictionary<string, UsdPrimState>(StringComparer.Ordinal);
                    var branchAttributes =
                        new Dictionary<string, UsdAttributeState>(StringComparer.Ordinal);
                    var branchRelationships = new List<(UsdRelationshipState, UsdRelationship)>();
                    var branchConnections = new List<(UsdAttributeState, UsdAttribute)>();

                    PopulatePrimNode(
                        context, branchNode, branch, "/" + branch.Name, ns, options,
                        branchPrims, branchAttributes, branchRelationships, branchConnections,
                        recorder);

                    ResolveRelationshipTargets(
                        context, branchRelationships, branchPrims, branchAttributes);
                    ResolveConnections(context, branchConnections, branchAttributes);
                }
            }
        }

        private static void MaterializeMetadata(
            ISystemContext context, UsdPrimState primNode, UsdPrim prim, ushort ns)
        {
            FolderState folder = primNode.CreateOrReplaceMetadata(context, null!);
            EnsureNodeId(context, folder);
            MaterializeMetadataEntries(context, folder, prim.Metadata, ns);
        }

        /// <summary>
        /// Populates a <c>Metadata/</c> folder from a set of authored entries (§6.3). Each scalar
        /// or array entry becomes a Property carrying its value in its own type — chosen per value
        /// rather than stringified — so a client reads the authored kind and the exporter recovers
        /// it. A nested dictionary becomes a nested Metadata folder, so structured <c>customData</c>
        /// is browsable as authored; this recurses to whatever depth the source nests.
        /// </summary>
        private static void MaterializeMetadataEntries(
            ISystemContext context,
            NodeState folder,
            IEnumerable<KeyValuePair<string, UsdValue>> entries,
            ushort ns)
        {
            foreach (KeyValuePair<string, UsdValue> entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Key))
                {
                    continue;
                }

                if (entry.Value.TryGetDictionary(
                    out IReadOnlyDictionary<string, UsdValue> nested))
                {
                    var subFolder = new FolderState(folder)
                    {
                        BrowseName = new QualifiedName(entry.Key, ns),
                        DisplayName = new LocalizedText(entry.Key),
                        TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes
                    };
                    folder.AddChild(subFolder);
                    subFolder.NodeId = context.RequireNodeIdFactory().New(context, subFolder);
                    MaterializeMetadataEntries(context, subFolder, nested, ns);
                    continue;
                }

                var property = new PropertyState(folder)
                {
                    BrowseName = new QualifiedName(entry.Key, ns),
                    DisplayName = new LocalizedText(entry.Key),
                    TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                    DataType = Opc.Ua.DataTypeIds.BaseDataType,
                    ValueRank = ValueRanks.Scalar
                };
                if (!entry.Value.IsNull)
                {
                    if (TryCoerceMetadataValue(
                        entry.Value, out Variant variant, out NodeId dataType, out int valueRank))
                    {
                        // A recognised value keeps its type through the round trip, so the
                        // exporter recovers the authored value rather than an opaque string (§6.3).
                        property.Value = variant;
                        property.DataType = dataType;
                        property.ValueRank = valueRank;
                    }
                    else
                    {
                        // A value of no representable type is carried as its invariant textual
                        // form rather than dropped — the §6.3 last resort, not a typed guess.
                        property.Value = Variant.From(entry.Value.ToString());
                        property.DataType = Opc.Ua.DataTypeIds.String;
                    }
                }
                folder.AddChild(property);
                property.NodeId = context.RequireNodeIdFactory().New(context, property);
            }
        }

        /// <summary>
        /// Chooses a Variant, DataType and ValueRank for a leaf metadata value from its kind,
        /// so a scalar keeps its exact type and an array keeps its (inferred) element type through
        /// the materialize→export round trip (§6.3). Returns <c>false</c> for a value whose kind is
        /// not representable, so the caller carries its textual form instead of guessing.
        /// </summary>
        private static bool TryCoerceMetadataValue(
            UsdValue value, out Variant variant, out NodeId dataType, out int valueRank)
        {
            valueRank = ValueRanks.Scalar;
            switch (value.Kind)
            {
                case UsdValueKind.Boolean:
                    value.TryGetBoolean(out bool b);
                    variant = Variant.From(b);
                    dataType = Opc.Ua.DataTypeIds.Boolean;
                    return true;
                case UsdValueKind.Integer:
                    value.TryGetInteger(out long l);
                    variant = Variant.From(l);
                    dataType = Opc.Ua.DataTypeIds.Int64;
                    return true;
                case UsdValueKind.Double:
                    value.TryGetDouble(out double d);
                    variant = Variant.From(d);
                    dataType = Opc.Ua.DataTypeIds.Double;
                    return true;
                case UsdValueKind.String:
                case UsdValueKind.Token:
                case UsdValueKind.AssetPath:
                case UsdValueKind.PathReference:
                    value.TryGetText(out string s);
                    variant = Variant.From(s);
                    dataType = Opc.Ua.DataTypeIds.String;
                    return true;
                case UsdValueKind.Tuple:
                case UsdValueKind.Array:
                case UsdValueKind.Matrix:
                    value.TryGetItems(out ArrayOf<UsdValue> items);
                    return TryCoerceMetadataArray(items, out variant, out dataType, out valueRank);
                default:
                    variant = default;
                    dataType = Opc.Ua.DataTypeIds.String;
                    return false;
            }
        }

        /// <summary>
        /// Coerces a metadata array or tuple into a typed 1-D Variant, inferring the element type
        /// from the first non-absent element. A homogeneous numeric or boolean sequence keeps its
        /// element type; an empty or mixed sequence is carried as a string array so it round-trips
        /// as a sequence rather than being dropped (§6.3, fail closed — no numeric guess).
        /// </summary>
        private static bool TryCoerceMetadataArray(
            ArrayOf<UsdValue> sequence,
            out Variant variant,
            out NodeId dataType,
            out int valueRank)
        {
            valueRank = ValueRanks.OneDimension;
            UsdValue[] items = sequence.ToArray() ?? [];
            UsdValueKind first = UsdValueKind.Null;
            for (int ii = 0; ii < items.Length; ii++)
            {
                if (!items[ii].IsNull)
                {
                    first = items[ii].Kind;
                    break;
                }
            }
            switch (first)
            {
                case UsdValueKind.Boolean
                    when TryFillArray(items, static (UsdValue v, out bool r) => v.TryGetBoolean(out r), out bool[] bools):
                    variant = Variant.From((ArrayOf<bool>)bools);
                    dataType = Opc.Ua.DataTypeIds.Boolean;
                    return true;
                case UsdValueKind.Integer
                    when TryFillArray(items, static (UsdValue v, out long r) => v.TryGetInteger(out r), out long[] longs):
                    variant = Variant.From((ArrayOf<long>)longs);
                    dataType = Opc.Ua.DataTypeIds.Int64;
                    return true;
                case UsdValueKind.Double
                    when TryFillArray(items, static (UsdValue v, out double r) => v.TryGetNumber(out r), out double[] doubles):
                    variant = Variant.From((ArrayOf<double>)doubles);
                    dataType = Opc.Ua.DataTypeIds.Double;
                    return true;
                default:
                    var strings = new string[items.Length];
                    for (int i = 0; i < items.Length; i++)
                    {
                        strings[i] = items[i].TryGetText(out string text)
                            ? text
                            : items[i].ToString();
                    }
                    variant = Variant.From((ArrayOf<string>)strings);
                    dataType = Opc.Ua.DataTypeIds.String;
                    return true;
            }
        }

        private delegate bool UsdValueReader<T>(UsdValue value, out T result);

        /// <summary>
        /// Fills a typed array by reading every element with <paramref name="read"/>, failing
        /// closed if any element cannot be read so a heterogeneous array falls back to text.
        /// </summary>
        /// <typeparam name="T">The element type of the array being filled.</typeparam>
        private static bool TryFillArray<T>(
            UsdValue[] items, UsdValueReader<T> read, out T[] result)
        {
            var array = new T[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (!read(items[i], out T converted))
                {
                    result = Array.Empty<T>();
                    return false;
                }
                array[i] = converted;
            }
            result = array;
            return true;
        }

        private static void MaterializeAppliedSchemas(
            ISystemContext context,
            UsdPrimState primNode,
            UsdPrim prim,
            ushort ns,
            UsdMaterializationOptions options)
        {
            FolderState folder = primNode.CreateOrReplaceAppliedSchemas(context, null!);
            EnsureNodeId(context, folder);

            bool sawGeoreference = false;
            bool sawGlobeAnchor = false;

            foreach (UsdApiSchema schema in prim.ApiSchemas)
            {
                string browseName = string.IsNullOrEmpty(schema.InstanceName)
                    ? schema.FamilyName
                    : schema.FamilyName + "_" + schema.InstanceName;

                UsdApiSchemaState node;
                if (string.Equals(schema.FamilyName, "CollectionAPI", StringComparison.Ordinal))
                {
                    UsdCollectionAPIState collection = context.CreateInstanceOfUsdCollectionAPIType(
                        folder, new QualifiedName(browseName, ns));
                    if (!string.IsNullOrEmpty(schema.ExpansionRule))
                    {
                        collection.CreateOrReplaceExpansionRule(context, null!).Value =
                            schema.ExpansionRule;
                    }
                    node = collection;
                }
                else
                {
                    // An unknown API schema is never dropped: it degrades to a generic
                    // UsdApiSchemaType AddIn carrying its SchemaName (§8.4).
                    node = context.CreateInstanceOfUsdApiSchemaType(
                        folder, new QualifiedName(browseName, ns));
                }

                Attach(context, folder, node, Opc.Ua.ReferenceTypeIds.HasAddIn);
                node.CreateOrReplaceSchemaName(context, null!).Value = schema.SchemaName;

                sawGeoreference |= string.Equals(
                    schema.FamilyName, kCesiumGeoreference, StringComparison.Ordinal);
                sawGlobeAnchor |= string.Equals(
                    schema.FamilyName, kCesiumGlobeAnchor, StringComparison.Ordinal);
            }

            // A real Cesium stage authors the georeference as a *typed* prim
            // (def CesiumGeoreferencePrim "World" { ... }; §5.8, Annex B.1), which the reader
            // records in prim.TypeName rather than in ApiSchemas. The apiSchemas spelling used
            // by the Annex B.3 snippet is honoured above; either spelling must trigger the
            // portable dual-authoring so a generic client always finds a vendor-neutral anchor.
            sawGeoreference |= string.Equals(
                prim.TypeName, kCesiumGeoreference, StringComparison.Ordinal);
            sawGlobeAnchor |= string.Equals(
                prim.TypeName, kCesiumGlobeAnchor, StringComparison.Ordinal);

            if (!options.DualAuthorPortableGeoreference)
            {
                return;
            }
            // A materializer that recognises a vendor georeference schema should additionally
            // populate the portable schema, so a generic client obtains the anchor from one
            // well-known type while a vendor-aware client still reads the native one (§5.8).
            if (sawGeoreference)
            {
                AuthorPortableGeoreference(context, folder, prim, ns);
            }
            if (sawGlobeAnchor)
            {
                AuthorPortableGlobeAnchor(context, folder, prim, ns);
            }
        }

        private static void AuthorPortableGeoreference(
            ISystemContext context, FolderState folder, UsdPrim prim, ushort ns)
        {
            if (!TryReadAnchor(prim, out double latitude, out double longitude, out double height))
            {
                return;
            }
            UsdGeoreferenceApiState node = context.CreateInstanceOfUsdGeoreferenceApiType(
                folder, new QualifiedName(Opc.Ua.OpenUsd.Scene.BrowseNames.UsdGeoreferenceApiType, ns));
            Attach(context, folder, node, Opc.Ua.ReferenceTypeIds.HasAddIn);
            node.CreateOrReplaceSchemaName(context, null!).Value =
                Opc.Ua.OpenUsd.Scene.BrowseNames.UsdGeoreferenceApiType;
            node.CreateOrReplaceLatitude(context, null!).Value = latitude;
            node.CreateOrReplaceLongitude(context, null!).Value = longitude;
            node.CreateOrReplaceHeight(context, null!).Value = height;
            node.CreateOrReplaceEpsgCode(context, null!).Value = kEpsgWgs84;
            node.CreateOrReplaceTangentPlane(context, null!).Value = "ENU";
        }

        private static void AuthorPortableGlobeAnchor(
            ISystemContext context, FolderState folder, UsdPrim prim, ushort ns)
        {
            if (!TryReadAnchor(prim, out double latitude, out double longitude, out double height))
            {
                return;
            }
            UsdGlobeAnchorApiState node = context.CreateInstanceOfUsdGlobeAnchorApiType(
                folder, new QualifiedName(Opc.Ua.OpenUsd.Scene.BrowseNames.UsdGlobeAnchorApiType, ns));
            Attach(context, folder, node, Opc.Ua.ReferenceTypeIds.HasAddIn);
            node.CreateOrReplaceSchemaName(context, null!).Value =
                Opc.Ua.OpenUsd.Scene.BrowseNames.UsdGlobeAnchorApiType;
            node.CreateOrReplaceLatitude(context, null!).Value = latitude;
            node.CreateOrReplaceLongitude(context, null!).Value = longitude;
            node.CreateOrReplaceHeight(context, null!).Value = height;
        }

        private static bool TryReadAnchor(
            UsdPrim prim, out double latitude, out double longitude, out double height)
        {
            latitude = 0.0;
            longitude = 0.0;
            height = 0.0;
            bool haveLatitude = false;
            bool haveLongitude = false;
            bool haveHeight = false;

            foreach (UsdAttribute attribute in prim.Attributes)
            {
                if (string.Equals(attribute.Name, kCesiumLatitude, StringComparison.Ordinal) &&
                    TryToDouble(attribute.Value, out double lat))
                {
                    latitude = lat;
                    haveLatitude = true;
                }
                else if (string.Equals(attribute.Name, kCesiumLongitude, StringComparison.Ordinal) &&
                    TryToDouble(attribute.Value, out double lon))
                {
                    longitude = lon;
                    haveLongitude = true;
                }
                else if (string.Equals(attribute.Name, kCesiumHeight, StringComparison.Ordinal) &&
                    TryToDouble(attribute.Value, out double hgt))
                {
                    height = hgt;
                    haveHeight = true;
                }
            }

            // Fail closed: latitude, longitude and ellipsoidal height are all required to place
            // the prim. A missing height is *not* defaulted to 0 m — for a geodetic height above
            // the ellipsoid that silently publishes a wrong altitude — so like the two horizontal
            // components an absent height yields no portable anchor at all.
            return haveLatitude && haveLongitude && haveHeight;
        }

        private static bool TryToDouble(UsdValue value, out double result)
        {
            if (value.TryGetNumber(out result))
            {
                return true;
            }
            if (value.TryGetText(out string text) && double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                result = parsed;
                return true;
            }
            result = 0.0;
            return false;
        }

        private static void Attach(
            ISystemContext context, NodeState parent, BaseInstanceState node, NodeId referenceTypeId)
        {
            node.ReferenceTypeId = referenceTypeId;
            parent.AddChild(node);
            node.NodeId = context.RequireNodeIdFactory().New(context, node);
        }

        private static void EnsureNodeId(ISystemContext context, NodeState node)
        {
            if (node.NodeId.IsNull)
            {
                node.NodeId = context.RequireNodeIdFactory().New(context, node);
            }
        }
    }

    /// <summary>
    /// Accumulates the per-node state a materialization must carry to the exporter for a
    /// lossless <c>.usda</c> round trip (§7.2, §7.4): the time samples of every sampled
    /// attribute (with the §9 epoch / <c>TimeCodesPerSecond</c> mapping context) and the
    /// authored connection paths of every connected attribute. Both are keyed by node identity
    /// because materialized attributes deliberately share the model's placeholder NodeId, so
    /// only object identity distinguishes them.
    /// </summary>
    /// <remarks>
    /// Connection paths are recorded here — rather than reconstructed from the forward
    /// <c>UsdConnection</c> edges on export — for two reasons the reference graph cannot serve
    /// (§5.4). First, reference enumeration order is not the authored order, so replaying the
    /// recorded list is what keeps the exported connection sequence deterministic and equal to
    /// the authored order (M-2). Second, a <c>.connect</c> whose target lies outside the
    /// materialized subtree has no browsable edge at all, so only the recorded path lets it
    /// survive materialize→export (M-5) — the same full-fidelity guarantee a relationship gets
    /// from its <c>TargetPaths</c> property. The attribute model carries no <c>ConnectionPaths</c>
    /// member, so this side channel stands in for one until the model gains it.
    /// </remarks>
    internal sealed class UsdMaterializationRecorder
    {
        private readonly Dictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>> _samplesByNode =
            new Dictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>>();
        private readonly Dictionary<UsdAttributeState, IReadOnlyList<string>> _connectionsByNode =
            new Dictionary<UsdAttributeState, IReadOnlyList<string>>();

        public UsdMaterializationRecorder(DateTime? epochUtc, double? timeCodesPerSecond)
        {
            EpochUtc = epochUtc;
            TimeCodesPerSecond = timeCodesPerSecond;
        }

        public DateTime? EpochUtc { get; }

        public double? TimeCodesPerSecond { get; }

        public IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<UsdTimeSample>> SamplesByNode =>
            _samplesByNode;

        public IReadOnlyDictionary<UsdAttributeState, IReadOnlyList<string>> ConnectionsByNode =>
            _connectionsByNode;

        public void Record(UsdAttributeState node, UsdAttribute attribute)
        {
            var samples = new List<UsdTimeSample>(attribute.TimeSamples.Count);
            foreach (KeyValuePair<double, UsdValue> sample in attribute.TimeSamples)
            {
                samples.Add(new UsdTimeSample(sample.Key, sample.Value));
            }
            _samplesByNode[node] = samples;
        }

        public void RecordConnections(UsdAttributeState node, IList<string> connections)
        {
            // Snapshot the authored order verbatim, including any target outside the
            // materialized subtree; the exporter replays this list to reproduce the exact
            // authored connection sequence (§5.4).
            var paths = new List<string>(connections.Count);
            foreach (string path in connections)
            {
                paths.Add(path);
            }
            _connectionsByNode[node] = paths;
        }
    }
}
