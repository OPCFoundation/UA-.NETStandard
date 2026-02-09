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

using Opc.Ua.Types;
using Opc.Ua.Export;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Schema.Model
{
    /// <summary>
    /// Node set reader settings
    /// </summary>
    public class NodeSetReaderSettings
    {
        /// <summary>
        /// Create node set reader settings
        /// </summary>
        public NodeSetReaderSettings()
        {
            NamespaceUris = new NamespaceTable();
            DesignFilePaths = new Dictionary<string, string>();
            NodesByQName = new Dictionary<XmlQualifiedName, NodeDesign>();
            NodesById = new Dictionary<NodeId, NodeDesign>();
            NamespaceTables = new Dictionary<string, string[]>();
        }

        /// <summary>
        /// Namespace uris.
        /// </summary>
        public NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Design file paths by namespace uri.
        /// </summary>
        public IDictionary<string, string> DesignFilePaths { get; set; }

        /// <summary>
        /// Nodes by qualified name.
        /// </summary>
        public IDictionary<XmlQualifiedName, NodeDesign> NodesByQName { get; set; }

        /// <summary>
        /// Namespace tables by model uri.
        /// </summary>
        public IDictionary<string, string[]> NamespaceTables { get; set; }

        /// <summary>
        /// Nodes by node id.
        /// </summary>
        public IDictionary<NodeId, NodeDesign> NodesById { get; set; }
    }

    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    public class NodeSetToModelDesign
    {
        /// <summary>
        /// Create converter
        /// </summary>
        /// <param name="fileSystem"></param>
        /// <param name="filePath"></param>
        /// <param name="settings"></param>
        /// <param name="telemetry"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        public NodeSetToModelDesign(
            IFileSystem fileSystem,
            string filePath,
            NodeSetReaderSettings settings,
            ITelemetryContext telemetry)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
            m_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<NodeSetToModelDesign>();
            m_index = [];
            m_symbolicIds = [];

            using Stream istrm = m_fileSystem.OpenRead(filePath);
            m_nodeset = UANodeSet.Read(istrm);

            if (m_nodeset.NamespaceUris != null)
            {
                foreach (string ns in m_nodeset.NamespaceUris)
                {
                    m_settings.NamespaceUris.GetIndexOrAppend(ns);
                }
            }

            m_settings.NamespaceTables[m_nodeset.Models[0].ModelUri] =
                m_nodeset.NamespaceUris;

            if (m_nodeset.Aliases != null)
            {
                foreach (NodeIdAlias ii in m_nodeset.Aliases)
                {
                    m_aliases[ii.Alias] = ImportNodeId(ii.Value, false);
                    if (m_aliases[ii.Alias].IsNullNodeId)
                    {
                        throw new InvalidDataException(
                            $"Alias ({ii.Alias}) is not valid.");
                    }
                }
            }

            if (m_nodeset.Items != null)
            {
                foreach (UANode node in m_nodeset.Items)
                {
                    NodeId nodeId = ImportNodeId(node.NodeId, false);
                    if (nodeId.IsNullNodeId)
                    {
                        throw new InvalidDataException(
                            $"NodeId ({node.BrowseName}) is not valid.");
                    }
                    m_index.Add(nodeId, node);
                }
            }
        }

        /// <summary>
        /// Test whether the file is a node set file.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool IsNodeSet(IFileSystem fileSystem, string filePath)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }

            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            using TextReader reader = fileSystem.CreateTextReader(filePath);
            for (int ii = 0; ii < 40; ii++)
            {
                string line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }
                line = line.TrimStart();

                if (line.StartsWith("<?", StringComparison.Ordinal) ||
                    line.StartsWith("<!", StringComparison.Ordinal) ||
                    !line.StartsWith('<') ||
                    string.IsNullOrEmpty(line))
                {
                    continue;
                }

                string[] fields = line.Split();

                if (fields.Length == 0)
                {
                    break;
                }

                return fields[0].Contains("UANodeSet", StringComparison.Ordinal);
            }
            return false;
        }

        private static T Load<T>(IFileSystem fileSystem, string path)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit
            };

            using Stream stream = fileSystem.OpenRead(path);
            var serializer = new XmlSerializer(typeof(T));

            using var reader = XmlReader.Create(stream, settings);
            return (T)serializer.Deserialize(reader);
        }

        private static void CollectModels(ModelTableEntry model, List<ModelTableEntry> models)
        {
            if (model.RequiredModel != null)
            {
                foreach (ModelTableEntry dependency in model.RequiredModel)
                {
                    CollectModels(dependency, models);
                }
            }

            for (int ii = 0; ii < models.Count; ii++)
            {
                if (models[ii].ModelUri == model.ModelUri)
                {
                    if (model.PublicationDate > models[ii].PublicationDate)
                    {
                        models[ii] = model;
                    }

                    return;
                }
            }

            models.Add(model);
        }

        private static Namespace CreateNamespace(ModelTableEntry model)
        {
            Namespace ns;

            if (model.ModelUri.StartsWith(Namespaces.OpcUa, StringComparison.Ordinal))
            {
                ns = new Namespace
                {
                    Name = model.ModelUri[Namespaces.OpcUa.Length..]
                        .Replace("/", " ", StringComparison.Ordinal)
                        .Trim()
                        .Replace(" ", ".", StringComparison.Ordinal),
                    Value = model.ModelUri,
                    XmlNamespace = model.XmlSchemaUri,
                    PublicationDate = model.PublicationDate
                        .ToString("yyyy-MM-ddT00:00:00Z", CultureInfo.InvariantCulture),
                    Version = model.Version
                };
                ns.XmlPrefix = ns.Prefix = "Opc.Ua." + ns.Name;
                ns.Name = ns.Name
                    .Replace(".", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace(",", string.Empty, StringComparison.Ordinal)
                    .Replace(":", string.Empty, StringComparison.Ordinal);
            }
            else
            {
                ns = new Namespace
                {
                    Name = model.ModelUri
                        .Replace("http://", string.Empty, StringComparison.Ordinal)
                        .Replace("/", " ", StringComparison.Ordinal)
                        .Trim()
                        .Replace(" ", ".", StringComparison.Ordinal),
                    Value = model.ModelUri,
                    XmlNamespace = model.XmlSchemaUri,
                    PublicationDate = model.PublicationDate
                        .ToString("yyyy-MM-ddT00:00:00Z", CultureInfo.InvariantCulture),
                    Version = model.Version
                };
                ns.XmlPrefix = ns.Prefix = ns.Name;
                ns.Name = ns.Name
                    .Replace(".", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace(",", string.Empty, StringComparison.Ordinal)
                    .Replace(":", string.Empty, StringComparison.Ordinal);
            }

            if (ns.Value == Namespaces.OpcUa)
            {
                ns.Name = "OpcUa";
                ns.XmlPrefix = ns.Prefix = "Opc.Ua";
            }

            return ns;
        }

        /// <summary>
        /// Load namespaces
        /// </summary>
        /// <param name="fileSystem"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static List<Namespace> LoadNamespaces(IFileSystem fileSystem, string filePath)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }

            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            UANodeSet nodeset = Load<UANodeSet>(fileSystem, filePath);

            List<ModelTableEntry> models = [];

            if (nodeset.Models != null)
            {
                foreach (ModelTableEntry model in nodeset.Models)
                {
                    CollectModels(model, models);
                }
            }

            List<Namespace> namespaces = [];

            foreach (ModelTableEntry model in models)
            {
                Namespace ns = CreateNamespace(model);
                ModelTableEntry topLevelModel = nodeset.Models
                    .FirstOrDefault(x => x.ModelUri == model.ModelUri);

                if (topLevelModel != null)
                {
                    ns.FilePath = filePath;
                }

                namespaces.Add(ns);
            }

            return namespaces;
        }

        private static void ImportModel(IList<Namespace> namespaces, ModelTableEntry model)
        {
            Namespace ns = namespaces.FirstOrDefault(x => x.Value == model.ModelUri);
            if (ns != null)
            {
                return;
            }

            ns = CreateNamespace(model);
            namespaces.Add(ns);

            if (model.RequiredModel != null)
            {
                foreach (ModelTableEntry ii in model.RequiredModel)
                {
                    ImportModel(namespaces, ii);
                }
            }
        }

        private bool IsTypeOf(NodeId subTypeId, NodeId superTypeId)
        {
            if (!m_settings.NodesById.TryGetValue(subTypeId, out NodeDesign node1))
            {
                return false;
            }

            if (!m_settings.NodesById.TryGetValue(superTypeId, out NodeDesign node2))
            {
                return false;
            }

            if (node1 is TypeDesign td1 && node2 is TypeDesign td2)
            {
                XmlQualifiedName parentId = td1.BaseType;

                while (parentId != null)
                {
                    if (parentId == td2.SymbolicId)
                    {
                        return true;
                    }

                    if (!m_settings.NodesByQName.TryGetValue(parentId, out NodeDesign parent))
                    {
                        return false;
                    }

                    td1 = parent as TypeDesign;

                    if (td1?.BaseType == null)
                    {
                        return false;
                    }

                    parentId = td1.BaseType;
                }
            }

            return false;
        }

        private XmlQualifiedName ImportSymbolicName(UANode input)
        {
            XmlQualifiedName browseName = ImportQualifiedName(input.BrowseName);

            if (!string.IsNullOrEmpty(input.SymbolicName))
            {
                if (input.BrowseName.Contains(":<", StringComparison.Ordinal) &&
                    !input.SymbolicName.EndsWith("_Placeholder", StringComparison.Ordinal))
                {
                    return new XmlQualifiedName(input.SymbolicName + "_Placeholder", browseName.Namespace);
                }

                return new XmlQualifiedName(input.SymbolicName, browseName.Namespace);
            }

            return new XmlQualifiedName(ToSymbolicName(browseName.Name), browseName.Namespace);
        }

        private static LocalizedText ImportLocalizedText(Export.LocalizedText[] input)
        {
            if (input != null && input.Length > 0 && !string.IsNullOrWhiteSpace(input[0].Value))
            {
                return new LocalizedText { Value = input[0].Value, IsAutogenerated = false };
            }

            return null;
        }

        private ReferenceTypeDesign FindReferenceType(ExpandedNodeId targetId)
        {
            if (m_settings.NodesById.TryGetValue((NodeId)targetId, out NodeDesign design))
            {
                return design as ReferenceTypeDesign;
            }

            if (m_index.TryGetValue((NodeId)targetId, out UANode node) && node is UAReferenceType rt)
            {
                return ImportReferenceType(rt);
            }

            return null;
        }

        private T FindNode<T>(ExpandedNodeId targetId) where T : NodeDesign
        {
            if (targetId.IsNull)
            {
                return default;
            }

            if (m_settings.NodesById.TryGetValue((NodeId)targetId, out NodeDesign design))
            {
                return design as T;
            }

            if (m_index.TryGetValue((NodeId)targetId, out UANode node))
            {
                return ImportNode(node) as T;
            }

            return null;
        }

        private T FindSuperType<T>(UAType source) where T : TypeDesign
        {
            if (source.References != null)
            {
                foreach (Export.Reference reference in source.References)
                {
                    NodeId rtid = ImportNodeId(reference.ReferenceType);

                    if (rtid.IsNullNodeId || ReferenceTypeIds.HasSubtype != rtid || reference.IsForward)
                    {
                        continue;
                    }

                    NodeId targetId = ImportNodeId(reference.Value);

                    if (targetId.IsNullNodeId)
                    {
                        return default;
                    }

                    return FindNode<T>(targetId);
                }
            }

            return null;
        }

        private ReferenceTypeDesign ImportReferenceType(UAReferenceType input)
        {
            NodeId nodeId = ImportNodeId(input.NodeId);
            NodeDesign existing = null;

            if (nodeId.IsNullNodeId || m_settings.NodesById.TryGetValue(nodeId, out existing))
            {
                if (existing is ReferenceTypeDesign rt)
                {
                    return rt;
                }

                throw new InvalidDataException(
                    $"Node exists and it is not a ReferenceType: {existing.SymbolicId}'.");
            }

            var output = new ReferenceTypeDesign
            {
                SymbolicName = ImportSymbolicName(input),
                BrowseName = QualifiedName.Parse(input.BrowseName).Name,
                Description = ImportLocalizedText(input.Description),
                DisplayName = ImportLocalizedText(input.DisplayName),
                WriteAccess = input.WriteMask,
                IsAbstract = input.IsAbstract,
                InverseName = ImportLocalizedText(input.InverseName),
                Symmetric = input.Symmetric,
                SymmetricSpecified = true,
                ReleaseStatus = ImportReleaseStatus(input.ReleaseStatus),
                Category = ImportCategories(input.Category)
            };
            output.SymbolicId = output.SymbolicName;

            if (nodeId.TryGetIdentifier(out uint id))
            {
                output.NumericId = id;
                output.NumericIdSpecified = true;
            }

            foreach (Export.Reference ii in input.References)
            {
                ReferenceNode reference = ImportReference(ii);

                if (reference.ReferenceTypeId == ReferenceTypes.HasSubtype &&
                    reference.IsInverse)
                {
                    ReferenceTypeDesign superType = FindReferenceType(reference.TargetId);

                    if (superType != null)
                    {
                        output.BaseType = superType.SymbolicId;
                        output.BaseTypeNode = superType;
                    }

                    break;
                }
            }

            if (output.BaseType == null)
            {
                throw new InvalidDataException(
                    $"Could not find supertype for '{input.BrowseName}'.");
            }

            m_settings.NodesByQName[output.SymbolicId] = output;
            m_settings.NodesById[nodeId] = output;

            return output;
        }

        private static NodeDesign CreateNodeDesign(UANode input)
        {
            if (input is UAObjectType)
            {
                return new ObjectTypeDesign();
            }

            if (input is UAVariableType)
            {
                return new VariableTypeDesign();
            }

            if (input is UADataType)
            {
                return new DataTypeDesign();
            }

            if (input is UAReferenceType)
            {
                return new ReferenceTypeDesign();
            }

            if (input is UAObject)
            {
                return new ObjectDesign();
            }

            if (input is UAVariable)
            {
                return new VariableDesign();
            }

            if (input is UAMethod)
            {
                return new MethodDesign();
            }

            if (input is UAView)
            {
                return new ViewDesign();
            }

            throw new InvalidDataException(
                $"Object is not a valid NodeClass: '{input.BrowseName}/{input.GetType().Name}'.");
        }

        private void UpdateTypeDesign(UAType input, TypeDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            output.ClassName = output.SymbolicName.Name;
            output.IsAbstract = input.IsAbstract;

            foreach (Export.Reference ii in input.References)
            {
                ReferenceNode reference = ImportReference(ii);

                if (reference.ReferenceTypeId == ReferenceTypes.HasSubtype &&
                    reference.IsInverse)
                {
                    TypeDesign superType = FindNode<TypeDesign>(reference.TargetId);

                    if (superType != null)
                    {
                        output.BaseType = superType.SymbolicId;
                        output.BaseTypeNode = superType;
                    }

                    break;
                }
            }

            if (output.BaseType == null)
            {
                throw new InvalidDataException(
                    $"Could not find supertype for '{input.BrowseName}'.");
            }
        }

        private void UpdateVariableTypeDesign(UAVariableType input, VariableTypeDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            UpdateTypeDesign(input, output);

            output.DefaultValue = input.Value;
            output.ValueRankSpecified = true;
            output.ValueRank = ImportValueRank(input.ValueRank);
            output.ValueRankSpecified = true;
            output.ArrayDimensions = input.ArrayDimensions;

            if (input.Value != null)
            {
                XmlDecoder decoder = CreateDecoder(input.Value);
                output.DecodedValue = decoder.ReadVariantContents(out _);
                decoder.Close();
            }

            DataTypeDesign dataType = FindNode<DataTypeDesign>(ImportNodeId(input.DataType)) ??
                throw new InvalidDataException($"Could not find DataType Node for '{input.BrowseName}/{input.DataType}'.");

            output.DataType = dataType.SymbolicId;
            output.DataTypeNode = dataType;
        }

        private void UpdateObjectTypeDesign(UAObjectType input, ObjectTypeDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            UpdateTypeDesign(input, output);
        }

        private void UpdateReferenceTypeDesign(UAReferenceType input, ReferenceTypeDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            UpdateTypeDesign(input, output);

            output.InverseName = ImportLocalizedText(input.InverseName);
            output.Symmetric = input.Symmetric;
            output.SymmetricSpecified = true;

            if (!output.Symmetric && output.InverseName == null)
            {
                output.InverseName = new LocalizedText
                {
                    Value = output.BrowseName ?? output.SymbolicName.Name,
                    IsAutogenerated = false
                };
            }
        }

        private bool IsTypeOf(UAType subtype, NodeId superTypeId)
        {
            NodeId nodeId = ImportNodeId(subtype.NodeId);

            if (nodeId.IsNullNodeId)
            {
                return false;
            }

            if (nodeId == superTypeId)
            {
                return true;
            }

            TypeDesign parent = FindSuperType<TypeDesign>(subtype);

            while (parent != null)
            {
                var parentId = new NodeId(
                    parent.NumericId,
                    (ushort)m_settings.NamespaceUris.GetIndex(
                        parent.SymbolicId.Namespace));

                if (parentId == superTypeId)
                {
                    return true;
                }

                if (parent.BaseTypeNode == null)
                {
                    return false;
                }

                parent = parent.BaseTypeNode;
            }

            return false;
        }

        private static bool IsLetterOrDigit(char ch)
        {
            if (ch is >= '0' and <= '9')
            {
                return true;
            }

            if (ch is >= 'A' and <= 'Z')
            {
                return true;
            }

            if (ch is >= 'a' and <= 'z')
            {
                return true;
            }

            return false;
        }

        private void UpdateDataTypeDesign(UADataType input, DataTypeDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            UpdateTypeDesign(input, output);

            output.IsStructure = false;
            output.IsUnion = false;
            output.IsEnumeration = input.Definition?.IsOptionSet ?? false;
            output.IsOptionSet = input.Definition?.IsOptionSet ?? false;
            output.BasicDataType = output.DetermineBasicDataType();
            output.HasFields = false;
            output.Fields = null;
            output.HasEncodings = false;

            if (output.BasicDataType == BasicDataType.UserDefined &&
                input.Definition == null)
            {
                output.IsStructure = true;
            }

            if (input.Definition != null)
            {
                if (IsTypeOf(input, DataTypeIds.OptionSet))
                {
                    output.IsEnumeration = true;
                    output.IsStructure = false;
                    output.IsUnion = false;
                    output.IsOptionSet = true;
                }
                else if (IsTypeOf(input, DataTypeIds.Structure))
                {
                    output.IsStructure = true;
                    output.IsUnion = input.Definition.IsUnion;
                    output.IsEnumeration = false;
                    output.IsOptionSet = false;
                    output.HasEncodings = true;
                }
                else if (IsTypeOf(input, DataTypeIds.Enumeration))
                {
                    output.IsEnumeration = true;
                    output.IsStructure = false;
                    output.IsUnion = false;
                    output.IsOptionSet = false;
                }

                var fields = new List<Parameter>();

                if (input.Definition.Field != null)
                {
                    foreach (DataTypeField ii in input.Definition.Field)
                    {
                        string symbolicName = ii.SymbolicName;

                        if (string.IsNullOrEmpty(symbolicName))
                        {
                            symbolicName = ToSymbolicName(ii.Name);
                        }

                        var field = new Parameter
                        {
                            Name = symbolicName,
                            Description = ImportLocalizedText(ii.Description),
                            ArrayDimensions = ii.ArrayDimensions,
                            ValueRank = ImportValueRank(ii.ValueRank),
                            Parent = output
                        };

                        if (ii.Name != field.Name)
                        {
                            field.DisplayName = new LocalizedText { Value = ii.Name, IsAutogenerated = false };
                        }

                        DataTypeDesign dataType = FindNode<DataTypeDesign>(ImportNodeId(ii.DataType)) ??
                            throw new InvalidDataException($"Could not find DataType Node for Field '{input.BrowseName}/{ii.Name}/{ii.DataType}'.");

                        field.DataType = dataType.SymbolicId;
                        field.DataTypeNode = dataType;
                        field.AllowSubTypes = ii.AllowSubTypes;
                        field.IsOptional = ii.IsOptional;

                        if (output.IsOptionSet)
                        {
                            long mask = 1L << ii.Value;
                            field.BitMask = $"{mask:X8}";
                            field.Identifier = mask;
                            field.IdentifierSpecified = true;
                        }
                        else if (output.IsEnumeration)
                        {
                            field.BitMask = null;
                            field.Identifier = ii.Value;
                            field.IdentifierSpecified = true;
                        }

                        fields.Add(field);
                    }
                }

                output.HasFields = fields.Count > 0;
                output.Fields = [.. fields];
            }
        }

        private void UpdateInstanceDesign(UAInstance input, InstanceDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            output.ModellingRule = ModellingRule.None;
            output.ModellingRuleSpecified = false;

            foreach (Export.Reference ii in input.References)
            {
                ReferenceNode reference = ImportReference(ii);

                if (reference.ReferenceTypeId == ReferenceTypes.HasTypeDefinition && !reference.IsInverse)
                {
                    TypeDesign typeDefinition = FindNode<TypeDesign>(reference.TargetId);

                    if (typeDefinition != null)
                    {
                        output.TypeDefinition = typeDefinition.SymbolicId;
                        output.TypeDefinitionNode = typeDefinition;
                    }
                }

                if (reference.ReferenceTypeId == ReferenceTypes.HasModellingRule && !reference.IsInverse)
                {
                    output.ModellingRule = ImportModellingRule(reference.TargetId);
                    output.ModellingRuleSpecified = true;
                }
            }

            if (output.TypeDefinition == null)
            {
                throw new InvalidDataException($"Could not find TypeDefinition for '{input.BrowseName}'.");
            }
        }

        private void UpdateObjectDesign(UAObject input, ObjectDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            UpdateInstanceDesign(input, output);

            output.SupportsEvents = (input.EventNotifier & EventNotifiers.SubscribeToEvents) != 0;
            output.SupportsEventsSpecified = true;
        }

        private static void UpdateViewDesign(UAView input, ViewDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            output.SupportsEvents = (input.EventNotifier & EventNotifiers.SubscribeToEvents) != 0;
        }

        private void UpdateVariableDesign(UAVariable input, VariableDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            UpdateInstanceDesign(input, output);

            output.DefaultValue = input.Value;
            output.ValueRank = ImportValueRank(input.ValueRank);
            output.ValueRankSpecified = true;
            output.ArrayDimensions = input.ArrayDimensions;
            output.MinimumSamplingInterval = (int)input.MinimumSamplingInterval;
            output.MinimumSamplingIntervalSpecified = true;
            output.Historizing = input.Historizing;
            output.HistorizingSpecified = input.Historizing;
            output.AccessLevel = ImportAccessLevel(input.AccessLevel);
            output.AccessLevelSpecified = true;

            if (input.Value != null)
            {
                XmlDecoder decoder = CreateDecoder(input.Value);
                output.DecodedValue = decoder.ReadVariantContents(out _);
                decoder.Close();
            }

            DataTypeDesign dataType = FindNode<DataTypeDesign>(ImportNodeId(input.DataType)) ??
                throw new InvalidDataException($"Could not find DataType Node for '{input.BrowseName}/{input.DataType}'.");

            output.DataType = dataType.SymbolicId;
            output.DataTypeNode = dataType;
        }

        private List<Parameter> ImportArguments(MethodDesign method, string sourceNodeSetUri, XmlElement input)
        {
            var output = new List<Parameter>();

            if (input != null)
            {
                XmlDecoder decoder = CreateDecoder(input, sourceNodeSetUri);
                object value = decoder.ReadVariantContents(out _);
                decoder.Close();

                foreach (Argument argument in (IList<Argument>)ExtensionObject.ToArray(value, typeof(Argument)))
                {
                    DataTypeDesign dataType = FindNode<DataTypeDesign>(argument.DataType) ??
                        throw new InvalidDataException(
                            $"Method ({method.SymbolicId.Name}) argument ({argument.Name}) datatype ({argument.DataType}) not found.");

                    var parameter = new Parameter
                    {
                        Name = argument.Name,
                        ArrayDimensions = ImportArrayDimensions(argument.ArrayDimensions),
                        ValueRank = ImportValueRank(argument.ValueRank),
                        Parent = method,
                        DataType = dataType.SymbolicId,
                        DataTypeNode = dataType,
                        Description = null
                    };

                    if (!string.IsNullOrEmpty(argument.Description.Text))
                    {
                        parameter.Description = new LocalizedText
                        {
                            Value = argument.Description.Text,
                            IsAutogenerated = false
                        };
                    }

                    output.Add(parameter);
                }
            }

            return output;
        }

        private void UpdateMethodDesign(UAMethod input, MethodDesign output)
        {
            if (input == null || output == null)
            {
                return;
            }

            output.ModellingRule = ModellingRule.None;
            output.ModellingRuleSpecified = false;

            if (input.References != null)
            {
                foreach (Export.Reference ii in input.References)
                {
                    ReferenceNode reference = ImportReference(ii);

                    if (reference.ReferenceTypeId == ReferenceTypes.HasModellingRule && !reference.IsInverse)
                    {
                        output.ModellingRule = ImportModellingRule(reference.TargetId);
                        output.ModellingRuleSpecified = true;
                        break;
                    }
                }
            }

            output.NonExecutable = !input.Executable;
            output.NonExecutableSpecified = !input.Executable;

            if (input.MethodDeclarationId != null)
            {
                NodeId methodId = ImportNodeId(input.MethodDeclarationId);
                output.MethodDeclarationNode = FindNode<MethodDesign>(methodId);
            }

            if (input.References != null)
            {
                foreach (Export.Reference ii in input.References)
                {
                    ReferenceNode reference = ImportReference(ii);

                    if (reference.ReferenceTypeId == ReferenceTypes.HasProperty && !reference.IsInverse)
                    {
                        VariableDesign property = FindNode<VariableDesign>(reference.TargetId);

                        if (property != null && property.DefaultValue != null)
                        {
                            if (property.BrowseName == BrowseNames.InputArguments)
                            {
                                output.InputArguments =
                                    [.. ImportArguments(output, property.SymbolicId.Namespace, property.DefaultValue)];
                                output.HasArguments = true;
                            }
                            else if (property.BrowseName == BrowseNames.OutputArguments)
                            {
                                output.OutputArguments =
                                    [.. ImportArguments(output, property.SymbolicId.Namespace, property.DefaultValue)];
                                output.HasArguments = true;
                            }
                        }
                    }
                }
            }
        }

        private void LinkChildToParent(UAInstance input)
        {
            NodeId nodeId = ImportNodeId(input.NodeId);
            NodeId parentId = ImportNodeId(input.ParentNodeId);

            if (nodeId.NamespaceIndex != parentId.NamespaceIndex)
            {
                return;
            }

            NodeDesign referenceType = null;
            bool nonHierarchical = false;

            UANode parentNode = FindNode(m_nodeset, parentId) ??
                throw new InvalidDataException($"ParentNode ({input.ParentNodeId}) not found for node {input.NodeId} ({input.BrowseName}).");

            if (!m_settings.NodesById.TryGetValue(parentId, out NodeDesign parent))
            {
                parent = ImportNode(parentNode);
            }

            if (parentNode.References != null)
            {
                foreach (Export.Reference reference in parentNode.References)
                {
                    if (reference.Value == input.NodeId && reference.IsForward)
                    {
                        NodeId referenceTypeId = ImportNodeId(reference.ReferenceType);

                        if (!IsTypeOf(referenceTypeId, ReferenceTypeIds.HierarchicalReferences))
                        {
                            continue;
                        }

                        referenceType = FindReferenceType(referenceTypeId);
                    }
                }

                // check for non-hierarchical.
                if (referenceType == null)
                {
                    foreach (Export.Reference reference in input.References)
                    {
                        if (reference.Value == input.NodeId && reference.IsForward)
                        {
                            NodeId referenceTypeId = ImportNodeId(reference.ReferenceType);
                            referenceType = FindReferenceType(referenceTypeId);
                        }
                    }

                    nonHierarchical = referenceType != null;
                }
            }

            if (referenceType == null && input.References != null)
            {
                foreach (Export.Reference reference in input.References)
                {
                    if (reference.Value == input.ParentNodeId && !reference.IsForward)
                    {
                        NodeId referenceTypeId = ImportNodeId(reference.ReferenceType);

                        if (!IsTypeOf(referenceTypeId, ReferenceTypeIds.HierarchicalReferences))
                        {
                            continue;
                        }

                        referenceType = FindReferenceType(referenceTypeId);
                    }
                }

                // check for non-hierarchical.
                if (referenceType == null)
                {
                    foreach (Export.Reference reference in input.References)
                    {
                        if (reference.Value == input.ParentNodeId && !reference.IsForward)
                        {
                            NodeId referenceTypeId = ImportNodeId(reference.ReferenceType);
                            referenceType = FindReferenceType(referenceTypeId);
                        }
                    }

                    nonHierarchical = referenceType != null;
                }
            }

            if (referenceType == null)
            {
                throw new InvalidDataException(
                    $"HierarchicalReference from ParentNode ({input.ParentNodeId}) to Node {input.NodeId} ({input.BrowseName}) not found.");
            }

            if (!nonHierarchical && m_settings.NodesById.TryGetValue(nodeId, out NodeDesign child))
            {
                LinkChildToParent(parent, referenceType.SymbolicId, nodeId, child as InstanceDesign);
            }
        }

        private void LinkChildToParent(
            NodeDesign parent,
            XmlQualifiedName referenceTypeId,
            NodeId childId,
            InstanceDesign child)
        {
            if (!child.SymbolicId.Name.StartsWith(parent.SymbolicId.Name, StringComparison.Ordinal))
            {
                child.SymbolicId = new XmlQualifiedName(
                    $"{parent.SymbolicId.Name}_{child.SymbolicId.Name}",
                    m_settings.NamespaceUris.GetString(childId.NamespaceIndex));
            }

            List<InstanceDesign> children = [];

            if (parent.Children?.Items != null)
            {
                children.AddRange(parent.Children?.Items);
            }

            child.Parent = parent;
            child.ReferenceType = referenceTypeId;
            children.Add(child);
            parent.Children = new ListOfChildren { Items = [.. children] };
            parent.HasChildren = true;
        }

        private NodeDesign ImportNode(UANode input)
        {
            NodeId nodeId = ImportNodeId(input.NodeId);

            if (m_settings.NodesById.TryGetValue(nodeId, out NodeDesign existing))
            {
                return existing;
            }

            NodeDesign output = CreateNodeDesign(input);

            output.SymbolicId = m_symbolicIds[input.NodeId];
            output.SymbolicName = ImportSymbolicName(input);
            output.Extensions = input.Extensions;

            if (input is UAType &&
                output.SymbolicId.Name.EndsWith(
                    "_" + nodeId.IdentifierAsString, StringComparison.Ordinal))
            {
                output.SymbolicName = new XmlQualifiedName(
                    $"{output.SymbolicName.Name}_{nodeId.IdentifierAsString}",
                    output.SymbolicName.Namespace);
            }

            output.BrowseName = QualifiedName.Parse(input.BrowseName).Name;
            output.Description = ImportLocalizedText(input.Description);
            output.DisplayName = ImportLocalizedText(input.DisplayName);
            output.WriteAccess = input.WriteMask;
            output.ReleaseStatus = ImportReleaseStatus(input.ReleaseStatus);
            output.Category = ImportCategories(input.Category);

            if (nodeId.TryGetIdentifier(out uint id))
            {
                output.NumericId = id;
                output.NumericIdSpecified = true;
            }
            else if (nodeId.TryGetIdentifier(out string stringId))
            {
                output.StringId = stringId;
                output.NumericIdSpecified = false;
            }
            else if (nodeId.IsNullNodeId)
            {
                m_logger.LogInformation("NodeId is not specified.");
            }
            else
            {
                m_logger.LogInformation("NodeId {NodeId} is not supported.", nodeId);
            }

            m_settings.NodesByQName[output.SymbolicId] = output;
            m_settings.NodesById[nodeId] = output;

            switch (output)
            {
                case ObjectTypeDesign ot:
                    UpdateObjectTypeDesign(input as UAObjectType, ot);
                    break;
                case VariableTypeDesign vt:
                    UpdateVariableTypeDesign(input as UAVariableType, vt);
                    break;
                case ReferenceTypeDesign rt:
                    UpdateReferenceTypeDesign(input as UAReferenceType, rt);
                    break;
                case DataTypeDesign dt:
                    UpdateDataTypeDesign(input as UADataType, dt);
                    break;
                case ObjectDesign oi:
                    UpdateObjectDesign(input as UAObject, oi);
                    break;
                case VariableDesign vi:
                    UpdateVariableDesign(input as UAVariable, vi);
                    break;
                case MethodDesign mi:
                    UpdateMethodDesign(input as UAMethod, mi);
                    break;
                case ViewDesign wi:
                    UpdateViewDesign(input as UAView, wi);
                    break;
            }

            return output;
        }

        private UANode FindNode(UANodeSet nodeset, ExpandedNodeId targetId)
        {
            if (targetId.IsNull)
            {
                return null;
            }

            foreach (UANode ii in nodeset.Items)
            {
                NodeId id = ImportNodeId(ii.NodeId, true);

                if (id == targetId)
                {
                    return ii;
                }
            }

            return null;
        }

        private NodeId FindTarget(
            UANode source,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId = default)
        {
            if (source.References == null)
            {
                return default;
            }

            foreach (Export.Reference ii in source.References)
            {
                ReferenceNode reference = ImportReference(ii);

                if (reference.ReferenceTypeId == referenceTypeId && reference.IsInverse == isInverse)
                {
                    if (!targetId.IsNull && reference.TargetId != targetId)
                    {
                        continue;
                    }

                    return ExpandedNodeId.ToNodeId(reference.TargetId, m_settings.NamespaceUris);
                }
            }

            return default;
        }

        private XmlQualifiedName ImportAndFixSymbolicName(UANode input)
        {
            XmlQualifiedName name = ImportSymbolicName(input);

            NodeId typeDefinitionId = FindTarget(input, ReferenceTypes.HasTypeDefinition, false);

            if (typeDefinitionId == ObjectTypeIds.DataTypeEncodingType)
            {
                if (input.SymbolicName.Contains("Default", StringComparison.Ordinal) &&
                    input.SymbolicName.Contains("XML", StringComparison.OrdinalIgnoreCase) &&
                    input.SymbolicName != "DefaultXml")
                {
                    throw new InvalidDataException(
                        $"{input.SymbolicName} is not a valid symbolic name for a DataTypeEncoding node (should be 'DefaultXml').");
                }

                NodeId dataTypeId = FindTarget(input, ReferenceTypes.HasEncoding, true);

                if (dataTypeId.IsNullNodeId)
                {
                    NodeId encodingId = ImportNodeId(input.NodeId);

                    foreach (UANode dataType in m_nodeset.Items.Where(x => x is UADataType))
                    {
                        NodeId result = FindTarget(dataType, ReferenceTypes.HasEncoding, false, encodingId);

                        if (!result.IsNullNodeId)
                        {
                            dataTypeId = ImportNodeId(dataType.NodeId);

                            return new XmlQualifiedName(
                                $"{ImportSymbolicName(dataType).Name}_Encoding_{name.Name}",
                                m_settings.NamespaceUris.GetString(dataTypeId.NamespaceIndex));
                        }
                    }
                }
                else
                {
                    UANode dataType = FindNode(m_nodeset, dataTypeId);

                    if (dataType != null)
                    {
                        return new XmlQualifiedName(
                            $"{ImportSymbolicName(dataType).Name}_Encoding_{name.Name}",
                            m_settings.NamespaceUris.GetString(dataTypeId.NamespaceIndex));
                    }
                }
            }

            return name;
        }

        private void ImportReferences(UANode input)
        {
            NodeId nodeId = ImportNodeId(input.NodeId);

            if (!m_settings.NodesById.TryGetValue(nodeId, out NodeDesign existing))
            {
                return;
            }

            var references = new List<Reference>();

            if (existing.References != null)
            {
                references.AddRange(existing.References);
            }

            if (input.References != null)
            {
                foreach (Export.Reference ii in input.References)
                {
                    NodeId referenceTypeId = ImportNodeId(ii.ReferenceType);
                    NodeDesign referenceType = FindNode<NodeDesign>(referenceTypeId);

                    if (referenceType == null)
                    {
                        continue;
                    }

                    NodeId targetId = ImportNodeId(ii.Value);
                    NodeDesign target = FindNode<NodeDesign>(targetId);

                    if (target == null)
                    {
                        continue;
                    }

                    if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition ||
                        referenceTypeId == ReferenceTypeIds.HasSubtype ||
                        referenceTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        continue;
                    }

                    if (targetId.NamespaceIndex != nodeId.NamespaceIndex ||
                        IsTypeOf(referenceTypeId, ReferenceTypeIds.NonHierarchicalReferences))
                    {
                        references.Add(new Reference
                        {
                            ReferenceType = referenceType.SymbolicId,
                            IsInverse = !ii.IsForward,
                            TargetId = target.SymbolicId,
                            TargetNode = target,
                            SourceNode = existing
                        });

                        continue;
                    }

                    if (!ii.IsForward)
                    {
                        bool found = false;

                        if (target.Children?.Items != null)
                        {
                            foreach (InstanceDesign child in target.Children.Items)
                            {
                                if (existing.SymbolicId == child.SymbolicId)
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (!found)
                        {
                            references.Add(new Reference
                            {
                                ReferenceType = referenceType.SymbolicId,
                                IsInverse = !ii.IsForward,
                                TargetId = target.SymbolicId,
                                TargetNode = target,
                                SourceNode = existing
                            });
                        }
                    }
                }
            }

            if (references.Count > 0)
            {
                existing.HasReferences = true;
                existing.References = [.. references];
            }
        }

        private static Permissions[] ToPermissions(PermissionType input)
        {
            var permissions = new List<Permissions>();

            if ((input & PermissionType.Browse) != 0)
            {
                permissions.Add(Permissions.Browse);
            }

            if ((input & PermissionType.ReadRolePermissions) != 0)
            {
                permissions.Add(Permissions.ReadRolePermissions);
            }

            if ((input & PermissionType.WriteAttribute) != 0)
            {
                permissions.Add(Permissions.WriteAttribute);
            }

            if ((input & PermissionType.WriteRolePermissions) != 0)
            {
                permissions.Add(Permissions.WriteRolePermissions);
            }

            if ((input & PermissionType.WriteHistorizing) != 0)
            {
                permissions.Add(Permissions.WriteHistorizing);
            }

            if ((input & PermissionType.Read) != 0)
            {
                permissions.Add(Permissions.Read);
            }

            if ((input & PermissionType.Write) != 0)
            {
                permissions.Add(Permissions.Write);
            }

            if ((input & PermissionType.ReadHistory) != 0)
            {
                permissions.Add(Permissions.ReadHistory);
            }

            if ((input & PermissionType.InsertHistory) != 0)
            {
                permissions.Add(Permissions.InsertHistory);
            }

            if ((input & PermissionType.ModifyHistory) != 0)
            {
                permissions.Add(Permissions.ModifyHistory);
            }

            if ((input & PermissionType.DeleteHistory) != 0)
            {
                permissions.Add(Permissions.DeleteHistory);
            }

            if ((input & PermissionType.ReceiveEvents) != 0)
            {
                permissions.Add(Permissions.ReceiveEvents);
            }

            if ((input & PermissionType.Call) != 0)
            {
                permissions.Add(Permissions.Call);
            }

            if ((input & PermissionType.AddReference) != 0)
            {
                permissions.Add(Permissions.AddReference);
            }

            if ((input & PermissionType.RemoveReference) != 0)
            {
                permissions.Add(Permissions.RemoveReference);
            }

            if ((input & PermissionType.DeleteNode) != 0)
            {
                permissions.Add(Permissions.DeleteNode);
            }

            if ((input & PermissionType.AddNode) != 0)
            {
                permissions.Add(Permissions.AddNode);
            }

            return [.. permissions];
        }

        private RolePermissionSet ToPermissionSet(Export.RolePermission[] input)
        {
            if (input == null)
            {
                return null;
            }

            var permissions = new List<RolePermission>();

            foreach (Export.RolePermission ii in input)
            {
                NodeId roleId = ImportNodeId(ii.Value);
                NodeDesign role = FindNode<NodeDesign>(roleId);

                if (role == null)
                {
                    continue;
                }

                permissions.Add(new RolePermission
                {
                    Role = role.SymbolicId,
                    Permission = ToPermissions((PermissionType)ii.Permissions)
                });
            }

            if (permissions.Count == 0)
            {
                return null;
            }

            return new RolePermissionSet
            {
                RolePermission = [.. permissions]
            };
        }

        private static AccessRestrictions ToAccessRestrictions(AccessRestrictionType input)
        {
            if ((input & AccessRestrictionType.EncryptionRequired) != 0)
            {
                input &= ~AccessRestrictionType.SigningRequired;
            }

            switch (input)
            {
                case AccessRestrictionType.SigningRequired:
                    return AccessRestrictions.SigningRequired;
                case AccessRestrictionType.EncryptionRequired:
                    return AccessRestrictions.EncryptionRequired;
                case AccessRestrictionType.SessionRequired:
                    return AccessRestrictions.SessionRequired;
                case AccessRestrictionType.SigningRequired |
                    AccessRestrictionType.SessionRequired:
                    return AccessRestrictions.SessionWithSigningRequired;
                case AccessRestrictionType.EncryptionRequired |
                    AccessRestrictionType.SessionRequired:
                    return AccessRestrictions.SessionWithEncryptionRequired;
                case AccessRestrictionType.SigningRequired |
                    AccessRestrictionType.ApplyRestrictionsToBrowse:
                    return AccessRestrictions.SigningAndApplyToBrowseRequired;
                case AccessRestrictionType.EncryptionRequired |
                    AccessRestrictionType.ApplyRestrictionsToBrowse:
                    return AccessRestrictions.EncryptionAndApplyToBrowseRequired;
                case AccessRestrictionType.SessionRequired |
                    AccessRestrictionType.ApplyRestrictionsToBrowse:
                    return AccessRestrictions.SessionAndApplyToBrowseRequired;
                case AccessRestrictionType.SigningRequired |
                    AccessRestrictionType.SessionRequired |
                    AccessRestrictionType.ApplyRestrictionsToBrowse:
                    return AccessRestrictions.SessionWithSigningAndApplyToBrowseRequired;
                case AccessRestrictionType.EncryptionRequired |
                    AccessRestrictionType.SessionRequired |
                    AccessRestrictionType.ApplyRestrictionsToBrowse:
                    return AccessRestrictions.SessionWithEncryptionAndApplyToBrowseRequired;
            }

            return AccessRestrictions.EncryptionRequired;
        }

        private void ImportPermissions(UANode input)
        {
            NodeId nodeId = ImportNodeId(input.NodeId);

            if (!m_settings.NodesById.TryGetValue(nodeId, out NodeDesign existing))
            {
                return;
            }

            existing.RolePermissions = ToPermissionSet(input.RolePermissions);
            existing.AccessRestrictions = input.AccessRestrictionsSpecified ?
                ToAccessRestrictions((AccessRestrictionType)input.AccessRestrictions) : 0;
            existing.AccessRestrictionsSpecified = input.AccessRestrictionsSpecified;
        }

        private XmlQualifiedName BuildSymbolicId(UANode node)
        {
            NodeId nodeId = ImportNodeId(node.NodeId, false);

            if (node is not UAInstance instance)
            {
                return new XmlQualifiedName(
                    ImportAndFixSymbolicName(node).Name,
                    m_settings.NamespaceUris.GetString(nodeId.NamespaceIndex));
            }

            if (string.IsNullOrWhiteSpace(instance.ParentNodeId))
            {
                return new XmlQualifiedName(
                    ImportAndFixSymbolicName(instance).Name,
                    m_settings.NamespaceUris.GetString(nodeId.NamespaceIndex));
            }

            NodeId parentId = ImportNodeId(instance.ParentNodeId);

            if (parentId.NamespaceIndex != nodeId.NamespaceIndex)
            {
                return new XmlQualifiedName(
                    ImportAndFixSymbolicName(instance).Name,
                    m_settings.NamespaceUris.GetString(nodeId.NamespaceIndex));
            }

            if (parentId.IsNullNodeId || !m_index.TryGetValue(parentId, out UANode parent))
            {
                throw new InvalidDataException(
                    $"Parent node ({instance.ParentNodeId}) for {node.NodeId} not found.");
            }

            XmlQualifiedName symbolicId = BuildSymbolicId(parent);
            XmlQualifiedName symbolicName = ImportSymbolicName(node);

            if (nodeId.IsNullNodeId)
            {
                throw new InvalidDataException(
                    $"Node ({symbolicId.Name}) does not have a valid NodeId.");
            }

            return new XmlQualifiedName(
                $"{symbolicId.Name}_{symbolicName.Name}",
                m_settings.NamespaceUris.GetString(nodeId.NamespaceIndex));
        }

        /// <summary>
        /// Imports a node from the set.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public ModelDesign Import(string prefix, string name)
        {
            var dictionary = new ModelDesign();

            if (m_nodeset.Models == null || m_nodeset.Models.Length == 0)
            {
                throw new InvalidOperationException(
                    $"NodeSet ({m_nodeset.NamespaceUris[0]}) does not have any Models defined!");
            }

            if (m_nodeset.Models != null)
            {
                ModelTableEntry model = m_nodeset.Models[0];

                dictionary.TargetNamespace = model.ModelUri;
                dictionary.TargetXmlNamespace = model.XmlSchemaUri;
                dictionary.TargetPublicationDate = model.PublicationDate;
                dictionary.TargetPublicationDateSpecified = model.PublicationDateSpecified;
                dictionary.TargetVersion = model.Version;

                List<Namespace> namespaces = [];
                ImportModel(namespaces, model);
                dictionary.Namespaces = [.. namespaces];

                Namespace targetNamespace = namespaces[0];

                if (name != null)
                {
                    targetNamespace.Name = name;
                }

                if (prefix != null)
                {
                    targetNamespace.XmlPrefix = targetNamespace.Prefix = prefix;
                }
            }

            foreach (UANode node in m_nodeset.Items)
            {
                // hack to ensure DataTypeEncodings have right symbolic names.
                if (node is UAObject)
                {
                    if (string.IsNullOrEmpty(node.SymbolicName))
                    {
                        switch (node.BrowseName)
                        {
                            case BrowseNames.DefaultBinary:
                                node.SymbolicName = nameof(BrowseNames.DefaultBinary);
                                break;
                            case BrowseNames.DefaultXml:
                                node.SymbolicName = nameof(BrowseNames.DefaultXml);
                                break;
                            case BrowseNames.DefaultJson:
                                node.SymbolicName = nameof(BrowseNames.DefaultJson);
                                break;
                        }
                    }
                    else if (node.SymbolicName == "DefaultXML")
                    {
                        node.SymbolicName = nameof(BrowseNames.DefaultXml);
                    }
                }

                if (node is UAInstance instance)
                {
                    // ensure parents are in the same namespace.
                    if (instance.ParentNodeId != null)
                    {
                        NodeId parentId = ImportNodeId(instance.ParentNodeId);
                        NodeId childId = ImportNodeId(instance.NodeId);

                        if (parentId.NamespaceIndex != childId.NamespaceIndex)
                        {
                            instance.ParentNodeId = null;
                        }
                    }

                    // handle missing ParentNodeId when an inverse reference exists.
                    else
                    {
                        foreach (Export.Reference ii in instance.References
                            .Where(x => !x.IsForward))
                        {
                            NodeId referenceTypeId = ImportNodeId(ii.ReferenceType);

                            if (referenceTypeId == ReferenceTypeIds.HasProperty ||
                                referenceTypeId == ReferenceTypeIds.HasComponent)
                            {
                                instance.ParentNodeId = ii.Value;
                                break;
                            }
                        }
                    }
                }

                XmlQualifiedName symbolicId = BuildSymbolicId(node);

                while (m_symbolicIds.Values.Any(x => x == symbolicId))
                {
                    symbolicId = new XmlQualifiedName(
                        $"{symbolicId.Name}_{NodeId.Parse(node.NodeId).IdentifierAsString}",
                        symbolicId.Namespace);
                }

                m_symbolicIds.Add(node.NodeId, symbolicId);
            }

            foreach (UANode node in m_nodeset.Items)
            {
                ImportNode(node);
            }

            foreach (UANode node in m_nodeset.Items)
            {
                if (node is UAInstance child &&
                    !string.IsNullOrWhiteSpace(child.ParentNodeId))
                {
                    LinkChildToParent(child);
                }
            }

            foreach (UANode node in m_nodeset.Items)
            {
                ImportReferences(node);
            }

            foreach (UANode node in m_nodeset.Items)
            {
                ImportPermissions(node);
            }

            List<NodeDesign> items = [];

            for (int ii = 0; ii < m_nodeset.Items.Length; ii++)
            {
                UANode node = m_nodeset.Items[ii];

                if (node is UAInstance instance && instance.ParentNodeId != null)
                {
                    continue;
                }

                NodeId nodeId = ImportNodeId(node.NodeId);

                if (!nodeId.IsNullNodeId &&
                    m_settings.NodesById.TryGetValue(nodeId, out NodeDesign design))
                {
                    items.Add(design);
                }
            }

            Dictionary<XmlQualifiedName, MethodDesign> methods = [];
            CollectMethodDefinitions(dictionary.TargetNamespace, methods);

            items.AddRange(methods.Values);

            foreach (NodeDesign item in items)
            {
                if (item is TypeDesign type)
                {
                    string className = type.SymbolicName.Name;

                    if (type is DataTypeDesign dt)
                    {
                        if (dt.HasFields)
                        {
                            foreach (Parameter field in dt.Fields)
                            {
                                if (field.Name == className)
                                {
                                    type.ClassName = className + "DataType";
                                    break;
                                }
                            }
                        }
                    }
                    else if (type.HasChildren)
                    {
                        if (className.EndsWith("Type", StringComparison.Ordinal))
                        {
                            className = className[..^"Type".Length];
                        }

                        foreach (InstanceDesign child in type.Children.Items)
                        {
                            if (child.BrowseName == type.ClassName)
                            {
                                type.ClassName = className +
                                    (type is VariableTypeDesign ? "Variable" : "Object");
                                break;
                            }
                        }
                    }
                }
            }
            dictionary.Items = [.. items];
            dictionary.NamespaceUris = m_settings.NamespaceUris;
            return dictionary;
        }

        private void CollectMethodDefinitions(
            string targetNamespace,
            Dictionary<XmlQualifiedName, MethodDesign> methods)
        {
            foreach (NodeDesign node in m_settings.NodesById.Values)
            {
                if (node is MethodDesign method)
                {
                    if (node.SymbolicId.Namespace != targetNamespace)
                    {
                        continue;
                    }

                    if (method.HasArguments && method.MethodDeclarationNode == null)
                    {
                        var name = new XmlQualifiedName(
                            node.SymbolicName.Name + "MethodType",
                            node.SymbolicId.Namespace);

                        if (methods.ContainsKey(name))
                        {
                            continue;
                        }

                        var declaration = new MethodDesign
                        {
                            SymbolicId = name,
                            SymbolicName = name,
                            BrowseName = name.Name,
                            DisplayName = new LocalizedText
                            {
                                Value = name.Name,
                                IsAutogenerated = true
                            },
                            InputArguments = method.InputArguments,
                            OutputArguments = method.OutputArguments,
                            HasArguments = true
                        };

                        methods.Add(declaration.SymbolicName, declaration);
                        method.MethodDeclarationNode = declaration;
                    }
                }
            }
        }

        /// <summary>
        /// Creates an decoder to restore Variant values.
        /// </summary>
        private XmlDecoder CreateDecoder(XmlElement source, string sourceNodeSetUri = null)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext(m_telemetry)
            {
                NamespaceUris = m_settings.NamespaceUris,
                ServerUris = m_serverUris
            };

            var decoder = new XmlDecoder(source, messageContext);

            var namespaceUris = new NamespaceTable();

            if (sourceNodeSetUri == null ||
                !m_settings.NamespaceTables.TryGetValue(sourceNodeSetUri, out string[] sourceNamespaceUris))
            {
                sourceNamespaceUris = m_nodeset.NamespaceUris;
            }

            if (sourceNamespaceUris != null)
            {
                for (int ii = 0; ii < sourceNamespaceUris.Length; ii++)
                {
                    namespaceUris.Append(sourceNamespaceUris[ii]);
                }
            }

            var serverUris = new StringTable();

            if (m_nodeset.ServerUris != null)
            {
                serverUris.Append(m_serverUris.GetString(0));

                for (int ii = 0; ii < m_nodeset.ServerUris.Length; ii++)
                {
                    serverUris.Append(m_nodeset.ServerUris[ii]);
                }
            }

            decoder.SetMappingTables(namespaceUris, serverUris);

            return decoder;
        }

        private static AccessLevel ImportAccessLevel(uint input)
        {
            if ((AccessLevelExType.CurrentRead & (AccessLevelExType)input) != 0)
            {
                if ((AccessLevelExType.CurrentWrite & (AccessLevelExType)input) != 0)
                {
                    return AccessLevel.ReadWrite;
                }

                return AccessLevel.Read;
            }

            if ((AccessLevelExType.CurrentWrite & (AccessLevelExType)input) != 0)
            {
                return AccessLevel.Write;
            }

            if ((AccessLevelExType.HistoryRead & (AccessLevelExType)input) != 0)
            {
                if ((AccessLevelExType.HistoryWrite & (AccessLevelExType)input) != 0)
                {
                    return AccessLevel.HistoryReadWrite;
                }

                return AccessLevel.HistoryRead;
            }

            if ((AccessLevelExType.HistoryWrite & (AccessLevelExType)input) != 0)
            {
                return AccessLevel.HistoryWrite;
            }

            return AccessLevel.None;
        }

        private static ValueRank ImportValueRank(int input)
        {
            return input switch
            {
                > 1 => ValueRank.OneOrMoreDimensions,
                1 => ValueRank.Array,
                0 => ValueRank.OneOrMoreDimensions,
                -1 => ValueRank.Scalar,
                -2 => ValueRank.ScalarOrArray,
                -3 => ValueRank.ScalarOrOneDimension,
                < -3 => ValueRank.ScalarOrArray
            };
        }

        private static ModellingRule ImportModellingRule(ExpandedNodeId input)
        {
            if (input == ObjectIds.ModellingRule_Mandatory)
            {
                return ModellingRule.Mandatory;
            }

            if (input == ObjectIds.ModellingRule_Optional)
            {
                return ModellingRule.Optional;
            }

            if (input == ObjectIds.ModellingRule_MandatoryPlaceholder)
            {
                return ModellingRule.MandatoryPlaceholder;
            }

            if (input == ObjectIds.ModellingRule_OptionalPlaceholder)
            {
                return ModellingRule.OptionalPlaceholder;
            }

            if (input == ObjectIds.ModellingRule_ExposesItsArray)
            {
                return ModellingRule.ExposesItsArray;
            }

            return ModellingRule.None;
        }

        private static string ImportCategories(string[] input)
        {
            if (input != null)
            {
                StringBuilder output = new();

                foreach (string ii in input)
                {
                    if (output.Length > 0)
                    {
                        output.Append(',');
                    }

                    output.Append(ii);
                }

                return output.ToString();
            }

            return null;
        }

        private static ReleaseStatus ImportReleaseStatus(Export.ReleaseStatus input)
        {
            return input switch
            {
                Export.ReleaseStatus.Deprecated => ReleaseStatus.Deprecated,
                Export.ReleaseStatus.Draft => ReleaseStatus.Draft,
                _ => ReleaseStatus.Released
            };
        }

        /// <summary>
        ///  Imports a NodeId
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        private ReferenceNode ImportReference(Export.Reference source)
        {
            if (source == null)
            {
                return null;
            }

            NodeId referenceTypeId = ImportNodeId(source.ReferenceType, true);
            if (referenceTypeId.IsNullNodeId)
            {
                throw new InvalidDataException($"ReferenceType ({source.ReferenceType}) is not valid.");
            }

            ExpandedNodeId targetId = ImportExpandedNodeId(source.Value);

            return new ReferenceNode
            {
                ReferenceTypeId = referenceTypeId,
                IsInverse = !source.IsForward,
                TargetId = targetId
            };
        }

        /// <summary>
        ///  Imports a NodeId
        /// </summary>
        private NodeId ImportNodeId(string source, bool lookupAlias = true)
        {
            if (string.IsNullOrEmpty(source))
            {
                return NodeId.Null;
            }

            // lookup alias.
            if (lookupAlias && m_aliases.TryGetValue(source, out NodeId nodeId))
            {
                return nodeId;
            }

            // parse the string.
            nodeId = NodeId.Parse(source);

            if (nodeId.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(
                    nodeId.NamespaceIndex,
                    m_settings.NamespaceUris);
                nodeId = nodeId.WithNamespaceIndex(namespaceIndex);
            }

            return nodeId;
        }

        /// <summary>
        /// Imports a ExpandedNodeId
        /// </summary>
        private ExpandedNodeId ImportExpandedNodeId(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return ExpandedNodeId.Null;
            }

            // lookup aliases
            if (m_nodeset.Aliases != null)
            {
                for (int ii = 0; ii < m_nodeset.Aliases.Length; ii++)
                {
                    if (m_nodeset.Aliases[ii].Alias == source)
                    {
                        source = m_nodeset.Aliases[ii].Value;
                        break;
                    }
                }
            }

            // parse the node.
            var nodeId = ExpandedNodeId.Parse(source);

            if (nodeId.ServerIndex <= 0 &&
                nodeId.NamespaceIndex <= 0 &&
                string.IsNullOrEmpty(nodeId.NamespaceUri))
            {
                return nodeId;
            }

            uint serverIndex = ImportServerIndex(
                nodeId.ServerIndex,
                m_serverUris);
            ushort namespaceIndex = ImportNamespaceIndex(
                nodeId.NamespaceIndex,
                m_settings.NamespaceUris);

            if (serverIndex > 0)
            {
                string namespaceUri = nodeId.NamespaceUri;

                if (string.IsNullOrEmpty(nodeId.NamespaceUri))
                {
                    namespaceUri = m_settings.NamespaceUris.GetString(namespaceIndex);
                }

                return nodeId.WithNamespaceUri(namespaceUri).WithServerIndex(serverIndex);
            }

            return nodeId.WithNamespaceIndex(namespaceIndex).WithServerIndex(0);
        }

        /// <summary>
        /// Imports a QualifiedName
        /// </summary>
        private XmlQualifiedName ImportQualifiedName(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }

            var qname = QualifiedName.Parse(source);

            var builder = new StringBuilder();

            foreach (char ch in qname.Name)
            {
                if (builder.Length == 0)
                {
                    if (!char.IsLetter(ch) && ch != '_')
                    {
                        builder.Append('_');
                        continue;
                    }
                }
                else if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.' && ch != '+')
                {
                    builder.Append('_');
                    continue;
                }

                builder.Append(ch);
            }

            if (qname.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(
                    qname.NamespaceIndex,
                    m_settings.NamespaceUris);
                return new XmlQualifiedName(
                    builder.ToString(),
                    m_settings.NamespaceUris.GetString(namespaceIndex));
            }

            return new XmlQualifiedName(
                builder.ToString(),
                m_settings.NamespaceUris.GetString(0));
        }

        /// <summary>
        /// Imports the array dimensions.
        /// </summary>
        private static string ImportArrayDimensions(IList<uint> arrayDimensions)
        {
            if (arrayDimensions == null)
            {
                return null;
            }

            StringBuilder output = new();

            foreach (uint ii in arrayDimensions)
            {
                if (output.Length > 0)
                {
                    output.Append(',');
                }

                output.Append(ii);
            }

            return output.ToString();
        }

        /// <summary>
        /// Imports a namespace index.
        /// </summary>
        private ushort ImportNamespaceIndex(ushort namespaceIndex, NamespaceTable namespaceUris)
        {
            // nothing special required for indexes 0 and 1.
            if (namespaceIndex < 1)
            {
                return namespaceIndex;
            }

            // return a bad value if parameters are bad.
            if (namespaceUris == null ||
                m_nodeset.NamespaceUris == null ||
                m_nodeset.NamespaceUris.Length <= namespaceIndex - 1)
            {
                return ushort.MaxValue;
            }

            // find or append uri.
            return namespaceUris.GetIndexOrAppend(m_nodeset.NamespaceUris[namespaceIndex - 1]);
        }

        /// <summary>
        /// Imports a server index.
        /// </summary>
        private uint ImportServerIndex(uint serverIndex, StringTable serverUris)
        {
            // nothing special required for indexes 0.
            if (serverIndex <= 0)
            {
                return serverIndex;
            }

            // return a bad value if parameters are bad.
            if (serverUris == null ||
                m_nodeset.ServerUris == null ||
                m_nodeset.ServerUris.Length <= serverIndex - 1)
            {
                return ushort.MaxValue;
            }

            // find or append uri.
            return serverUris.GetIndexOrAppend(m_nodeset.ServerUris[serverIndex - 1]);
        }

        /// <summary>
        /// Convert to symbolic name.
        /// </summary>
        private static string ToSymbolicName(string name)
        {
            if (s_keywords.Contains(name))
            {
                name = "_" + name;
            }

            StringBuilder output = new();

            foreach (char ch in name)
            {
                if (!IsLetterOrDigit(ch))
                {
                    if (output.Length == 0)
                    {
                        output.Append('x');
                        continue;
                    }

                    output.Append('_');
                    continue;
                }

                if (output.Length == 0 && ch >= '0' && ch <= '9')
                {
                    output.Append('n');
                }

                output.Append(ch);
            }

            return output.ToString();
        }

        private static readonly string[] s_keywords =
        [
            "private",
            "public",
            "protected",
            "internal",
            "lock",
            "char",
            "byte",
            "int",
            "uint",
            "long",
            "ulong",
            "float",
            "double",
            "decimal",
            "for",
            "foreach",
            "while",
            "string"
        ];

        private readonly NodeSetReaderSettings m_settings;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly IFileSystem m_fileSystem;
        private readonly StringTable m_serverUris = new();
        private readonly UANodeSet m_nodeset;
        private readonly Dictionary<string, NodeId> m_aliases = [];
        private readonly Dictionary<NodeId, UANode> m_index;
        private readonly Dictionary<string, XmlQualifiedName> m_symbolicIds;
    }
}
