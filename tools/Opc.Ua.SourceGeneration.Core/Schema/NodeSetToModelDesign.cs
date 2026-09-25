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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using Opc.Ua.Export;
using Opc.Ua.SourceGeneration;
using Opc.Ua.Types;

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

            if (m_nodeset.Models == null ||
                m_nodeset.Models.Length == 0 ||
                string.IsNullOrEmpty(m_nodeset.Models[0].ModelUri))
            {
                throw new InvalidDataException(
                    $"NodeSet ({filePath}) does not declare a <Models> entry with a ModelUri.");
            }

            m_settings.NamespaceTables[m_nodeset.Models[0].ModelUri] =
                m_nodeset.NamespaceUris;

            if (m_nodeset.Aliases != null)
            {
                foreach (NodeIdAlias ii in m_nodeset.Aliases)
                {
                    m_aliases[ii.Alias] = ImportNodeId(ii.Value, false);
                    if (m_aliases[ii.Alias].IsNull)
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
                    if (nodeId.IsNull)
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
            // Only the root element decides. Reading it with an XmlReader copes
            // with a root on the same line as the XML declaration (minified
            // output) and with comments spanning several lines, which a line
            // based scan misread.
            using TextReader reader = fileSystem.CreateTextReader(filePath);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };
            try
            {
                using var xmlReader = XmlReader.Create(reader, settings);
                return xmlReader.MoveToContent() == XmlNodeType.Element &&
                    xmlReader.LocalName == "UANodeSet";
            }
            catch (XmlException)
            {
                // A NodeSet that is not well formed is still a NodeSet: it
                // has to fail as one, in isolation, rather than fall through
                // to the ModelDesign pass and abort every model with it.
                return HasNodeSetRootName(fileSystem, filePath);
            }
        }

        /// <summary>
        /// Scans the raw text of a document that is not well formed for the
        /// name of its first element (skipping the declaration, processing
        /// instructions, comments and a DOCTYPE) and checks whether that name
        /// is UANodeSet, with or without a namespace prefix.
        /// </summary>
        private static bool HasNodeSetRootName(IFileSystem fileSystem, string filePath)
        {
            string text;
            using (TextReader reader = fileSystem.CreateTextReader(filePath))
            {
                text = reader.ReadToEnd();
            }

            int index = 0;
            while ((index = text.IndexOf('<', index)) >= 0)
            {
                if (string.CompareOrdinal(text, index, "<!--", 0, 4) == 0)
                {
                    int end = text.IndexOf("-->", index + 4, StringComparison.Ordinal);
                    index = end < 0 ? text.Length : end + 3;
                    continue;
                }
                if (index + 1 < text.Length && text[index + 1] is '?' or '!')
                {
                    int end = text.IndexOf('>', index + 1);
                    index = end < 0 ? text.Length : end + 1;
                    continue;
                }

                int start = index + 1;
                int stop = start;
                while (stop < text.Length &&
                    !char.IsWhiteSpace(text[stop]) &&
                    text[stop] is not '>' and not '/')
                {
                    stop++;
                }

                string name = text[start..stop];
                int colon = name.IndexOf(':');
                return (colon < 0 ? name : name[(colon + 1)..]) == "UANodeSet";
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

                // A NodeSet can declare a type as its own subtype (the root
                // reference types routinely carry an inverse HasSubtype to
                // themselves). Without a visited set that walk never ends and
                // the generator hangs instead of reporting bad input.
                var visited = new HashSet<XmlQualifiedName>();

                while (parentId != null && visited.Add(parentId))
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

            // A placeholder browse name "<Name>" (no explicit SymbolicName in the
            // NodeSet) follows the ModelCompiler convention of mapping to
            // "Name_Placeholder" so generated identifiers match those produced
            // from the equivalent ModelDesign source (e.g. a combined NodeSet
            // that incorporates a model authored as ModelDesign). The raw browse
            // name is used because ImportQualifiedName rewrites '<' and '>' to '_'.
            string rawName = QualifiedName.Parse(input.BrowseName).Name;
            if (rawName != null &&
                rawName.Length > 2 &&
                rawName[0] == '<' &&
                rawName[^1] == '>')
            {
                return new XmlQualifiedName(
                    ToSymbolicName(rawName[1..^1]) + "_Placeholder",
                    browseName.Namespace);
            }

            // ImportQualifiedName replaces a leading digit with '_', which
            // ToSymbolicName then turns into 'x'. Where that lossy name clashes
            // (see CollectDigitPreservingNames) keep the digit, following the
            // ToSymbolicName convention for a leading digit ("1Axis" ->
            // "n1Axis").
            if (m_digitPreservingNames.Contains(input.NodeId) &&
                browseName.Name.Length > 0 &&
                rawName is { Length: > 0 })
            {
                return new XmlQualifiedName(
                    ToSymbolicName(rawName[0] + browseName.Name[1..]),
                    browseName.Namespace);
            }

            return new XmlQualifiedName(ToSymbolicName(browseName.Name), browseName.Namespace);
        }

        private static LocalizedText ImportLocalizedText(Export.LocalizedText[] input)
        {
            if (input == null || input.Length == 0)
            {
                return null;
            }

            // The design schema carries a single, locale-less default text. A
            // NodeSet may list several translations in any order, so prefer the
            // invariant or English entry over whichever happens to come first,
            // and never let an empty first entry hide a later translation.
            Export.LocalizedText selected =
                Array.Find(input, x => !string.IsNullOrWhiteSpace(x?.Value) &&
                    string.IsNullOrEmpty(x.Locale)) ??
                Array.Find(input, x => !string.IsNullOrWhiteSpace(x?.Value) &&
                    IsEnglishLocale(x.Locale)) ??
                Array.Find(input, x => !string.IsNullOrWhiteSpace(x?.Value));

            if (selected == null)
            {
                return null;
            }

            return new LocalizedText { Value = selected.Value, IsAutogenerated = false };
        }

        private static bool IsEnglishLocale(string locale)
        {
            return locale != null &&
                (string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase) ||
                locale.StartsWith("en-", StringComparison.OrdinalIgnoreCase));
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

                    if (rtid.IsNull || ReferenceTypeIds.HasSubtype != rtid || reference.IsForward)

                    {
                        continue;
                    }
                    NodeId targetId = ImportNodeId(reference.Value);

                    if (targetId.IsNull)

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

            if (nodeId.IsNull || m_settings.NodesById.TryGetValue(nodeId, out existing))
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

            output.SetIdentifier(ImportIdentifier(nodeId));

            // <References> is optional in the schema.
            foreach (Export.Reference ii in input.References ?? [])
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
            if (output.ClassName.EndsWith("Type", StringComparison.Ordinal))
            {
                output.ClassName = output.ClassName[..^"Type".Length];
            }
            output.IsAbstract = input.IsAbstract;

            // <References> is optional in the schema. A root type declares no
            // base type and can legitimately arrive without one.
            foreach (Export.Reference ii in input.References ?? [])
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
            output.ArrayDimensions = ImportArrayDimensions(input.ValueRank, input.ArrayDimensions);

            if (input.Value != null)
            {
                XmlDecoder decoder = CreateValueDecoder(input.Value, out NamespaceTable valueNamespaceUris);
                output.DecodedValue = decoder
                    .ReadVariantValue(null, default)
                    .AsBoxedObject(Variant.BoxingBehavior.Legacy);
                // The value keeps the NodeSet's own indexes; record their table.
                output.DecodedValueNamespaceUris = valueNamespaceUris;
                decoder.Close();
            }

            DataTypeDesign dataType = FindNode<DataTypeDesign>(ImportNodeId(input.DataType)) ??
                throw new InvalidDataException(
                    $"Could not find DataType Node for '{input.BrowseName}/{input.DataType}'.");

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

            if (nodeId.IsNull)

            {
                return false;
            }
            if (nodeId == superTypeId)

            {
                return true;
            }
            TypeDesign parent = FindSuperType<TypeDesign>(subtype);

            // A root type may declare itself as its own supertype (see the
            // other IsTypeOf overload); stop instead of walking that forever.
            var visited = new HashSet<XmlQualifiedName>();

            while (parent != null && visited.Add(parent.SymbolicId))
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

        /// <summary>
        /// The mask of an OptionSet bit. Computed in decimal (exact up to
        /// bit 95), since a signed shift turns bit 63 into a negative mask.
        /// </summary>
        private static decimal GetBitMask(int bit)
        {
            decimal mask = 1m;
            for (int ii = 0; ii < bit; ii++)
            {
                mask *= 2;
            }
            return mask;
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
                bool isStructureOptionSet = IsTypeOf(input, DataTypeIds.OptionSet);
                if (isStructureOptionSet)
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
                            ArrayDimensions = ImportArrayDimensions(ii.ValueRank, ii.ArrayDimensions),
                            ValueRank = ImportValueRank(ii.ValueRank),
                            Parent = output
                        };

                        if (ii.Name != field.Name)
                        {
                            field.DisplayName = new LocalizedText { Value = ii.Name, IsAutogenerated = false };
                        }

                        DataTypeDesign dataType = FindNode<DataTypeDesign>(ImportNodeId(ii.DataType)) ??
                            throw new InvalidDataException(
                                "Could not find DataType Node for Field " +
                                $"'{input.BrowseName}/{ii.Name}/{ii.DataType}'.");

                        field.DataType = dataType.SymbolicId;
                        field.DataTypeNode = dataType;
                        field.AllowSubTypes = ii.AllowSubTypes;
                        field.IsOptional = ii.IsOptional;

                        if (output.IsOptionSet)
                        {
                            // The Value of an OptionSet field is its bit
                            // position. It defaults to -1 when absent. A
                            // numeric OptionSet is at most a UInt64, while a
                            // subtype of the OptionSet structure carries its
                            // bits in a ByteString and may use any position
                            // (up to the 96 bits a decimal mask can hold). A
                            // field without a usable position is reported and
                            // left out, so one bad field does not abort the
                            // import of every model.
                            int maxBits = isStructureOptionSet ? kMaxDecimalMaskBits : 64;
                            if (ii.Value < 0 || ii.Value >= maxBits)
                            {
                                m_logger.LogError(
                                    "OptionSet field '{DataType}/{Field}' has an invalid bit position ({Value}); " +
                                    "expected a Value between 0 and {MaxBit}. The field is ignored.",
                                    input.BrowseName,
                                    ii.Name,
                                    ii.Value,
                                    maxBits - 1);
                                continue;
                            }

                            decimal mask = GetBitMask(ii.Value);
                            field.BitMask = mask <= ulong.MaxValue
                                ? ((ulong)mask).ToString("X8", CultureInfo.InvariantCulture)
                                : null;
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

            // <References> is optional in the schema. An instance without one
            // has no type definition either, which the check below reports.
            foreach (Export.Reference ii in input.References ?? [])
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
            output.ContainsNoLoops = input.ContainsNoLoops;
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
            output.ArrayDimensions = ImportArrayDimensions(input.ValueRank, input.ArrayDimensions);
            output.MinimumSamplingInterval = ImportMinimumSamplingInterval(input.MinimumSamplingInterval);
            output.MinimumSamplingIntervalSpecified = true;
            output.Historizing = input.Historizing;
            output.HistorizingSpecified = input.Historizing;
            output.AccessLevel = ImportAccessLevel(input.AccessLevel);
            output.AccessLevelSpecified = true;

            // The ModelDesign AccessLevel enumeration cannot express
            // combinations such as CurrentRead | HistoryRead (5), so the
            // verbatim bitmask is carried alongside it and preferred by
            // code generation. See GetAccessLevelAsCode.
            output.RawAccessLevel = input.AccessLevel;

            // Carry the explicit UserAccessLevel when the NodeSet2 model sets it.
            // When the attribute is absent the UserAccessLevel is derived from
            // AccessLevel by code generation, matching the runtime importer.
            output.RawUserAccessLevel = input.UserAccessLevelSpecified
                ? input.UserAccessLevel
                : (uint?)null;

            if (input.Value != null)
            {
                XmlDecoder decoder = CreateValueDecoder(input.Value, out NamespaceTable valueNamespaceUris);
                output.DecodedValue = decoder
                    .ReadVariantValue(null, default)
                    .AsBoxedObject(Variant.BoxingBehavior.Legacy);
                // The value keeps the NodeSet's own indexes; record their table.
                output.DecodedValueNamespaceUris = valueNamespaceUris;
                decoder.Close();
            }

            DataTypeDesign dataType = FindNode<DataTypeDesign>(ImportNodeId(input.DataType)) ??
                throw new InvalidDataException(
                    $"Could not find DataType Node for '{input.BrowseName}/{input.DataType}'.");

            output.DataType = dataType.SymbolicId;
            output.DataTypeNode = dataType;
        }

        private List<Parameter> ImportArguments(
            MethodDesign method,
            string sourceNodeSetUri,
            System.Xml.XmlElement input)
        {
            var output = new List<Parameter>();
            if (input == null)
            {
                return output;
            }

            XmlDecoder decoder = CreateDecoder(input, sourceNodeSetUri);
            Variant value = decoder.ReadVariantValue(null, default);
            decoder.Close();

            foreach (Argument argument in value.GetStructureArray<Argument>())
            {
                DataTypeDesign dataType = FindNode<DataTypeDesign>(argument.DataType) ??
                    throw new InvalidDataException(
                        $"Method ({method.SymbolicId.Name}) argument ({argument.Name}) " +
                        $"datatype ({argument.DataType}) not found.");

                var parameter = new Parameter
                {
                    Name = argument.Name,
                    ArrayDimensions = ImportArrayDimensions(
                        argument.ValueRank,
                        ImportArrayDimensions(argument.ArrayDimensions)),
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

            if (output.Count == 0)
            {
                output.AddRange(ImportArgumentsFromXml(method, input));
            }
            return output;
        }

        private List<Parameter> ImportArgumentsFromXml(
            MethodDesign method,
            System.Xml.XmlElement input)
        {
            var output = new List<Parameter>();
            foreach (System.Xml.XmlElement argument in FindElements(input, "Argument"))
            {
                string name = GetElementValue(argument, "Name");
                string dataTypeText = GetElementValue(argument, "DataType", "Identifier");
                NodeId dataTypeId = ImportNodeId(dataTypeText);
                DataTypeDesign dataType = FindNode<DataTypeDesign>(dataTypeId) ??
                    throw new InvalidDataException(
                        $"Method ({method.SymbolicId.Name}) argument ({name}) datatype ({dataTypeText}) not found.");
                string valueRankText = GetElementValue(argument, "ValueRank");
                int valueRank = int.TryParse(
                    valueRankText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedValueRank)
                        ? parsedValueRank
                        : -1;
                string description = GetElementValue(argument, "Description", "Text");
                output.Add(new Parameter
                {
                    Name = name,
                    ArrayDimensions = ImportArrayDimensions(
                        valueRank,
                        GetListElementValue(argument, "ArrayDimensions")),
                    ValueRank = ImportValueRank(valueRank),
                    Parent = method,
                    DataType = dataType.SymbolicId,
                    DataTypeNode = dataType,
                    Description = string.IsNullOrEmpty(description)
                        ? null
                        : new LocalizedText
                        {
                            Value = description,
                            IsAutogenerated = false
                        }
                });
            }
            return output;
        }

        private static IEnumerable<System.Xml.XmlElement> FindElements(
            System.Xml.XmlElement element,
            string localName)
        {
            foreach (XmlNode child in element.ChildNodes)
            {
                if (child is not System.Xml.XmlElement childElement)
                {
                    continue;
                }

                if (childElement.LocalName == localName)
                {
                    yield return childElement;
                }

                foreach (System.Xml.XmlElement descendant in FindElements(childElement, localName))
                {
                    yield return descendant;
                }
            }
        }

        private static string GetElementValue(
            System.Xml.XmlElement element,
            params string[] path)
        {
            System.Xml.XmlElement current = element;
            foreach (string localName in path)
            {
                current = current?.ChildNodes
                    .OfType<System.Xml.XmlElement>()
                    .FirstOrDefault(child => child.LocalName == localName);
            }
            return current?.InnerText;
        }

        /// <summary>
        /// Returns the child element texts of a list element (e.g. a
        /// ListOfUInt32) joined with ','. InnerText would concatenate them
        /// without a separator, turning dimensions 2 and 3 into "23".
        /// </summary>
        private static string GetListElementValue(
            System.Xml.XmlElement element,
            string localName)
        {
            System.Xml.XmlElement list = element?.ChildNodes
                .OfType<System.Xml.XmlElement>()
                .FirstOrDefault(child => child.LocalName == localName);
            if (list == null)
            {
                return null;
            }

            string[] items =
            [
                .. list.ChildNodes
                    .OfType<System.Xml.XmlElement>()
                    .Select(child => child.InnerText.Trim())
            ];
            if (items.Length == 0)
            {
                string text = list.InnerText.Trim();
                return text.Length == 0 ? null : text;
            }

            return string.Join(",", items);
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

            UpdateMethodArguments(input, output);
        }

        private void UpdateMethodArguments(UAMethod input, MethodDesign output)
        {
            var propertyIds = new List<ExpandedNodeId>();
            if (input.References != null)
            {
                foreach (Export.Reference reference in input.References)
                {
                    ReferenceNode importedReference = ImportReference(reference);
                    if (importedReference.ReferenceTypeId == ReferenceTypes.HasProperty &&
                        !importedReference.IsInverse)
                    {
                        propertyIds.Add(importedReference.TargetId);
                    }
                }
            }

            NodeId methodId = ImportNodeId(input.NodeId);

            foreach (UAVariable property in GetVariablesByParent(methodId))
            {
                if (property.References == null)
                {
                    continue;
                }

                if (property.References.Any(reference =>
                    {
                        ReferenceNode importedReference = ImportReference(reference);
                        return importedReference.ReferenceTypeId == ReferenceTypes.HasProperty &&
                            importedReference.IsInverse &&
                            importedReference.TargetId == methodId;
                    }))
                {
                    propertyIds.Add(ImportNodeId(property.NodeId));
                }
            }

            foreach (ExpandedNodeId propertyId in propertyIds.Distinct())
            {
                VariableDesign property = FindNode<VariableDesign>(propertyId);
                if (property?.DefaultValue == null)
                {
                    continue;
                }

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

            output.AssignMethodArgumentCodeNames();
        }

        /// <summary>
        /// Returns the variables whose ParentNodeId is <paramref name="parentId"/>,
        /// in NodeSet order. The index is built once on first use (after the
        /// ParentNodeIds were normalized) instead of scanning every variable
        /// for each method, which was quadratic for large NodeSets.
        /// </summary>
        private List<UAVariable> GetVariablesByParent(NodeId parentId)
        {
            if (m_variablesByParent == null)
            {
                m_variablesByParent = [];
                foreach (UAVariable variable in m_nodeset.Items.OfType<UAVariable>())
                {
                    NodeId key = ImportNodeId(variable.ParentNodeId);
                    if (key.IsNull)
                    {
                        continue;
                    }
                    if (!m_variablesByParent.TryGetValue(key, out List<UAVariable> list))
                    {
                        m_variablesByParent.Add(key, list = []);
                    }
                    list.Add(variable);
                }
            }

            return m_variablesByParent.TryGetValue(parentId, out List<UAVariable> variables)
                ? variables
                : [];
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

            UANode parentNode = FindNode(parentId) ??
                throw new InvalidDataException(
                    $"ParentNode ({input.ParentNodeId}) not found for node " +
                    $"{input.NodeId} ({input.BrowseName}).");

            if (!m_settings.NodesById.TryGetValue(parentId, out NodeDesign parent))
            {
                parent = ImportNode(parentNode);
            }

            // A hierarchical reference on either side is what makes the child a
            // child, so look for one on both sides before settling for a
            // non-hierarchical one. Taking the parent's non-hierarchical
            // reference first would shadow a perfectly good inverse HasComponent
            // on the child and drop the node out of the parent's Children.
            referenceType =
                FindHierarchicalReference(parentNode.References, input.NodeId, forward: true) ??
                FindHierarchicalReference(input.References, input.ParentNodeId, forward: false);

            if (referenceType == null)
            {
                // The reference to look at on the parent side is its forward
                // reference to this child - scanning the child for a reference
                // to itself never matches anything.
                referenceType =
                    FindAnyReference(parentNode.References, input.NodeId, forward: true) ??
                    FindAnyReference(input.References, input.ParentNodeId, forward: false);

                nonHierarchical = referenceType != null;
            }

            if (referenceType == null)
            {
                throw new InvalidDataException(
                    $"HierarchicalReference from ParentNode ({input.ParentNodeId}) to Node " +
                    $"{input.NodeId} ({input.BrowseName}) not found.");
            }

            if (nonHierarchical)
            {
                // Only a hierarchical reference makes the node a child. Keeping
                // the ParentNodeId without linking would leave the node neither
                // in its parent's Children nor among the top-level items, so it
                // (and its subtree) vanished from the model. Import it as a
                // top-level node instead; ImportReferences keeps the
                // non-hierarchical reference to the parent as an explicit one.
                input.ParentNodeId = null;
                return;
            }

            if (m_settings.NodesById.TryGetValue(nodeId, out NodeDesign child))
            {
                LinkChildToParent(parent, referenceType.SymbolicId, child as InstanceDesign);
            }
        }

        /// <summary>
        /// Returns the reference type of the last hierarchical reference in
        /// <paramref name="references"/> that points at <paramref name="targetId"/>
        /// in the requested direction, or <c>null</c> when there is none.
        /// </summary>
        private NodeDesign FindHierarchicalReference(
            Export.Reference[] references,
            string targetId,
            bool forward)
        {
            return FindReference(references, targetId, forward, hierarchicalOnly: true);
        }

        /// <summary>
        /// Same as <see cref="FindHierarchicalReference"/> but accepting any
        /// reference type, hierarchical or not.
        /// </summary>
        private NodeDesign FindAnyReference(
            Export.Reference[] references,
            string targetId,
            bool forward)
        {
            return FindReference(references, targetId, forward, hierarchicalOnly: false);
        }

        private NodeDesign FindReference(
            Export.Reference[] references,
            string targetId,
            bool forward,
            bool hierarchicalOnly)
        {
            NodeDesign referenceType = null;

            foreach (Export.Reference reference in references ?? [])
            {
                if (reference.Value != targetId || reference.IsForward != forward)
                {
                    continue;
                }

                NodeId referenceTypeId = ImportNodeId(reference.ReferenceType);

                if (hierarchicalOnly &&
                    !IsTypeOf(referenceTypeId, ReferenceTypeIds.HierarchicalReferences))
                {
                    continue;
                }

                referenceType = FindReferenceType(referenceTypeId);
            }

            return referenceType;
        }

        private void LinkChildToParent(
            NodeDesign parent,
            XmlQualifiedName referenceTypeId,
            InstanceDesign child)
        {
            // The SymbolicId is kept exactly as the symbolic id pass assigned
            // it. A child of a de-duplicated parent (e.g. "Parameters_5002")
            // was renamed here to "<parent id>_<child id>" after it had been
            // registered under its assigned id, which left NodesByQName keyed
            // by a name the generator never emitted, made the name depend on
            // the order in which children and grand-children were linked, and
            // disagreed with the ids GetImportedSymbols reports to identifier
            // (NodeIds.csv) sidecars.
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

            output.SymbolicName = NormalizeSymbolicNameNamespace(
                input,
                output.SymbolicId,
                output.SymbolicName);

            if (HasCollisionSuffix(input))
            {
                output.SymbolicName = new XmlQualifiedName(
                    $"{output.SymbolicName.Name}_{GetCollisionSuffix(input.NodeId)}",
                    output.SymbolicName.Namespace);
            }

            output.BrowseName = QualifiedName.Parse(input.BrowseName).Name;
            output.Description = ImportLocalizedText(input.Description);
            output.DisplayName = ImportLocalizedText(input.DisplayName);
            output.WriteAccess = input.WriteMask;
            output.ReleaseStatus = ImportReleaseStatus(input.ReleaseStatus);
            output.Category = ImportCategories(input.Category);

            // All four identifier types of OPC 10000-3 5.2.2 are carried over.
            output.SetIdentifier(ImportIdentifier(nodeId));

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

        /// <summary>
        /// Whether the symbolic id pass had to de-duplicate the SymbolicId of
        /// a type. Such a type also gets the suffix on its SymbolicName (and
        /// hence its class name). The clash is recorded when it happens: a
        /// name that merely ends in "_&lt;identifier&gt;" ("Point_7" with i=7)
        /// was not de-duplicated.
        /// </summary>
        private bool HasCollisionSuffix(UANode node)
        {
            return node is UAType && m_collisionSuffixed.Contains(node.NodeId);
        }

        /// <summary>
        /// The suffix appended to a clashing SymbolicId: the node's identifier,
        /// with every character that cannot appear in a C# identifier replaced
        /// by '_'. Numeric ids and identifier-safe string ids are used as is;
        /// a Guid ("6f1c...-..."), opaque (base64 '+', '/', '=') or string id
        /// such as "Line1.Motor" used to be inserted verbatim and produced a
        /// constant/class name that did not compile.
        /// </summary>
        private static string GetCollisionSuffix(string nodeId)
        {
            string identifier = NodeId.Parse(nodeId).IdentifierAsString;
            var builder = new StringBuilder(identifier.Length);

            foreach (char ch in identifier)
            {
                builder.Append(IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            return builder.ToString();
        }

        internal static XmlQualifiedName NormalizeSymbolicNameNamespace(
            UANode input,
            XmlQualifiedName symbolicId,
            XmlQualifiedName symbolicName)
        {
            if (input is UAType &&
                symbolicId != null &&
                symbolicName != null &&
                symbolicId.Namespace != symbolicName.Namespace)
            {
                return new XmlQualifiedName(symbolicName.Name, symbolicId.Namespace);
            }

            return symbolicName;
        }

        /// <summary>
        /// Looks up a node of this NodeSet by its (imported) NodeId. Uses the
        /// index built by the constructor; a linear scan parsing every NodeId
        /// per lookup made large instance NodeSets quadratic.
        /// </summary>
        private UANode FindNode(NodeId targetId)
        {
            if (targetId.IsNull)
            {
                return null;
            }

            return m_index.TryGetValue(targetId, out UANode node) ? node : null;
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

            NodeId typeDefinitionId = FindTarget(input, ReferenceTypeIds.HasTypeDefinition, false);

            if (typeDefinitionId == ObjectTypeIds.DataTypeEncodingType)
            {
                if (!string.IsNullOrEmpty(input.SymbolicName) &&
                    input.SymbolicName.Contains("Default", StringComparison.Ordinal) &&
                    input.SymbolicName.Contains("XML", StringComparison.OrdinalIgnoreCase) &&
                    input.SymbolicName != "DefaultXml")
                {
                    throw new InvalidDataException(
                        $"{input.SymbolicName} is not a valid symbolic name for a " +
                        "DataTypeEncoding node (should be 'DefaultXml').");
                }

                NodeId dataTypeId = FindTarget(input, ReferenceTypeIds.HasEncoding, true);
                UANode dataType = dataTypeId.IsNull
                    ? FindDataTypeByEncoding(ImportNodeId(input.NodeId))
                    : FindNode(dataTypeId);

                if (dataType != null)
                {
                    dataTypeId = ImportNodeId(dataType.NodeId);

                    return new XmlQualifiedName(
                        $"{GetDataTypeSymbolicName(dataType)}_Encoding_{name.Name}",
                        m_settings.NamespaceUris.GetString(dataTypeId.NamespaceIndex));
                }
            }

            return name;
        }

        /// <summary>
        /// Returns the first DataType of this NodeSet that declares a forward
        /// HasEncoding reference to <paramref name="encodingId"/>. Used for an
        /// encoding without the inverse reference; the index is built once
        /// instead of scanning all DataTypes per encoding.
        /// </summary>
        private UANode FindDataTypeByEncoding(NodeId encodingId)
        {
            if (m_dataTypesByEncoding == null)
            {
                m_dataTypesByEncoding = [];
                foreach (UANode dataType in m_nodeset.Items.Where(x => x is UADataType))
                {
                    foreach (Export.Reference ii in dataType.References ?? [])
                    {
                        ReferenceNode reference = ImportReference(ii);
                        if (reference.ReferenceTypeId != ReferenceTypeIds.HasEncoding ||
                            reference.IsInverse)
                        {
                            continue;
                        }

                        NodeId targetId = ExpandedNodeId.ToNodeId(
                            reference.TargetId,
                            m_settings.NamespaceUris);
                        if (!targetId.IsNull)
                        {
                            m_dataTypesByEncoding.TryAdd(targetId, dataType);
                        }
                    }
                }
            }

            return m_dataTypesByEncoding.TryGetValue(encodingId, out UANode node) ? node : null;
        }

        /// <summary>
        /// Returns the SymbolicName the DataType node ends up with, including the
        /// collision suffix <see cref="ImportNode"/> appends to a type whose
        /// SymbolicId was de-duplicated. Its encodings must be named after that
        /// final name, which is what code generation looks them up by; the bare
        /// name left a de-duplicated structure with null encoding ids.
        /// </summary>
        private string GetDataTypeSymbolicName(UANode dataType)
        {
            string name = ImportSymbolicName(dataType).Name;

            if (HasCollisionSuffix(dataType))
            {
                name += "_" + GetCollisionSuffix(dataType.NodeId);
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

                    bool explicitReference = (ii.IsForward &&
                            referenceTypeId == ReferenceTypeIds.Organizes &&
                            target is ViewDesign) ||
                        targetId.NamespaceIndex != nodeId.NamespaceIndex ||
                        IsTypeOf(referenceTypeId, ReferenceTypeIds.NonHierarchicalReferences);
                    if (!explicitReference)
                    {
                        if (ii.IsForward &&
                            !IsTypeOf(referenceTypeId, ReferenceTypeIds.HierarchicalReferences))
                        {
                            continue;
                        }

                        // Ownership represents only the selected reference type, not
                        // every hierarchical link between these nodes.
                        NodeDesign parent = ii.IsForward ? existing : target;
                        NodeDesign child = ii.IsForward ? target : existing;
                        if (child is InstanceDesign instance &&
                            instance.Parent == parent &&
                            instance.ReferenceType == referenceType.SymbolicId)
                        {
                            continue;
                        }
                    }

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

        /// <summary>
        /// Maps a NodeSet AccessRestrictions bit mask onto the design schema's
        /// enumeration. Returns <c>null</c> when the mask carries no restriction
        /// the schema can express - notably the empty mask (which means "no
        /// restrictions", not "encryption required") and a mask that only sets
        /// ApplyRestrictionsToBrowse.
        /// </summary>
        private static AccessRestrictions? ToAccessRestrictions(AccessRestrictionType input)
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

            // An unrecognised combination - a reserved or vendor bit, possibly
            // alongside a bit the schema does know. Stay fail-closed: a bit this
            // mapping cannot name is still a restriction the NodeSet asked for,
            // and dropping it would publish the node with no protection at all.
            // Only a mask that demands nothing - empty, or nothing beyond
            // ApplyRestrictionsToBrowse, which restricts nothing on its own -
            // maps to "unspecified".
            if ((input & ~AccessRestrictionType.ApplyRestrictionsToBrowse) != 0)
            {
                return (input & AccessRestrictionType.ApplyRestrictionsToBrowse) != 0
                    ? AccessRestrictions.SessionWithEncryptionAndApplyToBrowseRequired
                    : AccessRestrictions.SessionWithEncryptionRequired;
            }

            return null;
        }

        private void ImportPermissions(UANode input)
        {
            NodeId nodeId = ImportNodeId(input.NodeId);

            if (!m_settings.NodesById.TryGetValue(nodeId, out NodeDesign existing))
            {
                return;
            }

            existing.RolePermissions = ToPermissionSet(input.RolePermissions);

            AccessRestrictions? restrictions = input.AccessRestrictionsSpecified
                ? ToAccessRestrictions((AccessRestrictionType)input.AccessRestrictions)
                : null;
            existing.AccessRestrictions = restrictions ?? default;
            existing.AccessRestrictionsSpecified = restrictions.HasValue;
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

            if (parentId.IsNull || !m_index.TryGetValue(parentId, out UANode parent))
            {
                throw new InvalidDataException(
                    $"Parent node ({instance.ParentNodeId}) for {node.NodeId} not found.");
            }

            XmlQualifiedName symbolicId = BuildSymbolicId(parent);
            XmlQualifiedName symbolicName = ImportSymbolicName(node);

            if (nodeId.IsNull)
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

            AssignSymbolicIds();

            foreach (UANode node in m_nodeset.Items)
            {
                ImportNode(node);
            }

            foreach (UANode node in m_nodeset.Items)
            {
                if (node is UAMethod method &&
                    m_settings.NodesById.TryGetValue(ImportNodeId(method.NodeId), out NodeDesign design) &&
                    design is MethodDesign methodDesign)
                {
                    UpdateMethodArguments(method, methodDesign);
                }
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

                if (!nodeId.IsNull &&
                    m_settings.NodesById.TryGetValue(nodeId, out NodeDesign design))
                {
                    items.Add(design);
                }
            }

            Dictionary<XmlQualifiedName, MethodDesign> methods = [];
            CollectMethodDefinitions(dictionary.TargetNamespace, methods);

            items.AddRange(methods
                .OrderBy(entry => entry.Key.Namespace, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.Name, StringComparer.Ordinal)
                .Select(entry => entry.Value));

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

        /// <summary>
        /// Normalizes the <c>ParentNodeId</c> of an instance node so the symbolic
        /// id derived from it is the one the model actually uses. Shared by the
        /// import pass and by <see cref="GetImportedSymbols"/>, which must derive
        /// the same ids.
        /// </summary>
        private void NormalizeParentNodeId(UANode node)
        {
            if (node is not UAInstance instance)
            {
                return;
            }

            // View nodes are independent address-space nodes even when an
            // exporter sets ParentNodeId to an organizing folder. ViewState
            // derives from NodeState (not BaseInstanceState) and therefore
            // cannot be added as an AddChild component. Keeping the parent
            // would absorb the view into the folder's Children, exclude it
            // from the top-level model items and make the generator emit an
            // invalid AddChild(...) call. Clearing the parent keeps the view
            // a standalone predefined node linked purely via Organizes
            // references (mirrors the DataTypeEncoding handling below and the
            // way DataType/ReferenceType type nodes are modelled).
            if (node is UAView)
            {
                instance.ParentNodeId = null;
            }

            // ensure parents are in the same namespace.
            if (instance.ParentNodeId != null)
            {
                NodeId parentId = ImportNodeId(instance.ParentNodeId);
                NodeId childId = ImportNodeId(instance.NodeId);

                if (parentId.NamespaceIndex != childId.NamespaceIndex)
                {
                    instance.ParentNodeId = null;
                }
                else if (FindTarget(
                    node,
                    ReferenceTypeIds.HasTypeDefinition,
                    false) == ObjectTypeIds.DataTypeEncodingType)
                {
                    // DataTypeEncoding objects are independent address-space
                    // nodes even when an exporter sets ParentNodeId to the
                    // owning DataType. Keeping that parent absorbs the
                    // encoding into DataType.Children, excludes it from the
                    // top-level model items and prevents the NodeManager
                    // generator from registering the encoding node.
                    instance.ParentNodeId = null;
                }

                return;
            }

            // handle missing ParentNodeId when an inverse reference exists.
            // <References> is optional in the schema, so an instance can
            // arrive with neither a parent nor a reference list.
            if (instance.References == null)
            {
                return;
            }

            foreach (Export.Reference ii in instance.References
                .Where(x => !x.IsForward))
            {
                NodeId referenceTypeId = ImportNodeId(ii.ReferenceType);

                if (referenceTypeId == ReferenceTypeIds.HasProperty ||
                    referenceTypeId == ReferenceTypeIds.HasComponent)
                {
                    // Only a parent in the node's own namespace can own it -
                    // the same rule as for an explicit ParentNodeId above.
                    // An inferred parent elsewhere (e.g. a component added to
                    // the ns=0 Server object) would leave the node neither a
                    // child nor a top-level item, silently dropping it and
                    // its whole subtree from the model. Such a node stays
                    // top-level unless another inverse reference names an
                    // owner in its namespace.
                    if (ImportNodeId(ii.Value).NamespaceIndex !=
                        ImportNodeId(instance.NodeId).NamespaceIndex)
                    {
                        continue;
                    }

                    instance.ParentNodeId = ii.Value;
                    break;
                }
            }
        }

        /// <summary>
        /// Assigns the SymbolicId of every node in the set. Shared by
        /// <see cref="Import"/> and <see cref="GetImportedSymbols"/>, which must
        /// derive the same ids.
        /// </summary>
        private void AssignSymbolicIds()
        {
            m_symbolicIds.Clear();
            m_collisionSuffixed.Clear();

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
                        }
                    }
                    else if (node.SymbolicName == "DefaultXML")
                    {
                        node.SymbolicName = nameof(BrowseNames.DefaultXml);
                    }
                }
            }

            CollectDigitPreservingNames();

            var usedIds = new HashSet<XmlQualifiedName>();
            var encodings = new List<UANode>();

            foreach (UANode node in m_nodeset.Items)
            {
                NormalizeParentNodeId(node);

                // A DataTypeEncoding is named after its DataType's final
                // SymbolicName, which is only known once the DataType's own id
                // (and any collision suffix) has been assigned.
                if (IsDataTypeEncoding(node))
                {
                    encodings.Add(node);
                    continue;
                }

                AssignSymbolicId(node, usedIds);
            }

            foreach (UANode node in encodings)
            {
                AssignSymbolicId(node, usedIds);
            }
        }

        private bool IsDataTypeEncoding(UANode node)
        {
            return FindTarget(node, ReferenceTypeIds.HasTypeDefinition, false) ==
                ObjectTypeIds.DataTypeEncodingType;
        }

        private void AssignSymbolicId(UANode node, HashSet<XmlQualifiedName> usedIds)
        {
            XmlQualifiedName symbolicId = BuildSymbolicId(node);

            while (!usedIds.Add(symbolicId))
            {
                symbolicId = new XmlQualifiedName(
                    $"{symbolicId.Name}_{GetCollisionSuffix(node.NodeId)}",
                    symbolicId.Namespace);
                m_collisionSuffixed.Add(node.NodeId);
            }

            m_symbolicIds[node.NodeId] = symbolicId;
        }

        /// <summary>
        /// Finds the instances whose browse name starts with a digit and whose
        /// default SymbolicName therefore loses that digit ("1Axis" and "2Axis"
        /// both become "xAxis"). Where that makes the name clash with another
        /// node of a different browse name - which fails code generation or
        /// emits the wrong BrowseName constant - the digit is kept instead
        /// ("n1Axis"). Names that do not clash are left as they are so that
        /// existing generated identifiers stay stable.
        /// </summary>
        private void CollectDigitPreservingNames()
        {
            m_digitPreservingNames.Clear();

            var browseNamesBySymbolicName = new Dictionary<XmlQualifiedName, HashSet<string>>();
            var candidates = new List<(UANode Node, XmlQualifiedName Name)>();

            foreach (UANode node in m_nodeset.Items)
            {
                if (string.IsNullOrEmpty(node.BrowseName))
                {
                    continue;
                }

                XmlQualifiedName name = ImportSymbolicName(node);
                if (!browseNamesBySymbolicName.TryGetValue(name, out HashSet<string> browseNames))
                {
                    browseNamesBySymbolicName.Add(name, browseNames = new HashSet<string>(StringComparer.Ordinal));
                }
                browseNames.Add(QualifiedName.Parse(node.BrowseName).Name);

                if (node is UAInstance && HasLeadingDigitBrowseName(node))
                {
                    candidates.Add((node, name));
                }
            }

            foreach ((UANode node, XmlQualifiedName name) in candidates)
            {
                if (browseNamesBySymbolicName[name].Count > 1)
                {
                    m_digitPreservingNames.Add(node.NodeId);
                }
            }
        }

        /// <summary>
        /// Whether the default SymbolicName of the node is derived from a browse
        /// name that starts with a digit (the lossy case of ImportQualifiedName).
        /// </summary>
        private static bool HasLeadingDigitBrowseName(UANode node)
        {
            if (!string.IsNullOrEmpty(node.SymbolicName) || string.IsNullOrEmpty(node.BrowseName))
            {
                return false;
            }

            string rawName = QualifiedName.Parse(node.BrowseName).Name;
            return rawName != null &&
                rawName.Length > 0 &&
                rawName[0] is >= '0' and <= '9';
        }

        internal IEnumerable<NodesetImportedSymbol> GetImportedSymbols(string modelUri)
        {
            // Exactly the id assignment Import() applies, so the symbolic ids
            // the sidecar validator reports match the ones the import pass
            // derives.
            AssignSymbolicIds();

            foreach (UANode node in m_nodeset.Items)
            {
                NodeId nodeId = ImportNodeId(node.NodeId, false);
                if (!nodeId.TryGetValue(out uint numericId))
                {
                    continue;
                }

                XmlQualifiedName symbolicId = m_symbolicIds[node.NodeId];
                if (!string.Equals(symbolicId.Namespace, modelUri, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return new NodesetImportedSymbol(
                    symbolicId.Name,
                    numericId,
                    node switch
                    {
                        UAObject => NodeClass.Object,
                        UAVariable => NodeClass.Variable,
                        UAMethod => NodeClass.Method,
                        UAObjectType => NodeClass.ObjectType,
                        UAVariableType => NodeClass.VariableType,
                        UAReferenceType => NodeClass.ReferenceType,
                        UADataType => NodeClass.DataType,
                        UAView => NodeClass.View,
                        _ => NodeClass.Unspecified
                    });
            }
        }

        private void CollectMethodDefinitions(
            string targetNamespace,
            Dictionary<XmlQualifiedName, MethodDesign> methods)
        {
            // Index the explicit method nodes already present in the model by
            // their symbolic name. A concrete method with arguments normally
            // gets a synthesized "<Name>MethodType" declaration, but a combined
            // NodeSet may already ship that method-type node explicitly (e.g.
            // an incorporated companion specification method type).
            // Reuse the existing declaration in that case so code generation
            // does not emit two identifiers with the same name.
            Dictionary<XmlQualifiedName, MethodDesign> existingByName = [];
            foreach (NodeDesign node in m_settings.NodesById.Values)
            {
                if (node is MethodDesign existing &&
                    existing.SymbolicName != null &&
                    !existingByName.ContainsKey(existing.SymbolicName))
                {
                    existingByName.Add(existing.SymbolicName, existing);
                }
            }

            MethodDesign[] candidates =
            [
                .. m_settings.NodesById.Values
                    .OfType<MethodDesign>()
                    .Where(method =>
                        method.SymbolicId.Namespace == targetNamespace &&
                        MethodDesignArgumentResolver.HasDeclaredArguments(method))
                    .OrderBy(method => method.SymbolicId.Namespace, StringComparer.Ordinal)
                    .ThenBy(method => method.SymbolicId.Name, StringComparer.Ordinal)
            ];
            var groupedCandidates =
                new Dictionary<XmlQualifiedName, List<List<MethodDesign>>>();

            foreach (MethodDesign method in candidates)
            {
                if (method.MethodDeclarationNode != null)
                {
                    MethodDesign definition =
                        MethodDesignArgumentResolver.ResolveMethodDefinition(
                            method.MethodDeclarationNode);
                    if (MethodDesignArgumentResolver.HaveSameDeclaredSignature(
                        method,
                        definition))
                    {
                        continue;
                    }
                }

                // Skip methods whose BrowseName belongs to a base namespace
                // (e.g. the Core FileType Open/Close/Read/Write methods that a
                // FileType instance re-declares): they are instances of a
                // base-type method and must reuse that base method type rather
                // than get a synthesized method type in this model.
                if (method.SymbolicName != null &&
                    method.SymbolicName.Namespace != targetNamespace)
                {
                    continue;
                }

                // Skip a standalone node that already is a method type (no owning
                // parent and the conventional "MethodType" name, e.g. an
                // incorporated companion specification method type.
                // Synthesizing a declaration for it would emit a spurious
                // "<Name>MethodTypeMethodType" node. A parentless method that is
                // merely a declaration target of another method still needs one.
                if (method.Parent == null &&
                    method.SymbolicName != null &&
                    method.SymbolicName.Name.EndsWith("MethodType", StringComparison.Ordinal))
                {
                    continue;
                }

                var baseName = new XmlQualifiedName(
                    method.SymbolicName.Name + "MethodType",
                    method.SymbolicId.Namespace);
                if (!groupedCandidates.TryGetValue(
                    baseName,
                    out List<List<MethodDesign>> signatureGroups))
                {
                    groupedCandidates.Add(baseName, signatureGroups = []);
                }

                List<MethodDesign> signatureGroup = signatureGroups.FirstOrDefault(
                    group => MethodDesignArgumentResolver.HaveSameDeclaredSignature(
                        group[0],
                        method));
                if (signatureGroup == null)
                {
                    signatureGroups.Add([method]);
                }
                else
                {
                    signatureGroup.Add(method);
                }
            }

            var reservedNames = new HashSet<XmlQualifiedName>(
                m_settings.NodesByQName.Keys);
            foreach (KeyValuePair<XmlQualifiedName, List<List<MethodDesign>>> entry
                in groupedCandidates
                    .OrderBy(entry => entry.Key.Namespace, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.Name, StringComparer.Ordinal))
            {
                bool qualifyWithOwner = entry.Value.Count > 1 ||
                    reservedNames.Contains(entry.Key);
                foreach (List<MethodDesign> signatureGroup in entry.Value)
                {
                    MethodDesign representative = signatureGroup[0];
                    string preferredName = qualifyWithOwner
                        ? GetOwnerQualifiedMethodTypeName(representative)
                        : entry.Key.Name;
                    var name = new XmlQualifiedName(
                        preferredName,
                        representative.SymbolicId.Namespace);
                    int suffix = 2;
                    while (reservedNames.Contains(name) || methods.ContainsKey(name))
                    {
                        string stem = preferredName.EndsWith(
                            "MethodType",
                            StringComparison.Ordinal)
                                ? preferredName[..^"MethodType".Length]
                                : preferredName;
                        name = new XmlQualifiedName(
                            stem +
                                suffix.ToString(CultureInfo.InvariantCulture) +
                                "MethodType",
                            representative.SymbolicId.Namespace);
                        suffix++;
                    }

                    // Prefer an explicit method-type declaration already in
                    // the model over synthesizing a colliding duplicate. The
                    // concrete method carries the authoritative argument
                    // definitions, so copy them onto the reused declaration
                    // to guarantee code generation emits the correct method
                    // signature and result even when the incorporated
                    // NodeSet declares the method-type argument nodes apart
                    // from the concrete method.
                    if (existingByName.TryGetValue(name, out MethodDesign declared) &&
                        !signatureGroup.Any(method => ReferenceEquals(declared, method)))
                    {
                        declared.InputArguments = representative.InputArguments;
                        declared.OutputArguments = representative.OutputArguments;
                        declared.HasArguments = true;
                        foreach (MethodDesign method in signatureGroup)
                        {
                            method.MethodDeclarationNode = declared;
                            method.TypeDefinition = null;
                            method.MethodType = null;
                        }
                        reservedNames.Add(name);
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
                        InputArguments = representative.InputArguments,
                        OutputArguments = representative.OutputArguments,
                        HasArguments = true
                    };

                    methods.Add(declaration.SymbolicName, declaration);
                    reservedNames.Add(declaration.SymbolicName);
                    foreach (MethodDesign method in signatureGroup)
                    {
                        method.MethodDeclarationNode = declaration;
                        method.TypeDefinition = null;
                        method.MethodType = null;
                    }
                }
            }
        }

        private static string GetOwnerQualifiedMethodTypeName(MethodDesign method)
        {
            string ownerName = method.SymbolicId.Name;
            string methodSuffix = "_" + method.SymbolicName.Name;
            if (ownerName.EndsWith(methodSuffix, StringComparison.Ordinal))
            {
                ownerName = ownerName[..^methodSuffix.Length];
            }
            ownerName = ownerName.Replace("_", string.Empty, StringComparison.Ordinal);
            return method.SymbolicName.Name + ownerName + "MethodType";
        }

        /// <summary>
        /// Creates an decoder to restore Variant values.
        /// </summary>
        private XmlDecoder CreateDecoder(System.Xml.XmlElement source, string sourceNodeSetUri = null)
        {
            return CreateDecoder(source, sourceNodeSetUri, mapNamespaces: true, out _);
        }

        /// <summary>
        /// Creates a decoder for a Variable/VariableType value. The namespace
        /// indexes inside the value are left as the NodeSet wrote them, and
        /// <paramref name="sourceNamespaceUris"/> is the NodeSet's own table they
        /// refer to. The design validator decodes the same XML again without any
        /// mapping, so the decoded value must mean the same thing either way.
        /// </summary>
        private XmlDecoder CreateValueDecoder(
            System.Xml.XmlElement source,
            out NamespaceTable sourceNamespaceUris)
        {
            return CreateDecoder(source, null, mapNamespaces: false, out sourceNamespaceUris);
        }

        private XmlDecoder CreateDecoder(
            System.Xml.XmlElement source,
            string sourceNodeSetUri,
            bool mapNamespaces,
            out NamespaceTable sourceNamespaceUris)
        {
            // The factory knows the standard OPC UA encodeable types. Without them, structured
            // NodeSet2 values such as method Argument lists (InputArguments/OutputArguments)
            // cannot be decoded and the generated typed method state would lose its arguments
            // and result fields.
            var namespaceUris = new NamespaceTable();

            if (sourceNodeSetUri == null ||
                !m_settings.NamespaceTables.TryGetValue(sourceNodeSetUri, out string[] nodeSetNamespaceUris))
            {
                nodeSetNamespaceUris = m_nodeset.NamespaceUris;
            }

            if (nodeSetNamespaceUris != null)
            {
                for (int ii = 0; ii < nodeSetNamespaceUris.Length; ii++)
                {
                    namespaceUris.Append(nodeSetNamespaceUris[ii]);
                }
            }
            sourceNamespaceUris = namespaceUris;

            var messageContext = new ServiceMessageContext(m_telemetry, s_valueDecodingFactory);
            messageContext.NamespaceUris = mapNamespaces ? m_settings.NamespaceUris : namespaceUris;
            messageContext.ServerUris = m_serverUris;

            var decoder = new XmlDecoder((XmlElement)source, messageContext);

            var serverUris = new StringTable();

            if (m_nodeset.ServerUris != null)
            {
                // Index 0 of a ServerUris table is the local server, which the
                // NodeSet's <ServerUris> table does not list (its first entry
                // is svr=1). m_serverUris is seeded with a local placeholder,
                // so svr=n in a value maps onto m_nodeset.ServerUris[n-1].
                serverUris.Append(m_serverUris.GetString(0));

                for (int ii = 0; ii < m_nodeset.ServerUris.Length; ii++)
                {
                    serverUris.Append(m_nodeset.ServerUris[ii]);

                    // The mapping is created without updating the target
                    // table, so the remote servers must be known to it.
                    m_serverUris.GetIndexOrAppend(m_nodeset.ServerUris[ii]);
                }
            }

            decoder.SetMappingTables(mapNamespaces ? namespaceUris : null, serverUris);

            return decoder;
        }

        /// <summary>
        /// Builds the factory used to decode Variable values and Method arguments out of a NodeSet2.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="ServiceMessageContext.CreateEmpty(ITelemetryContext)"/> hands back a factory with
        /// <b>no</b> registered types, which silently loses every ExtensionObject in the NodeSet — most
        /// visibly a Method's <c>InputArguments</c> / <c>OutputArguments</c>, which are encoded as a
        /// list of <see cref="Argument"/>. That produced argument-less MethodStates and ObjectType
        /// proxies for every NodeSet2-sourced model.
        /// </para>
        /// <para>
        /// Rather than hand-picking the types we happen to know about, every encodeable type exported
        /// from the assembly that defines the OPC UA built-ins is registered, so a NodeSet carrying any
        /// other standard structure decodes as well. The full set of known types lives in
        /// <c>Opc.Ua.Core.Types</c>, which the generator deliberately does not reference; a model that
        /// needs a type from there declares it in its own NodeSet and the generator emits it.
        /// </para>
        /// </remarks>
        private static IEncodeableFactory CreateValueDecodingFactory()
        {
            IEncodeableFactory factory = ServiceMessageContext.CreateEmpty(null).Factory;
            factory.AddEncodeableTypes(typeof(Argument).Assembly);
            return factory;
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

        /// <summary>
        /// Access level extended type (subset) to support above conversion to
        /// model level AccessLevel enumeration values.
        /// </summary>
        [Flags]
        private enum AccessLevelExType : uint
        {
            /// <summary>
            /// No extensions
            /// </summary>
            None = 0,

            /// <summary>
            /// Current read
            /// </summary>
            CurrentRead = 1,

            /// <summary>
            /// Current write
            /// </summary>
            CurrentWrite = 2,

            /// <summary>
            /// History read
            /// </summary>
            HistoryRead = 4,

            /// <summary>
            /// History write
            /// </summary>
            HistoryWrite = 8
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
            if (referenceTypeId.IsNull)
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
        /// Returns the identifier of a NodeId in the representation used by
        /// <see cref="NodeDesign"/>. All four identifier types defined by
        /// OPC 10000-3 5.2.2 are supported.
        /// </summary>
        private static object ImportIdentifier(NodeId nodeId)
        {
            if (nodeId.TryGetValue(out uint numericId))
            {
                return numericId;
            }
            if (nodeId.TryGetValue(out string stringId))
            {
                return stringId;
            }
            if (nodeId.TryGetValue(out Guid guidId))
            {
                return guidId;
            }
            if (nodeId.TryGetValue(out ByteString opaqueId))
            {
                return opaqueId;
            }
            return null;
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
        private static string ImportArrayDimensions(ArrayOf<uint> arrayDimensions)
        {
            if (arrayDimensions.IsEmpty)
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
        /// Returns the ArrayDimensions to carry into the design for the given
        /// NodeSet ValueRank. The design ValueRank enumeration cannot hold a
        /// rank above one (it maps to OneOrMoreDimensions) and code generation
        /// recovers the rank from the number of ArrayDimensions entries, so a
        /// ValueRank of n &gt; 1 without ArrayDimensions (optional in NodeSet2)
        /// gets n unknown-length (0) dimensions instead of losing the rank.
        /// </summary>
        private static string ImportArrayDimensions(int valueRank, string arrayDimensions)
        {
            if (valueRank > 1 && string.IsNullOrWhiteSpace(arrayDimensions))
            {
                return string.Join(",", Enumerable.Repeat("0", valueRank));
            }

            return arrayDimensions;
        }

        /// <summary>
        /// The design schema carries MinimumSamplingInterval as whole
        /// milliseconds. Round a fractional interval up: truncating 0.5 to 0
        /// would turn "at least 0.5 ms" into "continuous / exception based".
        /// </summary>
        private static int ImportMinimumSamplingInterval(double input)
        {
            if (double.IsNaN(input))
            {
                return 0;
            }
            if (input <= 0)
            {
                // -1 (indeterminate) and 0 (continuous) are carried unchanged.
                return input <= int.MinValue ? int.MinValue : (int)input;
            }

            double rounded = Math.Ceiling(input);
            return rounded >= int.MaxValue ? int.MaxValue : (int)rounded;
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

        /// <summary>
        /// Creates the server table used while importing. Index 0 is reserved
        /// for the local server; without an entry there the first remote
        /// server (svr=1 in the NodeSet) was appended at index 0 and treated
        /// as local.
        /// </summary>
        private static StringTable CreateServerUris()
        {
            var serverUris = new StringTable();
            serverUris.Append(kLocalServerUri);
            return serverUris;
        }

        /// <summary>
        /// Placeholder for the (unknown) local server at index 0 of the server
        /// table. The converter has no running server.
        /// </summary>
        /// <summary>
        /// The bits a decimal OptionSet mask can represent exactly.
        /// </summary>
        private const int kMaxDecimalMaskBits = 96;

        private const string kLocalServerUri = "urn:opcfoundation.org:SourceGeneration:LocalServer";

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

        /// <summary>
        /// Encodeable factory used to decode NodeSet2 <c>Value</c> elements. A NodeSet2 encodes a
        /// Method's <c>InputArguments</c>/<c>OutputArguments</c> Property as a list of
        /// <see cref="Argument"/> ExtensionObjects, so the decoder must be able to resolve that
        /// encoding id; an empty factory silently yields zero arguments and every generated method
        /// wrapper (NodeState handler and ObjectType proxy) would lose its parameters.
        /// </summary>
        private static readonly IEncodeableFactory s_valueDecodingFactory = CreateValueDecodingFactory();

        private readonly NodeSetReaderSettings m_settings;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly IFileSystem m_fileSystem;
        private readonly StringTable m_serverUris = CreateServerUris();
        private readonly UANodeSet m_nodeset;
        private readonly Dictionary<string, NodeId> m_aliases = [];
        private readonly Dictionary<NodeId, UANode> m_index;
        private readonly Dictionary<string, XmlQualifiedName> m_symbolicIds;
        private readonly HashSet<string> m_digitPreservingNames = [];
        private readonly HashSet<string> m_collisionSuffixed = new(StringComparer.Ordinal);
        private Dictionary<NodeId, List<UAVariable>> m_variablesByParent;
        private Dictionary<NodeId, UANode> m_dataTypesByEncoding;
    }
}
