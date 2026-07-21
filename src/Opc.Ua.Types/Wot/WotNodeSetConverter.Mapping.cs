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
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Native readable mapping (NodeSet2 to WoT affordances) and WoT to
    /// NodeSet2 synthesis for the <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        private const uint AccessLevelCurrentRead = 1;
        private const uint AccessLevelCurrentWrite = 2;

        private static readonly Dictionary<string, string> s_dataTypeToJsonType =
            new(StringComparer.Ordinal)
            {
                ["i=1"] = "boolean",
                ["Boolean"] = "boolean",
                ["i=2"] = "integer",
                ["SByte"] = "integer",
                ["i=3"] = "integer",
                ["Byte"] = "integer",
                ["i=4"] = "integer",
                ["Int16"] = "integer",
                ["i=5"] = "integer",
                ["UInt16"] = "integer",
                ["i=6"] = "integer",
                ["Int32"] = "integer",
                ["i=7"] = "integer",
                ["UInt32"] = "integer",
                ["i=8"] = "integer",
                ["Int64"] = "integer",
                ["i=9"] = "integer",
                ["UInt64"] = "integer",
                ["i=10"] = "number",
                ["Float"] = "number",
                ["i=11"] = "number",
                ["Double"] = "number",
                ["i=12"] = "string",
                ["String"] = "string"
            };

        private static void WriteContext(Utf8JsonWriter writer)
        {
            writer.WritePropertyName("@context");
            writer.WriteStartArray();
            writer.WriteStringValue(WotVocabulary.WotContext);
            writer.WriteStartObject();
            writer.WriteString("uav", WotVocabulary.VocabularyNamespace);
            writer.WriteEndObject();
            writer.WriteEndArray();
        }

        private static void WriteRootType(Utf8JsonWriter writer, UANode? root)
        {
            switch (root)
            {
                case UAObjectType:
                    writer.WritePropertyName("@type");
                    writer.WriteStartArray();
                    writer.WriteStringValue(WotVocabulary.ThingModelType);
                    writer.WriteStringValue("uav:objectType");
                    writer.WriteEndArray();
                    break;
                case UAVariableType:
                    writer.WritePropertyName("@type");
                    writer.WriteStartArray();
                    writer.WriteStringValue(WotVocabulary.ThingModelType);
                    writer.WriteStringValue("uav:variableType");
                    writer.WriteEndArray();
                    break;
                case UAObject:
                    writer.WriteString("@type", "uav:object");
                    break;
                case UAVariable:
                    writer.WriteString("@type", "uav:variable");
                    break;
                default:
                    writer.WriteString("@type", WotVocabulary.ThingModelType);
                    break;
            }
        }

        private static void WriteDescription(Utf8JsonWriter writer, Opc.Ua.Export.LocalizedText[]? description)
        {
            string? text = FirstText(description);
            if (!string.IsNullOrEmpty(text))
            {
                writer.WriteString("description", text);
            }
        }

        private static void WriteAffordances(
            Utf8JsonWriter writer,
            UANodeSet nodeSet,
            UANode? root,
            List<WotDiagnostic> diagnostics,
            WotNodeSetConverterOptions options)
        {
            if (root?.References is null)
            {
                return;
            }

            Dictionary<string, UANode> index = BuildIndex(nodeSet);
            var properties = new List<UAVariable>();
            var actions = new List<UAMethod>();
            var events = new List<UANode>();

            foreach (Reference reference in root.References)
            {
                if (!reference.IsForward || reference.Value is null)
                {
                    continue;
                }
                if (IsComponentReference(reference.ReferenceType))
                {
                    if (index.TryGetValue(reference.Value, out UANode? target))
                    {
                        if (target is UAVariable variable)
                        {
                            properties.Add(variable);
                        }
                        else if (target is UAMethod method)
                        {
                            actions.Add(method);
                        }
                    }
                }
                else if (IsGeneratesEventReference(reference.ReferenceType) &&
                    index.TryGetValue(reference.Value, out UANode? eventType))
                {
                    events.Add(eventType);
                }
            }

            bool isThingModel = root is UAObjectType or UAVariableType;
            int affordanceCount = 0;

            if (properties.Count > 0)
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (UAVariable variable in properties)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    writer.WritePropertyName(UniqueKey(LocalName(variable.BrowseName), used));
                    WriteVariableAffordance(writer, variable, isThingModel);
                }
                writer.WriteEndObject();
            }

            if (actions.Count > 0)
            {
                writer.WritePropertyName("actions");
                writer.WriteStartObject();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (UAMethod method in actions)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    writer.WritePropertyName(UniqueKey(LocalName(method.BrowseName), used));
                    WriteMethodAffordance(writer, method);
                }
                writer.WriteEndObject();
            }

            if (events.Count > 0)
            {
                writer.WritePropertyName("events");
                writer.WriteStartObject();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (UANode eventType in events)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    writer.WritePropertyName(UniqueKey(LocalName(eventType.BrowseName), used));
                    WriteEventAffordance(writer, eventType);
                }
                writer.WriteEndObject();
            }
        }

        private static void WriteVariableAffordance(
            Utf8JsonWriter writer,
            UAVariable variable,
            bool isThingModel)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", isThingModel ? "uav:variableType" : "uav:variable");
            WriteOptional(writer, "title", FirstText(variable.DisplayName));
            WriteDescription(writer, variable.Description);
            WriteOptional(writer, "uav:browseName", variable.BrowseName);
            WriteOptional(writer, "uav:id", variable.NodeId);

            string? jsonType = MapDataTypeToJson(variable.DataType);
            if (jsonType is not null)
            {
                writer.WriteString("type", jsonType);
            }

            bool readable = (variable.AccessLevel & AccessLevelCurrentRead) != 0;
            bool writable = (variable.AccessLevel & AccessLevelCurrentWrite) != 0;
            if (readable && !writable)
            {
                writer.WriteBoolean("readOnly", true);
            }
            else if (writable && !readable)
            {
                writer.WriteBoolean("writeOnly", true);
            }
            if (readable)
            {
                writer.WriteBoolean("observable", true);
            }

            WriteModellingRule(writer, variable);
            writer.WriteEndObject();
        }

        private static void WriteMethodAffordance(Utf8JsonWriter writer, UAMethod method)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", "uav:method");
            WriteOptional(writer, "title", FirstText(method.DisplayName));
            WriteDescription(writer, method.Description);
            WriteOptional(writer, "uav:browseName", method.BrowseName);
            WriteOptional(writer, "uav:id", method.NodeId);
            WriteModellingRule(writer, method);
            writer.WriteEndObject();
        }

        private static void WriteEventAffordance(Utf8JsonWriter writer, UANode eventType)
        {
            writer.WriteStartObject();
            WriteOptional(writer, "title", FirstText(eventType.DisplayName));
            WriteDescription(writer, eventType.Description);
            writer.WriteBoolean("uav:isEvent", true);
            WriteOptional(writer, "uav:browseName", eventType.BrowseName);
            WriteOptional(writer, "uav:id", eventType.NodeId);
            WriteModellingRule(writer, eventType);
            writer.WriteEndObject();
        }

        private static void WriteModellingRule(Utf8JsonWriter writer, UANode node)
        {
            string? rule = GetBaselineModellingRule(node);
            if (rule is not null)
            {
                writer.WriteString("uav:modellingRule", rule);
            }
        }

        private static UANodeSet? Synthesize(
            WotDocument document,
            WotNodeSetConverterOptions options,
            IWotThingResolver? thingResolver,
            WotResolutionContext? resolutionContext,
            List<WotDiagnostic> diagnostics)
        {
            WotDocumentKind kind = document.Kind;
            if (kind == WotDocumentKind.Unknown)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NoConvertibleContent,
                    "The document is neither a Thing Model nor a Thing Description and carries no preservation envelope or native projection."));
                return null;
            }

            bool isThingModel = kind == WotDocumentKind.ThingModel;
            string modelUri = DeriveModelUri(document);
            string rootLocal = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";
            string rootNodeId = GenerateNodeId(rootLocal);
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Info,
                WotDiagnosticCode.GeneratedNodeId,
                "NodeIds were generated deterministically from the target namespace and browse paths.",
                new WotLocation(nodeId: rootNodeId)));

            var nodeSet = new UANodeSet
            {
                NamespaceUris = [modelUri],
                Models =
                [
                    new ModelTableEntry { ModelUri = modelUri }
                ]
            };

            var items = new List<UANode>();
            var rootReferences = new List<Reference>();

            UANode rootNode;
            if (isThingModel)
            {
                rootNode = new UAObjectType { IsAbstract = false };
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    Value = WotVocabulary.BaseObjectType
                });
            }
            else
            {
                rootNode = new UAObject();
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasTypeDefinition",
                    IsForward = true,
                    Value = WotVocabulary.BaseObjectType
                });
            }

            rootNode.NodeId = rootNodeId;
            rootNode.BrowseName = GetUavString(document, "browseName") ?? "1:" + rootLocal;
            rootNode.DisplayName = MakeText(document.Title ?? rootLocal);
            string? rootDescription = GetRootString(document, "description");
            if (rootDescription is not null)
            {
                rootNode.Description = MakeText(rootDescription);
            }

            int affordanceCount = 0;

            foreach (KeyValuePair<string, JsonElement> property in document.Properties)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeProperty(
                    property.Key, property.Value, rootLocal, rootNodeId, isThingModel,
                    items, rootReferences, diagnostics);
            }

            foreach (KeyValuePair<string, JsonElement> action in document.Actions)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeAction(
                    action.Key, action.Value, rootLocal, rootNodeId, items, rootReferences);
            }

            foreach (KeyValuePair<string, JsonElement> eventAffordance in document.Events)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeEvent(
                    eventAffordance.Key, eventAffordance.Value, rootLocal, items, rootReferences);
            }

            SynthesizeLinks(
                document, rootReferences, thingResolver, resolutionContext, options, diagnostics);
            SynthesizeComponentArrays(document, rootReferences);

            rootNode.References = [.. rootReferences];
            items.Insert(0, rootNode);
            nodeSet.Items = [.. items];
            return nodeSet;
        }

        private static void SynthesizeProperty(
            string key,
            JsonElement schema,
            string rootLocal,
            string rootNodeId,
            bool isThingModel,
            List<UANode> items,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            string local = LocalName(GetElementString(schema, "uav:browseName")) ?? key;
            string nodeId = GenerateNodeId(rootLocal + "/" + local);
            var variable = new UAVariable
            {
                NodeId = nodeId,
                BrowseName = GetElementString(schema, "uav:browseName") ?? "1:" + local,
                ParentNodeId = rootNodeId,
                DataType = MapJsonSchemaToDataType(schema),
                AccessLevel = MapAccessLevel(schema)
            };
            string? title = GetElementString(schema, "title");
            if (title is not null)
            {
                variable.DisplayName = MakeText(title);
            }
            string? description = GetElementString(schema, "description");
            if (description is not null)
            {
                variable.Description = MakeText(description);
            }

            var references = new List<Reference>
            {
                new Reference
                {
                    ReferenceType = "HasTypeDefinition",
                    IsForward = true,
                    Value = WotVocabulary.BaseDataVariableType
                },
                new Reference
                {
                    ReferenceType = "HasComponent",
                    IsForward = false,
                    Value = rootNodeId
                }
            };
            AddModellingRule(schema, references);
            variable.References = [.. references];

            ReportUnsupportedSchema(schema, nodeId, diagnostics);

            items.Add(variable);
            rootReferences.Add(new Reference
            {
                ReferenceType = "HasComponent",
                IsForward = true,
                Value = nodeId
            });
            _ = isThingModel;
        }

        private static void SynthesizeAction(
            string key,
            JsonElement action,
            string rootLocal,
            string rootNodeId,
            List<UANode> items,
            List<Reference> rootReferences)
        {
            string local = LocalName(GetElementString(action, "uav:browseName")) ?? key;
            string nodeId = GenerateNodeId(rootLocal + "/" + local);
            var method = new UAMethod
            {
                NodeId = nodeId,
                BrowseName = GetElementString(action, "uav:browseName") ?? "1:" + local,
                ParentNodeId = rootNodeId
            };
            string? title = GetElementString(action, "title");
            if (title is not null)
            {
                method.DisplayName = MakeText(title);
            }

            var references = new List<Reference>
            {
                new Reference
                {
                    ReferenceType = "HasComponent",
                    IsForward = false,
                    Value = rootNodeId
                }
            };
            AddModellingRule(action, references);
            method.References = [.. references];

            items.Add(method);
            rootReferences.Add(new Reference
            {
                ReferenceType = "HasComponent",
                IsForward = true,
                Value = nodeId
            });
        }

        private static void SynthesizeEvent(
            string key,
            JsonElement eventAffordance,
            string rootLocal,
            List<UANode> items,
            List<Reference> rootReferences)
        {
            string local = LocalName(GetElementString(eventAffordance, "uav:browseName")) ?? key;
            string nodeId = GenerateNodeId(rootLocal + "/" + local);
            var eventType = new UAObjectType
            {
                NodeId = nodeId,
                BrowseName = GetElementString(eventAffordance, "uav:browseName") ?? "1:" + local,
                IsAbstract = false
            };
            string? title = GetElementString(eventAffordance, "title");
            if (title is not null)
            {
                eventType.DisplayName = MakeText(title);
            }
            eventType.References =
            [
                new Reference
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    Value = WotVocabulary.BaseEventType
                }
            ];

            items.Add(eventType);
            rootReferences.Add(new Reference
            {
                ReferenceType = "GeneratesEvent",
                IsForward = true,
                Value = nodeId
            });
        }

        private static void SynthesizeLinks(
            WotDocument document,
            List<Reference> rootReferences,
            IWotThingResolver? thingResolver,
            WotResolutionContext? resolutionContext,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            foreach (JsonElement link in document.Links)
            {
                string? rel = GetElementString(link, "rel");
                string? href = GetElementString(link, "href");
                if (rel is null || href is null)
                {
                    continue;
                }

                if (string.Equals(rel, "tm:extends", StringComparison.Ordinal))
                {
                    if (TryResolveTargetNodeId(
                        href, thingResolver, resolutionContext, options, diagnostics, out string extendsTarget))
                    {
                        SetSuperType(rootReferences, extendsTarget);
                    }
                    continue;
                }

                if (!IsUavReferenceRel(rel))
                {
                    continue;
                }

                string referenceType = GetElementString(link, "uav:refType")
                    ?? DefaultReferenceType(rel);
                if (TryResolveTargetNodeId(
                    href, thingResolver, resolutionContext, options, diagnostics, out string linkTarget))
                {
                    rootReferences.Add(new Reference
                    {
                        ReferenceType = referenceType,
                        IsForward = true,
                        Value = linkTarget
                    });
                }
            }
        }

        private static void SynthesizeComponentArrays(
            WotDocument document,
            List<Reference> rootReferences)
        {
            if (document.TryGetUav("hasComponent", out JsonElement hasComponent) &&
                hasComponent.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement target in hasComponent.EnumerateArray())
                {
                    if (target.ValueKind == JsonValueKind.String)
                    {
                        rootReferences.Add(new Reference
                        {
                            ReferenceType = "HasComponent",
                            IsForward = true,
                            Value = target.GetString()
                        });
                    }
                }
            }
            if (document.TryGetUav("componentOf", out JsonElement componentOf) &&
                componentOf.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement target in componentOf.EnumerateArray())
                {
                    if (target.ValueKind == JsonValueKind.String)
                    {
                        rootReferences.Add(new Reference
                        {
                            ReferenceType = "HasComponent",
                            IsForward = false,
                            Value = target.GetString()
                        });
                    }
                }
            }
        }

        private static bool TryResolveTargetNodeId(
            string reference,
            IWotThingResolver? resolver,
            WotResolutionContext? context,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            out string nodeId)
        {
            if (IsNodeId(reference))
            {
                nodeId = reference;
                return true;
            }
            nodeId = string.Empty;
            if (resolver is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnresolvedReference,
                    $"The reference '{reference}' could not be resolved to a NodeId without an external resolver.",
                    new WotLocation(reference: reference)));
                return false;
            }

            context ??= new WotResolutionContext();
            var entered = new List<string>();
            try
            {
                string current = reference;
                while (true)
                {
                    if (!context.TryEnter(WotResolutionKind.Thing, current, out WotDiagnostic? blocking))
                    {
                        diagnostics.Add(blocking!);
                        return false;
                    }
                    entered.Add(current);

                    WotResolverResult result = resolver.ResolveThing(current, context);
                    if (!result.Found)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Warning,
                            WotDiagnosticCode.ResolverNotFound,
                            $"The referenced document '{current}' could not be resolved.",
                            new WotLocation(reference: current)));
                        return false;
                    }
                    if (!context.TryAddBytes(current, result.Content.Length, out WotDiagnostic? limit))
                    {
                        diagnostics.Add(limit!);
                        return false;
                    }

                    using WotDocument resolved = WotDocument.Parse(result.Content, options);
                    string? resolvedId = GetUavString(resolved, "id");
                    if (resolvedId is not null)
                    {
                        nodeId = resolvedId;
                        return true;
                    }
                    string? congruent = GetUavString(resolved, "congruentType");
                    if (congruent is not null &&
                        !string.Equals(congruent, current, StringComparison.Ordinal))
                    {
                        current = congruent;
                        continue;
                    }
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.UnresolvedReference,
                        $"The referenced document '{current}' does not declare a uav:id.",
                        new WotLocation(reference: current)));
                    return false;
                }
            }
            finally
            {
                for (int ii = entered.Count - 1; ii >= 0; ii--)
                {
                    context.Leave(entered[ii]);
                }
            }
        }

        private static void ReportUnsupportedSchema(
            JsonElement schema,
            string nodeId,
            List<WotDiagnostic> diagnostics)
        {
            if (schema.TryGetProperty("uav:externalSchema", out JsonElement external) &&
                external.ValueKind == JsonValueKind.String)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnsupportedSchema,
                    $"The property references an external schema '{external.GetString()}' that was not inlined.",
                    WotLocation.FromNode(nodeId)));
                return;
            }
            string? type = GetElementString(schema, "type");
            if (string.Equals(type, "object", StringComparison.Ordinal) ||
                string.Equals(type, "array", StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnsupportedSchema,
                    $"The '{type}' DataSchema was mapped to a generic DataType; a custom DataType may be required.",
                    WotLocation.FromNode(nodeId)));
            }
        }

        private static void AddModellingRule(JsonElement schema, List<Reference> references)
        {
            string? rule = GetElementString(schema, "uav:modellingRule");
            if (rule is not null && WotVocabulary.TryGetModellingRuleNodeId(rule, out string ruleNodeId))
            {
                references.Add(new Reference
                {
                    ReferenceType = "HasModellingRule",
                    IsForward = true,
                    Value = ruleNodeId
                });
            }
        }

        private static void SetSuperType(List<Reference> references, string target)
        {
            for (int ii = 0; ii < references.Count; ii++)
            {
                if (string.Equals(references[ii].ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                    !references[ii].IsForward)
                {
                    references[ii].Value = target;
                    return;
                }
            }
            references.Add(new Reference
            {
                ReferenceType = "HasSubtype",
                IsForward = false,
                Value = target
            });
        }

        private static Dictionary<string, UANode> BuildIndex(UANodeSet nodeSet)
        {
            var index = new Dictionary<string, UANode>(StringComparer.Ordinal);
            if (nodeSet.Items is not null)
            {
                foreach (UANode node in nodeSet.Items)
                {
                    if (!string.IsNullOrEmpty(node.NodeId))
                    {
                        index[node.NodeId!] = node;
                    }
                }
            }
            return index;
        }

        private static UANode? SelectRootNode(UANodeSet nodeSet)
        {
            if (nodeSet.Items is null || nodeSet.Items.Length == 0)
            {
                return null;
            }
            return FirstOf<UAObjectType>(nodeSet)
                ?? FirstOf<UAObject>(nodeSet)
                ?? FirstOf<UAVariableType>(nodeSet)
                ?? FirstOf<UAType>(nodeSet)
                ?? nodeSet.Items[0];
        }

        private static UANode? FirstOf<T>(UANodeSet nodeSet) where T : UANode
        {
            foreach (UANode node in nodeSet.Items!)
            {
                if (node is T)
                {
                    return node;
                }
            }
            return null;
        }

        private static bool CheckAffordanceBudget(
            ref int count,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            if (count >= options.MaxAffordanceCount)
            {
                if (count == options.MaxAffordanceCount)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.AffordanceCountExceeded,
                        $"The affordance count exceeded the configured limit of {options.MaxAffordanceCount}."));
                    count++;
                }
                return false;
            }
            count++;
            return true;
        }

        private static bool IsComponentReference(string? referenceType)
        {
            return string.Equals(referenceType, "HasComponent", StringComparison.Ordinal) ||
                string.Equals(referenceType, "HasProperty", StringComparison.Ordinal) ||
                string.Equals(referenceType, WotVocabulary.HasComponent, StringComparison.Ordinal) ||
                string.Equals(referenceType, WotVocabulary.HasProperty, StringComparison.Ordinal);
        }

        private static bool IsGeneratesEventReference(string? referenceType)
        {
            return string.Equals(referenceType, "GeneratesEvent", StringComparison.Ordinal) ||
                string.Equals(referenceType, WotVocabulary.GeneratesEvent, StringComparison.Ordinal);
        }

        private static bool IsUavReferenceRel(string rel)
        {
            return string.Equals(rel, "uav:typedReference", StringComparison.Ordinal) ||
                string.Equals(rel, "uav:reference", StringComparison.Ordinal) ||
                string.Equals(rel, "uav:componentModel", StringComparison.Ordinal) ||
                string.Equals(rel, "uav:capability", StringComparison.Ordinal);
        }

        private static string DefaultReferenceType(string rel)
        {
            if (string.Equals(rel, "uav:componentModel", StringComparison.Ordinal))
            {
                return "HasComponent";
            }
            return "Organizes";
        }

        private static bool IsNodeId(string reference)
        {
            return reference.StartsWith("ns=", StringComparison.Ordinal) ||
                reference.StartsWith("nsu=", StringComparison.Ordinal) ||
                reference.StartsWith("i=", StringComparison.Ordinal) ||
                reference.StartsWith("s=", StringComparison.Ordinal) ||
                reference.StartsWith("g=", StringComparison.Ordinal) ||
                reference.StartsWith("b=", StringComparison.Ordinal);
        }

        private static string GenerateNodeId(string browsePath)
        {
            return "ns=1;s=" + browsePath;
        }

        private static string DeriveModelUri(WotDocument document)
        {
            string? uavId = GetUavString(document, "id");
            if (uavId is not null)
            {
                const string marker = "nsu=";
                if (uavId.StartsWith(marker, StringComparison.Ordinal))
                {
                    int semicolon = uavId.IndexOf(';', marker.Length);
                    string ns = semicolon < 0
                        ? uavId.Substring(marker.Length)
                        : uavId.Substring(marker.Length, semicolon - marker.Length);
                    if (ns.Length > 0)
                    {
                        return ns;
                    }
                }
            }
            string? id = document.Id;
            if (!string.IsNullOrEmpty(id))
            {
                return id!;
            }
            return "urn:opcua:wot:synthesized";
        }

        private static string? MapDataTypeToJson(string? dataType)
        {
            if (dataType is not null &&
                s_dataTypeToJsonType.TryGetValue(dataType, out string? jsonType))
            {
                return jsonType;
            }
            return null;
        }

        private static string MapJsonSchemaToDataType(JsonElement schema)
        {
            return WotVocabulary.MapJsonTypeToDataType(GetElementString(schema, "type"));
        }

        private static uint MapAccessLevel(JsonElement schema)
        {
            bool readOnly = GetElementBool(schema, "readOnly");
            bool writeOnly = GetElementBool(schema, "writeOnly");
            uint access = 0;
            if (!writeOnly)
            {
                access |= AccessLevelCurrentRead;
            }
            if (!readOnly)
            {
                access |= AccessLevelCurrentWrite;
            }
            return access == 0 ? AccessLevelCurrentRead : access;
        }

        private static string UniqueKey(string? candidate, HashSet<string> used)
        {
            string key = string.IsNullOrEmpty(candidate) ? "member" : candidate!;
            if (used.Add(key))
            {
                return key;
            }
            int suffix = 2;
            string unique = key + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (!used.Add(unique))
            {
                suffix++;
                unique = key + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return unique;
        }

        private static string? LocalName(string? browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                return null;
            }
            int colon = browseName!.IndexOf(':', StringComparison.Ordinal);
            return colon >= 0 && colon + 1 < browseName.Length
                ? browseName.Substring(colon + 1)
                : browseName;
        }

        private static string? SanitizeName(string? title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }
            var builder = new System.Text.StringBuilder(title!.Length);
            foreach (char character in title!)
            {
                if (char.IsLetterOrDigit(character) || character is '_' or '-')
                {
                    builder.Append(character);
                }
            }
            return builder.Length == 0 ? null : builder.ToString();
        }

        private static Opc.Ua.Export.LocalizedText[] MakeText(string value)
        {
            return [new Opc.Ua.Export.LocalizedText { Value = value }];
        }

        private static string? FirstText(Opc.Ua.Export.LocalizedText[]? texts)
        {
            if (texts is null)
            {
                return null;
            }
            foreach (Opc.Ua.Export.LocalizedText text in texts)
            {
                if (!string.IsNullOrEmpty(text.Value))
                {
                    return text.Value;
                }
            }
            return null;
        }

        private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                writer.WriteString(name, value);
            }
        }

        private static string? GetUavString(WotDocument document, string localName)
        {
            return document.TryGetUav(localName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string? GetRootString(WotDocument document, string name)
        {
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string? GetElementString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool GetElementBool(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.True;
        }
    }
}
