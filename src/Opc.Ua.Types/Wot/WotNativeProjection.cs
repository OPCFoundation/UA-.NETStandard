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
using System.IO;
using System.Text.Json;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Schema-complete, deterministic JSON projection of the UANodeSet XSD.
    /// </summary>
    internal static class WotNativeProjection
    {
        public const string ProjectionType = "uav:NodeModel";
        public const string ProfileVersion = "1.0";

        public static byte[] Write(
            UANodeSet nodeSet,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(
                output,
                new JsonWriterOptions { Indented = true, SkipValidation = false }))
            {
                writer.WriteStartObject();
                writer.WriteString("@type", ProjectionType);
                writer.WriteString("profileVersion", ProfileVersion);
                WriteStrings(writer, "namespaceUris", nodeSet.NamespaceUris);
                WriteStrings(writer, "serverUris", nodeSet.ServerUris);
                WriteModels(writer, nodeSet.Models);
                WriteAliases(writer, nodeSet.Aliases);
                WriteXmlElements(writer, "extensions", nodeSet.Extensions);
                if (nodeSet.LastModifiedSpecified)
                {
                    writer.WriteString("lastModified", FormatDate(nodeSet.LastModified));
                }

                writer.WritePropertyName("nodes");
                writer.WriteStartArray();
                if (nodeSet.Items is not null)
                {
                    int count = 0;
                    foreach (UANode node in nodeSet.Items)
                    {
                        if (count++ >= options.MaxNodeCount)
                        {
                            diagnostics.Add(new WotDiagnostic(
                                WotDiagnosticSeverity.Error,
                                WotDiagnosticCode.NodeCountExceeded,
                                $"The NodeSet contains more than the configured " +
                                $"{options.MaxNodeCount} native projection nodes."));
                            break;
                        }
                        WriteNode(writer, node);
                    }
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return output.ToArray();
        }

        public static UANodeSet? Read(
            JsonElement projection,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            int initialErrors = CountErrors(diagnostics);
            if (projection.ValueKind != JsonValueKind.Object ||
                !string.Equals(
                    GetString(projection, "@type"),
                    ProjectionType,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    $"The uav:nodes member shall be an object whose @type is " +
                    $"{ProjectionType}.",
                    WotLocation.FromPointer("/uav:nodes")));
                return null;
            }

            string? version = GetString(projection, "profileVersion");
            if (!string.Equals(version, ProfileVersion, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    $"Unsupported uav:nodes profileVersion '{version}'.",
                    WotLocation.FromPointer("/uav:nodes/profileVersion")));
                return null;
            }

            var nodeSet = new UANodeSet
            {
                NamespaceUris = ReadStrings(projection, "namespaceUris"),
                ServerUris = ReadStrings(projection, "serverUris"),
                Models = ReadModels(projection, diagnostics),
                Aliases = ReadAliases(projection, diagnostics),
                Extensions = ReadXmlElements(
                    projection,
                    "extensions",
                    "/uav:nodes/extensions",
                    diagnostics)
            };

            if (TryGetDate(projection, "lastModified", out DateTime lastModified))
            {
                nodeSet.LastModified = lastModified;
                nodeSet.LastModifiedSpecified = true;
            }

            if (!projection.TryGetProperty("nodes", out JsonElement nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    "The uav:nodes projection is missing its nodes array.",
                    WotLocation.FromPointer("/uav:nodes/nodes")));
                return null;
            }

            var items = new List<UANode>();
            int index = 0;
            foreach (JsonElement element in nodes.EnumerateArray())
            {
                if (items.Count >= options.MaxNodeCount)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NodeCountExceeded,
                        $"The native projection contains more than the configured " +
                        $"{options.MaxNodeCount} nodes.",
                        WotLocation.FromPointer("/uav:nodes/nodes")));
                    break;
                }
                UANode? node = ReadNode(
                    element,
                    $"/uav:nodes/nodes/{index.ToString(CultureInfo.InvariantCulture)}",
                    diagnostics);
                if (node is not null)
                {
                    items.Add(node);
                }
                index++;
            }
            nodeSet.Items = [.. items];

            return CountErrors(diagnostics) == initialErrors ? nodeSet : null;
        }

        private static void WriteModels(Utf8JsonWriter writer, ModelTableEntry[]? models)
        {
            if (models is null || models.Length == 0)
            {
                return;
            }
            writer.WritePropertyName("models");
            writer.WriteStartArray();
            foreach (ModelTableEntry model in models)
            {
                WriteModel(writer, model);
            }
            writer.WriteEndArray();
        }

        private static void WriteModel(Utf8JsonWriter writer, ModelTableEntry model)
        {
            writer.WriteStartObject();
            WriteString(writer, "modelUri", model.ModelUri);
            WriteString(writer, "xmlSchemaUri", model.XmlSchemaUri);
            WriteString(writer, "version", model.Version);
            if (model.PublicationDateSpecified)
            {
                writer.WriteString("publicationDate", FormatDate(model.PublicationDate));
            }
            WriteString(writer, "modelVersion", model.ModelVersion);
            if (model.AccessRestrictions != 0)
            {
                writer.WriteNumber("accessRestrictions", model.AccessRestrictions);
            }
            WriteRolePermissions(writer, model.RolePermissions);
            if (model.RequiredModel is { Length: > 0 })
            {
                writer.WritePropertyName("requiredModels");
                writer.WriteStartArray();
                foreach (ModelTableEntry required in model.RequiredModel)
                {
                    WriteModel(writer, required);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        private static ModelTableEntry[]? ReadModels(
            JsonElement projection,
            List<WotDiagnostic> diagnostics)
        {
            if (!projection.TryGetProperty("models", out JsonElement models) ||
                models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<ModelTableEntry>();
            int index = 0;
            foreach (JsonElement model in models.EnumerateArray())
            {
                result.Add(ReadModel(
                    model,
                    $"/uav:nodes/models/{index.ToString(CultureInfo.InvariantCulture)}",
                    diagnostics));
                index++;
            }
            return [.. result];
        }

        private static ModelTableEntry ReadModel(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            var model = new ModelTableEntry
            {
                ModelUri = GetString(element, "modelUri"),
                XmlSchemaUri = GetString(element, "xmlSchemaUri"),
                Version = GetString(element, "version"),
                ModelVersion = GetString(element, "modelVersion"),
                RolePermissions = ReadRolePermissions(element, "rolePermissions")
            };
            if (TryGetDate(element, "publicationDate", out DateTime publicationDate))
            {
                model.PublicationDate = publicationDate;
                model.PublicationDateSpecified = true;
            }
            if (TryGetUInt16(element, "accessRestrictions", out ushort accessRestrictions))
            {
                model.AccessRestrictions = accessRestrictions;
            }
            if (element.TryGetProperty("requiredModels", out JsonElement required) &&
                required.ValueKind == JsonValueKind.Array)
            {
                var entries = new List<ModelTableEntry>();
                int index = 0;
                foreach (JsonElement item in required.EnumerateArray())
                {
                    entries.Add(ReadModel(
                        item,
                        pointer + "/requiredModels/" +
                        index.ToString(CultureInfo.InvariantCulture),
                        diagnostics));
                    index++;
                }
                model.RequiredModel = [.. entries];
            }
            if (string.IsNullOrEmpty(model.ModelUri))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    "A native model entry is missing modelUri.",
                    WotLocation.FromPointer(pointer)));
            }
            return model;
        }

        private static void WriteAliases(Utf8JsonWriter writer, NodeIdAlias[]? aliases)
        {
            if (aliases is null || aliases.Length == 0)
            {
                return;
            }
            writer.WritePropertyName("aliases");
            writer.WriteStartArray();
            foreach (NodeIdAlias alias in aliases)
            {
                writer.WriteStartObject();
                WriteString(writer, "alias", alias.Alias);
                WriteString(writer, "value", alias.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static NodeIdAlias[]? ReadAliases(
            JsonElement projection,
            List<WotDiagnostic> diagnostics)
        {
            if (!projection.TryGetProperty("aliases", out JsonElement aliases) ||
                aliases.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<NodeIdAlias>();
            int index = 0;
            foreach (JsonElement item in aliases.EnumerateArray())
            {
                string? alias = GetString(item, "alias");
                string? value = GetString(item, "value");
                if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(value))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionInvalid,
                        "A native alias entry requires alias and value.",
                        WotLocation.FromPointer(
                            "/uav:nodes/aliases/" +
                            index.ToString(CultureInfo.InvariantCulture))));
                }
                result.Add(new NodeIdAlias { Alias = alias, Value = value });
                index++;
            }
            return [.. result];
        }

        private static void WriteNode(Utf8JsonWriter writer, UANode node)
        {
            writer.WriteStartObject();
            writer.WriteString("nodeClass", GetNodeClass(node));
            WriteString(writer, "nodeId", node.NodeId);
            WriteString(writer, "browseName", node.BrowseName);
            WriteString(writer, "symbolicName", node.SymbolicName);
            WriteTexts(writer, "displayName", node.DisplayName);
            WriteTexts(writer, "description", node.Description);
            WriteStrings(writer, "category", node.Category);
            WriteString(writer, "documentation", node.Documentation);
            WriteReferences(writer, node.References);
            WriteRolePermissions(writer, node.RolePermissions);
            WriteXmlElements(writer, "extensions", node.Extensions);
            if (node.WriteMask != 0)
            {
                writer.WriteNumber("writeMask", node.WriteMask);
            }
            if (node.UserWriteMask != 0)
            {
                writer.WriteNumber("userWriteMask", node.UserWriteMask);
            }
            if (node.AccessRestrictionsSpecified)
            {
                writer.WriteNumber("accessRestrictions", node.AccessRestrictions);
            }
            if (node.HasNoPermissions)
            {
                writer.WriteBoolean("hasNoPermissions", true);
            }
            if (node.ReleaseStatus != ReleaseStatus.Released)
            {
                writer.WriteString("releaseStatus", node.ReleaseStatus.ToString());
            }

            switch (node)
            {
                case UAVariable variable:
                    WriteInstance(writer, variable);
                    WriteVariable(writer, variable);
                    break;
                case UAVariableType variableType:
                    WriteType(writer, variableType);
                    WriteVariableType(writer, variableType);
                    break;
                case UAObject uaObject:
                    WriteInstance(writer, uaObject);
                    if (uaObject.EventNotifier != 0)
                    {
                        writer.WriteNumber("eventNotifier", uaObject.EventNotifier);
                    }
                    break;
                case UAMethod method:
                    WriteInstance(writer, method);
                    WriteMethod(writer, method);
                    break;
                case UAView view:
                    WriteInstance(writer, view);
                    if (view.ContainsNoLoops)
                    {
                        writer.WriteBoolean("containsNoLoops", true);
                    }
                    if (view.EventNotifier != 0)
                    {
                        writer.WriteNumber("eventNotifier", view.EventNotifier);
                    }
                    break;
                case UADataType dataType:
                    WriteType(writer, dataType);
                    WriteDataType(writer, dataType);
                    break;
                case UAReferenceType referenceType:
                    WriteType(writer, referenceType);
                    WriteTexts(writer, "inverseName", referenceType.InverseName);
                    if (referenceType.Symmetric)
                    {
                        writer.WriteBoolean("symmetric", true);
                    }
                    break;
                case UAObjectType objectType:
                    WriteType(writer, objectType);
                    break;
            }

            WriteDerivedFacts(writer, node);
            writer.WriteEndObject();
        }

        private static UANode? ReadNode(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            string? nodeClass = GetString(element, "nodeClass");
            UANode? node = nodeClass switch
            {
                "Object" => new UAObject(),
                "Variable" => new UAVariable(),
                "Method" => new UAMethod(),
                "View" => new UAView(),
                "ObjectType" => new UAObjectType(),
                "VariableType" => new UAVariableType(),
                "DataType" => new UADataType(),
                "ReferenceType" => new UAReferenceType(),
                _ => null
            };
            if (node is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    $"Unknown native nodeClass '{nodeClass}'.",
                    WotLocation.FromPointer(pointer)));
                return null;
            }

            node.NodeId = GetString(element, "nodeId");
            node.BrowseName = GetString(element, "browseName");
            node.SymbolicName = GetString(element, "symbolicName");
            node.DisplayName = ReadTexts(element, "displayName");
            node.Description = ReadTexts(element, "description");
            node.Category = ReadStrings(element, "category");
            node.Documentation = GetString(element, "documentation");
            node.References = ReadReferences(element);
            node.RolePermissions = ReadRolePermissions(element, "rolePermissions");
            node.Extensions = ReadXmlElements(
                element,
                "extensions",
                pointer + "/extensions",
                diagnostics);
            if (TryGetUInt32(element, "writeMask", out uint writeMask))
            {
                node.WriteMask = writeMask;
            }
            if (TryGetUInt32(element, "userWriteMask", out uint userWriteMask))
            {
                node.UserWriteMask = userWriteMask;
            }
            if (TryGetUInt16(element, "accessRestrictions", out ushort accessRestrictions))
            {
                node.AccessRestrictions = accessRestrictions;
                node.AccessRestrictionsSpecified = true;
            }
            node.HasNoPermissions = GetBoolean(element, "hasNoPermissions");
            string? releaseStatus = GetString(element, "releaseStatus");
            if (releaseStatus is not null &&
                Enum.TryParse(releaseStatus, ignoreCase: false, out ReleaseStatus status))
            {
                node.ReleaseStatus = status;
            }

            switch (node)
            {
                case UAVariable variable:
                    ReadInstance(element, variable);
                    ReadVariable(element, variable, pointer, diagnostics);
                    break;
                case UAVariableType variableType:
                    ReadType(element, variableType);
                    ReadVariableType(element, variableType, pointer, diagnostics);
                    break;
                case UAObject uaObject:
                    ReadInstance(element, uaObject);
                    if (TryGetByte(element, "eventNotifier", out byte objectNotifier))
                    {
                        uaObject.EventNotifier = objectNotifier;
                    }
                    break;
                case UAMethod method:
                    ReadInstance(element, method);
                    ReadMethod(element, method);
                    break;
                case UAView view:
                    ReadInstance(element, view);
                    view.ContainsNoLoops = GetBoolean(element, "containsNoLoops");
                    if (TryGetByte(element, "eventNotifier", out byte viewNotifier))
                    {
                        view.EventNotifier = viewNotifier;
                    }
                    break;
                case UADataType dataType:
                    ReadType(element, dataType);
                    ReadDataType(element, dataType, pointer, diagnostics);
                    break;
                case UAReferenceType referenceType:
                    ReadType(element, referenceType);
                    referenceType.InverseName = ReadTexts(element, "inverseName");
                    referenceType.Symmetric = GetBoolean(element, "symmetric");
                    break;
                case UAObjectType objectType:
                    ReadType(element, objectType);
                    break;
            }

            if (string.IsNullOrEmpty(node.NodeId) || string.IsNullOrEmpty(node.BrowseName))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    "A native node requires nodeId and browseName.",
                    WotLocation.FromPointer(pointer)));
            }
            return node;
        }

        private static void WriteInstance(Utf8JsonWriter writer, UAInstance instance)
        {
            WriteString(writer, "parentNodeId", instance.ParentNodeId);
            if (instance.DesignToolOnly)
            {
                writer.WriteBoolean("designToolOnly", true);
            }
        }

        private static void ReadInstance(JsonElement element, UAInstance instance)
        {
            instance.ParentNodeId = GetString(element, "parentNodeId");
            instance.DesignToolOnly = GetBoolean(element, "designToolOnly");
        }

        private static void WriteType(Utf8JsonWriter writer, UAType type)
        {
            if (type.IsAbstract)
            {
                writer.WriteBoolean("isAbstract", true);
            }
        }

        private static void ReadType(JsonElement element, UAType type)
        {
            type.IsAbstract = GetBoolean(element, "isAbstract");
        }

        private static void WriteVariable(Utf8JsonWriter writer, UAVariable variable)
        {
            WriteXmlElement(writer, "valueXml", variable.Value);
            WriteTranslations(writer, variable.Translation);
            WriteString(writer, "dataType", variable.DataType);
            if (variable.ValueRank != -1)
            {
                writer.WriteNumber("valueRank", variable.ValueRank);
            }
            WriteString(writer, "arrayDimensions", variable.ArrayDimensions);
            if (variable.AccessLevel != 1)
            {
                writer.WriteNumber("accessLevel", variable.AccessLevel);
            }
            if (variable.UserAccessLevel != 1)
            {
                writer.WriteNumber("userAccessLevel", variable.UserAccessLevel);
            }
            if (variable.MinimumSamplingInterval != 0D)
            {
                writer.WriteNumber("minimumSamplingInterval", variable.MinimumSamplingInterval);
            }
            if (variable.Historizing)
            {
                writer.WriteBoolean("historizing", true);
            }
        }

        private static void ReadVariable(
            JsonElement element,
            UAVariable variable,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            variable.Value = ReadXmlElement(
                element,
                "valueXml",
                pointer + "/valueXml",
                diagnostics);
            variable.Translation = ReadTranslations(element);
            variable.DataType = GetString(element, "dataType") ?? WotVocabulary.BaseDataType;
            if (TryGetInt32(element, "valueRank", out int valueRank))
            {
                variable.ValueRank = valueRank;
            }
            variable.ArrayDimensions = GetString(element, "arrayDimensions") ?? string.Empty;
            if (TryGetUInt32(element, "accessLevel", out uint accessLevel))
            {
                variable.AccessLevel = accessLevel;
            }
            if (TryGetUInt32(element, "userAccessLevel", out uint userAccessLevel))
            {
                variable.UserAccessLevel = userAccessLevel;
            }
            if (TryGetDouble(
                    element,
                    "minimumSamplingInterval",
                    out double minimumSamplingInterval))
            {
                variable.MinimumSamplingInterval = minimumSamplingInterval;
            }
            variable.Historizing = GetBoolean(element, "historizing");
        }

        private static void WriteVariableType(
            Utf8JsonWriter writer,
            UAVariableType variableType)
        {
            WriteXmlElement(writer, "valueXml", variableType.Value);
            WriteString(writer, "dataType", variableType.DataType);
            if (variableType.ValueRank != -1)
            {
                writer.WriteNumber("valueRank", variableType.ValueRank);
            }
            WriteString(writer, "arrayDimensions", variableType.ArrayDimensions);
        }

        private static void ReadVariableType(
            JsonElement element,
            UAVariableType variableType,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            variableType.Value = ReadXmlElement(
                element,
                "valueXml",
                pointer + "/valueXml",
                diagnostics);
            variableType.DataType =
                GetString(element, "dataType") ?? WotVocabulary.BaseDataType;
            if (TryGetInt32(element, "valueRank", out int valueRank))
            {
                variableType.ValueRank = valueRank;
            }
            variableType.ArrayDimensions =
                GetString(element, "arrayDimensions") ?? string.Empty;
        }

        private static void WriteMethod(Utf8JsonWriter writer, UAMethod method)
        {
            if (method.ArgumentDescription is { Length: > 0 })
            {
                writer.WritePropertyName("argumentDescriptions");
                writer.WriteStartArray();
                foreach (UAMethodArgument argument in method.ArgumentDescription)
                {
                    writer.WriteStartObject();
                    WriteString(writer, "name", argument.Name);
                    WriteTexts(writer, "description", argument.Description);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            if (!method.Executable)
            {
                writer.WriteBoolean("executable", false);
            }
            if (!method.UserExecutable)
            {
                writer.WriteBoolean("userExecutable", false);
            }
            WriteString(writer, "methodDeclarationId", method.MethodDeclarationId);
        }

        private static void ReadMethod(JsonElement element, UAMethod method)
        {
            if (element.TryGetProperty(
                    "argumentDescriptions",
                    out JsonElement descriptions) &&
                descriptions.ValueKind == JsonValueKind.Array)
            {
                var arguments = new List<UAMethodArgument>();
                foreach (JsonElement argument in descriptions.EnumerateArray())
                {
                    arguments.Add(new UAMethodArgument
                    {
                        Name = GetString(argument, "name"),
                        Description = ReadTexts(argument, "description")
                    });
                }
                method.ArgumentDescription = [.. arguments];
            }
            if (element.TryGetProperty("executable", out JsonElement executable) &&
                executable.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                method.Executable = executable.GetBoolean();
            }
            if (element.TryGetProperty(
                    "userExecutable",
                    out JsonElement userExecutable) &&
                userExecutable.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                method.UserExecutable = userExecutable.GetBoolean();
            }
            method.MethodDeclarationId = GetString(element, "methodDeclarationId");
        }

        private static void WriteDataType(Utf8JsonWriter writer, UADataType dataType)
        {
            if (dataType.Definition is not null)
            {
                writer.WritePropertyName("definition");
                WriteDefinition(writer, dataType.Definition);
            }
            if (dataType.Purpose != DataTypePurpose.Normal)
            {
                writer.WriteString("purpose", dataType.Purpose.ToString());
            }
        }

        private static void ReadDataType(
            JsonElement element,
            UADataType dataType,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (element.TryGetProperty("definition", out JsonElement definition) &&
                definition.ValueKind == JsonValueKind.Object)
            {
                dataType.Definition = ReadDefinition(
                    definition,
                    pointer + "/definition",
                    diagnostics);
            }
            string? purpose = GetString(element, "purpose");
            if (purpose is not null &&
                Enum.TryParse(purpose, ignoreCase: false, out DataTypePurpose parsed))
            {
                dataType.Purpose = parsed;
            }
        }

        private static void WriteDefinition(
            Utf8JsonWriter writer,
            Opc.Ua.Export.DataTypeDefinition definition)
        {
            writer.WriteStartObject();
            WriteString(writer, "name", definition.Name);
            WriteString(writer, "symbolicName", definition.SymbolicName);
            if (definition.IsUnion)
            {
                writer.WriteBoolean("isUnion", true);
            }
            if (definition.IsOptionSet)
            {
                writer.WriteBoolean("isOptionSet", true);
            }
            WriteString(writer, "baseType", definition.BaseType);
            if (definition.Field is { Length: > 0 })
            {
                writer.WritePropertyName("fields");
                writer.WriteStartArray();
                foreach (Opc.Ua.Export.DataTypeField field in definition.Field)
                {
                    writer.WriteStartObject();
                    WriteString(writer, "name", field.Name);
                    WriteString(writer, "symbolicName", field.SymbolicName);
                    WriteTexts(writer, "displayName", field.DisplayName);
                    WriteTexts(writer, "description", field.Description);
                    WriteString(writer, "dataType", field.DataType);
                    if (field.ValueRank != -1)
                    {
                        writer.WriteNumber("valueRank", field.ValueRank);
                    }
                    WriteString(writer, "arrayDimensions", field.ArrayDimensions);
                    if (field.MaxStringLength != 0)
                    {
                        writer.WriteNumber("maxStringLength", field.MaxStringLength);
                    }
                    if (field.Value != -1)
                    {
                        writer.WriteNumber("value", field.Value);
                    }
                    if (field.IsOptional)
                    {
                        writer.WriteBoolean("isOptional", true);
                    }
                    if (field.AllowSubTypes)
                    {
                        writer.WriteBoolean("allowSubTypes", true);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        private static Opc.Ua.Export.DataTypeDefinition ReadDefinition(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            var definition = new Opc.Ua.Export.DataTypeDefinition
            {
                Name = GetString(element, "name"),
                SymbolicName = GetString(element, "symbolicName"),
                IsUnion = GetBoolean(element, "isUnion"),
                IsOptionSet = GetBoolean(element, "isOptionSet"),
                BaseType = GetString(element, "baseType") ?? string.Empty
            };
            if (element.TryGetProperty("fields", out JsonElement fields) &&
                fields.ValueKind == JsonValueKind.Array)
            {
                var result = new List<Opc.Ua.Export.DataTypeField>();
                int index = 0;
                foreach (JsonElement item in fields.EnumerateArray())
                {
                    var field = new Opc.Ua.Export.DataTypeField
                    {
                        Name = GetString(item, "name"),
                        SymbolicName = GetString(item, "symbolicName"),
                        DisplayName = ReadTexts(item, "displayName"),
                        Description = ReadTexts(item, "description"),
                        DataType = GetString(item, "dataType") ?? WotVocabulary.BaseDataType,
                        ArrayDimensions =
                            GetString(item, "arrayDimensions") ?? string.Empty,
                        IsOptional = GetBoolean(item, "isOptional"),
                        AllowSubTypes = GetBoolean(item, "allowSubTypes")
                    };
                    if (TryGetInt32(item, "valueRank", out int valueRank))
                    {
                        field.ValueRank = valueRank;
                    }
                    if (TryGetUInt32(item, "maxStringLength", out uint maxStringLength))
                    {
                        field.MaxStringLength = maxStringLength;
                    }
                    if (TryGetInt32(item, "value", out int value))
                    {
                        field.Value = value;
                    }
                    if (string.IsNullOrEmpty(field.Name))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.NativeProjectionInvalid,
                            "A DataType definition field is missing name.",
                            WotLocation.FromPointer(
                                pointer + "/fields/" +
                                index.ToString(CultureInfo.InvariantCulture))));
                    }
                    result.Add(field);
                    index++;
                }
                definition.Field = [.. result];
            }
            if (string.IsNullOrEmpty(definition.Name))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    "A DataType definition is missing name.",
                    WotLocation.FromPointer(pointer)));
            }
            return definition;
        }

        private static void WriteTranslations(
            Utf8JsonWriter writer,
            TranslationType[]? translations)
        {
            if (translations is null || translations.Length == 0)
            {
                return;
            }
            writer.WritePropertyName("translations");
            writer.WriteStartArray();
            foreach (TranslationType translation in translations)
            {
                writer.WriteStartObject();
                if (translation.Items is { Length: > 0 })
                {
                    writer.WritePropertyName("items");
                    writer.WriteStartArray();
                    foreach (object item in translation.Items)
                    {
                        writer.WriteStartObject();
                        switch (item)
                        {
                            case Opc.Ua.Export.LocalizedText text:
                                writer.WriteString("kind", "text");
                                WriteText(writer, text);
                                break;
                            case StructureTranslationType field:
                                writer.WriteString("kind", "field");
                                WriteString(writer, "name", field.Name);
                                WriteTexts(writer, "text", field.Text);
                                break;
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static TranslationType[]? ReadTranslations(JsonElement element)
        {
            if (!element.TryGetProperty("translations", out JsonElement translations) ||
                translations.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<TranslationType>();
            foreach (JsonElement translation in translations.EnumerateArray())
            {
                var items = new List<object>();
                if (translation.TryGetProperty("items", out JsonElement array) &&
                    array.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        string? kind = GetString(item, "kind");
                        if (string.Equals(kind, "text", StringComparison.Ordinal))
                        {
                            items.Add(ReadText(item));
                        }
                        else if (string.Equals(kind, "field", StringComparison.Ordinal))
                        {
                            items.Add(new StructureTranslationType
                            {
                                Name = GetString(item, "name"),
                                Text = ReadTexts(item, "text")
                            });
                        }
                    }
                }
                result.Add(new TranslationType { Items = [.. items] });
            }
            return [.. result];
        }

        private static void WriteDerivedFacts(Utf8JsonWriter writer, UANode node)
        {
            if (node.References is null)
            {
                return;
            }
            foreach (Reference reference in node.References)
            {
                if (reference.IsForward &&
                    IsReference(reference.ReferenceType, "HasTypeDefinition", "i=40"))
                {
                    WriteString(writer, "typeDefinition", reference.Value);
                }
                else if (!reference.IsForward &&
                    IsReference(reference.ReferenceType, "HasSubtype", "i=45"))
                {
                    WriteString(writer, "superType", reference.Value);
                }
                else if (reference.IsForward &&
                    IsReference(reference.ReferenceType, "HasModellingRule", "i=37") &&
                    reference.Value is not null &&
                    WotVocabulary.TryGetModellingRuleName(
                        reference.Value,
                        out string modellingRule))
                {
                    writer.WriteString("modellingRule", modellingRule);
                }
            }
        }

        private static bool IsReference(string? value, string name, string nodeId)
        {
            return string.Equals(value, name, StringComparison.Ordinal) ||
                string.Equals(value, nodeId, StringComparison.Ordinal);
        }

        private static void WriteReferences(
            Utf8JsonWriter writer,
            Reference[]? references)
        {
            if (references is null || references.Length == 0)
            {
                return;
            }
            writer.WritePropertyName("references");
            writer.WriteStartArray();
            foreach (Reference reference in references)
            {
                writer.WriteStartObject();
                WriteString(writer, "referenceType", reference.ReferenceType);
                if (!reference.IsForward)
                {
                    writer.WriteBoolean("isForward", false);
                }
                WriteString(writer, "target", reference.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static Reference[]? ReadReferences(JsonElement element)
        {
            if (!element.TryGetProperty("references", out JsonElement references) ||
                references.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<Reference>();
            foreach (JsonElement reference in references.EnumerateArray())
            {
                result.Add(new Reference
                {
                    ReferenceType = GetString(reference, "referenceType"),
                    IsForward = !reference.TryGetProperty(
                        "isForward",
                        out JsonElement isForward) ||
                        isForward.ValueKind != JsonValueKind.False,
                    Value = GetString(reference, "target")
                });
            }
            return [.. result];
        }

        private static void WriteRolePermissions(
            Utf8JsonWriter writer,
            RolePermission[]? rolePermissions)
        {
            if (rolePermissions is null || rolePermissions.Length == 0)
            {
                return;
            }
            writer.WritePropertyName("rolePermissions");
            writer.WriteStartArray();
            foreach (RolePermission permission in rolePermissions)
            {
                writer.WriteStartObject();
                if (permission.Permissions != 0)
                {
                    writer.WriteNumber("permissions", permission.Permissions);
                }
                WriteString(writer, "roleId", permission.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static RolePermission[]? ReadRolePermissions(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(name, out JsonElement permissions) ||
                permissions.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<RolePermission>();
            foreach (JsonElement permission in permissions.EnumerateArray())
            {
                var rolePermission = new RolePermission
                {
                    Value = GetString(permission, "roleId")
                };
                if (TryGetUInt32(permission, "permissions", out uint value))
                {
                    rolePermission.Permissions = value;
                }
                result.Add(rolePermission);
            }
            return [.. result];
        }

        private static void WriteTexts(
            Utf8JsonWriter writer,
            string name,
            Opc.Ua.Export.LocalizedText[]? texts)
        {
            if (texts is null || texts.Length == 0)
            {
                return;
            }
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (Opc.Ua.Export.LocalizedText text in texts)
            {
                writer.WriteStartObject();
                WriteText(writer, text);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteText(
            Utf8JsonWriter writer,
            Opc.Ua.Export.LocalizedText text)
        {
            WriteString(writer, "locale", text.Locale);
            WriteString(writer, "value", text.Value);
        }

        private static Opc.Ua.Export.LocalizedText[]? ReadTexts(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(name, out JsonElement texts) ||
                texts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<Opc.Ua.Export.LocalizedText>();
            foreach (JsonElement text in texts.EnumerateArray())
            {
                result.Add(ReadText(text));
            }
            return [.. result];
        }

        private static Opc.Ua.Export.LocalizedText ReadText(JsonElement element)
        {
            return new Opc.Ua.Export.LocalizedText
            {
                Locale = GetString(element, "locale") ?? string.Empty,
                Value = GetString(element, "value")
            };
        }

        private static void WriteXmlElements(
            Utf8JsonWriter writer,
            string name,
            System.Xml.XmlElement[]? elements)
        {
            if (elements is null || elements.Length == 0)
            {
                return;
            }
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (System.Xml.XmlElement element in elements)
            {
                writer.WriteStringValue(element.OuterXml);
            }
            writer.WriteEndArray();
        }

        private static System.Xml.XmlElement[]? ReadXmlElements(
            JsonElement element,
            string name,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(name, out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<System.Xml.XmlElement>();
            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    System.Xml.XmlElement? parsed = ParseXml(
                        item.GetString(),
                        pointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                        diagnostics);
                    if (parsed is not null)
                    {
                        result.Add(parsed);
                    }
                }
                index++;
            }
            return [.. result];
        }

        private static void WriteXmlElement(
            Utf8JsonWriter writer,
            string name,
            System.Xml.XmlElement? element)
        {
            if (element is not null)
            {
                writer.WriteString(name, element.OuterXml);
            }
        }

        private static System.Xml.XmlElement? ReadXmlElement(
            JsonElement element,
            string name,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            return ParseXml(GetString(element, name), pointer, diagnostics);
        }

        private static System.Xml.XmlElement? ParseXml(
            string? xml,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                using var reader = XmlReader.Create(
                    new StringReader(xml),
                    CoreUtils.DefaultXmlReaderSettings());
                document.Load(reader);
                return document.DocumentElement;
            }
            catch (XmlException ex)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionInvalid,
                    $"The native XML fragment is malformed: {ex.Message}",
                    WotLocation.FromPointer(pointer)));
                return null;
            }
        }

        private static void WriteStrings(
            Utf8JsonWriter writer,
            string name,
            string[]? values)
        {
            if (values is null || values.Length == 0)
            {
                return;
            }
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (string value in values)
            {
                writer.WriteStringValue(value);
            }
            writer.WriteEndArray();
        }

        private static string[]? ReadStrings(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var result = new List<string>();
            foreach (JsonElement value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    result.Add(value.GetString()!);
                }
            }
            return [.. result];
        }

        private static void WriteString(
            Utf8JsonWriter writer,
            string name,
            string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                writer.WriteString(name, value);
            }
        }

        private static string? GetString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool GetBoolean(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.True;
        }

        private static bool TryGetByte(
            JsonElement element,
            string name,
            out byte value)
        {
            value = default;
            return element.TryGetProperty(name, out JsonElement number) &&
                number.ValueKind == JsonValueKind.Number &&
                number.TryGetByte(out value);
        }

        private static bool TryGetUInt16(
            JsonElement element,
            string name,
            out ushort value)
        {
            value = default;
            return element.TryGetProperty(name, out JsonElement number) &&
                number.ValueKind == JsonValueKind.Number &&
                number.TryGetUInt16(out value);
        }

        private static bool TryGetUInt32(
            JsonElement element,
            string name,
            out uint value)
        {
            value = default;
            return element.TryGetProperty(name, out JsonElement number) &&
                number.ValueKind == JsonValueKind.Number &&
                number.TryGetUInt32(out value);
        }

        private static bool TryGetInt32(
            JsonElement element,
            string name,
            out int value)
        {
            value = default;
            return element.TryGetProperty(name, out JsonElement number) &&
                number.ValueKind == JsonValueKind.Number &&
                number.TryGetInt32(out value);
        }

        private static bool TryGetDouble(
            JsonElement element,
            string name,
            out double value)
        {
            value = default;
            return element.TryGetProperty(name, out JsonElement number) &&
                number.ValueKind == JsonValueKind.Number &&
                number.TryGetDouble(out value);
        }

        private static bool TryGetDate(
            JsonElement element,
            string name,
            out DateTime value)
        {
            value = default;
            string? text = GetString(element, name);
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            try
            {
                value = XmlConvert.ToDateTime(text, XmlDateTimeSerializationMode.RoundtripKind);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static string FormatDate(DateTime value)
        {
            return XmlConvert.ToString(value, XmlDateTimeSerializationMode.RoundtripKind);
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

        private static int CountErrors(List<WotDiagnostic> diagnostics)
        {
            int count = 0;
            foreach (WotDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == WotDiagnosticSeverity.Error)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
