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
using System.Globalization;
using System.Text.Json;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// One localized text value in a native projection.
    /// </summary>
    public sealed class WotNativeText
    {
        /// <summary>Gets or sets the BCP 47 locale, if any.</summary>
        public string? Locale { get; set; }

        /// <summary>Gets or sets the text value.</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// A generic reference in a native projection.
    /// </summary>
    public sealed class WotNativeReference
    {
        /// <summary>Gets or sets the reference type (NodeId or browse name).</summary>
        public string ReferenceType { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the reference is forward.</summary>
        public bool IsForward { get; set; } = true;

        /// <summary>Gets or sets the target NodeId of the reference.</summary>
        public string? Target { get; set; }
    }

    /// <summary>
    /// A required model entry in a native projection.
    /// </summary>
    public sealed class WotNativeRequiredModel
    {
        /// <summary>Gets or sets the model URI.</summary>
        public string? ModelUri { get; set; }

        /// <summary>Gets or sets the model version.</summary>
        public string? Version { get; set; }

        /// <summary>Gets or sets the publication date (ISO 8601).</summary>
        public string? PublicationDate { get; set; }
    }

    /// <summary>
    /// One generic UANode record in a native projection.
    /// </summary>
    public sealed class WotNativeNode
    {
        /// <summary>Gets or sets the NodeClass name (for example <c>Variable</c>).</summary>
        public string NodeClass { get; set; } = string.Empty;

        /// <summary>Gets or sets the NodeId string.</summary>
        public string? NodeId { get; set; }

        /// <summary>Gets or sets the BrowseName in <c>namespaceIndex:name</c> form.</summary>
        public string? BrowseName { get; set; }

        /// <summary>Gets or sets the SymbolicName.</summary>
        public string? SymbolicName { get; set; }

        /// <summary>Gets the DisplayName localized values.</summary>
        public List<WotNativeText> DisplayName { get; } = [];

        /// <summary>Gets the Description localized values.</summary>
        public List<WotNativeText> Description { get; } = [];

        /// <summary>
        /// Gets the InverseName localized values of a ReferenceType node. Empty
        /// for every other NodeClass.
        /// </summary>
        public List<WotNativeText> InverseName { get; } = [];

        /// <summary>Gets or sets whether the type node is abstract.</summary>
        public bool? IsAbstract { get; set; }

        /// <summary>Gets or sets the ParentNodeId of an instance node.</summary>
        public string? ParentNodeId { get; set; }

        /// <summary>Gets or sets the DataType NodeId of a variable node.</summary>
        public string? DataType { get; set; }

        /// <summary>Gets or sets the ValueRank of a variable node.</summary>
        public int? ValueRank { get; set; }

        /// <summary>Gets or sets the ArrayDimensions of a variable node.</summary>
        public string? ArrayDimensions { get; set; }

        /// <summary>Gets or sets the AccessLevel of a variable node.</summary>
        public uint? AccessLevel { get; set; }

        /// <summary>Gets or sets the derived TypeDefinition NodeId (informational).</summary>
        public string? TypeDefinition { get; set; }

        /// <summary>Gets or sets the derived supertype NodeId (informational).</summary>
        public string? SuperType { get; set; }

        /// <summary>Gets or sets the derived modelling rule name (informational).</summary>
        public string? ModellingRule { get; set; }

        /// <summary>Gets additional scalar attributes keyed by attribute name.</summary>
        public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the generic references of the node.</summary>
        public List<WotNativeReference> References { get; } = [];

        /// <summary>Gets or sets the raw XML of the node <c>Value</c>, if any.</summary>
        public string? ValueXml { get; set; }
    }

    /// <summary>
    /// A deterministic native projection of a NodeSet2 document: model
    /// metadata, the namespace table and one generic record per UANode.
    /// </summary>
    public sealed class WotNativeModel
    {
        /// <summary>Gets or sets the model URI.</summary>
        public string? ModelUri { get; set; }

        /// <summary>Gets or sets the model version.</summary>
        public string? Version { get; set; }

        /// <summary>Gets or sets the model publication date (ISO 8601).</summary>
        public string? PublicationDate { get; set; }

        /// <summary>Gets the namespace URI table.</summary>
        public List<string> NamespaceUris { get; } = [];

        /// <summary>Gets the required models.</summary>
        public List<WotNativeRequiredModel> RequiredModels { get; } = [];

        /// <summary>Gets the node records.</summary>
        public List<WotNativeNode> Nodes { get; } = [];
    }

    /// <summary>
    /// Builds and reconstructs the deterministic native <c>uav:nodes</c>
    /// projection of a NodeSet2 document.
    /// </summary>
    internal static class WotNativeProjection
    {
        public static WotNativeModel Build(UANodeSet nodeSet, WotNodeSetConverterOptions options)
        {
            var model = new WotNativeModel();

            if (nodeSet.Models is { Length: > 0 })
            {
                ModelTableEntry entry = nodeSet.Models[0];
                model.ModelUri = entry.ModelUri;
                model.Version = entry.Version;
                model.PublicationDate = entry.PublicationDateSpecified
                    ? FormatDate(entry.PublicationDate)
                    : null;
                if (entry.RequiredModel is { Length: > 0 })
                {
                    foreach (ModelTableEntry required in entry.RequiredModel)
                    {
                        model.RequiredModels.Add(new WotNativeRequiredModel
                        {
                            ModelUri = required.ModelUri,
                            Version = required.Version,
                            PublicationDate = required.PublicationDateSpecified
                                ? FormatDate(required.PublicationDate)
                                : null
                        });
                    }
                }
            }

            if (nodeSet.NamespaceUris is not null)
            {
                foreach (string uri in nodeSet.NamespaceUris)
                {
                    model.NamespaceUris.Add(uri);
                }
            }

            if (nodeSet.Items is not null)
            {
                foreach (UANode node in nodeSet.Items)
                {
                    if (model.Nodes.Count >= options.MaxNodeCount)
                    {
                        break;
                    }
                    model.Nodes.Add(BuildNode(node));
                }
            }

            return model;
        }

        public static void Write(Utf8JsonWriter writer, WotNativeModel model)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", "uav:NodeModel");

            writer.WritePropertyName("model");
            writer.WriteStartObject();
            WriteOptionalString(writer, "modelUri", model.ModelUri);
            WriteOptionalString(writer, "version", model.Version);
            WriteOptionalString(writer, "publicationDate", model.PublicationDate);
            writer.WritePropertyName("namespaceUris");
            writer.WriteStartArray();
            foreach (string uri in model.NamespaceUris)
            {
                writer.WriteStringValue(uri);
            }
            writer.WriteEndArray();
            if (model.RequiredModels.Count > 0)
            {
                writer.WritePropertyName("requiredModels");
                writer.WriteStartArray();
                foreach (WotNativeRequiredModel required in model.RequiredModels)
                {
                    writer.WriteStartObject();
                    WriteOptionalString(writer, "modelUri", required.ModelUri);
                    WriteOptionalString(writer, "version", required.Version);
                    WriteOptionalString(writer, "publicationDate", required.PublicationDate);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();

            writer.WritePropertyName("nodes");
            writer.WriteStartArray();
            foreach (WotNativeNode node in model.Nodes)
            {
                WriteNode(writer, node);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        public static WotNativeModel Read(JsonElement projection, WotNodeSetConverterOptions options)
        {
            var model = new WotNativeModel();
            if (projection.TryGetProperty("model", out JsonElement modelElement) &&
                modelElement.ValueKind == JsonValueKind.Object)
            {
                model.ModelUri = GetString(modelElement, "modelUri");
                model.Version = GetString(modelElement, "version");
                model.PublicationDate = GetString(modelElement, "publicationDate");
                if (modelElement.TryGetProperty("namespaceUris", out JsonElement uris) &&
                    uris.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement uri in uris.EnumerateArray())
                    {
                        if (uri.ValueKind == JsonValueKind.String)
                        {
                            model.NamespaceUris.Add(uri.GetString()!);
                        }
                    }
                }
                if (modelElement.TryGetProperty("requiredModels", out JsonElement required) &&
                    required.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in required.EnumerateArray())
                    {
                        model.RequiredModels.Add(new WotNativeRequiredModel
                        {
                            ModelUri = GetString(item, "modelUri"),
                            Version = GetString(item, "version"),
                            PublicationDate = GetString(item, "publicationDate")
                        });
                    }
                }
            }

            if (projection.TryGetProperty("nodes", out JsonElement nodes) &&
                nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement nodeElement in nodes.EnumerateArray())
                {
                    if (model.Nodes.Count >= options.MaxNodeCount)
                    {
                        break;
                    }
                    if (nodeElement.ValueKind == JsonValueKind.Object)
                    {
                        model.Nodes.Add(ReadNode(nodeElement));
                    }
                }
            }

            return model;
        }

        public static UANodeSet ToNodeSet(
            WotNativeModel model,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = model.NamespaceUris.Count > 0
                    ? [.. model.NamespaceUris]
                    : null
            };

            if (!string.IsNullOrEmpty(model.ModelUri))
            {
                var entry = new ModelTableEntry { ModelUri = model.ModelUri };
                if (!string.IsNullOrEmpty(model.Version))
                {
                    entry.Version = model.Version;
                }
                if (TryParseDate(model.PublicationDate, out DateTime published))
                {
                    entry.PublicationDate = published;
                    entry.PublicationDateSpecified = true;
                }
                if (model.RequiredModels.Count > 0)
                {
                    var required = new List<ModelTableEntry>();
                    foreach (WotNativeRequiredModel item in model.RequiredModels)
                    {
                        var requiredEntry = new ModelTableEntry { ModelUri = item.ModelUri };
                        if (!string.IsNullOrEmpty(item.Version))
                        {
                            requiredEntry.Version = item.Version;
                        }
                        if (TryParseDate(item.PublicationDate, out DateTime requiredDate))
                        {
                            requiredEntry.PublicationDate = requiredDate;
                            requiredEntry.PublicationDateSpecified = true;
                        }
                        required.Add(requiredEntry);
                    }
                    entry.RequiredModel = [.. required];
                }
                nodeSet.Models = [entry];
            }

            var items = new List<UANode>();
            foreach (WotNativeNode record in model.Nodes)
            {
                UANode? node = ReconstructNode(record, diagnostics);
                if (node is not null)
                {
                    items.Add(node);
                }
            }
            nodeSet.Items = [.. items];
            return nodeSet;
        }

        private static WotNativeNode BuildNode(UANode node)
        {
            var record = new WotNativeNode
            {
                NodeClass = GetNodeClass(node),
                NodeId = node.NodeId,
                BrowseName = node.BrowseName,
                SymbolicName = node.SymbolicName
            };

            AppendTexts(record.DisplayName, node.DisplayName);
            AppendTexts(record.Description, node.Description);

            if (node.WriteMask != 0)
            {
                record.Attributes["WriteMask"] = WotVocabulary.FormatUInt(node.WriteMask);
            }
            if (node.UserWriteMask != 0)
            {
                record.Attributes["UserWriteMask"] = WotVocabulary.FormatUInt(node.UserWriteMask);
            }

            switch (node)
            {
                case UAObjectType objectType:
                    record.IsAbstract = objectType.IsAbstract;
                    break;
                case UAVariableType variableType:
                    record.IsAbstract = variableType.IsAbstract;
                    record.DataType = variableType.DataType;
                    record.ValueRank = variableType.ValueRank;
                    record.ArrayDimensions = variableType.ArrayDimensions;
                    record.ValueXml = ToRawXml(variableType.Value);
                    break;
                case UAReferenceType referenceType:
                    record.IsAbstract = referenceType.IsAbstract;
                    record.Attributes["Symmetric"] = referenceType.Symmetric
                        ? "true" : "false";
                    // Preserve the full localized InverseName (locale + every
                    // entry) so it can be restored exactly on reconstruction.
                    AppendTexts(record.InverseName, referenceType.InverseName);
                    break;
                case UADataType dataType:
                    record.IsAbstract = dataType.IsAbstract;
                    if (dataType.Definition is not null)
                    {
                        record.Attributes["HasDefinition"] = "true";
                    }
                    break;
                case UAVariable variable:
                    record.ParentNodeId = variable.ParentNodeId;
                    record.DataType = variable.DataType;
                    record.ValueRank = variable.ValueRank;
                    record.ArrayDimensions = variable.ArrayDimensions;
                    record.AccessLevel = variable.AccessLevel;
                    record.Attributes["UserAccessLevel"] =
                        WotVocabulary.FormatUInt(variable.UserAccessLevel);
                    if (variable.Historizing)
                    {
                        record.Attributes["Historizing"] = "true";
                    }
                    if (variable.MinimumSamplingInterval != 0D)
                    {
                        record.Attributes["MinimumSamplingInterval"] =
                            variable.MinimumSamplingInterval.ToString(
                                "R", CultureInfo.InvariantCulture);
                    }
                    record.ValueXml = ToRawXml(variable.Value);
                    break;
                case UAMethod method:
                    record.ParentNodeId = method.ParentNodeId;
                    if (!method.Executable)
                    {
                        record.Attributes["Executable"] = "false";
                    }
                    if (!method.UserExecutable)
                    {
                        record.Attributes["UserExecutable"] = "false";
                    }
                    if (!string.IsNullOrEmpty(method.MethodDeclarationId))
                    {
                        record.Attributes["MethodDeclarationId"] = method.MethodDeclarationId!;
                    }
                    break;
                case UAView view:
                    record.ParentNodeId = view.ParentNodeId;
                    if (view.EventNotifier != 0)
                    {
                        record.Attributes["EventNotifier"] =
                            WotVocabulary.FormatInt(view.EventNotifier);
                    }
                    if (view.ContainsNoLoops)
                    {
                        record.Attributes["ContainsNoLoops"] = "true";
                    }
                    break;
                case UAObject uaObject:
                    record.ParentNodeId = uaObject.ParentNodeId;
                    if (uaObject.EventNotifier != 0)
                    {
                        record.Attributes["EventNotifier"] =
                            WotVocabulary.FormatInt(uaObject.EventNotifier);
                    }
                    break;
            }

            if (node.References is not null)
            {
                foreach (Reference reference in node.References)
                {
                    record.References.Add(new WotNativeReference
                    {
                        ReferenceType = reference.ReferenceType ?? string.Empty,
                        IsForward = reference.IsForward,
                        Target = reference.Value
                    });
                    ApplyDerived(record, reference);
                }
            }

            return record;
        }

        private static void ApplyDerived(WotNativeNode record, Reference reference)
        {
            if (string.Equals(reference.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal) &&
                reference.IsForward)
            {
                record.TypeDefinition = reference.Value;
            }
            else if (string.Equals(reference.ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                !reference.IsForward)
            {
                record.SuperType = reference.Value;
            }
            else if (string.Equals(reference.ReferenceType, "HasModellingRule", StringComparison.Ordinal) &&
                reference.IsForward &&
                reference.Value is not null &&
                WotVocabulary.TryGetModellingRuleName(reference.Value, out string ruleName))
            {
                record.ModellingRule = ruleName;
            }
        }

        private static void WriteNode(Utf8JsonWriter writer, WotNativeNode node)
        {
            writer.WriteStartObject();
            writer.WriteString("nodeClass", node.NodeClass);
            WriteOptionalString(writer, "nodeId", node.NodeId);
            WriteOptionalString(writer, "browseName", node.BrowseName);
            WriteOptionalString(writer, "symbolicName", node.SymbolicName);
            WriteTexts(writer, "displayName", node.DisplayName);
            WriteTexts(writer, "description", node.Description);
            WriteTexts(writer, "inverseName", node.InverseName);
            if (node.IsAbstract.HasValue)
            {
                writer.WriteBoolean("isAbstract", node.IsAbstract.Value);
            }
            WriteOptionalString(writer, "parentNodeId", node.ParentNodeId);
            WriteOptionalString(writer, "dataType", node.DataType);
            if (node.ValueRank.HasValue)
            {
                writer.WriteNumber("valueRank", node.ValueRank.Value);
            }
            if (!string.IsNullOrEmpty(node.ArrayDimensions))
            {
                writer.WriteString("arrayDimensions", node.ArrayDimensions);
            }
            if (node.AccessLevel.HasValue)
            {
                writer.WriteNumber("accessLevel", node.AccessLevel.Value);
            }
            WriteOptionalString(writer, "typeDefinition", node.TypeDefinition);
            WriteOptionalString(writer, "superType", node.SuperType);
            WriteOptionalString(writer, "modellingRule", node.ModellingRule);
            if (node.Attributes.Count > 0)
            {
                writer.WritePropertyName("attributes");
                writer.WriteStartObject();
                var keys = new List<string>(node.Attributes.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    writer.WriteString(key, node.Attributes[key]);
                }
                writer.WriteEndObject();
            }
            if (node.References.Count > 0)
            {
                writer.WritePropertyName("references");
                writer.WriteStartArray();
                foreach (WotNativeReference reference in node.References)
                {
                    writer.WriteStartObject();
                    writer.WriteString("referenceType", reference.ReferenceType);
                    writer.WriteBoolean("isForward", reference.IsForward);
                    WriteOptionalString(writer, "target", reference.Target);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            WriteOptionalString(writer, "valueXml", node.ValueXml);
            writer.WriteEndObject();
        }

        private static WotNativeNode ReadNode(JsonElement element)
        {
            var node = new WotNativeNode
            {
                NodeClass = GetString(element, "nodeClass") ?? string.Empty,
                NodeId = GetString(element, "nodeId"),
                BrowseName = GetString(element, "browseName"),
                SymbolicName = GetString(element, "symbolicName"),
                ParentNodeId = GetString(element, "parentNodeId"),
                DataType = GetString(element, "dataType"),
                ArrayDimensions = GetString(element, "arrayDimensions"),
                TypeDefinition = GetString(element, "typeDefinition"),
                SuperType = GetString(element, "superType"),
                ModellingRule = GetString(element, "modellingRule"),
                ValueXml = GetString(element, "valueXml")
            };
            ReadTexts(node.DisplayName, element, "displayName");
            ReadTexts(node.Description, element, "description");
            ReadTexts(node.InverseName, element, "inverseName");
            if (element.TryGetProperty("isAbstract", out JsonElement isAbstract) &&
                (isAbstract.ValueKind == JsonValueKind.True ||
                 isAbstract.ValueKind == JsonValueKind.False))
            {
                node.IsAbstract = isAbstract.GetBoolean();
            }
            if (element.TryGetProperty("valueRank", out JsonElement valueRank) &&
                valueRank.ValueKind == JsonValueKind.Number &&
                valueRank.TryGetInt32(out int rank))
            {
                node.ValueRank = rank;
            }
            if (element.TryGetProperty("accessLevel", out JsonElement accessLevel) &&
                accessLevel.ValueKind == JsonValueKind.Number &&
                accessLevel.TryGetUInt32(out uint level))
            {
                node.AccessLevel = level;
            }
            if (element.TryGetProperty("attributes", out JsonElement attributes) &&
                attributes.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty attribute in attributes.EnumerateObject())
                {
                    if (attribute.Value.ValueKind == JsonValueKind.String)
                    {
                        node.Attributes[attribute.Name] = attribute.Value.GetString()!;
                    }
                }
            }
            if (element.TryGetProperty("references", out JsonElement references) &&
                references.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement reference in references.EnumerateArray())
                {
                    node.References.Add(new WotNativeReference
                    {
                        ReferenceType = GetString(reference, "referenceType") ?? string.Empty,
                        IsForward = !reference.TryGetProperty("isForward", out JsonElement forward) ||
                            forward.ValueKind != JsonValueKind.False,
                        Target = GetString(reference, "target")
                    });
                }
            }
            return node;
        }

        private static UANode? ReconstructNode(WotNativeNode record, List<WotDiagnostic> diagnostics)
        {
            UANode node = record.NodeClass switch
            {
                "ObjectType" => new UAObjectType(),
                "VariableType" => new UAVariableType(),
                "ReferenceType" => new UAReferenceType(),
                "DataType" => new UADataType(),
                "Object" => new UAObject(),
                "Variable" => new UAVariable(),
                "Method" => new UAMethod(),
                "View" => new UAView(),
                _ => null!
            };

            if (node is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    $"Unknown NodeClass '{record.NodeClass}' in native projection was skipped.",
                    WotLocation.FromNode(record.NodeId ?? string.Empty)));
                return null;
            }

            node.NodeId = record.NodeId;
            node.BrowseName = record.BrowseName;
            node.SymbolicName = record.SymbolicName;
            node.DisplayName = ToLocalizedTexts(record.DisplayName);
            node.Description = ToLocalizedTexts(record.Description);

            ApplyAttribute(record, "WriteMask", value =>
            {
                if (TryParseUInt(value, out uint mask))
                {
                    node.WriteMask = mask;
                }
            });
            ApplyAttribute(record, "UserWriteMask", value =>
            {
                if (TryParseUInt(value, out uint mask))
                {
                    node.UserWriteMask = mask;
                }
            });

            switch (node)
            {
                case UAObjectType objectType:
                    objectType.IsAbstract = record.IsAbstract ?? false;
                    break;
                case UAVariableType variableType:
                    variableType.IsAbstract = record.IsAbstract ?? false;
                    variableType.DataType = record.DataType ?? WotVocabulary.BaseDataType;
                    variableType.ValueRank = record.ValueRank ?? -1;
                    variableType.ArrayDimensions = record.ArrayDimensions ?? string.Empty;
                    variableType.Value = FromRawXml(record.ValueXml);
                    break;
                case UAReferenceType referenceType:
                    referenceType.IsAbstract = record.IsAbstract ?? false;
                    if (record.Attributes.TryGetValue("Symmetric", out string? symmetric))
                    {
                        referenceType.Symmetric = string.Equals(
                            symmetric, "true", StringComparison.Ordinal);
                    }
                    // Restore the InverseName exactly, including every localized
                    // entry, so a non-symmetric reference type is not silently
                    // stripped of its inverse browse name on reconstruction.
                    referenceType.InverseName = ToLocalizedTexts(record.InverseName);
                    break;
                case UADataType dataType:
                    dataType.IsAbstract = record.IsAbstract ?? false;
                    if (record.Attributes.ContainsKey("HasDefinition"))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Warning,
                            WotDiagnosticCode.LossySynthesis,
                            "A DataType Definition could not be reconstructed from the native projection.",
                            WotLocation.FromNode(record.NodeId ?? string.Empty)));
                    }
                    break;
                case UAVariable variable:
                    variable.ParentNodeId = record.ParentNodeId;
                    variable.DataType = record.DataType ?? WotVocabulary.BaseDataType;
                    variable.ValueRank = record.ValueRank ?? -1;
                    variable.ArrayDimensions = record.ArrayDimensions ?? string.Empty;
                    variable.AccessLevel = record.AccessLevel ?? 1;
                    ApplyAttribute(record, "UserAccessLevel", value =>
                    {
                        if (TryParseUInt(value, out uint level))
                        {
                            variable.UserAccessLevel = level;
                        }
                    });
                    ApplyAttribute(record, "Historizing", value =>
                        variable.Historizing = string.Equals(value, "true", StringComparison.Ordinal));
                    ApplyAttribute(record, "MinimumSamplingInterval", value =>
                    {
                        if (double.TryParse(
                            value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double interval))
                        {
                            variable.MinimumSamplingInterval = interval;
                        }
                    });
                    variable.Value = FromRawXml(record.ValueXml);
                    break;
                case UAMethod method:
                    method.ParentNodeId = record.ParentNodeId;
                    ApplyAttribute(record, "Executable", value =>
                        method.Executable = !string.Equals(value, "false", StringComparison.Ordinal));
                    ApplyAttribute(record, "UserExecutable", value =>
                        method.UserExecutable = !string.Equals(value, "false", StringComparison.Ordinal));
                    ApplyAttribute(record, "MethodDeclarationId", value =>
                        method.MethodDeclarationId = value);
                    break;
                case UAView view:
                    view.ParentNodeId = record.ParentNodeId;
                    ApplyAttribute(record, "EventNotifier", value =>
                    {
                        if (byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out byte notifier))
                        {
                            view.EventNotifier = notifier;
                        }
                    });
                    ApplyAttribute(record, "ContainsNoLoops", value =>
                        view.ContainsNoLoops = string.Equals(value, "true", StringComparison.Ordinal));
                    break;
                case UAObject uaObject:
                    uaObject.ParentNodeId = record.ParentNodeId;
                    ApplyAttribute(record, "EventNotifier", value =>
                    {
                        if (byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out byte notifier))
                        {
                            uaObject.EventNotifier = notifier;
                        }
                    });
                    break;
            }

            if (record.References.Count > 0)
            {
                var references = new List<Reference>();
                foreach (WotNativeReference reference in record.References)
                {
                    references.Add(new Reference
                    {
                        ReferenceType = reference.ReferenceType,
                        IsForward = reference.IsForward,
                        Value = reference.Target
                    });
                }
                node.References = [.. references];
            }

            return node;
        }

        private static void ApplyAttribute(WotNativeNode record, string name, Action<string> apply)
        {
            if (record.Attributes.TryGetValue(name, out string? value))
            {
                apply(value);
            }
        }

        private static void AppendTexts(List<WotNativeText> target, Opc.Ua.Export.LocalizedText[]? source)
        {
            if (source is null)
            {
                return;
            }
            foreach (Opc.Ua.Export.LocalizedText text in source)
            {
                target.Add(new WotNativeText
                {
                    Locale = string.IsNullOrEmpty(text.Locale) ? null : text.Locale,
                    Value = text.Value ?? string.Empty
                });
            }
        }

        private static Opc.Ua.Export.LocalizedText[]? ToLocalizedTexts(List<WotNativeText> source)
        {
            if (source.Count == 0)
            {
                return null;
            }
            var result = new Opc.Ua.Export.LocalizedText[source.Count];
            for (int ii = 0; ii < source.Count; ii++)
            {
                result[ii] = new Opc.Ua.Export.LocalizedText
                {
                    Locale = source[ii].Locale ?? string.Empty,
                    Value = source[ii].Value
                };
            }
            return result;
        }

        private static void WriteTexts(Utf8JsonWriter writer, string name, List<WotNativeText> texts)
        {
            if (texts.Count == 0)
            {
                return;
            }
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (WotNativeText text in texts)
            {
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(text.Locale))
                {
                    writer.WriteString("locale", text.Locale);
                }
                writer.WriteString("value", text.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void ReadTexts(List<WotNativeText> target, JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    target.Add(new WotNativeText
                    {
                        Locale = GetString(item, "locale"),
                        Value = GetString(item, "value") ?? string.Empty
                    });
                }
            }
        }

        private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                writer.WriteString(name, value);
            }
        }

        private static string? GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string GetNodeClass(UANode node)
        {
            return node switch
            {
                UAObjectType => "ObjectType",
                UAVariableType => "VariableType",
                UAReferenceType => "ReferenceType",
                UADataType => "DataType",
                UAView => "View",
                UAMethod => "Method",
                UAVariable => "Variable",
                UAObject => "Object",
                _ => "Unknown"
            };
        }

        private static string? ToRawXml(System.Xml.XmlElement? element)
        {
            return element?.OuterXml;
        }

        private static System.Xml.XmlElement? FromRawXml(string? xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }
            var document = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(
                new System.IO.StringReader(xml!),
                CoreUtils.DefaultXmlReaderSettings());
            document.Load(reader);
            return document.DocumentElement;
        }

        private static bool TryParseUInt(string value, out uint result)
        {
            return uint.TryParse(
                value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }

        private static string FormatDate(DateTime value)
        {
            return value.ToUniversalTime().ToString(
                "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        private static bool TryParseDate(string? value, out DateTime result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = default;
                return false;
            }
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out result);
        }
    }
}
