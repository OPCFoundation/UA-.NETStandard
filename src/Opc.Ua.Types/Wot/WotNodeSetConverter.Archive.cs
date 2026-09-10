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
using System.IO;
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        private static void ValidatePreservedReadableFacts(
            WotDocument document,
            UANodeSet baseline,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            UANodeSet? archiveContext = null)
        {
            // Identity normalization may append namespaces. It must never change
            // the authoritative NodeSet, even when a readable identity conflicts.
            var identities = new UANodeSet
            {
                NamespaceUris = baseline.NamespaceUris is null
                    ? null
                    : (string[])baseline.NamespaceUris.Clone()
            };
            INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(baseline, WotNodeSetAliases.Instance);
            Dictionary<string, UANode?> index = BuildArchivedFactIndex(baseline, aliases);
            UANode? root = FindArchivedNode(
                document, document.RootElement, null, index,
                identities, aliases, string.Empty, diagnostics);
            root ??= SelectRootNode(baseline);
            if (root is null)
            {
                ResolvePreservedAffordances(document, index, identities, aliases, diagnostics);
                if (document.RootElement.TryGetProperty("uav:dataTypeDefinitions", out JsonElement definitions))
                {
                    CompareArchivedDefinitions(
                        document, definitions, default, index, identities, aliases,
                        "/uav:dataTypeDefinitions", diagnostics);
                }
                return;
            }
            if (archiveContext is not null)
            {
                baseline = archiveContext;
                index = BuildArchivedFactIndex(baseline, aliases);
            }

            Dictionary<string, UANode?> resolved = ResolvePreservedAffordances(
                document, index, identities, aliases, diagnostics);
            bool needsProjection = RequiresPreservedReadableProjection(document.RootElement);
            var projectedNodes = new HashSet<string>(StringComparer.Ordinal);
            if (root.NodeId is not null && document.RootElement.TryGetProperty(DataMember, out _))
            {
                projectedNodes.Add(root.NodeId);
            }
            foreach ((string collection, IReadOnlyDictionary<string, JsonElement> affordances) in new[]
                {
                    ("properties", document.Properties), ("actions", document.Actions), ("events", document.Events)
                })
            {
                foreach (KeyValuePair<string, JsonElement> affordance in affordances)
                {
                    string pointer = "/" + collection + "/" + EscapeJsonPointerToken(affordance.Key);
                    if (RequiresPreservedReadableProjection(affordance.Value) &&
                        resolved[pointer]?.NodeId is { } nodeId)
                    {
                        needsProjection = true;
                        projectedNodes.Add(nodeId);
                    }
                }
            }
            using WotDocument? readable = needsProjection
                ? CreatePreservedReadableProjection(document, baseline, root, projectedNodes, options, diagnostics)
                : null;
            if (needsProjection && readable is null)
            {
                return;
            }
            JsonElement regenerated = readable?.RootElement ?? default;
            CompareArchivedNode(
                document, document.RootElement, regenerated, root, baseline, index,
                identities, aliases, string.Empty, isRoot: true, diagnostics, readable);

            CompareArchivedAffordances(
                document, document.Properties, "properties", regenerated,
                root, baseline, index, identities, aliases, diagnostics, resolved, readable);
            CompareArchivedAffordances(
                document, document.Actions, "actions", regenerated,
                root, baseline, index, identities, aliases, diagnostics, resolved, readable);
            CompareArchivedAffordances(
                document, document.Events, "events", regenerated,
                root, baseline, index, identities, aliases, diagnostics, resolved, readable);
        }

        private static Dictionary<string, UANode?> ResolvePreservedAffordances(
            WotDocument document,
            Dictionary<string, UANode?> index,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            List<WotDiagnostic> diagnostics)
        {
            var resolved = new Dictionary<string, UANode?>(StringComparer.Ordinal);
            Resolve(document.Properties, "properties");
            Resolve(document.Actions, "actions");
            Resolve(document.Events, "events");
            return resolved;

            void Resolve(IReadOnlyDictionary<string, JsonElement> affordances, string collection)
            {
                foreach (KeyValuePair<string, JsonElement> affordance in affordances)
                {
                    string pointer = "/" + collection + "/" + EscapeJsonPointerToken(affordance.Key);
                    resolved.Add(pointer, FindArchivedNode(
                        document, affordance.Value, affordance.Key, index,
                        identities, aliases, pointer, diagnostics));
                }
            }
        }

        private static bool RequiresPreservedReadableProjection(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            foreach (JsonProperty member in node.EnumerateObject())
            {
                if (member.Name == "uav:dataTypeDefinitions" &&
                    member.Value.ValueKind == JsonValueKind.Array &&
                    member.Value.GetArrayLength() == 0)
                {
                    continue;
                }
                if (member.Name is InputMember or OutputMember or "uav:dataTypeDefinition" or
                    "uav:dataTypeDefinitions" or InverseNameTerm or SymmetricTerm or DataMember or
                    EngineeringUnitsTerm or InstrumentRangeTerm or MinimumMember or MaximumMember or
                    ConditionTypeTerm or ConditionTypeIdTerm or ConditionActionTerm or ActsOnTerm)
                {
                    return true;
                }
            }
            return false;
        }

        private static WotDocument? CreatePreservedReadableProjection(
            WotDocument document,
            UANodeSet baseline,
            UANode root,
            HashSet<string> projectedNodes,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var projectionDiagnostics = new List<WotDiagnostic>();
            try
            {
                byte[] regenerated = WriteReadableDocument(
                    baseline, root, document.Title ?? string.Empty, explicitTitle: true, [],
                    nativeProjection: null, emitEnvelope: false, options, projectionDiagnostics,
                    projectedNodeIds: projectedNodes);
                if (HasErrors(projectionDiagnostics))
                {
                    return null;
                }
                return WotDocument.FromOwnedBytes(regenerated, options);
            }
            catch (JsonException exception)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionConflict,
                    "The readable assertions cannot be compared within the configured JSON depth limit: " +
                    exception.Message,
                    WotLocation.FromNode(root.NodeId)));
                return null;
            }
            finally
            {
                diagnostics.AddRange(projectionDiagnostics);
            }
        }

        private static Dictionary<string, UANode?> BuildArchivedFactIndex(
            UANodeSet baseline,
            INodeSetAliasResolver aliases)
        {
            var index = new Dictionary<string, UANode?>(StringComparer.Ordinal);
            foreach (UANode node in baseline.Items ?? [])
            {
                Add("id:" + ResolveArchivedAlias(node.NodeId, aliases), node);
                Add("name:" + NormalizeArchivedBrowseName(node.BrowseName), node);
                if (node is UADataType)
                {
                    Add("datatype-name:" + NormalizeArchivedBrowseName(node.BrowseName), node);
                }
                Add("key:" + LocalName(node.BrowseName), node);
                if (node is UAInstance instance && !string.IsNullOrEmpty(instance.ParentNodeId))
                {
                    Add("owned:" + ResolveArchivedAlias(node.NodeId, aliases), node);
                }
                foreach (Reference reference in node.References ?? [])
                {
                    if (IsComponentReference(ResolveArchivedAlias(reference.ReferenceType, aliases)))
                    {
                        Add("owned:" +
                            ResolveArchivedAlias(
                                reference.IsForward ? reference.Value : node.NodeId, aliases), node);
                    }
                }
                if (node is UAReferenceType referenceType)
                {
                    string name = NormalizeArchivedBrowseName(node.BrowseName) ?? string.Empty;
                    Add("forward:" + name, node);
                    if (!referenceType.Symmetric)
                    {
                        int separator = name.IndexOf(':', StringComparison.Ordinal);
                        string prefix = separator < 0 ? string.Empty : name[..(separator + 1)];
                        foreach (Export.LocalizedText inverse in referenceType.InverseName ?? [])
                        {
                            if (!string.IsNullOrEmpty(inverse.Value))
                            {
                                Add("inverse:" + prefix + inverse.Value, node);
                            }
                        }
                    }
                }
            }
            return index;

            void Add(string key, UANode node)
            {
                if (index.TryGetValue(key, out UANode? previous))
                {
                    if (!ReferenceEquals(previous, node))
                    {
                        index[key] = null;
                    }
                }
                else
                {
                    index.Add(key, node);
                }
            }
        }

        private static void CompareArchivedAffordances(
            WotDocument document,
            IReadOnlyDictionary<string, JsonElement> affordances,
            string collection,
            JsonElement regenerated,
            UANode root,
            UANodeSet baseline,
            Dictionary<string, UANode?> index,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            List<WotDiagnostic> diagnostics,
            Dictionary<string, UANode?> resolved,
            WotDocument? regeneratedDocument)
        {
            var referenceTypes = WotReferenceTypeNames.Build(baseline);
            JsonElement generatedMap = default;
            if (regenerated.ValueKind == JsonValueKind.Object)
            {
                regenerated.TryGetProperty(collection, out generatedMap);
            }
            var generatedNodes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (generatedMap.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty candidate in generatedMap.EnumerateObject())
                {
                    string? id = GetElementString(candidate.Value, "uav:id");
                    if (id is not null)
                    {
                        generatedNodes[ToNodeSetNodeId(id, identities, diagnostics)] = candidate.Value;
                    }
                }
            }
            foreach (KeyValuePair<string, JsonElement> affordance in affordances)
            {
                string pointer = "/" + collection + "/" + EscapeJsonPointerToken(affordance.Key);
                UANode? node = resolved[pointer];
                if (node is null)
                {
                    continue;
                }
                bool classMatches = collection switch
                {
                    "properties" => node is UAVariable,
                    "actions" => node is UAMethod,
                    "events" => node is UAObjectType,
                    _ => false
                };
                if (!classMatches)
                {
                    ReportArchiveConflict(pointer, "NodeClass", diagnostics, node.NodeId);
                    continue;
                }

                string owner = ResolveArchivedAlias(root.NodeId, aliases);
                string? conditionOwner = null;
                if (collection == "properties")
                {
                    owner = ReadComponentOfParent(affordance.Value, identities, diagnostics) ?? owner;
                }
                else if (collection == "actions" &&
                    affordance.Value.ValueKind == JsonValueKind.Object &&
                    affordance.Value.TryGetProperty(ActsOnTerm, out _))
                {
                    string? eventName = GetElementString(affordance.Value, ActsOnTerm);
                    if (string.IsNullOrEmpty(eventName) || !document.Events.ContainsKey(eventName))
                    {
                        ReportArchiveConflict(pointer + "/" + ActsOnTerm, "Condition event identity",
                            diagnostics, node.NodeId);
                    }
                    else if (resolved["/events/" + EscapeJsonPointerToken(eventName)] is { } eventNode)
                    {
                        conditionOwner = ResolveArchivedAlias(eventNode.NodeId, aliases);
                    }
                }
                if (collection == "events")
                {
                    if (!HasArchivedReference(
                        root, index, aliases, "i=41", isForward: true, ResolveArchivedAlias(node.NodeId, aliases)))
                    {
                        ReportArchiveConflict(pointer, "GeneratesEvent Reference", diagnostics, node.NodeId);
                    }
                }
                else if (index.ContainsKey("owned:" + ResolveArchivedAlias(node.NodeId, aliases)) &&
                    !HasOwner(owner) &&
                    (conditionOwner is null || !HasOwner(conditionOwner)))
                {
                    ReportArchiveConflict(pointer, "affordance owner", diagnostics, node.NodeId);
                }
                generatedNodes.TryGetValue(ResolveArchivedAlias(node.NodeId, aliases), out JsonElement expected);
                CompareArchivedNode(
                    document, affordance.Value, expected, node, baseline, index, identities,
                    aliases, pointer, isRoot: false, diagnostics, regeneratedDocument);

                bool HasOwner(string candidate)
                {
                    return HasArchivedReference(
                        node, index, aliases, null, isForward: false, candidate, referenceTypes) ||
                        (node is UAInstance owned && ResolveArchivedAlias(owned.ParentNodeId, aliases) == candidate);
                }
            }
        }

        private static UANode? FindArchivedNode(
            WotDocument document,
            JsonElement authored,
            string? key,
            Dictionary<string, UANode?> index,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            string? id = GetElementString(authored, "uav:id");
            string? browseName = GetElementString(authored, "uav:browseName");
            if (id is null && browseName is null && key is null)
            {
                return null;
            }
            string? normalizedId = id is null
                ? null
                : NormalizeArchivedIdentity(document, id, identities, aliases, diagnostics, authored);
            string? normalizedName = browseName is null
                ? null
                : NormalizeArchivedBrowseName(
                    ToNodeSetQualifiedName(document, browseName, identities, diagnostics, authored));
            string lookup = normalizedId is not null
                ? "id:" + normalizedId
                : normalizedName is not null ? "name:" + normalizedName : "key:" + key;
            index.TryGetValue(lookup, out UANode? found);
            if (found is null)
            {
                ReportArchiveConflict(
                    pointer + (id is not null ? "/uav:id" : browseName is not null ? "/uav:browseName" : string.Empty),
                    "node identity", diagnostics, normalizedId);
            }
            return found;
        }

        private static void CompareArchivedNode(
            WotDocument document,
            JsonElement authored,
            JsonElement regenerated,
            UANode node,
            UANodeSet baseline,
            Dictionary<string, UANode?> index,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            string pointer,
            bool isRoot,
            List<WotDiagnostic> diagnostics,
            WotDocument? regeneratedDocument = null)
        {
            if (authored.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            int firstDiagnostic = diagnostics.Count;
            foreach (string identityMember in new[] { "uav:id", "uav:browseName" })
            {
                if (authored.TryGetProperty(identityMember, out JsonElement identity) &&
                    identity.ValueKind != JsonValueKind.String)
                {
                    ReportArchiveConflict(pointer + "/" + identityMember, "node identity", diagnostics);
                }
            }
            string? browseName = GetElementString(authored, "uav:browseName");
            if (browseName is not null &&
                !string.Equals(
                    NormalizeArchivedBrowseName(
                        ToNodeSetQualifiedName(document, browseName, identities, diagnostics, authored)),
                    NormalizeArchivedBrowseName(node.BrowseName),
                    StringComparison.Ordinal))
            {
                ReportArchiveConflict(pointer + "/uav:browseName", "BrowseName", diagnostics);
            }
            if (authored.TryGetProperty("@type", out JsonElement types))
            {
                CompareArchivedNodeClass(types, node, isRoot, pointer + "/@type", diagnostics);
            }
            string? dataType = node switch
            {
                UAVariable variable => variable.DataType,
                UAVariableType variableType => variableType.DataType,
                _ => null
            };
            int? valueRank = node switch
            {
                UAVariable variable => variable.ValueRank,
                UAVariableType variableType => variableType.ValueRank,
                _ => null
            };
            string? dimensions = node switch
            {
                UAVariable variable => variable.ArrayDimensions,
                UAVariableType variableType => variableType.ArrayDimensions,
                _ => null
            };
            string? locale = GetDeclaredLocale(document);
            if (!isRoot || authored.TryGetProperty(TitlesMember, out _))
            {
                CompareArchivedText(
                    ReadTitle(authored, locale), node.DisplayName, pointer + "/title", diagnostics);
            }
            CompareArchivedText(
                ReadDescription(authored, locale), node.Description, pointer + "/description", diagnostics);
            foreach (JsonProperty member in authored.EnumerateObject())
            {
                string location = pointer + "/" + EscapeJsonPointerToken(member.Name);
                switch (member.Name)
                {
                    case "uav:mapToType":
                    case "uav:dataTypeId":
                        if (dataType is not null &&
                            (member.Value.ValueKind != JsonValueKind.String ||
                                !string.Equals(
                                    NormalizeArchivedIdentity(
                                        document, member.Value.GetString()!, identities, aliases, diagnostics, authored),
                                    ResolveArchivedAlias(dataType, aliases),
                                    StringComparison.Ordinal)))
                        {
                            ReportArchiveConflict(location, "DataType", diagnostics);
                        }
                        break;
                    case ValueRankTerm:
                        if (valueRank is not null &&
                            (member.Value.ValueKind != JsonValueKind.Number ||
                                !member.Value.TryGetInt32(out int rank) ||
                                rank != valueRank))
                        {
                            ReportArchiveConflict(location, "ValueRank", diagnostics);
                        }
                        break;
                    case ArrayDimensionsTerm:
                        if (valueRank is not null &&
                            !string.Equals(
                                ReadArrayDimensions(authored, node.BrowseName ?? pointer, diagnostics) ?? string.Empty,
                                NormalizeArchivedDimensions(dimensions),
                                StringComparison.Ordinal))
                        {
                            ReportArchiveConflict(location, "ArrayDimensions", diagnostics);
                        }
                        break;
                    case "type":
                        CompareArchivedJsonType(
                            member.Value,
                            node is UAVariable unit && IsUnitAffordance(unit)
                                ? WotVocabulary.String
                                : ResolveArchivedAlias(dataType, aliases),
                            valueRank ?? ScalarValueRank,
                            location,
                            diagnostics);
                        break;
                    case "uav:isAbstract":
                        if (node is UAType type &&
                            member.Value.ValueKind != (type.IsAbstract ? JsonValueKind.True : JsonValueKind.False))
                        {
                            ReportArchiveConflict(location, "IsAbstract", diagnostics);
                        }
                        break;
                    case "readOnly":
                    case "writeOnly":
                    case "observable":
                        if (node is UAVariable accessible)
                        {
                            bool readable = (accessible.AccessLevel & AccessLevelCurrentRead) != 0;
                            bool writable = (accessible.AccessLevel & AccessLevelCurrentWrite) != 0;
                            bool expected = member.Name switch
                            {
                                "readOnly" => readable && !writable,
                                "writeOnly" => writable && !readable,
                                _ => readable
                            };
                            if (member.Value.ValueKind != (expected ? JsonValueKind.True : JsonValueKind.False))
                            {
                                ReportArchiveConflict(location, "AccessLevel", diagnostics);
                            }
                        }
                        break;
                    case "links":
                        CompareArchivedLinks(
                            document, member.Value, node, baseline, index, identities, aliases, location, diagnostics);
                        break;
                    case "uav:hasComponent":
                    case "uav:componentOf":
                        CompareArchivedComponents(
                            document, member.Value, member.Name == "uav:hasComponent",
                            node, baseline, index, identities, aliases, location, diagnostics, authored);
                        break;
                    case "const":
                    case "default":
                        if (node is UAVariable valued)
                        {
                            CompareArchivedValue(member.Value, valued, location, diagnostics);
                        }
                        else if (node is UAVariableType valuedType)
                        {
                            CompareArchivedValue(
                                member.Value, new UAVariable { Value = valuedType.Value }, location, diagnostics);
                        }
                        break;
                    case "uav:modellingRule":
                        string? rule = GetElementString(authored, member.Name);
                        if (rule is not null &&
                            WotVocabulary.TryGetModellingRuleNodeId(rule, out string ruleId) &&
                            !HasArchivedReference(
                                node, index, aliases, "i=37", isForward: true, ruleId))
                        {
                            ReportArchiveConflict(location, "ModellingRule", diagnostics);
                        }
                        break;
                    case InputMember:
                    case OutputMember:
                        if (regenerated.ValueKind == JsonValueKind.Object &&
                            regenerated.TryGetProperty(member.Name, out _))
                        {
                            if (!PreservedArgumentSchemasMatch(
                                document, authored, regeneratedDocument ?? document, regenerated,
                                member.Name, identities, aliases, diagnostics))
                            {
                                ReportArchiveConflict(location, member.Name, diagnostics);
                            }
                        }
                        else if (AnalyzeArgumentSchema(authored, member.Name).Kind != WotArgumentShapeKind.None)
                        {
                            ReportArchiveConflict(location, member.Name, diagnostics);
                        }
                        break;
                    case "uav:dataTypeDefinition":
                    case InverseNameTerm:
                    case SymmetricTerm:
                    case DataMember:
                    case EngineeringUnitsTerm:
                    case InstrumentRangeTerm:
                    case MinimumMember:
                    case MaximumMember:
                    case ConditionTypeTerm:
                    case ConditionTypeIdTerm:
                    case ConditionActionTerm:
                    case ActsOnTerm:
                        if (regenerated.ValueKind == JsonValueKind.Object &&
                            regenerated.TryGetProperty(member.Name, out JsonElement expectedFact) &&
                            !IsArchivedJsonSubset(
                                member.Value, expectedFact, document, identities, aliases, diagnostics,
                                expectedDocument: regeneratedDocument))
                        {
                            ReportArchiveConflict(location, member.Name, diagnostics);
                        }
                        break;
                    case "uav:dataTypeDefinitions":
                        CompareArchivedDefinitions(
                            document, member.Value, regenerated, index, identities, aliases,
                            location, diagnostics, regeneratedDocument);
                        break;
                }
            }
            AttachPreservedNodeIdentity(diagnostics, firstDiagnostic, node.NodeId);
        }

        private static void AttachPreservedNodeIdentity(
            List<WotDiagnostic> diagnostics, int firstDiagnostic, string? nodeId)
        {
            for (int index = firstDiagnostic; index < diagnostics.Count; index++)
            {
                WotDiagnostic diagnostic = diagnostics[index];
                WotLocation? location = diagnostic.Location;
                if (diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict && location?.NodeId is null)
                {
                    diagnostics[index] = new WotDiagnostic(
                        diagnostic.Severity, diagnostic.Code, diagnostic.Message,
                        new WotLocation(location?.JsonPointer, nodeId, location?.Attribute, location?.Reference));
                }
            }
        }

        private static bool AllowsOptionalPreservedConditionComment(JsonElement authored, JsonElement regenerated)
        {
            string? action = GetElementString(authored, ConditionActionTerm);
            if (action is null || Array.IndexOf(s_occurrenceActions, action) < 0)
            {
                return false;
            }
            WotArgumentShape shape = AnalyzeArgumentSchema(regenerated, InputMember);
            if (shape.Kind != WotArgumentShapeKind.Members ||
                shape.Members.Count != 2 ||
                shape.Members[0] != EventIdField ||
                shape.Members[1] != CommentField)
            {
                return false;
            }
            JsonElement properties = regenerated.GetProperty(InputMember).GetProperty("properties");
            JsonElement eventId = properties.GetProperty(EventIdField);
            JsonElement comment = properties.GetProperty(CommentField);
            return GetElementString(eventId, "uav:mapToType") == WotVocabulary.ByteString &&
                GetElementString(comment, "uav:mapToType") == "i=21" &&
                GetElementInt32(eventId, ValueRankTerm) == ScalarValueRank &&
                GetElementInt32(comment, ValueRankTerm) == ScalarValueRank;
        }

        internal static bool PreservedArgumentSchemasMatch(
            WotDocument authoredDocument,
            JsonElement authoredAction,
            WotDocument generatedDocument,
            JsonElement generatedAction,
            string member,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            List<WotDiagnostic> diagnostics)
        {
            WotConversionResult<WotMethodArgumentLayout> authored =
                GetMethodArgumentLayout(authoredAction, member);
            WotConversionResult<WotMethodArgumentLayout> generated =
                GetMethodArgumentLayout(generatedAction, member);
            if (!authored.Success ||
                !generated.Success ||
                authored.Value!.ArgumentCount != generated.Value!.ArgumentCount)
            {
                return false;
            }
            if (authored.Value.ArgumentCount == 0)
            {
                return authored.Value.Kind == generated.Value.Kind;
            }
            // Layout snapshots are detached; contextual type lookup needs the original declaration elements.
            JsonElement authoredSchema = authoredAction.GetProperty(member);
            JsonElement generatedSchema = generatedAction.GetProperty(member);
            var factDiagnostics = new List<WotDiagnostic>();
            DataTypeDefinitionContext authoredTypes = CreateDataTypeDefinitionContext(
                authoredDocument, identities, [], [], factDiagnostics);
            bool factsMatch = true;
            for (int index = 0; index < authored.Value.ArgumentCount; index++)
            {
                WotMethodArgument authoredArgument = ReadArgument(
                    authoredDocument, authored.Value.GetArgumentSchema(authoredSchema, index), "Argument",
                    identities, factDiagnostics, authoredTypes);
                WotMethodArgument generatedArgument = ReadArgument(
                    generatedDocument, generated.Value.GetArgumentSchema(generatedSchema, index), "Argument",
                    identities, factDiagnostics, null);
                if (ResolveArchivedAlias(authoredArgument.DataType, aliases) !=
                    ResolveArchivedAlias(generatedArgument.DataType, aliases) ||
                    authoredArgument.ValueRank != generatedArgument.ValueRank ||
                    NormalizeArchivedDimensions(authoredArgument.ArrayDimensions) !=
                    NormalizeArchivedDimensions(generatedArgument.ArrayDimensions))
                {
                    factsMatch = false;
                    break;
                }
            }
            diagnostics.AddRange(factDiagnostics);
            if (!factsMatch || HasErrors(factDiagnostics))
            {
                return false;
            }
            if (authored.Value.Kind == WotMethodArgumentLayoutKind.Single &&
                generated.Value.Kind == WotMethodArgumentLayoutKind.Named)
            {
                JsonElement schema = authoredSchema;
                if ((schema.TryGetProperty("uav:browseName", out _) || schema.TryGetProperty("title", out _)) &&
                    ReadArgumentName(schema, member == InputMember ? DefaultInputArgumentName : DefaultOutputArgumentName) !=
                        generated.Value.FieldOrder[0])
                {
                    return false;
                }
                return IsArchivedJsonSubset(
                    schema, generated.Value.GetArgumentSchema(generatedSchema, 0),
                    authoredDocument, identities, aliases, diagnostics,
                    expectedDocument: generatedDocument);
            }
            if (authored.Value.Kind != generated.Value.Kind)
            {
                return false;
            }
            if (authored.Value.Kind == WotMethodArgumentLayoutKind.None)
            {
                return true;
            }
            return IsArchivedJsonSubset(
                authoredSchema, generatedSchema, authoredDocument, identities, aliases, diagnostics,
                optionalRequiredField: member == InputMember &&
                    AllowsOptionalPreservedConditionComment(authoredAction, generatedAction)
                        ? CommentField : null,
                expectedDocument: generatedDocument);
        }

        private static void CompareArchivedJsonType(
            JsonElement authored,
            string? dataType,
            int valueRank,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            string? jsonType = valueRank >= 0 ? "array" : MapReadableScalarType(dataType);
            if (jsonType is not null &&
                (authored.ValueKind != JsonValueKind.String || authored.GetString() != jsonType))
            {
                ReportArchiveConflict(pointer, "DataType", diagnostics);
            }
        }

        private static void CompareArchivedText(
            Export.LocalizedText[]? authored,
            Export.LocalizedText[]? expected,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            foreach (Export.LocalizedText text in authored ?? [])
            {
                bool found = false;
                foreach (Export.LocalizedText candidate in expected ?? [])
                {
                    if ((string.IsNullOrEmpty(candidate.Locale) ||
                        string.Equals(
                            EffectiveLocale(text.Locale), candidate.Locale, StringComparison.Ordinal)) &&
                        string.Equals(text.Value, candidate.Value, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    ReportArchiveConflict(pointer, "LocalizedText", diagnostics);
                }
            }
        }

        private static void CompareArchivedNodeClass(
            JsonElement types,
            UANode node,
            bool isRoot,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (types.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement type in types.EnumerateArray())
                {
                    CompareArchivedNodeClass(type, node, isRoot, pointer, diagnostics);
                }
                return;
            }
            if (types.ValueKind != JsonValueKind.String)
            {
                return;
            }
            bool matches = types.GetString() switch
            {
                "tm:ThingModel" => node is not (UAObject or UAVariable),
                "uav:objectType" or "uav:eventType" => node is UAObjectType,
                "uav:variableType" => isRoot ? node is UAVariableType : node is UAVariable,
                "uav:referenceType" => node is UAReferenceType,
                "uav:dataType" => node is UADataType,
                "uav:object" => node is UAObject,
                "uav:variable" => node is UAVariable,
                "uav:method" => node is UAMethod,
                _ => true
            };
            if (!matches)
            {
                ReportArchiveConflict(pointer, "NodeClass", diagnostics);
            }
        }

        private static void CompareArchivedValue(
            JsonElement authored,
            UAVariable node,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                WriteVariableValue(writer, node);
                writer.WriteEndObject();
            }
            using var expected = JsonDocument.Parse(output.ToArray());
            if (expected.RootElement.TryGetProperty("const", out JsonElement constant))
            {
                if (!IsArchivedJsonSubset(authored, constant))
                {
                    ReportArchiveConflict(pointer, "Value", diagnostics);
                }
            }
            else if (node.Value is null)
            {
                ReportArchiveConflict(pointer, "Value", diagnostics);
            }
        }

        private static void CompareArchivedComponents(
            WotDocument document,
            JsonElement targets,
            bool isForward,
            UANode node,
            UANodeSet baseline,
            Dictionary<string, UANode?> nodes,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            string pointer,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode)
        {
            if (targets.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            var referenceTypes = WotReferenceTypeNames.Build(baseline);
            int index = 0;
            foreach (JsonElement target in targets.EnumerateArray())
            {
                if (target.ValueKind == JsonValueKind.String &&
                    !HasArchivedReference(
                        node, nodes, aliases, null, isForward,
                        NormalizeArchivedIdentity(
                            document, target.GetString()!, identities, aliases, diagnostics, carryingNode),
                        referenceTypes))
                {
                    ReportArchiveConflict(
                        pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "component Reference", diagnostics);
                }
                index++;
            }
        }

        private static void CompareArchivedLinks(
            WotDocument document,
            JsonElement links,
            UANode node,
            UANodeSet baseline,
            Dictionary<string, UANode?> nodes,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (links.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            int index = -1;
            foreach (JsonElement link in links.EnumerateArray())
            {
                index++;
                string? href = GetElementString(link, "href");
                string? rel = GetElementString(link, "rel");
                if (href is null || rel is null || !LooksLikeNodeId(href))
                {
                    continue;
                }
                string? referenceType = null;
                bool isForward = true;
                if (rel == "tm:extends")
                {
                    referenceType = "i=45";
                    isForward = false;
                }
                else if (rel is ComponentOfRel or ComponentOfAliasRel)
                {
                    isForward = false;
                }
                else
                {
                    WotReferenceTypeAnswer answer = ResolveReferenceTypeName(document, rel, null, link);
                    if (answer.Outcome == WotReferenceTypeOutcome.Unresolved &&
                        TrySplitCompactModelName(rel, out _, out _))
                    {
                        string name = NormalizeArchivedBrowseName(
                            ToNodeSetQualifiedName(document, rel, identities, diagnostics, link))!;
                        var matches = new List<WotResolvedReferenceType>();
                        if (nodes.TryGetValue("forward:" + name, out UANode? forward) &&
                            forward?.NodeId is { } forwardId)
                        {
                            matches.Add(new WotResolvedReferenceType(
                                ToPortableNodeId(forwardId, baseline.NamespaceUris)!, rel, true));
                        }
                        if (nodes.TryGetValue("inverse:" + name, out UANode? inverse) &&
                            inverse?.NodeId is { } inverseId)
                        {
                            matches.Add(new WotResolvedReferenceType(
                                ToPortableNodeId(inverseId, baseline.NamespaceUris)!, rel, false));
                        }
                        answer = WotReferenceTypeAnswer.FromMatches(matches.ToArrayOf());
                    }
                    if (answer.Outcome == WotReferenceTypeOutcome.Resolved)
                    {
                        referenceType = answer.Single.NodeId;
                        isForward = answer.Single.IsForward;
                    }
                    else if (answer.Outcome == WotReferenceTypeOutcome.Ambiguous)
                    {
                        string? pin = GetElementString(link, "uav:refId");
                        if (!TrySettleAmbiguousReferenceType(
                            answer, rel, pin, pin, diagnostics, out string settled, out isForward))
                        {
                            ReportArchiveConflict(
                                pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                "ReferenceType", diagnostics);
                            continue;
                        }
                        referenceType = settled;
                    }
                    else if (GetElementString(link, "uav:refId") is { } definitive)
                    {
                        referenceType = definitive;
                    }
                    else
                    {
                        continue;
                    }
                }
                string? normalizedReferenceType = referenceType is null
                    ? null
                    : NormalizeArchivedIdentity(
                        document,
                        ToPortableNodeId(ResolveArchivedAlias(referenceType, aliases), baseline.NamespaceUris)!,
                        identities, aliases, diagnostics, link);
                if (GetElementString(link, "uav:refId") is { } pinned)
                {
                    string normalized = NormalizeArchivedIdentity(
                        document, pinned, identities, aliases, diagnostics, link);
                    if (normalizedReferenceType is not null && normalizedReferenceType != normalized)
                    {
                        ReportArchiveConflict(
                            pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            "ReferenceType", diagnostics);
                        continue;
                    }
                    normalizedReferenceType = normalized;
                }
                string location = pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string target = NormalizeArchivedIdentity(document, href, identities, aliases, diagnostics, link);
                if (link.TryGetProperty("uav:declaration", out JsonElement declaration))
                {
                    CompareArchivedComponentDeclaration(
                        document, declaration, node, baseline, nodes, identities, aliases,
                        normalizedReferenceType, isForward, target, location, diagnostics);
                    continue;
                }
                if (!HasArchivedReference(
                    node, nodes, aliases, normalizedReferenceType, isForward, target))
                {
                    ReportArchiveConflict(location, "Reference", diagnostics);
                }
            }
        }

        private static void CompareArchivedComponentDeclaration(
            WotDocument document,
            JsonElement authored,
            UANode owner,
            UANodeSet baseline,
            Dictionary<string, UANode?> nodes,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            string? referenceType,
            bool isForward,
            string typeId,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            var referenceTypes = WotReferenceTypeNames.Build(baseline);
            if (owner is not (UAObjectType or UAVariableType) ||
                !isForward ||
                referenceType is null ||
                !referenceTypes.IsOwnershipReference(referenceType) ||
                authored.ValueKind != JsonValueKind.Object ||
                string.IsNullOrEmpty(GetElementString(authored, "uav:browseName")))
            {
                ReportArchiveConflict(pointer, "component declaration", diagnostics);
                return;
            }
            string declarationPointer = pointer + "/uav:declaration";
            UANode? declaration = FindArchivedNode(
                document, authored, null, nodes, identities, aliases, declarationPointer, diagnostics);
            if (declaration is null)
            {
                return;
            }
            string declarationId = ResolveArchivedAlias(declaration.NodeId, aliases);
            nodes.TryGetValue("id:" + typeId, out UANode? type);
            if (declaration is not (UAObject or UAVariable) ||
                declarationId == typeId ||
                (declaration is UAObject &&
                    (owner is not UAObjectType || !referenceTypes.IsHasComponentReference(referenceType))) ||
                (type is not null &&
                    (declaration is UAObject ? type is not UAObjectType : type is not UAVariableType)) ||
                !HasArchivedReference(owner, nodes, aliases, referenceType, isForward: true, declarationId) ||
                !HasArchivedReference(declaration, nodes, aliases, "i=40", isForward: true, typeId))
            {
                ReportArchiveConflict(pointer, "component declaration Reference", diagnostics);
                return;
            }
            CompareArchivedNode(
                document, authored, default, declaration, baseline, nodes, identities, aliases,
                declarationPointer, isRoot: false, diagnostics);
        }

        private static bool HasArchivedReference(
            UANode node,
            Dictionary<string, UANode?> nodes,
            INodeSetAliasResolver aliases,
            string? referenceType,
            bool isForward,
            string target,
            WotReferenceTypeNames? referenceTypes = null)
        {
            nodes.TryGetValue("id:" + target, out UANode? targetNode);
            return Matches(node, isForward, target) ||
                (targetNode is not null &&
                    Matches(targetNode, !isForward, ResolveArchivedAlias(node.NodeId, aliases)));

            bool Matches(UANode candidate, bool direction, string expectedTarget)
            {
                foreach (Reference reference in candidate.References ?? [])
                {
                    string type = ResolveArchivedAlias(reference.ReferenceType, aliases);
                    bool symmetric = nodes.TryGetValue("id:" + type, out UANode? typeNode) &&
                        typeNode is UAReferenceType { Symmetric: true };
                    if ((referenceType is null
                            ? referenceTypes?.IsOwnershipReference(type) ?? IsComponentReference(type)
                            : type == referenceType) &&
                        (reference.IsForward == direction || symmetric) &&
                        string.Equals(
                            ResolveArchivedAlias(reference.Value, aliases),
                            expectedTarget,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static string NormalizeArchivedIdentity(
            WotDocument document,
            string value,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default)
        {
            if (!LooksLikeNodeId(value) &&
                TrySplitCompactModelName(value, out string prefix, out string name) &&
                TryGetContextNamespace(document, prefix, out string uri, carryingNode) &&
                uri == WotVocabulary.OpcUaNamespace)
            {
                value = name;
            }
            return ToNodeSetNodeId(ResolveArchivedAlias(value, aliases), identities, diagnostics);
        }

        private static string ResolveArchivedAlias(string? value, INodeSetAliasResolver aliases)
        {
            if (value is null)
            {
                return string.Empty;
            }
            if (aliases.TryResolve(value, out string resolved))
            {
                value = resolved;
            }
            return NodeId.TryParse(value, out NodeId identifier) ? identifier.ToString() : value;
        }

        private static string NormalizeArchivedDimensions(string? dimensions)
        {
            if (string.IsNullOrEmpty(dimensions))
            {
                return string.Empty;
            }
            string[] values = dimensions!.Split(',');
            for (int index = 0; index < values.Length; index++)
            {
                if (uint.TryParse(
                    values[index].Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out uint dimension))
                {
                    values[index] = dimension.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            return string.Join(",", values);
        }

        private static string? NormalizeArchivedBrowseName(string? value)
        {
            return value?.StartsWith("0:", StringComparison.Ordinal) == true ? value[2..] : value;
        }

        private static void CompareArchivedDefinitions(
            WotDocument document,
            JsonElement definitions,
            JsonElement regenerated,
            Dictionary<string, UANode?> nativeIndex,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            string pointer,
            List<WotDiagnostic> diagnostics,
            WotDocument? regeneratedDocument = null)
        {
            if (definitions.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            var expectedDefinitions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (regenerated.ValueKind == JsonValueKind.Object &&
                regenerated.TryGetProperty("uav:dataTypeDefinitions", out JsonElement expected) &&
                expected.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement definition in expected.EnumerateArray())
                {
                    string? id = GetElementString(definition, "uav:dataTypeId");
                    if (id is not null)
                    {
                        expectedDefinitions[NormalizeArchivedIdentity(
                            regeneratedDocument ?? document, id, identities, aliases, diagnostics, definition)] = definition;
                    }
                }
            }
            int index = 0;
            foreach (JsonElement definition in definitions.EnumerateArray())
            {
                string? id = GetElementString(definition, "uav:dataTypeId") ?? GetElementString(definition, "@id");
                string? normalized = id is null ? null : NormalizeArchivedIdentity(
                    document, id, identities, aliases, diagnostics, definition);
                if (normalized is null && GetElementString(definition, "uav:dataTypeName") is { } name)
                {
                    string qualified = ToNodeSetQualifiedName(document, name, identities, diagnostics, definition);
                    if (nativeIndex.TryGetValue(
                            "datatype-name:" + NormalizeArchivedBrowseName(qualified), out UANode? namedType))
                    {
                        normalized = namedType?.NodeId is null ? null : ResolveArchivedAlias(namedType.NodeId, aliases);
                    }
                }
                string location = pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (normalized is null ||
                    !expectedDefinitions.TryGetValue(normalized, out JsonElement expectedDefinition) ||
                    !IsArchivedJsonSubset(
                        definition, expectedDefinition, document, identities, aliases, diagnostics,
                        expectedDocument: regeneratedDocument))
                {
                    ReportArchiveConflict(location, "DataType definition", diagnostics, normalized);
                }
                index++;
            }
        }

        private static bool PreservedEncodingClaimsMatch(
            JsonElement authored,
            JsonElement expected,
            WotDocument document,
            UANodeSet identities,
            INodeSetAliasResolver aliases,
            List<WotDiagnostic> diagnostics,
            WotDocument? expectedDocument)
        {
            EncodingPresence presence = EncodingPresence.None;
            foreach (string term in s_encodingTerms)
            {
                if (expected.TryGetProperty(term, out JsonElement encoding) &&
                    encoding.ValueKind == JsonValueKind.String)
                {
                    presence |= PresenceForEncodingTerm(term);
                }
            }
            bool binary = (presence & EncodingPresence.Binary) != 0;
            if (authored.TryGetProperty(DefaultEncodingsTerm, out _))
            {
                var errors = new List<WotDiagnostic>();
                EncodingPresence claimed = ReadEncodingPresence(
                    authored, GetElementString(expected, "uav:dataTypeName") ?? string.Empty,
                    expected.TryGetProperty("uav:isAbstract", out JsonElement abstractType) &&
                    abstractType.ValueKind == JsonValueKind.True,
                    GetElementString(expected, "@type") ?? string.Empty,
                    binary, errors);
                if (HasErrors(errors) || claimed != presence)
                {
                    return false;
                }
            }
            if (authored.TryGetProperty("uav:hasDefaultEncoding", out JsonElement hasDefault) &&
                hasDefault.ValueKind != (binary ? JsonValueKind.True : JsonValueKind.False))
            {
                return false;
            }
            foreach (string term in s_encodingTerms)
            {
                if (!authored.TryGetProperty(term, out JsonElement encoding))
                {
                    continue;
                }
                string expectedTerm = term == "uav:defaultEncodingId" ? "uav:binaryEncodingId" : term;
                string expectedId = GetElementString(expected, expectedTerm) ?? "i=0";
                if (encoding.ValueKind != JsonValueKind.String ||
                    NormalizeArchivedIdentity(
                        document, encoding.GetString()!, identities, aliases, diagnostics, authored) !=
                    NormalizeArchivedIdentity(
                        expectedDocument ?? document, expectedId, identities, aliases, diagnostics, expected))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsArchivedJsonSubset(
            JsonElement authored,
            JsonElement expected,
            WotDocument? document = null,
            UANodeSet? identities = null,
            INodeSetAliasResolver? aliases = null,
            List<WotDiagnostic>? diagnostics = null,
            bool namedMap = false,
            string? optionalRequiredField = null,
            WotDocument? expectedDocument = null)
        {
            if (authored.ValueKind != expected.ValueKind)
            {
                return false;
            }
            switch (authored.ValueKind)
            {
                case JsonValueKind.Object:
                    bool definition = document is not null &&
                        expected.TryGetProperty("uav:dataTypeName", out _) &&
                        expected.TryGetProperty("uav:dataTypeId", out _) &&
                        GetElementString(expected, "@type") is
                            "uav:StructureDefinition" or "uav:EnumDefinition" or "uav:SimpleDataType";
                    if (definition &&
                        !PreservedEncodingClaimsMatch(
                            authored, expected, document!, identities!, aliases!, diagnostics!, expectedDocument))
                    {
                        return false;
                    }
                    foreach (JsonProperty member in authored.EnumerateObject())
                    {
                        if (definition &&
                            (member.Name is DefaultEncodingsTerm or "uav:hasDefaultEncoding" ||
                                PresenceForEncodingTerm(member.Name) != EncodingPresence.None))
                        {
                            continue;
                        }
                        if (document is not null && member.Name is "@id" or "@context")
                        {
                            continue;
                        }
                        if (document is not null &&
                            member.Name == "@type" &&
                            expected.TryGetProperty("@type", out JsonElement expectedTypes))
                        {
                            if (!IsArchivedTypeSubset(member.Value, expectedTypes))
                            {
                                return false;
                            }
                            continue;
                        }
                        if (!expected.TryGetProperty(member.Name, out JsonElement value))
                        {
                            if (document is not null &&
                                !namedMap &&
                                (member.Name is not (
                                    "uav:isAbstract" or "uav:maxStringLength" or ArrayDimensionsTerm) ||
                                    IsDefaultArchivedSchemaFact(member.Name, member.Value)))
                            {
                                continue;
                            }
                            return false;
                        }
                        if (!namedMap && member.Name == "required")
                        {
                            if (!PreservedRequiredFieldsMatch(member.Value, value, optionalRequiredField))
                            {
                                return false;
                            }
                            continue;
                        }
                        if (document is not null &&
                            member.Value.ValueKind == JsonValueKind.String &&
                            value.ValueKind == JsonValueKind.String &&
                            member.Name is "uav:dataTypeId" or "uav:fieldDataTypeId" or "uav:mapToType" or "uav:id")
                        {
                            if (NormalizeArchivedIdentity(
                                    document, member.Value.GetString()!, identities!,
                                    aliases!, diagnostics!, authored) !=
                                NormalizeArchivedIdentity(
                                    expectedDocument ?? document, value.GetString()!,
                                    identities!, aliases!, diagnostics!, expected))
                            {
                                return false;
                            }
                            continue;
                        }
                        if (document is not null &&
                            member.Name is "uav:dataTypeName" or "uav:browseName" &&
                            member.Value.ValueKind == JsonValueKind.String &&
                            value.ValueKind == JsonValueKind.String)
                        {
                            if (NormalizeArchivedBrowseName(ToNodeSetQualifiedName(
                                    document, member.Value.GetString()!, identities!, diagnostics!, authored)) !=
                                NormalizeArchivedBrowseName(ToNodeSetQualifiedName(
                                    expectedDocument ?? document, value.GetString()!,
                                    identities!, diagnostics!, expected)))
                            {
                                return false;
                            }
                            continue;
                        }
                        if (!IsArchivedJsonSubset(
                            member.Value, value, document, identities, aliases, diagnostics,
                            member.Name is "properties" or TitlesMember or DescriptionsMember or "displayNames",
                            expectedDocument: expectedDocument))
                        {
                            return false;
                        }
                    }
                    return true;
                case JsonValueKind.Array:
                    if (authored.GetArrayLength() != expected.GetArrayLength())
                    {
                        return false;
                    }
                    for (int index = 0; index < authored.GetArrayLength(); index++)
                    {
                        if (!IsArchivedJsonSubset(
                            authored[index], expected[index], document, identities, aliases, diagnostics,
                            expectedDocument: expectedDocument))
                        {
                            return false;
                        }
                    }
                    return true;
                case JsonValueKind.Number:
                    if (authored.TryGetDecimal(out decimal left) && expected.TryGetDecimal(out decimal right))
                    {
                        return left == right;
                    }
                    return authored.TryGetDouble(out double leftNumber) &&
                        expected.TryGetDouble(out double rightNumber) &&
                        !double.IsInfinity(leftNumber) &&
                        leftNumber.Equals(rightNumber);
                case JsonValueKind.String:
                    return string.Equals(authored.GetString(), expected.GetString(), StringComparison.Ordinal);
                default:
                    return true;
            }
        }

        private static bool PreservedRequiredFieldsMatch(
            JsonElement authored, JsonElement expected, string? optionalField)
        {
            if (authored.ValueKind != JsonValueKind.Array || expected.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            var remaining = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement field in expected.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.String || !remaining.Add(field.GetString()!))
                {
                    return false;
                }
            }
            foreach (JsonElement field in authored.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.String || !remaining.Remove(field.GetString()!))
                {
                    return false;
                }
            }
            return remaining.Count == 0 ||
                (optionalField is not null && remaining.Count == 1 && remaining.Contains(optionalField));
        }

        private static bool IsArchivedTypeSubset(JsonElement authored, JsonElement expected)
        {
            if (authored.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement type in authored.EnumerateArray())
                {
                    if (!IsArchivedTypeSubset(type, expected))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (authored.ValueKind != JsonValueKind.String ||
                !WotBindingConformance.IsKnownTerm(authored.GetString()))
            {
                return true;
            }
            if (expected.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement type in expected.EnumerateArray())
                {
                    if (type.ValueKind == JsonValueKind.String && type.GetString() == authored.GetString())
                    {
                        return true;
                    }
                }
                return false;
            }
            return expected.ValueKind == JsonValueKind.String && expected.GetString() == authored.GetString();
        }

        private static bool IsDefaultArchivedSchemaFact(string name, JsonElement value)
        {
            return (name == "uav:isAbstract" && value.ValueKind == JsonValueKind.False) ||
                (name == "uav:maxStringLength" &&
                    value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out int length) &&
                    length == 0) ||
                (name == ArrayDimensionsTerm && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);
        }

        private static void ReportArchiveConflict(
            string pointer,
            string fact,
            List<WotDiagnostic> diagnostics,
            string? nodeId = null)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.NativeProjectionConflict,
                $"The readable {fact} conflicts with the authoritative preserved NodeSet.",
                new WotLocation(pointer, nodeId)));
        }
    }
}
