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
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Export;
using Opc.Ua.Schema.Types;
using Opc.Ua.SourceGeneration;
using Opc.Ua.Types;

namespace Opc.Ua.Schema.Model
{
    /// <summary>
    /// Generates files used to describe data types.
    /// </summary>
    public class ModelDesignValidator : SchemaValidator, IModelDesign
    {
        /// <summary>
        /// Create model design validator
        /// </summary>
        public ModelDesignValidator(
            IFileSystem fileSystem,
            uint startId,
            IReadOnlyList<string> exclusions,
            ITelemetryContext telemetry,
            SpecificationVersion standardVersion)
            : base(fileSystem, null, null)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ModelDesignValidator>();
            m_context = new ServiceMessageContext(telemetry);
            m_startId = startId;
            m_exclusions = exclusions;
            m_standardVersion = standardVersion;
        }

        /// <inheritdoc/>
        public IEnumerable<ModelTableEntry> Dependencies => m_dependencies.Values;

        /// <inheritdoc/>
        public Namespace[] Namespaces => m_dictionary?.Namespaces;

        /// <inheritdoc/>
        public Namespace TargetNamespace { get; private set; }

        /// <inheritdoc/>
        public DateTime? TargetPublicationDate =>
            m_dictionary.TargetPublicationDateSpecified &&
            m_dictionary.TargetPublicationDate != DateTime.MinValue ?
                m_dictionary.TargetPublicationDate : null;

        /// <inheritdoc/>
        public string TargetVersion => m_dictionary.TargetVersion;

        /// <inheritdoc/>
        public bool UseAllowSubtypes { get; set; }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris { get; private set; }

        /// <inheritdoc/>
        public NodeDesign[] Nodes => m_dictionary.Items;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, RolePermissionSet> RolePermissions
            => m_defaultRolePermissions;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, AccessRestrictions?> AccessRestrictions
            => m_defaultAccessRestrictions;

        /// <summary>
        /// Max recursion
        /// </summary>
        public int MaxRecursionDepth { get; set; } = 100;

        /// <summary>
        /// Is Release candidate
        /// </summary>
        public bool ReleaseCandidate { get; set; }

        /// <summary>
        /// ModelVersion
        /// </summary>
        public string ModelVersion { get; set; }

        /// <summary>
        /// ModelPublicationDate
        /// </summary>
        public string ModelPublicationDate { get; set; }

        /// <summary>
        /// Returns a list of services filter by their service category.
        /// </summary>
        public Service[] GetListOfServices(params ServiceCategory[] serviceCategories)
        {
            return [.. m_nodes.Values
                .OfType<DataTypeDesign>()
                .Where(x =>
                    !IsExcluded(x) &&
                    x.Service != null &&
                    (
                        serviceCategories == null ||
                        serviceCategories.Length == 0 ||
                        serviceCategories.Contains(x.Service.Category)
                    ))
                .Select(x => x.Service)
                .Distinct()];
        }

        /// <summary>
        /// Returns a list of nodes in the design that should be
        /// used to generate code for
        /// </summary>
        public IEnumerable<NodeDesign> GetNodeDesigns()
        {
            foreach (NodeDesign node in m_dictionary.Items)
            {
                // Fiter nodes that are only declared but not part
                // of code generation
                if (!IsExcluded(node) && !node.IsDeclaration)
                {
                    yield return node;
                }
            }
        }

        /// <summary>
        /// Validate model designs
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public void Validate(
            IReadOnlyList<string> designFilePaths,
            string identifierFilePath)
        {
            if (designFilePaths == null || designFilePaths.Count == 0)
            {
                throw new ArgumentException(
                    "No design files specified",
                    nameof(designFilePaths));
            }

            if (designFilePaths.Count == 2 &&
                designFilePaths[0] == BuiltInDesignFiles.StandardTypesXml &&
                designFilePaths[1] == BuiltInDesignFiles.UACoreServicesXml &&
                identifierFilePath == BuiltInDesignFiles.StandardTypesCsv)
            {
                // Stack generation flow
                ValidateCoreModel(designFilePaths, identifierFilePath);
                return;
            }

            ValidateModel(designFilePaths, identifierFilePath);
        }

        /// <summary>
        /// Is excluded design
        /// </summary>
        /// <param name="node"></param>
        public bool IsExcluded(NodeDesign node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.Purpose == DataTypePurpose.Testing)
            {
                return true;
            }

            if (m_exclusions != null)
            {
                foreach (string exclusion in m_exclusions)
                {
                    if (exclusion == node.ReleaseStatus.ToString())
                    {
                        return true;
                    }

                    if (exclusion == node.Purpose.ToString())
                    {
                        return true;
                    }

                    if (node.Category != null &&
                        node.Category.Contains(exclusion, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Is excluded data type
        /// </summary>
        public bool IsExcluded(DataType dataType)
        {
            if (m_exclusions != null)
            {
                foreach (string exclusion in m_exclusions)
                {
                    if (exclusion == dataType.ReleaseStatus.ToString())
                    {
                        return true;
                    }

                    if (exclusion == dataType.Purpose.ToString())
                    {
                        return true;
                    }

                    if (dataType.Category != null &&
                        dataType.Category.Contains(exclusion, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Is excluded field
        /// </summary>
        public bool IsExcluded(Parameter field)
        {
            if (field == null)
            {
                return false;
            }

            if (m_exclusions != null)
            {
                foreach (string exclusion in m_exclusions)
                {
                    if (exclusion == field.ReleaseStatus.ToString())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Find node design
        /// </summary>
        public bool TryFindNode(
            XmlQualifiedName symbolicId,
            string sourceName,
            string referenceName,
            out NodeDesign target)
        {
            if (IsNull(symbolicId))
            {
                throw Exception(
                    "The {0} reference for node is missing: {1}.",
                    referenceName,
                    sourceName);
            }
            return m_nodes.TryGetValue(symbolicId, out target);
        }

        /// <summary>
        /// Validate model
        /// </summary>
        /// <param name="designFilePaths"></param>
        /// <param name="identifierFilePath"></param>
        private void ValidateModel(
            IReadOnlyList<string> designFilePaths,
            string identifierFilePath)
        {
            string inputPath = designFilePaths[0];

            // initialize tables.
            m_identifiers = [];
            m_nodes = [];
            m_namespaceTables = [];
            m_nodesByNodeId = [];
            m_browseNames = [];
            m_designFilePaths = [];

            LoadBuiltInModel();

            m_designFilePaths[Ua.Types.Namespaces.OpcUa] = string.Empty;

            // load the design files.
            List<Namespace> namespaces = GetNamespaceList(designFilePaths);

            for (int ii = namespaces.Count - 1; ii > 0; ii--)
            {
                if (namespaces[ii].FilePath == null)
                {
                    continue;
                }

                ModelDesign dependency = LoadDesignFile(
                    namespaces,
                    namespaces[ii].FilePath,
                    null);

                if (dependency.Namespaces != null)
                {
                    Namespace ns = dependency.Namespaces
                        .FirstOrDefault(x => x.Value == dependency.TargetNamespace);
                    namespaces[ii].Name = ns.Name;
                    namespaces[ii].Prefix = ns.Prefix;
                    namespaces[ii].XmlPrefix = ns.XmlPrefix;
                }
            }

            ModelDesign targetModel = LoadDesignFile(
                namespaces,
                designFilePaths[0],
                identifierFilePath);

            // set a default xml namespace.
            if (string.IsNullOrEmpty(targetModel.TargetXmlNamespace))
            {
                targetModel.TargetXmlNamespace = targetModel.TargetNamespace;

                if (!string.IsNullOrEmpty(ModelVersion))
                {
                    targetModel.TargetVersion = ModelVersion;
                }

                if (!string.IsNullOrEmpty(ModelPublicationDate))
                {
                    var dt = DateTime.Parse(
                        ModelPublicationDate,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None);
                    targetModel.TargetPublicationDate = new DateTime(
                        dt.Year,
                        dt.Month,
                        dt.Day,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc);
                    targetModel.TargetPublicationDateSpecified = true;
                }
            }

            // mark the target namespace as found.
            m_designFilePaths[targetModel.TargetNamespace] = inputPath;

            // load any included design files.
            if (targetModel.Namespaces != null)
            {
                m_dependencies = [];

                for (int ii = 0; ii < targetModel.Namespaces.Length; ii++)
                {
                    Namespace ns = targetModel.Namespaces[ii];

                    if (ns.Value != targetModel.TargetNamespace)
                    {
                        Namespace dependency = namespaces
                            .FirstOrDefault(x => x.Value == ns.Value);
                        if (dependency != null)
                        {
                            ns.Name = dependency.Name;
                            ns.Prefix = dependency.Prefix;
                            ns.XmlPrefix = dependency.XmlPrefix;
                            ns.FilePath = dependency.FilePath;
                        }

                        DateTime? pd = ns.PublicationDate != null ?
                            DateTime.Parse(
                                ns.PublicationDate,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None) :
                            null;

                        m_dependencies[ns.Value] = new ModelTableEntry
                        {
                            ModelUri = ns.Value,
                            XmlSchemaUri = ns.XmlNamespace,
                            Version = ns.Version,
                            ModelVersion = CoreUtils.FixupAsSemanticVersion(ns.Version),
                            PublicationDate = pd ?? DateTime.MinValue,
                            PublicationDateSpecified = pd != null
                        };
                    }

                    // override the publication date.
                    if (ns.PublicationDate == null && ns.Version == null)
                    {
                        continue;
                    }
                    if (!m_dependencies.TryGetValue(ns.Value, out ModelTableEntry modelInfo))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(ns.Version))
                    {
                        modelInfo.Version = ns.Version;
                        modelInfo.ModelVersion =
                            CoreUtils.FixupAsSemanticVersion(ns.Version);
                    }

                    if (!string.IsNullOrWhiteSpace(ns.PublicationDate))
                    {
                        modelInfo.PublicationDate = XmlConvert.ToDateTime(
                            ns.PublicationDate,
                            XmlDateTimeSerializationMode.Utc);
                    }
                }
            }

            // save the dictionary in a member variable during processing.
            m_dictionary = targetModel;

            // build table of namespaces.
            UpdateNamespaceTables(m_dictionary);
            m_dictionary.TargetXmlNamespace = GetXmlNamespace(m_dictionary.TargetNamespace);

            if (m_dictionary.Items == null || m_dictionary.Items.Length == 0)
            {
                m_logger.LogWarning("Nothing to do because design file has no entries.");
                return;
            }

            UpdateNamespaceObject(m_dictionary);
        }

        /// <summary>
        /// Validate core model
        /// </summary>
        private void ValidateCoreModel(
            IReadOnlyList<string> designFilePaths,
            string identifierFilePath)
        {
            string inputPath = designFilePaths[0];

            // initialize tables.
            m_identifiers = [];
            m_nodes = [];
            m_namespaceTables = [];
            m_nodesByNodeId = [];
            m_browseNames = [];
            m_startId = 15000;
            m_designFilePaths = new Dictionary<string, string>
            {
                [Ua.Types.Namespaces.OpcUa] = string.Empty
            };

            // load the design files.
            m_logger.LogInformation("Loading StandardTypes...");
            ModelDesign targetModel = LoadModelDesign(BuiltInDesignFiles.BuiltInTypesXml);
            targetModel.Items ??= [];

            for (int ii = 0; ii < designFilePaths.Count; ii++)
            {
                targetModel = LoadCoreDesignFile(targetModel, designFilePaths[ii]);
            }

            // set a default xml namespace.
            if (string.IsNullOrEmpty(targetModel.TargetXmlNamespace))
            {
                targetModel.TargetXmlNamespace = targetModel.TargetNamespace;

                if (!string.IsNullOrEmpty(ModelVersion))
                {
                    targetModel.TargetVersion = ModelVersion;
                }
                else
                {
                    ModelVersion = targetModel.TargetVersion;
                }

                if (!string.IsNullOrEmpty(ModelPublicationDate))
                {
                    var dt = DateTime.Parse(
                        ModelPublicationDate,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None);
                    targetModel.TargetPublicationDate = new DateTime(
                        dt.Year,
                        dt.Month,
                        dt.Day,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc);
                    targetModel.TargetPublicationDateSpecified = true;
                }
                else
                {
                    ModelPublicationDate = targetModel.TargetPublicationDateSpecified ?
                        targetModel.TargetPublicationDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture) :
                        null;
                }
            }

            // mark the target namespace as found.
            m_designFilePaths[targetModel.TargetNamespace] = inputPath;

            // load any included design files.
            if (targetModel.Namespaces != null)
            {
                m_dependencies = [];

                for (int ii = 0; ii < targetModel.Namespaces.Length; ii++)
                {
                    Namespace ns = targetModel.Namespaces[ii];

                    if (ns.Value != targetModel.TargetNamespace)
                    {
                        DateTime? pd = ns.PublicationDate != null ?
                            DateTime.Parse(
                                ns.PublicationDate,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None) :
                            null;

                        m_dependencies[ns.Value] = new ModelTableEntry
                        {
                            ModelUri = ns.Value,
                            XmlSchemaUri = ns.XmlNamespace,
                            Version = ns.Version,
                            ModelVersion = CoreUtils.FixupAsSemanticVersion(ns.Version),
                            PublicationDate = pd ?? DateTime.MinValue,
                            PublicationDateSpecified = pd != null
                        };
                    }

                    // override the publication date.
                    if (ns.PublicationDate == null && ns.Version == null)
                    {
                        continue;
                    }
                    if (!m_dependencies.TryGetValue(ns.Value, out ModelTableEntry modelInfo))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(ns.Version))
                    {
                        modelInfo.Version = ns.Version;
                        modelInfo.ModelVersion = CoreUtils.FixupAsSemanticVersion(ns.Version);
                    }

                    if (!string.IsNullOrWhiteSpace(ns.PublicationDate))
                    {
                        modelInfo.PublicationDate = XmlConvert.ToDateTime(
                            ns.PublicationDate,
                            XmlDateTimeSerializationMode.Utc);
                    }
                }
            }

            // save the model in a member variable during processing.
            m_dictionary = targetModel;

            // build table of namespaces.
            UpdateNamespaceTables(m_dictionary);
            m_dictionary.TargetXmlNamespace = GetXmlNamespace(m_dictionary.TargetNamespace);

            // import types from target dictionary.
            var nodes = new List<NodeDesign>();

            foreach (NodeDesign node in m_dictionary.Items)
            {
                if (Import(m_dictionary, node, null))
                {
                    nodes.Add(node);
                }
            }

            if (nodes.Count == 0)
            {
                m_logger.LogWarning("Nothing to do because design files have no entries.");
                return;
            }

            m_dictionary.Items = [.. nodes];

            // do additional fix up after import.
            ValidateDictionary(m_dictionary);

            // validate node in target dictionary.
            foreach (NodeDesign node in m_dictionary.Items)
            {
                node.Hierarchy = BuildInstanceHierarchy(node, 0);
            }

            AssignIdentifiers(m_dictionary, identifierFilePath);
            UpdateNamespaceObject(m_dictionary);
        }

        /// <summary>
        /// Load built in model
        /// </summary>
        /// <returns></returns>
        private ModelDesign LoadBuiltInModel()
        {
            ModelDesign model = LoadBuiltInModels();

            LoadNodes(model);

            using Stream stream = OpenRead("StandardTypes.csv");
            AssignIdentifiers(model, ParseIdentifiersFromStream(stream));

            // flag built-in types as declarations.
            foreach (NodeDesign node in model.Items)
            {
                node.Description = null;
                node.IsDeclaration = true;
            }

            return model;
        }

        private void LoadNodes(ModelDesign model)
        {
            UpdateNamespaceTables(model);

            // import types from target dictionary.
            var nodes = new List<NodeDesign>();

            foreach (NodeDesign node in model.Items)
            {
                if (Import(model, node, null))
                {
                    nodes.Add(node);
                }
            }

            model.Items = [.. nodes];

            // validate node in target dictionary.
            ValidateDictionary(model);

            // build hierarchy.
            foreach (NodeDesign node in model.Items)
            {
                node.Hierarchy = BuildInstanceHierarchy(node, 0);
            }
        }

        /// <summary>
        /// Load model design file
        /// </summary>
        private ModelDesign LoadModelDesign(
            string designFilePath,
            string identifierFilePath)
        {
            ModelDesign model = LoadModelDesign(designFilePath);

            model.SourceFilePath = designFilePath;
            model.IsSourceNodeSet = false;

            if (string.IsNullOrEmpty(model.TargetVersion))
            {
                model.TargetVersion = "1.0.0";
            }

            if (!model.TargetPublicationDateSpecified ||
                model.TargetPublicationDate == DateTime.MinValue)
            {
                model.TargetPublicationDate = FileSystem.GetLastWriteTime(designFilePath);
                model.TargetPublicationDateSpecified = true;
            }

            LoadNodes(model);

            // assigning identifiers.
            identifierFilePath ??= Path.Combine(
                Path.GetDirectoryName(designFilePath),
                Path.GetFileNameWithoutExtension(designFilePath) + ".csv");
            AssignIdentifiers(model, identifierFilePath);
            return model;
        }

        private static LocalizedText ImportDocumentation(Documentation documentation)
        {
            if (documentation != null &&
                documentation.Text != null &&
                documentation.Text.Length > 0)
            {
                return new LocalizedText
                {
                    Value = documentation.Text[0]?.Trim(),
                    IsAutogenerated = false
                };
            }

            return null;
        }

        private static Parameter ImportField(FieldType field)
        {
            if (field == null)
            {
                return null;
            }

            var parameter = new Parameter
            {
                Name = field.Name
            };
            // parameter.Description = ImportDocumentation(field.Documentation);

            if (field.DataType != null)
            {
                parameter.DataType = ImportTypeName(field.DataType);
            }
            else
            {
                parameter.DataType = s_baseDataTypeQn;
            }

            if (field.ValueRank is 0 or 1)
            {
                parameter.ValueRank = ValueRank.Array;
            }
            else if (field.ValueRank == 2)
            {
                parameter.ValueRank = ValueRank.Array;
                parameter.ArrayDimensions = "0";

                for (int ii = 1; ii < field.ValueRank; ii++)
                {
                    parameter.ArrayDimensions = ",0";
                }
            }
            else
            {
                parameter.ValueRank = ValueRank.Scalar;
            }

            parameter.IsOptional = field.IsOptional;
            parameter.AllowSubTypes = field.AllowSubTypes;
            parameter.DefaultValue = field.DefaultValue;
            parameter.ReleaseStatus = (ReleaseStatus)(int)field.ReleaseStatus;

            return parameter;
        }

        private static Parameter ImportEnumeratedValue(EnumeratedValue value)
        {
            if (value == null)
            {
                return null;
            }

            var parameter = new Parameter
            {
                Name = value.Name,
                Description = ImportDocumentation(value.Documentation),
                ReleaseStatus = (ReleaseStatus)(int)value.ReleaseStatus
            };

            if (value.ValueSpecified)
            {
                parameter.Identifier = value.Value;
                parameter.IdentifierSpecified = true;
            }

            if (!string.IsNullOrEmpty(value.BitMask))
            {
                parameter.BitMask = value.BitMask;
                parameter.IdentifierSpecified = false;
            }

            return parameter;
        }

        private static XmlQualifiedName ImportTypeName(XmlQualifiedName typeName)
        {
            if (typeName == null)
            {
                return null;
            }

            switch (typeName.Name)
            {
                case "ExtensionObject":
                    return s_structureQn;
                case "Variant":
                    return s_baseDataTypeQn;
            }

            return new XmlQualifiedName(typeName.Name, Ua.Types.Namespaces.OpcUa);
        }

        private void ImportFields(DataTypeDesign design, FieldType[] fields)
        {
            if (fields != null && fields.Length > 0)
            {
                var parameters = new List<Parameter>();

                for (int jj = 0; jj < fields.Length; jj++)
                {
                    FieldType field = fields[jj];
                    Parameter parameter = ImportField(field);

                    if (!IsExcluded(parameter))
                    {
                        parameters.Add(parameter);
                    }
                }

                design.Fields = [.. parameters];
            }
        }

        private ModelDesign ImportTypeDictionary(Stream stream)
        {
            var knownFiles = new Dictionary<string, string>();
            var validator = new TypeDictionaryValidator(FileSystem, knownFiles);
            validator.Validate(stream);

            string namespaceUri = validator.Dictionary.TargetNamespace;

            if (namespaceUri == "http://opcfoundation.org/UA/Core/")
            {
                namespaceUri = Ua.Types.Namespaces.OpcUa;
            }

            var nodes = new List<NodeDesign>();

            for (int ii = 0; ii < validator.Dictionary.Items.Length; ii++)
            {
                DataType dataType = validator.Dictionary.Items[ii];

                if (IsExcluded(dataType))
                {
                    continue;
                }

                var design = new DataTypeDesign
                {
                    SymbolicId = new XmlQualifiedName(dataType.Name, namespaceUri)
                };
                design.SymbolicName = design.SymbolicId;
                design.BaseType = s_baseDataTypeQn;
                design.NoArraysAllowed = !dataType.AllowArrays;
                design.NoClassGeneration = dataType.NotInAddressSpace;
                design.NotInAddressSpace = dataType.NotInAddressSpace;
                design.IsAbstract = false;
                design.PartNo = dataType.PartNo;
                design.Category = dataType.Category;
                design.ReleaseStatus = (ReleaseStatus)(int)dataType.ReleaseStatus;
                design.Purpose = (DataTypePurpose)(int)dataType.Purpose;

                if (design.PartNo == 0)
                {
                    design.PartNo = 4;
                }

                m_logger.LogDebug(
                    "Imported {Type}: {Name}",
                    design.GetType().Name,
                    design.SymbolicId.Name);

                if (dataType is TypeDeclaration simpleType)
                {
                    if (simpleType.SourceType != null)
                    {
                        design.BaseType = ImportTypeName(simpleType.SourceType);
                    }

                    nodes.Add(design);
                }

                if (dataType is ComplexType complexType)
                {
                    if (complexType.BaseType != null)
                    {
                        design.BaseType = ImportTypeName(complexType.BaseType);
                    }
                    else
                    {
                        design.BaseType = s_structureQn;
                    }

                    ImportFields(design, complexType.Field);
                    design.IsAbstract = complexType.IsAbstract;
                    design.IsUnion = complexType.IsUnion;
                    nodes.Add(design);
                }

                if (dataType is ServiceType serviceType)
                {
                    var service = new Service
                    {
                        Category = serviceType.InterfaceType switch
                        {
                            InterfaceType.Session => ServiceCategory.Session,
                            InterfaceType.SecureChannel => ServiceCategory.SecureChannel,
                            InterfaceType.Discovery => ServiceCategory.Discovery,
                            InterfaceType.Registration => ServiceCategory.Registration,
                            InterfaceType.Test => ServiceCategory.Test,
                            _ => ServiceCategory.None
                        },
                        Name = dataType.Name
                    };

                    design.SymbolicId = new XmlQualifiedName(dataType.Name + "Request", namespaceUri);
                    design.SymbolicName = design.SymbolicId;
                    design.Service = service;
                    design.BaseType = s_structureQn;
                    design.BasicDataType = design.DetermineBasicDataType();
                    design.NoArraysAllowed = true;
                    design.NoClassGeneration = false;
                    design.NotInAddressSpace = true;
                    design.IsAbstract = false;
                    design.PartNo = 4;
                    design.Category = dataType.Category;
                    design.ReleaseStatus = (ReleaseStatus)(int)dataType.ReleaseStatus;
                    design.Purpose = DataTypePurpose.ServicesOnly;

                    ImportFields(design, serviceType.Request);

                    nodes.Add(design);

                    var design2 = new DataTypeDesign
                    {
                        SymbolicId = new XmlQualifiedName(dataType.Name + "Response", namespaceUri)
                    };
                    design2.SymbolicName = design2.SymbolicId;
                    design2.IsServiceResponse = true;
                    design2.Service = service;
                    design2.BaseType = s_structureQn;
                    design2.BasicDataType = design.DetermineBasicDataType();
                    design2.NoArraysAllowed = true;
                    design2.NoClassGeneration = false;
                    design2.NotInAddressSpace = true;
                    design2.IsAbstract = false;
                    design2.PartNo = 4;
                    design2.Category = dataType.Category;
                    design2.ReleaseStatus = (ReleaseStatus)(int)dataType.ReleaseStatus;
                    design2.Purpose = DataTypePurpose.ServicesOnly;

                    ImportFields(design2, serviceType.Response);

                    nodes.Add(design2);

                    service.Request = design;
                    service.Response = design2;
                }

                if (dataType is EnumeratedType enumeratedType)
                {
                    design.BaseType = s_enumerationQn;

                    if (enumeratedType.IsOptionSet)
                    {
                        design.IsOptionSet = true;

                        if (enumeratedType.BaseType != null)
                        {
                            design.BaseType = ImportTypeName(enumeratedType.BaseType);
                        }
                        else
                        {
                            design.BaseType = new XmlQualifiedName("UInt32", Ua.Types.Namespaces.OpcUa);
                        }
                    }

                    if (enumeratedType.Value != null && enumeratedType.Value.Length > 0)
                    {
                        var parameters = new List<Parameter>();

                        for (int jj = 0; jj < enumeratedType.Value.Length; jj++)
                        {
                            EnumeratedValue value = enumeratedType.Value[jj];
                            Parameter parameter = ImportEnumeratedValue(value);
                            parameters.Add(parameter);
                        }

                        design.Fields = [.. parameters];
                    }

                    nodes.Add(design);
                }
            }

            var model = new ModelDesign
            {
                Items = [.. nodes],
                TargetNamespace = Ua.Types.Namespaces.OpcUa
            };
            model.TargetNamespace = validator.Dictionary.TargetVersion;
            model.TargetPublicationDate = validator.Dictionary.TargetPublicationDate;
            model.TargetPublicationDateSpecified = true;

            return model;
        }

        private ModelDesign LoadBuiltInModels()
        {
            var nodes = new List<NodeDesign>();

            // load the design files.
            ModelDesign builtin = LoadModelDesign(
                BuiltInDesignFiles.BuiltInTypesXml);

            nodes.AddRange(builtin.Items);

            ModelDesign datatypes = null;

            using (Stream stream = OpenRead(
                BuiltInDesignFiles.UACoreServicesXml))
            {
                datatypes = ImportTypeDictionary(stream);
            }

            if (datatypes != null)
            {
                nodes.AddRange(datatypes.Items);
            }

            ModelDesign standard = LoadModelDesign(
                BuiltInDesignFiles.StandardTypesXml);
            nodes.AddRange(standard.Items);

            builtin.PermissionSets = standard.PermissionSets;
            builtin.Items = [.. nodes];

            return builtin;
        }

        private static RolePermissionSet ResolvePermissions(
            ModelDesign dictionary,
            RolePermissionSet input)
        {
            if (input?.Name != null)
            {
                var permissions = new Dictionary<XmlQualifiedName, Permissions[]>();

                if (dictionary.PermissionSets != null)
                {
                    RolePermissionSet template = dictionary.PermissionSets
                        .FirstOrDefault(x => x.Name == input.Name);

                    if (template?.RolePermission != null)
                    {
                        foreach (RolePermission jj in template.RolePermission)
                        {
                            permissions[jj.Role] = jj.Permission;
                        }
                    }
                }

                if (input?.RolePermission != null)
                {
                    foreach (RolePermission jj in input?.RolePermission)
                    {
                        permissions[jj.Role] = jj.Permission;
                    }
                }

                if (permissions.Count > 0)
                {
                    List<RolePermission> combined = [];

                    foreach (KeyValuePair<XmlQualifiedName, Permissions[]> jj in permissions)
                    {
                        combined.Add(new RolePermission
                        {
                            Role = jj.Key,
                            Permission = jj.Value
                        });
                    }

                    return new RolePermissionSet
                    {
                        RolePermission = [.. combined],
                        DoNotInheirit = input?.DoNotInheirit ?? false
                    };
                }
            }

            return null;
        }

        private void ResolvePermissions(ModelDesign dictionary, NodeDesign input)
        {
            input.RolePermissions = ResolvePermissions(
                dictionary,
                input.RolePermissions);
            input.DefaultRolePermissions = ResolvePermissions(
                dictionary,
                input.DefaultRolePermissions);

            if (input.DefaultRolePermissions != null)
            {
                m_defaultRolePermissions[input.SymbolicId.Name] =
                    input.DefaultRolePermissions;
            }

            if (input.DefaultAccessRestrictionsSpecified)
            {
                m_defaultAccessRestrictions[input.SymbolicId.Name] =
                    input.DefaultAccessRestrictions;
            }

            if (input.HasChildren)
            {
                foreach (NodeDesign child in input.Children.Items)
                {
                    ResolvePermissions(dictionary, child);
                }
            }
        }

        private void ValidateDictionary(ModelDesign dictionary)
        {
            bool hasDataTypesDefined = false;
            bool hasMethodsDefined = false;

            foreach (NodeDesign node in dictionary.Items)
            {
                if (node is DataTypeDesign)
                {
                    hasDataTypesDefined = true;
                }

                if (node is MethodDesign)
                {
                    hasMethodsDefined = true;
                }

                Validate(node);
            }

            foreach (NodeDesign node in dictionary.Items)
            {
                ResolvePermissions(dictionary, node);
            }

            var nodes = new List<NodeDesign>();
            nodes.AddRange(dictionary.Items);

            if (hasDataTypesDefined)
            {
                AddDataTypeDictionary(
                    dictionary,
                    TargetNamespace,
                    EncodingType.Binary,
                    nodes);
                AddDataTypeDictionary(
                    dictionary,
                    TargetNamespace,
                    EncodingType.Xml,
                    nodes);

                if (m_standardVersion != SpecificationVersion.V103)
                {
                    AddDataTypeDictionary(
                        dictionary,
                        TargetNamespace,
                        EncodingType.Json,
                        nodes);
                }

                foreach (NodeDesign node in dictionary.Items)
                {
                    if (node is DataTypeDesign dataTypeDesign)
                    {
                        AddEnumStrings(dataTypeDesign);
                    }
                }
            }

            dictionary.Items = [.. nodes];

            // validate node in target dictionary.
            if (hasMethodsDefined)
            {
                foreach (NodeDesign node in dictionary.Items)
                {
                    if (node is MethodDesign methodDesign)
                    {
                        AddMethodArguments(methodDesign);
                    }
                }
            }

            foreach (NodeDesign node in m_nodes.Values)
            {
                if (ReleaseCandidate &&
                    node.ReleaseStatus == ReleaseStatus.RC)
                {
                    node.ReleaseStatus = ReleaseStatus.Released;
                }
            }
        }

        private void UpdateNamespaceObject(ModelDesign dictionary)
        {
            ObjectDesign metadata = null;

            var dynamicIds = new HashSet<uint>();

            foreach (NodeDesign node in dictionary.Items)
            {
                if (node.IsDynamic)
                {
                    CollectDynamicIds(node, dynamicIds);
                }

                if (metadata == null)
                {
                    metadata = node as ObjectDesign;

                    if (metadata != null &&
                        metadata.TypeDefinition != new XmlQualifiedName(
                            "NamespaceMetadataType",
                            Ua.Types.Namespaces.OpcUa))
                    {
                        metadata = null;
                    }
                }
            }

            if (metadata == null)
            {
                return;
            }

            var ranges = new List<string>();

            int start = 1;
            bool readingStaticRange = true;

            for (uint ii = 1; dynamicIds.Count > 0; ii++)
            {
                if (readingStaticRange)
                {
                    if (dynamicIds.Contains(ii))
                    {
                        readingStaticRange = false;

                        int end = (int)(ii - 1);

                        if (end > start)
                        {
                            ranges.Add(new NumericRange(start, end).ToString());
                        }
                        else
                        {
                            ranges.Add(new NumericRange(start).ToString());
                        }

                        dynamicIds.Remove(ii);

                        if (dynamicIds.Count == 0)
                        {
                            start = (int)ii + 1;
                            break;
                        }
                    }
                }
                else if (!dynamicIds.Remove(ii))
                {
                    start = (int)ii;
                    readingStaticRange = true;
                }
                else if (dynamicIds.Count == 0)
                {
                    start = (int)ii + 1;
                    break;
                }
            }

            ranges.Add(new NumericRange(start, int.MaxValue).ToString());

            // Fix avoid a null metadata in some cases
            if (metadata.Hierarchy.NodeList != null)
            {
                foreach (HierarchyNode child in metadata.Hierarchy.NodeList)
                {
                    if (child.Instance.BrowseName == "StaticNumericNodeIdRange")
                    {
                        var variable = child.Instance as VariableDesign;
                        variable.SetDefaultValue(ranges.ToArray(), m_context);
                    }

                    if (m_dictionary.TargetPublicationDateSpecified)
                    {
                        if (child.Instance.BrowseName == BrowseNames.NamespacePublicationDate)
                        {
                            var variable = child.Instance as VariableDesign;
                            variable.SetDefaultValue(m_dictionary.TargetPublicationDate, m_context);
                        }
                    }

                    if (!string.IsNullOrEmpty(m_dictionary.TargetVersion))
                    {
                        if (child.Instance.BrowseName == BrowseNames.NamespaceVersion)
                        {
                            var variable = child.Instance as VariableDesign;
                            variable.SetDefaultValue(m_dictionary.TargetVersion, m_context);
                        }
                    }
                }
            }
        }

        private static void CollectDynamicIds(NodeDesign node, HashSet<uint> dynamicIds)
        {
            dynamicIds.Add(node.NumericId);

            if (node.Hierarchy.NodeList != null)
            {
                foreach (HierarchyNode child in node.Hierarchy.NodeList)
                {
                    if (child.Instance.NumericIdSpecified)
                    {
                        dynamicIds.Add(child.Instance.NumericId);
                    }
                }
            }
        }

        private void UpdateNamespaceTables(ModelDesign dictionary)
        {
            // build table of namespaces.
            Namespace targetNamespace = null;
            NamespaceTable namespaceUris = dictionary.NamespaceUris ?? new NamespaceTable();

            if (dictionary.Namespaces != null)
            {
                List<Namespace> namespaces = [.. dictionary.Namespaces];

                for (int ii = 0; ii < namespaces.Count; ii++)
                {
                    Namespace current = namespaces[ii];
                    current.Value = current.Value.Trim();

                    if (current.Value == dictionary.TargetNamespace)
                    {
                        targetNamespace = current;
                    }

                    if (current.Value != Ua.Types.Namespaces.OpcUa)
                    {
                        namespaceUris.GetIndexOrAppend(dictionary.Namespaces[ii].Value);
                    }
                }
            }

            NamespaceUris = dictionary.NamespaceUris = namespaceUris;
            TargetNamespace = targetNamespace;
        }

        private ModelDesign LoadCoreDesignFile(
            ModelDesign dictionary,
            string designFilePath)
        {
            ModelDesign model = null;
            bool loadAsTypeDictionary = designFilePath == BuiltInDesignFiles.UACoreServicesXml;
            if (!loadAsTypeDictionary)
            {
                m_logger.LogInformation("Loading DesignFile: {File}", designFilePath);
                try
                {
                    using Stream stream = OpenRead(designFilePath);
                    model = LoadInput<ModelDesign>(stream);
                    model.Items ??= [];
                }
                catch (Exception e)
                {
                    m_logger.LogDebug(e,
                        "Error loading model {File}, trying as type dictionary",
                        designFilePath);
                    loadAsTypeDictionary = true;
                }
            }
            if (loadAsTypeDictionary)
            {
                try
                {
                    using Stream stream = OpenRead(designFilePath);
                    model = ImportTypeDictionary(stream);
                }
                catch (Exception e)
                {
                    m_logger.LogError(e,
                        "Error parsing type dictionary {File}",
                        designFilePath);
                    throw new InvalidDataException("Error parsing file " + designFilePath, e);
                }
            }

            if (dictionary != null)
            {
                var nodes2 = new List<NodeDesign>();

                // namespaces in primary dictionary replace all namespaces in secondary dictionaries.
                nodes2.AddRange(dictionary.Items);
                nodes2.AddRange(model.Items);

                if (model.PermissionSets?.Length > 0)
                {
                    dictionary.PermissionSets = model.PermissionSets;
                }

                dictionary.Items = [.. nodes2];
                model = dictionary;
            }

            foreach (NodeDesign node in model.Items)
            {
                if (node.Description == null)
                {
                    continue;
                }
                if (!node.Description.DoNotIgnore)
                {
                    node.Description = null;
                }
            }
            return model;
        }

        private void ExcludeNodes(ModelDesign model)
        {
            var nodes = new List<NodeDesign>();

            foreach (NodeDesign node in model.Items)
            {
                if (!IsExcluded(node))
                {
                    nodes.Add(node);
                }
            }

            model.Items = [.. nodes];
        }

        private void IndexNodesByNodeId(
            NamespaceTable namespaceUris,
            IEnumerable<NodeDesign> nodes,
            IDictionary<NodeId, NodeDesign> index,
            NodeDesign parent)
        {
            foreach (NodeDesign node in nodes)
            {
                bool hasNumericId = node.NumericIdSpecified && node.NumericId > 0;
                bool hasStringId = !node.NumericIdSpecified && !string.IsNullOrEmpty(node.StringId);

                if (hasNumericId || hasStringId)
                {
                    NodeId nodeId = hasNumericId ? new NodeId(node.NumericId) : new NodeId(node.StringId, 0);
                    nodeId = nodeId.WithNamespaceIndex(namespaceUris.GetIndexOrAppend(node.SymbolicId.Namespace));

                    index[nodeId] = node;

                    if (node.Children?.Items != null)
                    {
                        IndexNodesByNodeId(
                            namespaceUris,
                            node.Children.Items,
                            index,
                            parent ?? node);
                    }
                }
                else if (parent != null && parent.Hierarchy != null)
                {
                    string id = node.SymbolicId.Name[(parent.SymbolicId.Name.Length + 1)..];

                    if (parent.Hierarchy.Nodes.TryGetValue(id, out HierarchyNode hierarchyNode))
                    {
                        var nodeId = new NodeId(
                            hierarchyNode.Instance.NumericId,
                            namespaceUris.GetIndexOrAppend(node.SymbolicId.Namespace));

                        index[nodeId] = node;
                    }
                    else
                    {
                        m_logger.LogDebug(
                            "Node {Node} missing from parent {Parent} hierarchy.",
                            node.SymbolicId,
                            parent.SymbolicId);
                    }
                }
                else
                {
                    m_logger.LogDebug(
                        "Node {Node} does not have a valid NodeId.",
                        node);
                }
            }
        }

        private ModelDesign LoadDesignFile(
            List<Namespace> namespaces,
            string designFilePath,
            string identifierFilePath)
        {
            m_logger.LogInformation("Loading DesignFile: {File}", designFilePath);

            ModelDesign model;

            string[] fields = designFilePath.Split(',');
            string fileToLoad = fields[0];
            string prefix = fields.Length > 1 ? fields[1] : null;
            string name = fields.Length > 2 ? fields[2] : null;

            if (NodeSetToModelDesign.IsNodeSet(FileSystem, fileToLoad))
            {
                var settings = new NodeSetReaderSettings
                {
                    NodesByQName = m_nodes,
                    NamespaceTables = m_namespaceTables
                };

                foreach (Namespace ns in namespaces)
                {
                    settings.NamespaceUris.GetIndexOrAppend(ns.Value);
                }

                IndexNodesByNodeId(
                    settings.NamespaceUris,
                    m_nodes.Values,
                    settings.NodesById,
                    null);

                var reader = new NodeSetToModelDesign(
                    FileSystem,
                    fileToLoad,
                    settings,
                    m_telemetry);

                model = reader.Import(prefix, name);
                model.SourceFilePath = fileToLoad;
                model.IsSourceNodeSet = true;

                ExcludeNodes(model); // not consistent with model design, todo: consider removing

                foreach (NodeDesign node in model.Items)
                {
                    node.Hierarchy = BuildInstanceHierarchy(node, 0);

                    foreach (KeyValuePair<string, HierarchyNode> ii in node.Hierarchy.Nodes)
                    {
                        if (ii.Value.Instance is InstanceDesign instance &&
                            instance.NumericId <= 0 &&
                            instance.StringId == null &&
                            instance.ModellingRule == ModellingRule.Mandatory)
                        {
                            // Not an error, show informational
                            m_logger.LogInformation(
                                "{Value} missing NodeId for Mandatory child in NodeSet.",
                                ii.Key);
                        }
                    }
                }
            }
            else
            {
                model = LoadModelDesign(fileToLoad, identifierFilePath);

                Namespace ns = model.Namespaces
                    .FirstOrDefault(x => x.Value == model.TargetNamespace);
                if (name != null)
                {
                    ns.Name = name;
                }

                if (prefix != null)
                {
                    ns.XmlPrefix = ns.Prefix = prefix;
                }
            }

            return model;
        }

        private static void MergeNamespace(
            List<Namespace> namespaces,
            Namespace target)
        {
            for (int ii = 0; ii < namespaces.Count; ii++)
            {
                if (namespaces[ii].Value == target.Value)
                {
                    if (namespaces[ii].FilePath == null)
                    {
                        namespaces[ii].FilePath = target.FilePath;
                    }

                    if (string.CompareOrdinal(
                        namespaces[ii].PublicationDate,
                        target.PublicationDate) < 0)
                    {
                        namespaces[ii] = target;
                    }

                    return;
                }
            }
            namespaces.Add(target);
        }

        private List<Namespace> GetNamespaceList(IReadOnlyList<string> designFilePaths)
        {
            var namespaces = new List<Namespace>();

            foreach (string path in designFilePaths)
            {
                string[] fields = path.Split(',');

                string fileToLoad = fields[0];
                string prefix = fields.Length > 1 ? fields[1] : null;
                string name = fields.Length > 2 ? fields[2] : null;

                List<Namespace> fileNamespaces = null;

                if (NodeSetToModelDesign.IsNodeSet(FileSystem, fileToLoad))
                {
                    fileNamespaces = NodeSetToModelDesign.LoadNamespaces(FileSystem, fileToLoad);
                    fileNamespaces[^1].FilePath = path;
                }
                else
                {
                    ModelDesign design = LoadModelDesign(fileToLoad);

                    foreach (Namespace ns in design.Namespaces)
                    {
                        ns.Value = ns.Value.Trim();
                        ns.FilePath = ns.Value == design.TargetNamespace ? path : null;
                    }

                    fileNamespaces = [.. design.Namespaces
                        .Where(x => x.Name != "OpcUa" && x.Value != design.TargetNamespace)
                        .Reverse()];
                    fileNamespaces.Add(design.Namespaces
                        .FirstOrDefault(x => x.Value == design.TargetNamespace));
                }

                foreach (Namespace ns in fileNamespaces)
                {
                    if (ns.Value != Ua.Types.Namespaces.OpcUa)
                    {
                        MergeNamespace(namespaces, ns);
                    }
                }

                Namespace target = fileNamespaces[^1];

                foreach (Namespace ns in fileNamespaces)
                {
                    if (ns.Value == target.Value)
                    {
                        break;
                    }

                    if (ns.Value == Ua.Types.Namespaces.OpcUa)
                    {
                        continue;
                    }

                    int index1 = FindNamespace(namespaces, target.Value);
                    int index2 = FindNamespace(namespaces, ns.Value);

                    if (index1 > index2)
                    {
                        namespaces.Insert(index2, namespaces[index1]);
                        namespaces.RemoveAt(index1 + 1);
                    }
                }
            }

            return namespaces;
        }

        private static int FindNamespace(List<Namespace> namespaces, string uri)
        {
            for (int ii = 0; ii < namespaces.Count; ii++)
            {
                if (uri == namespaces[ii].Value)
                {
                    return ii;
                }
            }

            return -1;
        }

        private string GetXmlNamespace(string modelUri)
        {
            string ns = (
                from x in m_dictionary.Namespaces
                where x.Value == modelUri
                select x.XmlNamespace).FirstOrDefault();

            if (!string.IsNullOrEmpty(ns))
            {
                return ns;
            }

            return null;
        }

        private void AddMethodArguments(MethodDesign method)
        {
            var children = new List<InstanceDesign>();

            if (method.Children != null && method.Children.Items != null)
            {
                children.AddRange(method.Children.Items);
            }

            if (method.InputArguments != null && method.InputArguments.Length > 0)
            {
                var arguments = new List<Argument>();

                for (int ii = 0; ii < method.InputArguments.Length; ii++)
                {
                    Parameter parameter = method.InputArguments[ii];

                    var argument = new Argument
                    {
                        Value = parameter,
                        Name = parameter.Name,
                        DataType = new NodeId(
                            parameter.DataType.ToString(),
                            (ushort)m_context.NamespaceUris.GetIndex(parameter.DataType.Namespace)),
                        ValueRank = ConstructValueRank(parameter.ValueRank, parameter.ArrayDimensions),
                        ArrayDimensions = ConstructArrayDimensionsRW(parameter.ValueRank, parameter.ArrayDimensions),
                        Description = null
                    };

                    if (!string.IsNullOrEmpty(parameter.ArrayDimensions))
                    {
                        argument.ArrayDimensions = [];
                        var range = NumericRange.Parse(parameter.ArrayDimensions);
                        for (int jj = 0; jj < range.Dimensions; jj++)
                        {
                            argument.ArrayDimensions.Add((uint)range.SubRanges[jj].Begin);
                        }
                    }

                    if (parameter.Description != null && !parameter.Description.IsAutogenerated)
                    {
                        argument.Description = new Ua.LocalizedText(
                            parameter.Description.Key,
                            string.Empty,
                            parameter.Description.Value?.Trim());
                    }

                    arguments.Add(argument);
                }

                AddProperty(
                    method,
                    new XmlQualifiedName("InputArguments", Ua.Types.Namespaces.OpcUa),
                    new XmlQualifiedName("Argument", Ua.Types.Namespaces.OpcUa),
                    ValueRank.Array,
                    [(uint)arguments.Count],
                    arguments.ToArray(),
                    children);
            }

            if (method.OutputArguments != null && method.OutputArguments.Length > 0)
            {
                var arguments = new List<Argument>();

                for (int ii = 0; ii < method.OutputArguments.Length; ii++)
                {
                    Parameter parameter = method.OutputArguments[ii];

                    var argument = new Argument
                    {
                        Value = parameter,
                        Name = parameter.Name,
                        DataType = new NodeId(parameter.DataType.ToString(),
                            (ushort)m_context.NamespaceUris.GetIndex(parameter.DataType.Namespace)),
                        ValueRank = ConstructValueRank(parameter.ValueRank, parameter.ArrayDimensions),
                        ArrayDimensions = ConstructArrayDimensionsRW(parameter.ValueRank, parameter.ArrayDimensions),
                        Description = null
                    };

                    if (parameter.Description != null && !parameter.Description.IsAutogenerated)
                    {
                        argument.Description = new Ua.LocalizedText(
                            parameter.Description.Key,
                            string.Empty,
                            parameter.Description.Value?.Trim());
                    }

                    arguments.Add(argument);
                }

                AddProperty(
                    method,
                    new XmlQualifiedName("OutputArguments", Ua.Types.Namespaces.OpcUa),
                    new XmlQualifiedName("Argument", Ua.Types.Namespaces.OpcUa),
                    ValueRank.Array,
                    [(uint)arguments.Count],
                    arguments.ToArray(),
                    children);
            }

            method.Children = new ListOfChildren
            {
                Items = [.. children]
            };
        }

        private void AddEnumStrings(DataTypeDesign dataType)
        {
            var children = new List<InstanceDesign>();

            if (dataType.Children != null && dataType.Children.Items != null)
            {
                children.AddRange(dataType.Children.Items);
            }

            if (!dataType.IsEnumeration ||
                dataType.Fields == null ||
                dataType.Fields.Length == 0)
            {
                return;
            }

            if (dataType.IsOptionSet)
            {
                var values = new List<Ua.LocalizedText>();

                int last = 0;

                for (int ii = 0; ii < 32; ii++)
                {
                    int hit = 1 << ii;

                    foreach (Parameter parameter in dataType.Fields)
                    {
                        if (parameter.Identifier == hit)
                        {
                            while (last++ < ii)
                            {
                                values.Add(new Ua.LocalizedText(string.Empty, "Reserved"));
                            }

                            if (parameter.DisplayName?.Value != null && !parameter.DisplayName.IsAutogenerated)
                            {
                                values.Add(new Ua.LocalizedText(string.Empty, parameter.DisplayName.Value));
                            }
                            else
                            {
                                values.Add(new Ua.LocalizedText(string.Empty, parameter.Name));
                            }

                            last = ii + 1;
                            break;
                        }
                    }
                }

                AddProperty(
                    dataType,
                    new XmlQualifiedName("OptionSetValues", Ua.Types.Namespaces.OpcUa),
                    new XmlQualifiedName("LocalizedText", Ua.Types.Namespaces.OpcUa),
                    ValueRank.Array,
                    [(uint)values.Count],
                    values.ToArray(),
                    children);
            }
            else
            {
                int index = 0;
                bool sequentenial = false;

                if (!dataType.ForceEnumValues)
                {
                    sequentenial = true;

                    foreach (Parameter parameter in dataType.Fields)
                    {
                        if (parameter.Identifier != index)
                        {
                            sequentenial = false;
                            break;
                        }

                        index++;
                    }
                }

                if (sequentenial)
                {
                    var values = new List<Ua.LocalizedText>();

                    foreach (Parameter parameter in dataType.Fields)
                    {
                        if (parameter.DisplayName?.Value != null && !parameter.DisplayName.IsAutogenerated)
                        {
                            values.Add(new Ua.LocalizedText(string.Empty, parameter.DisplayName.Value));
                        }
                        else
                        {
                            values.Add(new Ua.LocalizedText(string.Empty, parameter.Name));
                        }
                    }

                    AddProperty(
                        dataType,
                        new XmlQualifiedName("EnumStrings", Ua.Types.Namespaces.OpcUa),
                        new XmlQualifiedName("LocalizedText", Ua.Types.Namespaces.OpcUa),
                        ValueRank.Array,
                        [(uint)values.Count],
                        values.ToArray(),
                        children);
                }
                else
                {
                    var values = new List<EnumValueType>();

                    foreach (Parameter parameter in dataType.Fields)
                    {
                        var value = new EnumValueType();

                        if (parameter.DisplayName?.Value != null && !parameter.DisplayName.IsAutogenerated)
                        {
                            value.DisplayName = new Ua.LocalizedText(string.Empty, parameter.DisplayName.Value);
                        }
                        else
                        {
                            value.DisplayName = new Ua.LocalizedText(string.Empty, parameter.Name);
                        }

                        value.Value = (long)parameter.Identifier;

                        if (parameter.Description != null && !parameter.Description.IsAutogenerated)
                        {
                            value.Description = new Ua.LocalizedText(parameter.Description.Key, string.Empty, parameter.Description.Value?.Trim());
                        }

                        values.Add(value);
                    }

                    AddProperty(
                        dataType,
                        new XmlQualifiedName("EnumValues", Ua.Types.Namespaces.OpcUa),
                        new XmlQualifiedName("EnumValueType", Ua.Types.Namespaces.OpcUa),
                        ValueRank.Array,
                        [(uint)values.Count],
                        values.ToArray(),
                        children);
                }
            }

            dataType.Children = new ListOfChildren
            {
                Items = [.. children]
            };
        }

        private void AddDataTypeDictionary(
            ModelDesign nodes,
            Namespace ns,
            EncodingType encodingType,
            List<NodeDesign> nodesToAdd)
        {
            DictionaryDesign dictionary = null;
            var descriptions = new List<InstanceDesign>();

            if (encodingType != EncodingType.Json)
            {
                dictionary = new DictionaryDesign();

                string namespaceUri = ns.Value;
                bool isXml = encodingType == EncodingType.Xml;

                if (isXml && !string.IsNullOrEmpty(ns.XmlNamespace))
                {
                    namespaceUri = ns.XmlNamespace;
                }

                if (!isXml)
                {
                    dictionary.SymbolicId = new XmlQualifiedName(
                        NodeDesign.CreateSymbolicId(ns.Name, "BinarySchema"),
                        ns.Value);
                }
                else
                {
                    dictionary.SymbolicId = new XmlQualifiedName(
                        NodeDesign.CreateSymbolicId(ns.Name, "XmlSchema"),
                        ns.Value);
                }

                dictionary.SymbolicName = dictionary.SymbolicId;
                dictionary.BrowseName = ns.Prefix;
                dictionary.DisplayName = new LocalizedText
                {
                    IsAutogenerated = true,
                    Value = dictionary.BrowseName
                };
                dictionary.WriteAccess = 0;
                dictionary.TypeDefinition = new XmlQualifiedName(
                    "DataTypeDictionaryType",
                    Ua.Types.Namespaces.OpcUa);
                dictionary.TypeDefinitionNode = this.FindNode<VariableTypeDesign>(
                    dictionary.TypeDefinition,
                    dictionary.SymbolicId.Name,
                    "TypeDefinition");
                dictionary.DataType = s_byteStringQn;
                dictionary.DataTypeNode = this.FindNode<DataTypeDesign>(
                    dictionary.DataType,
                    dictionary.SymbolicId.Name,
                    "DataType");
                dictionary.ValueRank = ValueRank.Scalar;
                dictionary.ValueRankSpecified = true;
                dictionary.ArrayDimensions = null;
                dictionary.AccessLevel = AccessLevel.Read;
                dictionary.AccessLevelSpecified = true;
                dictionary.MinimumSamplingInterval = 0;
                dictionary.MinimumSamplingIntervalSpecified = true;
                dictionary.Historizing = false;
                dictionary.HistorizingSpecified = true;

                if (m_standardVersion != SpecificationVersion.V103)
                {
                    dictionary.ReleaseStatus = ReleaseStatus.Deprecated;
                }

                if (ns.Value == Ua.Types.Namespaces.OpcUa)
                {
                    dictionary.PartNo = 5;
                }

                var reference = new Reference
                {
                    ReferenceType = new XmlQualifiedName(
                        "HasComponent",
                        Ua.Types.Namespaces.OpcUa),
                    IsInverse = true,
                    IsOneWay = false
                };

                if (isXml)
                {
                    reference.TargetId = new XmlQualifiedName(
                        "XmlSchema_TypeSystem",
                        Ua.Types.Namespaces.OpcUa);
                }
                else
                {
                    reference.TargetId = new XmlQualifiedName(
                        "OPCBinarySchema_TypeSystem",
                        Ua.Types.Namespaces.OpcUa);
                }

                dictionary.References = [reference];

                AddProperty(
                    dictionary,
                    new XmlQualifiedName("NamespaceUri", Ua.Types.Namespaces.OpcUa),
                    new XmlQualifiedName("String", Ua.Types.Namespaces.OpcUa),
                    ValueRank.Scalar,
                    null,
                    namespaceUri,
                    descriptions);

                if (m_standardVersion != SpecificationVersion.V103)
                {
                    AddProperty(
                        dictionary,
                        new XmlQualifiedName("Deprecated", Ua.Types.Namespaces.OpcUa),
                        new XmlQualifiedName("Boolean", Ua.Types.Namespaces.OpcUa),
                        ValueRank.Scalar,
                        null,
                        true,
                        descriptions);
                }
            }

            foreach (NodeDesign node in nodes.Items)
            {
                if (node is DataTypeDesign dataType &&
                    dataType.BasicDataType == BasicDataType.UserDefined)
                {
                    AddDataTypeDescription(dataType, dictionary, descriptions, encodingType, nodesToAdd);
                }
            }

            if (dictionary != null)
            {
                dictionary.Children = new ListOfChildren
                {
                    Items = [.. descriptions]
                };

                m_nodes[dictionary.SymbolicId] = dictionary;
                m_logger.LogDebug(
                    "Added {Type}: {Name}",
                    dictionary.GetType().Name,
                    dictionary.SymbolicId.Name);
                nodesToAdd.Add(dictionary);
            }
        }

        private void AddProperty(
            NodeDesign parent,
            XmlQualifiedName propertyName,
            XmlQualifiedName dataType,
            ValueRank valueRank,
            uint[] arrayDimensions,
            object value,
            List<InstanceDesign> children)
        {
            var property = new PropertyDesign
            {
                Parent = parent,
                ReferenceType = new XmlQualifiedName("HasProperty", Ua.Types.Namespaces.OpcUa),
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                SymbolicId = new XmlQualifiedName(
                    NodeDesign.CreateSymbolicId(parent.SymbolicId.Name, propertyName.Name),
                    parent.SymbolicId.Namespace),
                SymbolicName = propertyName,
                BrowseName = propertyName.Name
            };
            property.DisplayName = new LocalizedText
            {
                IsAutogenerated = true,
                Value = property.BrowseName
            };
            property.WriteAccess = 0;
            property.TypeDefinition = new XmlQualifiedName(
                "PropertyType",
                Ua.Types.Namespaces.OpcUa);
            property.TypeDefinitionNode = this.FindNode<VariableTypeDesign>(
                property.TypeDefinition,
                property.SymbolicId.Name,
                "TypeDefinition");
            property.DataType = dataType;
            property.DataTypeNode = this.FindNode<DataTypeDesign>(
                property.DataType,
                property.SymbolicId.Name,
                "DataType");
            property.ValueRank = valueRank;
            property.ValueRankSpecified = true;
            property.ArrayDimensions = null;
            property.AccessLevel = AccessLevel.Read;
            property.AccessLevelSpecified = true;
            property.MinimumSamplingInterval = 0;
            property.MinimumSamplingIntervalSpecified = true;
            property.Historizing = false;
            property.HistorizingSpecified = true;
            property.SetDefaultValue(value, m_context);
            property.PartNo = parent.PartNo;
            property.Category = parent.Category;
            property.ReleaseStatus = parent.ReleaseStatus;

            if (m_standardVersion != SpecificationVersion.V104 &&
                m_standardVersion != SpecificationVersion.V103 &&
                arrayDimensions != null)
            {
                property.ArrayDimensions = string.Empty;
                foreach (uint ii in arrayDimensions)
                {
                    if (property.ArrayDimensions.Length > 0)
                    {
                        property.ArrayDimensions += ",";
                    }

                    property.ArrayDimensions +=
                        ii.ToString(CultureInfo.InvariantCulture);
                }
            }

            children.Add(property);

            m_nodes[property.SymbolicId] = property;
            m_logger.LogDebug(
                "Added {Type}: {Name}",
                property.GetType().Name,
                property.SymbolicId.Name);
        }

        private void AddDataTypeDescription(
            DataTypeDesign dataType,
            DictionaryDesign dictionary,
            List<InstanceDesign> descriptions,
            EncodingType encodingType,
            IList<NodeDesign> nodesToAdd)
        {
            VariableDesign description = null;

            if (encodingType != EncodingType.Json && !dataType.NotInAddressSpace)
            {
                description = new VariableDesign
                {
                    Parent = dictionary,
                    ReferenceType = new XmlQualifiedName(
                        "HasComponent",
                        Ua.Types.Namespaces.OpcUa),
                    ModellingRule = ModellingRule.Mandatory,
                    ModellingRuleSpecified = true,
                    SymbolicId = new XmlQualifiedName(
                        NodeDesign.CreateSymbolicId(
                            dictionary.SymbolicId.Name,
                            dataType.SymbolicId.Name),
                        dictionary.SymbolicId.Namespace)
                };
                description.SymbolicName = new XmlQualifiedName(
                    dataType.SymbolicId.Name,
                    description.SymbolicId.Namespace);
                description.BrowseName = dataType.BrowseName;
                description.DisplayName = new LocalizedText
                {
                    IsAutogenerated = true,
                    Value = description.BrowseName
                };
                description.WriteAccess = 0;
                description.TypeDefinition = new XmlQualifiedName(
                    "DataTypeDescriptionType",
                    Ua.Types.Namespaces.OpcUa);
                description.TypeDefinitionNode = this.FindNode<VariableTypeDesign>(
                    description.TypeDefinition,
                    description.SymbolicId.Name,
                    "TypeDefinition");
                description.DataType = new XmlQualifiedName(
                    "String",
                    Ua.Types.Namespaces.OpcUa);
                description.DataTypeNode = this.FindNode<DataTypeDesign>(
                    description.DataType,
                    description.SymbolicId.Name,
                    "DataType");
                description.ValueRank = ValueRank.Scalar;
                description.ValueRankSpecified = true;
                description.ArrayDimensions = null;
                description.AccessLevel = AccessLevel.Read;
                description.AccessLevelSpecified = true;
                description.MinimumSamplingInterval = 0;
                description.MinimumSamplingIntervalSpecified = true;
                description.Historizing = false;
                description.HistorizingSpecified = true;
                description.PartNo = dataType.PartNo;
                description.NotInAddressSpace = dataType.NotInAddressSpace;
                description.ReleaseStatus = dataType.ReleaseStatus;
                description.Purpose = dataType.Purpose;

                if (encodingType == EncodingType.Xml)
                {
                    description.DecodedValue =
                        CoreUtils.Format("//xs:element[@name='{0}']", dataType.SymbolicName.Name);
                }
                else
                {
                    description.DecodedValue =
                        CoreUtils.Format("{0}", dataType.SymbolicName.Name);
                }

                if (description.ReleaseStatus == ReleaseStatus.Released &&
                    m_standardVersion != SpecificationVersion.V103)
                {
                    description.ReleaseStatus = ReleaseStatus.Deprecated;
                }

                if (!dataType.NotInAddressSpace)
                {
                    descriptions.Add(description);
                }

                m_nodes[description.SymbolicId] = description;
                m_logger.LogDebug(
                    "Added {Type}: {Name}",
                    description.GetType().Name,
                    description.SymbolicId.Name);
            }

            if (dataType.BasicDataType == BasicDataType.UserDefined && !dataType.NoEncodings)
            {
                AddDataTypeEncoding(dataType, description, encodingType, nodesToAdd);
            }
        }

        private enum EncodingType
        {
            Binary = 0,
            Xml = 1,
            Json = 2
        }

        private void AddDataTypeEncoding(
            DataTypeDesign dataType,
            VariableDesign description,
            EncodingType encodingType,
            IList<NodeDesign> nodesToAdd)
        {
            var encoding = new ObjectDesign
            {
                Parent = null,
                ReferenceType = null
            };

            if (encodingType == EncodingType.Xml)
            {
                encoding.SymbolicId = new XmlQualifiedName(
                    dataType.SymbolicId.Name + "_Encoding_DefaultXml",
                    dataType.SymbolicId.Namespace);
                encoding.SymbolicName = new XmlQualifiedName("DefaultXml", Ua.Types.Namespaces.OpcUa);
                encoding.BrowseName = "Default XML";
            }
            else if (encodingType == EncodingType.Json)
            {
                encoding.SymbolicId = new XmlQualifiedName(
                    dataType.SymbolicId.Name + "_Encoding_DefaultJson",
                    dataType.SymbolicId.Namespace);
                encoding.SymbolicName = new XmlQualifiedName("DefaultJson", Ua.Types.Namespaces.OpcUa);
                encoding.BrowseName = "Default JSON";
            }
            else
            {
                encoding.SymbolicId = new XmlQualifiedName(
                    dataType.SymbolicId.Name + "_Encoding_DefaultBinary",
                    dataType.SymbolicId.Namespace);
                encoding.SymbolicName = new XmlQualifiedName("DefaultBinary", Ua.Types.Namespaces.OpcUa);
                encoding.BrowseName = "Default Binary";
            }

            encoding.DisplayName = new LocalizedText
            {
                IsAutogenerated = true,
                Value = encoding.BrowseName
            };
            encoding.WriteAccess = 0;
            encoding.TypeDefinition = new XmlQualifiedName(
                "DataTypeEncodingType",
                Ua.Types.Namespaces.OpcUa);
            encoding.TypeDefinitionNode = this.FindNode<ObjectTypeDesign>(
                encoding.TypeDefinition,
                encoding.SymbolicId.Name,
                "TypeDefinition");
            encoding.SupportsEvents = false;
            encoding.SupportsEventsSpecified = true;
            encoding.PartNo = dataType.PartNo;
            encoding.NotInAddressSpace = dataType.NotInAddressSpace;
            encoding.Category = dataType.Category;
            encoding.ReleaseStatus = dataType.ReleaseStatus;
            encoding.Purpose = dataType.Purpose;
            encoding.Parent = dataType;

            var reference1 = new Reference
            {
                ReferenceType = new XmlQualifiedName("HasEncoding", Ua.Types.Namespaces.OpcUa),
                IsInverse = true,
                IsOneWay = false,
                TargetId = dataType.SymbolicId,
                TargetNode = dataType
            };

            if (description != null && !dataType.NotInAddressSpace)
            {
                var reference2 = new Reference
                {
                    ReferenceType = new XmlQualifiedName("HasDescription", Ua.Types.Namespaces.OpcUa),
                    IsInverse = false,
                    IsOneWay = false,
                    TargetId = description.SymbolicId,
                    TargetNode = description
                };

                encoding.References = [reference1, reference2];
            }
            else
            {
                encoding.References = [reference1];
            }

            m_nodes[encoding.SymbolicId] = encoding;
            nodesToAdd.Add(encoding);
            m_logger.LogDebug(
                "Added {Type}: {Name}",
                encoding.GetType().Name,
                encoding.SymbolicId.Name);
        }

        /// <summary>
        /// Returns true if the node is a declaration.
        /// </summary>
        private static bool IsDeclaration(NodeDesign node)
        {
            for (NodeDesign parent = node; parent != null; parent = parent.Parent)
            {
                if (parent.IsDeclaration)
                {
                    return true;
                }
            }

            return false;
        }

        private class IdInfo
        {
            public object Id;
            public string SymbolicId;
            public NodeClass NodeClass;
            public ReleaseStatus ReleaseStatus;
            public string Category;
        }

        private Dictionary<string, object> ParseIdentifiersFromStream(Stream istrm)
        {
            var identifiers = new Dictionary<string, object>();

            var reader = new StreamReader(istrm);

            try
            {
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();

                    if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    {
                        continue;
                    }

                    int index = line.IndexOf(',', StringComparison.Ordinal);

                    if (index == -1)
                    {
                        continue;
                    }

                    // remove the node class if it is present.
                    int lastIndex = line.LastIndexOf(',');

                    if (lastIndex != -1 && index != lastIndex)
                    {
                        line = line[..lastIndex];
                    }

                    try
                    {
                        string name = line[..index].Trim();
                        string id = line[(index + 1)..].Trim();
                        m_logger.LogDebug("Loaded ID: {Name}={Id}", name, id);

                        if (id.StartsWith('"'))
                        {
                            identifiers[name] = id[1..^1];
                        }
                        else
                        {
                            identifiers[name] = Convert.ToUInt32(id, CultureInfo.InvariantCulture);
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(ex, "Error loading ID");
                    }
                }
            }
            finally
            {
                reader.Close();
            }

            return identifiers;
        }

        private object AssignIdToNode(
            NodeDesign node,
            Dictionary<string, object> identifiers,
            SortedDictionary<object, IdInfo> uniqueIdentifiers,
            Dictionary<string, object> duplicateIdentifiers,
            IdAllocator assignedIds,
            bool isImplicitlyDefined)
        {
            // assign identifier if one has not already been assigned.

            if (!identifiers.TryGetValue(node.SymbolicId.Name, out object id))
            {
                if (m_symbolicIdToNodeId.TryGetValue(node.SymbolicId, out NodeId nodeId))
                {
                    if (nodeId.TryGetIdentifier(out uint numeric))
                    {
                        id = numeric;
                    }
                    else if (nodeId.TryGetIdentifier(out string stringId))
                    {
                        id = stringId;
                    }
                    else
                    {
                        id = assignedIds.FindUnusedId(node.SymbolicId.Namespace, isImplicitlyDefined);
                    }
                }
                else
                {
                    id = assignedIds.FindUnusedId(node.SymbolicId.Namespace, isImplicitlyDefined);
                }

                identifiers.Add(node.SymbolicId.Name, id);
            }
            else if (isImplicitlyDefined)
            {
                id = assignedIds.FindUnusedId(node.SymbolicId.Namespace, isImplicitlyDefined);
                identifiers[node.SymbolicId.Name] = id;
            }

            // save identifier.
            if (uniqueIdentifiers.ContainsKey(id))
            {
                duplicateIdentifiers.Add(node.SymbolicId.Name, id);
            }
            else
            {
                var info = new IdInfo
                {
                    Id = id,
                    SymbolicId = node.SymbolicId.Name,
                    NodeClass = GetNodeClass(node),
                    ReleaseStatus = node.ReleaseStatus,
                    Category = node.Category
                };

                uniqueIdentifiers.Add(id, info);
            }

            // set identifier for node.
            if (id is uint numericId)
            {
                node.NumericId = numericId;
                node.NumericIdSpecified = true;
                node.StringId = null;
            }
            else
            {
                node.NumericId = 0;
                node.NumericIdSpecified = false;
                node.StringId = id as string;
            }

            m_logger.LogDebug("Assigned ID: {Name}={Id}", node.SymbolicId.Name, id);
            return id;
        }

        private static bool IsExplicitlyDefined(HierarchyNode current, NodeDesign root)
        {
            if (root.Children?.Items == null)
            {
                return false;
            }

            InstanceDesign parent = root.Children.Items.FirstOrDefault(x =>
                current.RelativePath.StartsWith(x.SymbolicName.Name, StringComparison.Ordinal));

            if (parent == null)
            {
                return false;
            }

            if (root is InstanceDesign instance &&
                current.Instance is InstanceDesign child &&
                child.ModellingRuleSpecified &&
                child.ModellingRule != ModellingRule.Optional &&
                child.ModellingRule != ModellingRule.Mandatory)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Loads the identifiers from a CSV file.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private SortedDictionary<object, IdInfo> AssignIdentifiers(
            ModelDesign dictionary,
            Dictionary<string, object> identifiers)
        {
            var uniqueIdentifiersComparer = Comparer<object>.Create((lhs, rhs) =>
            {
                Type lhst = lhs.GetType();
                Type rhst = rhs.GetType();
                if (lhst == rhst && lhs is IComparable c)
                {
                    return c.CompareTo(rhs);
                }
                return lhst.Name.CompareTo(rhst.Name, StringComparison.Ordinal);
            });

            var uniqueIdentifiers =
                new SortedDictionary<object, IdInfo>(uniqueIdentifiersComparer);
            var duplicateIdentifiers = new Dictionary<string, object>();
            var assignedIds = new IdAllocator(m_startId);

            foreach (object existingId in identifiers.Values)
            {
                uint? numericId = existingId as uint?;

                if (numericId != null)
                {
                    assignedIds.Add(numericId.Value);
                }
            }

            // Remove identifiers that are already known
            foreach (XmlQualifiedName symbolicId in m_symbolicIdToNodeId.Keys)
            {
                identifiers.Remove(symbolicId.Name);
            }

            // assign identifiers.
            for (int ii = 0; ii < dictionary.Items.Length; ii++)
            {
                NodeDesign node = dictionary.Items[ii];

                // assign identifier if one has not already been assigned.
                object id = AssignIdToNode(
                    node,
                    identifiers,
                    uniqueIdentifiers,
                    duplicateIdentifiers,
                    assignedIds,
                    false);

                if (node.Hierarchy == null)
                {
                    continue;
                }

                foreach (HierarchyNode current in node.Hierarchy.NodeList)
                {
                    if (string.IsNullOrEmpty(current.RelativePath))
                    {
                        current.Identifier = id;

                        // set identifier for node.
                        if (id is uint numericId)
                        {
                            current.Instance.NumericId = numericId;
                            current.Instance.NumericIdSpecified = true;
                            current.Instance.StringId = null;
                        }
                        else
                        {
                            current.Instance.NumericId = 0;
                            current.Instance.NumericIdSpecified = false;
                            current.Instance.StringId = id as string;
                        }

                        m_logger.LogDebug(
                            "Assigned ID: {Name}={Id}",
                            current.Instance.SymbolicId.Name,
                            id);
                        continue;
                    }

                    bool isExplicitlyDefined =
                        (node is InstanceDesign &&
                            !current.Instance.SymbolicId.Name.Contains(
                                "Placeholder",
                                StringComparison.Ordinal)) ||
                        current.ExplicitlyDefined ||
                        IsExplicitlyDefined(current, node);

                    current.Identifier = AssignIdToNode(
                        current.Instance,
                        identifiers,
                        uniqueIdentifiers,
                        duplicateIdentifiers,
                        assignedIds,
                        !isExplicitlyDefined);
                }
            }

            // check for duplicate nodes.
            if (duplicateIdentifiers.Count > 0)
            {
                var buffer = new StringBuilder();

                buffer.Append("Duplicate identifiers for these nodes:\r\n");

                foreach (KeyValuePair<string, object> current in duplicateIdentifiers)
                {
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0},{1}\r\n",
                        current.Key,
                        current.Value);
                }

                throw new InvalidOperationException(buffer.ToString());
            }

            foreach (KeyValuePair<string, object> ii in identifiers)
            {
                if (!uniqueIdentifiers.ContainsKey(ii.Value))
                {
                    uniqueIdentifiers[ii.Value] = new IdInfo
                    {
                        Id = ii.Value,
                        SymbolicId = ii.Key,
                        NodeClass = NodeClass.Unspecified,
                        ReleaseStatus = ReleaseStatus.Released
                    };
                }
            }
            return uniqueIdentifiers;
        }

        /// <summary>
        /// Loads the identifiers from a CSV file.
        /// </summary>
        /// <exception cref="FileNotFoundException"></exception>
        private void AssignIdentifiers(
            ModelDesign dictionary,
            string filePath = null)
        {
            Dictionary<string, object> identifiers;
            if (string.IsNullOrEmpty(filePath) ||
                !FileSystem.Exists(filePath))
            {
                identifiers = [];
            }
            else
            {
                using Stream istrm = FileSystem.OpenRead(filePath);
                identifiers = ParseIdentifiersFromStream(istrm);
            }
            AssignIdentifiers(dictionary, identifiers);
        }

        /// <summary>
        /// Imports a node.
        /// </summary>
        private bool Import(ModelDesign model, NodeDesign node, NodeDesign parent)
        {
            UpdateNamesAndIdentifiers(node, parent);

            if (node.NumericId != 0)
            {
                int ns = m_context.NamespaceUris.GetIndex(node.SymbolicId.Namespace);
                var nodeId = new NodeId(node.NumericId, (ushort)ns);
                m_nodesByNodeId[nodeId] = node;
                m_symbolicIdToNodeId[node.SymbolicId] = nodeId;
                node.NumericIdSpecified = true;
            }
            else if (!string.IsNullOrWhiteSpace(node.StringId))
            {
                int ns = m_context.NamespaceUris.GetIndex(node.SymbolicId.Namespace);
                var nodeId = new NodeId(node.StringId, (ushort)ns);
                m_nodesByNodeId[nodeId] = node;
                m_symbolicIdToNodeId[node.SymbolicId] = nodeId;
            }

            if (IsExcluded(node))
            {
                return false;
            }

            // assign default values for various subtypes.
            if (node is TypeDesign typeDesign)
            {
                ImportType(typeDesign);

                if (node.SymbolicId.Namespace == Ua.Types.Namespaces.OpcUa)
                {
                    node.Description = null;
                }
            }

            if (node is InstanceDesign instanceDesign)
            {
                ImportInstance(instanceDesign);

                if (parent != null && parent.SymbolicId.Namespace == Ua.Types.Namespaces.OpcUa)
                {
                    node.Description = null;
                }
            }

            m_nodes.Add(node.SymbolicId, node);
            m_logger.LogDebug(
                "Imported {Type}: {Name}",
                node.GetType().Name,
                node.SymbolicId.Name);

            // import children.
            if (node.Children != null && node.Children.Items != null)
            {
                node.HasChildren = true;

                var children = new List<InstanceDesign>();

                foreach (InstanceDesign child in node.Children.Items)
                {
                    if (IsExcluded(child))
                    {
                        continue;
                    }

                    // filter any children with unhandled modelling rules.
                    if (child.ModellingRuleSpecified)
                    {
                        bool skip = false;
                        switch (child.ModellingRule)
                        {
                            case ModellingRule.None:
                            case ModellingRule.Mandatory:
                            case ModellingRule.MandatoryShared:
                            case ModellingRule.Optional:
                            case ModellingRule.MandatoryPlaceholder:
                            case ModellingRule.OptionalPlaceholder:
                            case ModellingRule.ExposesItsArray:
                                break;
                            default:
                                skip = true;
                                break;
                        }
                        if (skip)
                        {
                            continue;
                        }
                    }

                    if (child.ReleaseStatus == ReleaseStatus.Released)
                    {
                        child.Category = node.Category;
                        child.ReleaseStatus = node.ReleaseStatus;
                    }

                    if (Import(model, child, node))
                    {
                        children.Add(child);
                    }
                }

                node.Children.Items = [.. children];
            }

            // import references
            if (node.References != null)
            {
                node.HasReferences = true;

                foreach (Reference reference in node.References)
                {
                    ImportReference(node, reference);
                }
            }

            return true;
        }

        /// <summary>
        /// Sets the browse name for a node.
        /// </summary>
        private void SetBrowseName(NodeDesign node)
        {
            // use the name to assign a browse name.
            if (string.IsNullOrEmpty(node.BrowseName))
            {
                if (!m_browseNames.TryGetValue(node.SymbolicName, out string browseName))
                {
                    m_browseNames[node.SymbolicName] = browseName = node.SymbolicName.Name;
                }

                node.BrowseName = browseName;
            }
            else if (!m_browseNames.TryGetValue(node.SymbolicName, out string browseName))
            {
                m_browseNames[node.SymbolicName] = node.BrowseName;
            }
            else if (node.BrowseName != browseName)
            {
                throw Exception(
                    "The SymbolicName {0} has a BrowseName {1} but expected {2}.",
                    node.SymbolicName.Name,
                    browseName,
                    node.BrowseName);
            }
        }

        /// <summary>
        /// Ensures all the names and ids in the node have valid values.
        /// </summary>
        private void UpdateNamesAndIdentifiers(NodeDesign node, NodeDesign parent)
        {
            if (node == null)
            {
                return;
            }

            // copy the symbolic name and browse name from the declaration if specified.

            if (node is InstanceDesign instance && !IsNull(instance.Declaration))
            {
                InstanceDesign declaration = this.FindNode<InstanceDesign>(
                    instance.Declaration,
                    instance.Declaration.Name,
                    "Declaration");

                if (declaration != null)
                {
                    instance.SymbolicName = declaration.SymbolicName;
                    instance.BrowseName = declaration.BrowseName;
                    instance.TypeDefinition = declaration.TypeDefinition;
                }
            }

            // check for missing name.
            if (IsNull(node.SymbolicId) &&
                IsNull(node.SymbolicName) &&
                string.IsNullOrEmpty(node.BrowseName))
            {
                throw Exception(
                    "A Node does not have SymbolicId, Name or a BrowseName. Parent={0}",
                    parent != null ? parent.SymbolicId.Name : "No Parent");
            }

            // use the browse name to assign a name.
            if (IsNull(node.SymbolicName))
            {
                if (string.IsNullOrEmpty(node.BrowseName))
                {
                    throw Exception(
                        "A Node does not have SymbolicId, SymbolicName or a BrowseName: {0}.",
                        node.SymbolicId.Name);
                }

                // remove any non-symbol characters.
                var name = new StringBuilder();

                for (int ii = 0; ii < node.BrowseName.Length; ii++)
                {
                    if (char.IsWhiteSpace(node.BrowseName[ii]))
                    {
                        name.Append(NodeDesign.PathChar);
                    }
                    else if (char.IsLetterOrDigit(node.BrowseName[ii]) ||
                        node.BrowseName[ii] == NodeDesign.PathChar)
                    {
                        name.Append(node.BrowseName[ii]);
                    }
                }

                string ns = m_dictionary.TargetNamespace;

                if (!IsNull(node.SymbolicId))
                {
                    ns = node.SymbolicId.Namespace;
                }

                // create the symbolic name.
                node.SymbolicName = new XmlQualifiedName(name.ToString(), ns);
            }

            // use the name to assign a browse name.
            SetBrowseName(node);

            // use the name to assign a symbolic id.
            if (IsNull(node.SymbolicId))
            {
                node.SymbolicId = new XmlQualifiedName(
                    NodeDesign.CreateSymbolicId(
                        parent?.SymbolicId,
                        node.SymbolicName.Name),
                    node.SymbolicName.Namespace);
            }

            // check for duplicates.
            if (m_nodes.ContainsKey(node.SymbolicId))
            {
                throw Exception(
                    "The SymbolicId is already used by another node: {0}.",
                    node.SymbolicId.Name);
            }

            // check numeric id.
            if (node.NumericIdSpecified)
            {
                if (m_identifiers.ContainsKey(node.NumericId))
                {
                    throw Exception(
                        "The NumericId is already used by another node: {0}.",
                        node.NumericId);
                }

                m_identifiers.Add(node.NumericId, node);
            }

            // add a display name.
            if (node.DisplayName == null)
            {
                node.DisplayName = new LocalizedText
                {
                    Value = node.BrowseName,
                    IsAutogenerated = true
                };
            }
            else if (node.DisplayName.Value != null)
            {
                node.DisplayName.Value = node.DisplayName.Value.Trim();
            }

            if (string.IsNullOrEmpty(node.DisplayName.Key))
            {
                node.DisplayName.Key = CoreUtils.Format("{0}_DisplayName", node.SymbolicId.Name);
            }

            // add a decription.
            if (node.Description?.Value != null && node.SymbolicId.Namespace != Ua.Types.Namespaces.OpcUa)
            {
                node.Description.Value = node.Description.Value.Trim();

                if (string.IsNullOrEmpty(node.Description.Key))
                {
                    node.Description.Key = CoreUtils.Format("{0}_Description", node.SymbolicId.Name);
                }
            }

            // save the relationship to the parent.
            node.Parent = parent;
        }

        /// <summary>
        /// Imports an TypeDesign
        /// </summary>
        private void ImportType(TypeDesign type)
        {
            // assign a class name.
            if (string.IsNullOrEmpty(type.ClassName))
            {
                type.ClassName = type.SymbolicName.Name;

                if (type.ClassName.EndsWith("Type", StringComparison.Ordinal))
                {
                    type.ClassName = type.ClassName[..^4];
                }
            }

            // assign missing fields for object types.

            if (type is ObjectTypeDesign objectType)
            {
                if (objectType.SymbolicId == s_baseObjectTypeQn)
                {
                    objectType.ClassName = "ObjectSource";
                }
                else if (type.BaseType == null)
                {
                    type.BaseType = s_baseObjectTypeQn;
                }

                if (objectType.SymbolicName != s_baseObjectTypeQn)
                {
                    objectType.BaseTypeNode = this.FindNode<ObjectTypeDesign>(
                        objectType.BaseType,
                        objectType.SymbolicId.Name,
                        "BaseType");
                }

                if (!objectType.SupportsEvents)
                {
                    objectType.SupportsEvents = false;
                }
            }

            // assign missing fields for variable types.

            if (type is VariableTypeDesign variableType)
            {
                if (variableType.SymbolicId == s_baseDataVariableTypeQn)
                {
                    variableType.ClassName = "DataVariable";
                }
                else if (type.BaseType == null)
                {
                    if (type.SymbolicId != new XmlQualifiedName("BaseVariableType", Ua.Types.Namespaces.OpcUa))
                    {
                        type.BaseType = s_baseDataVariableTypeQn;
                    }
                }

                if (variableType.SymbolicName != new XmlQualifiedName("BaseVariableType", Ua.Types.Namespaces.OpcUa))
                {
                    variableType.BaseTypeNode = this.FindNode<VariableTypeDesign>(
                        variableType.BaseType,
                        variableType.SymbolicId.Name,
                        "BaseType");

                    if (variableType.BaseTypeNode != null)
                    {
                        if (variableType.DataType == null ||
                            variableType.DataType == s_baseDataTypeQn)
                        {
                            variableType.DataType = ((VariableTypeDesign)variableType.BaseTypeNode).DataType;
                            ValueRank valueRank = ((VariableTypeDesign)variableType.BaseTypeNode).ValueRank;

                            if (!variableType.ValueRankSpecified && valueRank != ValueRank.ScalarOrArray)
                            {
                                variableType.ValueRank = valueRank;
                                variableType.ValueRankSpecified = true;
                            }
                        }
                    }
                }

                if (variableType.DataType == null)
                {
                    variableType.DataType = s_baseDataTypeQn;

                    for (var ii = variableType.BaseTypeNode as VariableTypeDesign;
                        ii != null;
                        ii = ii.BaseTypeNode as VariableTypeDesign)
                    {
                        if (ii.DataType != null)
                        {
                            variableType.DataType = ii.DataType;
                            break;
                        }
                    }
                }

                if (!variableType.ValueRankSpecified)
                {
                    variableType.ValueRank = ValueRank.Scalar;

                    for (var ii = variableType.BaseTypeNode as VariableTypeDesign;
                        ii != null;
                        ii = ii.BaseTypeNode as VariableTypeDesign)
                    {
                        if (ii.ValueRankSpecified)
                        {
                            variableType.ValueRank = ii.ValueRank;
                            variableType.ValueRankSpecified = true;
                            break;
                        }
                    }
                }

                if (!variableType.AccessLevelSpecified)
                {
                    variableType.AccessLevel = AccessLevel.Read;
                }

                if (!variableType.HistorizingSpecified)
                {
                    variableType.Historizing = false;
                }

                if (!variableType.MinimumSamplingIntervalSpecified)
                {
                    variableType.MinimumSamplingInterval = 0;
                }
            }

            // assign missing fields for data types.

            if (type is DataTypeDesign dataType)
            {
                if (dataType.SymbolicId == s_structureQn)
                {
                    dataType.ClassName = "global::Opc.Ua.IEncodeable";
                }
                else if (type.BaseType == null)
                {
                    if (dataType.SymbolicId != s_baseDataTypeQn)
                    {
                        type.BaseType = s_baseDataTypeQn;
                    }
                }

                if (dataType.SymbolicName != s_baseDataTypeQn)
                {
                    dataType.BaseTypeNode = this.FindNode<DataTypeDesign>(
                        dataType.BaseType,
                        dataType.SymbolicId.Name,
                        "BaseType");
                }

                dataType.IsStructure = dataType.BaseType == s_structureQn;
                dataType.IsEnumeration =
                    dataType.BaseType == s_enumerationQn ||
                    dataType.IsOptionSet;
                dataType.IsUnion =
                    dataType.BaseType == s_unionQn ||
                    dataType.IsUnion;

                Parameter[] parameters = dataType.Fields;
                dataType.HasFields = ImportParameters(dataType, ref parameters, "Field");
                dataType.Fields = parameters;

                dataType.HasEncodings = ImportEncodings(dataType);
            }

            // assign missing fields for reference types.

            if (type is ReferenceTypeDesign referenceType)
            {
                if (referenceType.BaseType == null &&
                    referenceType.SymbolicId != s_referencesQn)
                {
                    referenceType.BaseType = s_referencesQn;
                }

                // add an inverse name.
                referenceType.InverseName ??= new LocalizedText
                {
                    Value = referenceType.DisplayName.Value,
                    IsAutogenerated = true
                };

                if (string.IsNullOrEmpty(referenceType.InverseName.Key))
                {
                    referenceType.InverseName.Key = CoreUtils.Format(
                        "{0}_InverseName",
                        referenceType.SymbolicId.Name);
                }

                if (referenceType.SymbolicName != s_referencesQn)
                {
                    referenceType.BaseTypeNode = this.FindNode<ReferenceTypeDesign>(
                        referenceType.BaseType,
                        referenceType.SymbolicId.Name,
                        "BaseType");
                }
            }
        }

        /// <summary>
        /// Imports the encodings.
        /// </summary>
        private bool ImportEncodings(DataTypeDesign dataType)
        {
            if (dataType.Encodings == null || dataType.Encodings.Length == 0)
            {
                return false;
            }

            foreach (EncodingDesign encoding in dataType.Encodings)
            {
                if (IsNull(encoding.SymbolicName))
                {
                    throw Exception(
                        "Encoding node does not have a name: {0}.",
                        dataType.SymbolicId.Name);
                }

                if (encoding.Children != null && encoding.Children.Items.Length > 0)
                {
                    throw Exception(
                        "Encoding nodes cannot have childen",
                        dataType.SymbolicId.Name);
                }

                encoding.SymbolicId = new XmlQualifiedName(CoreUtils.Format(
                    "{0}_Encoding_{1}",
                    dataType.SymbolicId.Name,
                    encoding.SymbolicName.Name), dataType.SymbolicId.Namespace);
                encoding.BrowseName = encoding.SymbolicName.Name;

                // add a display name.
                if (encoding.DisplayName == null ||
                    string.IsNullOrEmpty(encoding.DisplayName.Value))
                {
                    encoding.DisplayName = new LocalizedText
                    {
                        Value = encoding.BrowseName,
                        IsAutogenerated = true
                    };
                }

                // add to table.
                m_nodes.Add(encoding.SymbolicId, encoding);
                m_logger.LogDebug(
                    "Imported {Type}: {Name}",
                    encoding.GetType().Name,
                    encoding.SymbolicId.Name);

                if (encoding.NumericIdSpecified)
                {
                    if (m_identifiers.ContainsKey(encoding.NumericId))
                    {
                        throw Exception(
                            "The NumericId is already used by another node: {0}.",
                            encoding.SymbolicId.Name);
                    }

                    m_identifiers.Add(encoding.NumericId, encoding);
                }
            }

            return true;
        }

        /// <summary>
        /// Imports an InstanceDesign
        /// </summary>
        private void ImportInstance(InstanceDesign instance)
        {
            // set the reference type.
            if (instance.ReferenceType == null)
            {
                if (instance is PropertyDesign)
                {
                    instance.ReferenceType =
                        new XmlQualifiedName("HasProperty", Ua.Types.Namespaces.OpcUa);
                }
                else
                {
                    instance.ReferenceType =
                        new XmlQualifiedName("HasComponent", Ua.Types.Namespaces.OpcUa);
                }
            }

            // set the type definition.
            if (instance.TypeDefinition == null)
            {
                if (instance is PropertyDesign)
                {
                    instance.TypeDefinition =
                        new XmlQualifiedName("PropertyType", Ua.Types.Namespaces.OpcUa);
                }
                else if (instance is VariableDesign)
                {
                    instance.TypeDefinition = s_baseDataVariableTypeQn;
                }
                else if (instance is ObjectDesign)
                {
                    instance.TypeDefinition = s_baseObjectTypeQn;
                }
            }

            if (!instance.ModellingRuleSpecified)
            {
                instance.ModellingRule = ModellingRule.Mandatory;
            }

            // assign missing fields for objects.

            if (instance is ObjectDesign objectDesign &&
                !objectDesign.SupportsEventsSpecified)
            {
                objectDesign.SupportsEvents = false;
            }

            // assign missing fields for variables.

            if (instance is VariableDesign variable)
            {
                if (variable.DataType == null)
                {
                    variable.DataType = s_baseDataTypeQn;
                }

                if (!variable.ValueRankSpecified)
                {
                    variable.ValueRank = ValueRank.Scalar;
                }

                if (!variable.AccessLevelSpecified)
                {
                    variable.AccessLevel = AccessLevel.Read;
                }

                if (!variable.MinimumSamplingIntervalSpecified)
                {
                    variable.MinimumSamplingInterval = 0;
                }

                if (!variable.HistorizingSpecified)
                {
                    variable.Historizing = false;
                }
            }

            // assign missing fields for variables.

            if (instance is MethodDesign method)
            {
                method.HasArguments = false;

                if (!method.NonExecutableSpecified)
                {
                    method.NonExecutableSpecified = false;
                }

                Parameter[] parameters = method.InputArguments;

                if (ImportParameters(method, ref parameters, "InputArgument"))
                {
                    method.HasArguments = true;
                }

                method.InputArguments = parameters;

                parameters = method.OutputArguments;

                if (ImportParameters(method, ref parameters, "OutputArgument"))
                {
                    method.HasArguments = true;
                }

                method.OutputArguments = parameters;
            }
        }

        /// <summary>
        /// Creates a property placeholder for the arguments of a method.
        /// </summary>
        private PropertyDesign CreateArgumentProperty(MethodDesign method, string type)
        {
            var property = new PropertyDesign
            {
                Parent = method,
                ReferenceType = new XmlQualifiedName("HasProperty", Ua.Types.Namespaces.OpcUa),
                TypeDefinition = new XmlQualifiedName("PropertyType", Ua.Types.Namespaces.OpcUa),
                SymbolicId = new XmlQualifiedName(
                    NodeDesign.CreateSymbolicId(method.SymbolicId.Name, type),
                    method.SymbolicId.Namespace),
                SymbolicName = new XmlQualifiedName(type, Ua.Types.Namespaces.OpcUa)
            };

            // use the name to assign a browse name.
            SetBrowseName(property);

            property.AccessLevel = AccessLevel.Read;
            property.ValueRank = ValueRank.Array;
            property.DataType = new XmlQualifiedName("Argument", Ua.Types.Namespaces.OpcUa);
            property.SetDefaultValue(null, m_context);
            property.DisplayName = new LocalizedText
            {
                Value = property.BrowseName,
                Key = CoreUtils.Format(
                    "{0}_(1)_DisplayName",
                    property.SymbolicId.Name,
                    type),
                IsAutogenerated = true
            };
            property.Historizing = false;
            property.MinimumSamplingInterval = 0;
            property.ModellingRule = ModellingRule.Mandatory;
            property.WriteAccess = 0;

            property.DataTypeNode = this.FindNode<DataTypeDesign>(
                property.DataType,
                method.SymbolicId.Name,
                "DataType");

            property.TypeDefinitionNode = this.FindNode<VariableTypeDesign>(
                property.TypeDefinition,
                method.SymbolicId.Name,
                "VariableType");

            m_nodes.Add(property.SymbolicId, property);
            m_logger.LogDebug(
                "Imported {Type}: {Name}",
                property.GetType().Name,
                property.SymbolicId.Name);

            return property;
        }

        /// <summary>
        /// Imports a list of parameters.
        /// </summary>
        private bool ImportParameters(
            NodeDesign node,
            ref Parameter[] parameters,
            string parameterType)
        {
            if (parameters == null || parameters.Length == 0)
            {
                return false;
            }

            decimal id = 0;
            var filteredParameters = new List<Parameter>();

            foreach (Parameter parameter in parameters)
            {
                if (IsExcluded(parameter))
                {
                    continue;
                }

                filteredParameters.Add(parameter);

                parameter.Parent = node;

                // check name.
                if (string.IsNullOrEmpty(parameter.Name))
                {
                    throw Exception(
                        "The node has a parameter without a name: {0}.",
                        node.SymbolicId.Name);
                }

                string name = parameter.Name;

                // assign an id.
                if (parameter.IdentifierSpecified)
                {
                    id = parameter.Identifier;
                }
                else if (!string.IsNullOrEmpty(parameter.BitMask))
                {
                    if (ulong.TryParse(
                        parameter.BitMask,
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out ulong mask))
                    {
                        byte[] bytes = BitConverter.GetBytes(mask);
                        parameter.Identifier = BitConverter.ToUInt64(bytes, 0);
                        parameter.IdentifierSpecified = true;
                    }
                }

                if (!parameter.IdentifierSpecified)
                {
                    parameter.Identifier = ++id;
                    parameter.IdentifierSpecified = true;
                }

                // update id if specified in name.
                int index = name.LastIndexOf(NodeDesign.PathChar);

                if (index != -1)
                {
                    for (int ii = index + 1; ii < name.Length; ii++)
                    {
                        if (!char.IsDigit(name[ii]))
                        {
                            index = -1;
                            break;
                        }
                    }

                    if (index > 0 && decimal.TryParse(name[(index + 1)..], out id))
                    {
                        parameter.Identifier = id;
                        parameter.IdentifierSpecified = true;
                    }
                }

                if (parameter.Description != null &&
                    string.IsNullOrEmpty(parameter.Description.Key))
                {
                    parameter.Description.Key = CoreUtils.Format(
                        "{0}_{1}_{2}_Description",
                        node.SymbolicId.Name,
                        parameterType,
                        parameter.Name);
                }

                // add a datatype.
                if (IsNull(parameter.DataType))
                {
                    parameter.DataType = s_baseDataTypeQn;
                }
            }

            parameters = [.. filteredParameters];

            return parameters != null && parameters.Length != 0;
        }

        /// <summary>
        /// Imports a reference.
        /// </summary>
        private void ImportReference(NodeDesign source, Reference reference)
        {
            reference.SourceNode = source;

            if (IsNull(reference.TargetId))
            {
                throw Exception(
                    "The TargetId for a reference is not valid: {0}.",
                    source.SymbolicId.Name);
            }

            m_logger.LogDebug(
                "Import Reference: {Source} => {Reference} => {Target}",
                reference.SourceNode.SymbolicName.Name,
                reference.ReferenceType.Name,
                reference.TargetId.Name);
        }

        /// <summary>
        /// Validates a node.
        /// </summary>
        private void Validate(NodeDesign node)
        {
            if (IsDeclaration(node))
            {
                return;
            }

            if (node is TypeDesign typeDesign)
            {
                ValidateType(typeDesign);
            }

            if (node is InstanceDesign instanceDesign)
            {
                ValidateInstance(instanceDesign);
            }

            if (node.HasChildren)
            {
                foreach (NodeDesign child in node.Children.Items)
                {
                    Validate(child);
                }
            }

            if (node.HasReferences)
            {
                var references = new List<Reference>();

                foreach (Reference reference in node.References)
                {
                    try
                    {
                        ValidateReference(reference);
                        references.Add(reference);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogWarning(
                            e,
                            "Ignoring InvalidReference {Name} => {Target}",
                            node.SymbolicId.Name,
                            reference.TargetId.Name);
                    }
                }

                node.References = [.. references];
            }
        }

        /// <summary>
        /// Validates the type.
        /// </summary>
        private void ValidateType(TypeDesign type)
        {
            // assign missing fields for object types.

            /*
            if (type is ObjectTypeDesign objectType)
            {
                if (objectType.SymbolicName != s_baseObjectTypeQn)
                {
                    objectType.BaseTypeNode = FindNode<ObjectTypeDesign>(
                        objectType.BaseType,
                        objectType.SymbolicId.Name,
                        "BaseType");
                }
            }
            */

            // assign missing fields for variable types.

            if (type is VariableTypeDesign variableType)
            {
                /*
                if (variableType.SymbolicName != s_baseVariableTypeQn)
                {
                    variableType.BaseTypeNode = FindNode<VariableTypeDesign>(
                        variableType.BaseType,
                        variableType.SymbolicId.Name,
                        "BaseType");

                    if (variableType.BaseTypeNode != null)
                    {
                        if (variableType.DataType == null ||
                            variableType.DataType == s_baseDataTypeQn)
                        {
                            variableType.DataType = ((VariableTypeDesign)variableType.BaseTypeNode).DataType;
                            ValueRank valueRank = ((VariableTypeDesign)variableType.BaseTypeNode).ValueRank;

                            if (!variableType.ValueRankSpecified && valueRank != ValueRank.ScalarOrArray)
                            {
                                variableType.ValueRank = valueRank;
                                variableType.ValueRankSpecified = true;
                            }
                        }
                    }
                }
                */

                variableType.DataTypeNode = this.FindNode<DataTypeDesign>(
                    variableType.DataType,
                    type.SymbolicId.Name,
                    "DataType");

                if (variableType.DefaultValue != null)
                {
                    var decoder = new XmlDecoder(variableType.DefaultValue, m_context);
                    variableType.DecodedValue = decoder.ReadVariantContents(out TypeInfo typeInfo);

                    if (!typeInfo.IsUnknown)
                    {
                        variableType.ValueRank = typeInfo.ValueRank == ValueRanks.Scalar ?
                            ValueRank.Scalar :
                            ValueRank.Array;
                        variableType.ValueRankSpecified = true;
                    }

                    decoder.Close();
                }

                if (variableType.BaseTypeNode != null)
                {
                    var baseType = variableType.BaseTypeNode as VariableTypeDesign;

                    if (baseType.DataType != s_baseDataTypeQn)
                    {
                        if (variableType.DataType == s_baseDataTypeQn)
                        {
                            variableType.DataType = baseType.DataType;
                            variableType.DataTypeNode = baseType.DataTypeNode;
                        }

                        if (baseType.DataType != variableType.DataType)
                        {
                            XmlQualifiedName ii = variableType.DataTypeNode.BaseType;

                            if (ii != null && ii != baseType.DataType)
                            {
                                DataTypeDesign parent = this.FindNode<DataTypeDesign>(
                                    ii,
                                    variableType.SymbolicId.Name,
                                    "DataType");

                                ii = parent.BaseType;
                            }

                            if (ii != baseType.DataType)
                            {
                                throw Exception(
                                    "The VariableType subtype cannot redefine the datatype. {0}",
                                    type.SymbolicId.Name);
                            }
                        }
                    }
                }
            }

            // assign missing fields for data types.

            if (type is DataTypeDesign dataType)
            {
                /*
                if (dataType.SymbolicName != s_baseDataTypeQn)
                {
                    dataType.BaseTypeNode = FindNode<DataTypeDesign>(
                        dataType.BaseType,
                        dataType.SymbolicId.Name,
                        "BaseType");
                }
                */

                ValidateParameters(dataType, dataType.Fields);

                dataType.IsStructure = IsTypeOf(
                    dataType,
                    s_structureQn);
                dataType.IsEnumeration = IsTypeOf(
                    dataType,
                    s_enumerationQn) ||
                    dataType.IsOptionSet;
                dataType.BasicDataType = dataType.DetermineBasicDataType();

                if (!dataType.IsStructure)
                {
                    if (dataType.HasEncodings)
                    {
                        throw Exception(
                            "The datatype has encodings but does not inherit from a structure: {0}",
                            type.SymbolicId.Name);
                    }

                    if (dataType.IsEnumeration)
                    {
                        if (!dataType.HasFields && !dataType.IsAbstract)
                        {
                            throw Exception(
                                "The datatype is an enumeration with no fields: {0}",
                                type.SymbolicId.Name);
                        }
                    }
                    else if (dataType.HasFields && !dataType.IsOptionSet)
                    {
                        throw Exception(
                            "The datatype is a simple type but it has fields defined: {0}",
                            type.SymbolicId.Name);
                    }
                }
                else
                {
                    // add the default encodings.
                    if (!dataType.HasEncodings)
                    {
                        EncodingDesign xmlEncoding = CreateEncoding(
                            dataType,
                            new XmlQualifiedName("DefaultXml", Ua.Types.Namespaces.OpcUa));
                        EncodingDesign binaryEncoding = CreateEncoding(
                            dataType,
                            new XmlQualifiedName("DefaultBinary", Ua.Types.Namespaces.OpcUa));

                        if (m_standardVersion != SpecificationVersion.V103)
                        {
                            EncodingDesign jsonEncoding = CreateEncoding(
                                dataType,
                                new XmlQualifiedName("DefaultJson", Ua.Types.Namespaces.OpcUa));
                            dataType.Encodings = [xmlEncoding, binaryEncoding, jsonEncoding];
                        }
                        else
                        {
                            dataType.Encodings = [xmlEncoding, binaryEncoding];
                        }

                        dataType.HasEncodings = true;
                    }

                    // check for duplicates.
                    var encodings = new Dictionary<XmlQualifiedName, EncodingDesign>();

                    foreach (EncodingDesign encoding in dataType.Encodings)
                    {
                        if (encodings.ContainsKey(encoding.SymbolicName))
                        {
                            throw Exception(
                                "The datatype has a duplicate encoding defined: {0} {1}",
                                dataType.SymbolicId.Name,
                                encoding.SymbolicName.Name);
                        }

                        encodings.Add(encoding.SymbolicName, encoding);
                    }
                }
            }

            // assign missing fields for references types.
            /*
            if (type is ReferenceTypeDesign referenceType)
            {
                if (referenceType.SymbolicName != s_referencesQn)
                {
                    referenceType.BaseTypeNode = FindNode<ReferenceTypeDesign>(
                        referenceType.BaseType,
                        referenceType.SymbolicId.Name,
                        "BaseType");
                }
            }
            */
        }

        /// <summary>
        /// Imports an InstanceDesign
        /// </summary>
        private void ValidateInstance(InstanceDesign instance)
        {
            // set the reference type.
            if (instance.ReferenceType == null && instance.Parent != null)
            {
                ReferenceTypeDesign referenceType = this.FindNode<ReferenceTypeDesign>(
                    instance.ReferenceType,
                    instance.SymbolicId.Name,
                    "ReferenceType");

                if (referenceType == null)
                {
                    m_logger.LogWarning("Reference type {Name} not found", instance.ReferenceType);
                }
            }

            // assign missing fields for object.

            if (instance is ObjectDesign objectd)
            {
                objectd.TypeDefinitionNode = this.FindNode<ObjectTypeDesign>(
                    instance.TypeDefinition,
                    instance.SymbolicId.Name,
                    "TypeDefinition");

                //if (!objectd.ModellingRuleSpecified || objectd.ModellingRule == ModellingRule.None)
                //{
                //    if (objectd.RolePermissions == null)
                //    {
                //        objectd.RolePermissions = objectd.TypeDefinitionNode.DefaultRolePermissions;
                //    }

                //    if (!objectd.AccessRestrictionsSpecified)
                //    {
                //        objectd.AccessRestrictions = objectd.TypeDefinitionNode.DefaultAccessRestrictions;
                //        objectd.AccessRestrictionsSpecified = objectd.TypeDefinitionNode.DefaultAccessRestrictionsSpecified;
                //    }
                //}
                //else
                //{
                //    if (objectd.DefaultRolePermissions == null)
                //    {
                //        objectd.DefaultRolePermissions = objectd.TypeDefinitionNode.DefaultRolePermissions;
                //    }

                //    if (!objectd.DefaultAccessRestrictionsSpecified)
                //    {
                //        objectd.DefaultAccessRestrictions = objectd.TypeDefinitionNode.DefaultAccessRestrictions;
                //        objectd.DefaultAccessRestrictionsSpecified = objectd.TypeDefinitionNode.DefaultAccessRestrictionsSpecified;
                //    }
                //}
            }

            // assign missing fields for variables.

            if (instance is VariableDesign variable)
            {
                if (variable is PropertyDesign && variable.HasChildren)
                {
                    throw Exception("The Property ({0}) has children defined.", variable.SymbolicId.Name);
                }

                variable.TypeDefinitionNode = this.FindNode<VariableTypeDesign>(
                    instance.TypeDefinition,
                    instance.SymbolicId.Name,
                    "TypeDefinition");

                if (variable.TypeDefinitionNode != null)
                {
                    if (variable.DataType == null ||
                        variable.DataType == s_baseDataTypeQn)
                    {
                        variable.DataType = ((VariableTypeDesign)variable.TypeDefinitionNode).DataType;

                        ValueRank valueRank = ((VariableTypeDesign)variable.TypeDefinitionNode).ValueRank;

                        if (!variable.ValueRankSpecified && valueRank != ValueRank.ScalarOrArray)
                        {
                            variable.ValueRank = valueRank;
                            variable.ValueRankSpecified = true;
                        }
                    }

                    //if (!variable.ModellingRuleSpecified || variable.ModellingRule == ModellingRule.None)
                    //{
                    //    if (variable.RolePermissions == null)
                    //    {
                    //        variable.RolePermissions = variable.TypeDefinitionNode.DefaultRolePermissions;
                    //    }

                    //    if (!variable.AccessRestrictionsSpecified)
                    //    {
                    //        variable.AccessRestrictions = variable.TypeDefinitionNode.DefaultAccessRestrictions;
                    //        variable.AccessRestrictionsSpecified = variable.TypeDefinitionNode.DefaultAccessRestrictionsSpecified;
                    //    }
                    //}
                    //else
                    //{
                    //    if (variable.DefaultRolePermissions == null)
                    //    {
                    //        variable.DefaultRolePermissions = variable.TypeDefinitionNode.DefaultRolePermissions;
                    //    }

                    //    if (!variable.DefaultAccessRestrictionsSpecified)
                    //    {
                    //        variable.DefaultAccessRestrictions = variable.TypeDefinitionNode.DefaultAccessRestrictions;
                    //        variable.DefaultAccessRestrictionsSpecified = variable.TypeDefinitionNode.DefaultAccessRestrictionsSpecified;
                    //    }
                    //}
                }

                variable.DataTypeNode = this.FindNode<DataTypeDesign>(
                    variable.DataType,
                    instance.SymbolicId.Name,
                    "DataType");

                if (variable.DefaultValue != null)
                {
                    var decoder = new XmlDecoder(variable.DefaultValue, m_context);
                    variable.DecodedValue = decoder.ReadVariantContents(out TypeInfo typeInfo);

                    if (!typeInfo.IsUnknown)
                    {
                        variable.ValueRank =
                            typeInfo.ValueRank == ValueRanks.Scalar ? ValueRank.Scalar : ValueRank.Array;
                        variable.ValueRankSpecified = true;
                    }

                    decoder.Close();
                }
            }

            // assign missing fields for methods.

            if (instance is MethodDesign method)
            {
                if (instance.TypeDefinition != null)
                {
                    method.MethodType = this.FindNode<MethodDesign>(
                        instance.TypeDefinition,
                        instance.SymbolicId.Name,
                        "TypeDefinition");

                    method.Description = method.MethodType.Description;
                    method.InputArguments = method.MethodType.InputArguments;
                    method.OutputArguments = method.MethodType.OutputArguments;
                    method.HasArguments = (method.InputArguments != null && method.InputArguments.Length > 0) ||
                        (method.OutputArguments != null && method.OutputArguments.Length > 0);

                    //if (!method.ModellingRuleSpecified || method.ModellingRule == ModellingRule.None)
                    //{
                    //    if (method.RolePermissions == null)
                    //    {
                    //        method.RolePermissions = method.MethodType.DefaultRolePermissions;
                    //    }

                    //    if (!method.AccessRestrictionsSpecified)
                    //    {
                    //        method.AccessRestrictions = method.MethodType.DefaultAccessRestrictions;
                    //        method.AccessRestrictionsSpecified = method.MethodType.DefaultAccessRestrictionsSpecified;
                    //    }
                    //}
                    //else
                    //{
                    //    if (method.DefaultRolePermissions == null)
                    //    {
                    //        method.DefaultRolePermissions = method.MethodType.DefaultRolePermissions;
                    //    }

                    //    if (!method.DefaultAccessRestrictionsSpecified)
                    //    {
                    //        method.DefaultAccessRestrictions = method.MethodType.DefaultAccessRestrictions;
                    //        method.DefaultAccessRestrictionsSpecified = method.MethodType.DefaultAccessRestrictionsSpecified;
                    //    }
                    //}
                }

                ValidateParameters(method, method.InputArguments);
                ValidateParameters(method, method.OutputArguments);

                if (method.Parent != null)
                {
                    var children = new List<InstanceDesign>();

                    if (method.Children != null && method.Children.Items != null)
                    {
                        children.AddRange(method.Children.Items);
                    }

                    if (method.InputArguments != null)
                    {
                        children.Add(CreateArgumentProperty(method, "InputArguments"));
                    }

                    if (method.OutputArguments != null)
                    {
                        children.Add(CreateArgumentProperty(method, "OutputArguments"));
                    }

                    if (children.Count > 0)
                    {
                        method.Children = new ListOfChildren
                        {
                            Items = [.. children]
                        };
                        method.HasChildren = true;
                    }
                }
            }
        }

        private void ValidateParameters(NodeDesign node, Parameter[] parameters)
        {
            if (parameters != null)
            {
                foreach (Parameter parameter in parameters)
                {
                    parameter.DataTypeNode = this.FindNode<DataTypeDesign>(
                        parameter.DataType,
                        node.SymbolicId.Name,
                        "DataType");

                    if (IsTypeOf(parameter.DataTypeNode, s_structureQn) &&
                        parameter.AllowSubTypes &&
                        !UseAllowSubtypes)
                    {
                        parameter.DataTypeNode = this.FindNode<DataTypeDesign>(
                            s_structureQn,
                            node.SymbolicId.Name,
                            "DataType");
                    }
                }
            }
        }

        private void ValidateReference(Reference reference)
        {
            ReferenceTypeDesign referenceType = this.FindNode<ReferenceTypeDesign>(
                reference.ReferenceType,
                reference.SourceNode.SymbolicId.Name,
                "ReferenceType");

            if (referenceType == null)
            {
                m_logger.LogWarning("Reference type {Name} not found", reference.ReferenceType);
            }

            reference.TargetNode = this.FindNode(
                reference.TargetId,
                reference.SourceNode.SymbolicId.Name,
                "TargetId");
        }

        private bool IsTypeOf(TypeDesign type, XmlQualifiedName superType)
        {
            if (type.SymbolicId == superType)
            {
                return true;
            }

            if (IsNull(type.BaseType))
            {
                return false;
            }

            if (!m_nodes.TryGetValue(type.BaseType, out NodeDesign node))
            {
                return false;
            }

            return IsTypeOf(node as TypeDesign, superType);
        }

        private EncodingDesign CreateEncoding(DataTypeDesign dataType, XmlQualifiedName encodingName)
        {
            var symbolicId = new XmlQualifiedName(
                CoreUtils.Format("{0}_Encoding_{1}", dataType.SymbolicId.Name, encodingName.Name),
                dataType.SymbolicId.Namespace);

            var encoding = new EncodingDesign
            {
                SymbolicName = encodingName,
                SymbolicId = symbolicId,
                ReleaseStatus = dataType.ReleaseStatus,
                Purpose = dataType.Purpose
            };

            if (m_nodes.TryGetValue(symbolicId, out NodeDesign target))
            {
                encoding.NumericId = target.NumericId;
                encoding.NumericIdSpecified = target.NumericIdSpecified;
                m_nodes.Remove(symbolicId);
            }

            // use the name to assign a browse name.
            SetBrowseName(encoding);

            encoding.TypeDefinition = new XmlQualifiedName("DataTypeEncodingType", Ua.Types.Namespaces.OpcUa);
            encoding.Parent = dataType;

            encoding.TypeDefinitionNode = this.FindNode<ObjectTypeDesign>(
                encoding.TypeDefinition,
                encoding.SymbolicId.Name,
                "DataTypeEncodingType");

            // add a display name.
            if (encoding.DisplayName == null ||
                string.IsNullOrEmpty(encoding.DisplayName.Value))
            {
                encoding.DisplayName = new LocalizedText
                {
                    Value = encoding.BrowseName,
                    IsAutogenerated = true
                };
            }

            m_nodes.Add(encoding.SymbolicId, encoding);
            m_logger.LogDebug(
                "Created {Type}: {Name}",
                encoding.GetType().Name,
                encoding.SymbolicId.Name);

            return encoding;
        }

        /// <summary>
        /// Get node class of design element
        /// </summary>
        private static NodeClass GetNodeClass(NodeDesign node)
        {
            if (node is ObjectDesign)
            {
                return NodeClass.Object;
            }

            if (node is ObjectTypeDesign)
            {
                return NodeClass.ObjectType;
            }

            if (node is DataTypeDesign)
            {
                return NodeClass.DataType;
            }

            if (node is ReferenceTypeDesign)
            {
                return NodeClass.ReferenceType;
            }

            if (node is MethodDesign)
            {
                return NodeClass.Method;
            }

            if (node is VariableDesign)
            {
                return NodeClass.Variable;
            }

            if (node is VariableTypeDesign)
            {
                return NodeClass.VariableType;
            }

            if (node is ViewDesign)
            {
                return NodeClass.View;
            }

            return NodeClass.Unspecified;
        }

        /// <summary>
        /// Maps the value rank enumeration onto a integer.
        /// </summary>
        private static int ConstructValueRank(ValueRank valueRank, string arrayDimensions)
        {
            switch (valueRank)
            {
                case ValueRank.Array:
                    return ValueRanks.OneDimension;
                case ValueRank.Scalar:
                    return ValueRanks.Scalar;
                case ValueRank.Any:
                case ValueRank.ScalarOrArray:
                    return ValueRanks.Any;
                case ValueRank.ScalarOrOneDimension:
                    return ValueRanks.ScalarOrOneDimension;
                case ValueRank.OneOrMoreDimensions:
                    if (string.IsNullOrEmpty(arrayDimensions))
                    {
                        return ValueRanks.OneOrMoreDimensions;
                    }

                    string[] dimensions = arrayDimensions.Split([','], StringSplitOptions.RemoveEmptyEntries);

                    return dimensions.Length;
            }

            return ValueRanks.Any;
        }

        /// <summary>
        /// Maps the array dimensions onto a constant declaration..
        /// </summary>
        private static UInt32Collection ConstructArrayDimensionsRW(ValueRank valueRank, string arrayDimensions)
        {
            if (valueRank is < 0 and not ValueRank.OneOrMoreDimensions)
            {
                return null;
            }

            if (string.IsNullOrEmpty(arrayDimensions))
            {
                if (valueRank == ValueRank.Array)
                {
                    return [.. new uint[1]];
                }

                return null;
            }

            string[] tokens = arrayDimensions.Split([','], StringSplitOptions.RemoveEmptyEntries);

            if (tokens == null || tokens.Length < 1)
            {
                return null;
            }

            var dimensions = new UInt32Collection();

            for (int ii = 0; ii < tokens.Length; ii++)
            {
                try
                {
                    dimensions.Add(Convert.ToUInt32(tokens[ii], CultureInfo.InvariantCulture));
                }
                catch
                {
                    dimensions.Add(0);
                }
            }

            return dimensions;
        }

        /// <summary>
        /// Returns the browse path to the instance.
        /// </summary>
        private static string GetBrowsePath(string basePath, InstanceDesign instance)
        {
            return NodeDesign.CreateSymbolicId(basePath, instance.SymbolicName.Name);
        }

        private TypeDesign MergeTypeHierarchy(TypeDesign type)
        {
            m_logger.LogDebug("Merging Type: {Name}", type.SymbolicId.Name);

            TypeDesign mergedType;
            if (type.BaseTypeNode == null)
            {
                mergedType = type.Copy();

                mergedType.NumericId = 0;
                mergedType.NumericIdSpecified = false;
                mergedType.StringId = null;
            }
            else
            {
                mergedType = MergeTypeHierarchy(type.BaseTypeNode);
                MergeTypes(mergedType, type);
            }
            return mergedType;
        }

        private static void MergeTypes(
            TypeDesign mergedType,
            TypeDesign type)
        {
            mergedType.SymbolicId = type.SymbolicId;
            mergedType.SymbolicName = type.SymbolicName;
            mergedType.NumericId = type.NumericId;
            mergedType.NumericIdSpecified = type.NumericIdSpecified;
            mergedType.StringId = type.StringId;
            mergedType.ClassName = type.ClassName;
            mergedType.BrowseName = type.BrowseName;
            mergedType.DisplayName = type.DisplayName;
            mergedType.Description = type.Description;
            mergedType.BaseType = type.BaseType;
            mergedType.BaseTypeNode = type.BaseTypeNode;
            mergedType.IsAbstract = type.IsAbstract;
            mergedType.Children = null;
            mergedType.References = null;
            mergedType.Category = type.Category;
            mergedType.Purpose = type.Purpose;
            mergedType.ReleaseStatus = type.ReleaseStatus;
            mergedType.RolePermissions = type.RolePermissions;
            mergedType.AccessRestrictions = type.AccessRestrictions;
            mergedType.AccessRestrictionsSpecified = type.AccessRestrictionsSpecified;
            mergedType.WriteAccess = type.WriteAccess;

            if (type is VariableTypeDesign variableType)
            {
                MergeTypes((VariableTypeDesign)mergedType, variableType);
            }

            if (type is ObjectTypeDesign objectType)
            {
                MergeTypes((ObjectTypeDesign)mergedType, objectType);
            }
        }

        private static void MergeTypes(
            VariableTypeDesign mergedType,
            VariableTypeDesign variableType)
        {
            if (variableType.DecodedValue != null)
            {
                mergedType.DecodedValue = variableType.DecodedValue;
            }
            if (variableType.DefaultValue != null)
            {
                mergedType.DefaultValue = variableType.DefaultValue;
            }

            if (variableType.DataType != null &&
                variableType.DataType != s_baseDataTypeQn)
            {
                mergedType.DataType = variableType.DataType;
                mergedType.DataTypeNode = variableType.DataTypeNode;
            }

            if (variableType.ValueRankSpecified)
            {
                mergedType.ValueRank = variableType.ValueRank;
                mergedType.ValueRankSpecified = true;
            }

            if (!string.IsNullOrEmpty(variableType.ArrayDimensions))
            {
                mergedType.ArrayDimensions = variableType.ArrayDimensions;
            }

            if (variableType.AccessLevelSpecified)
            {
                mergedType.AccessLevel = variableType.AccessLevel;
                mergedType.AccessLevelSpecified = true;
            }

            if (variableType.MinimumSamplingIntervalSpecified)
            {
                mergedType.MinimumSamplingInterval = variableType.MinimumSamplingInterval;
                mergedType.MinimumSamplingIntervalSpecified = true;
            }

            if (variableType.HistorizingSpecified)
            {
                mergedType.Historizing = variableType.Historizing;
                mergedType.HistorizingSpecified = true;
            }
        }

        private static void MergeTypes(
            ObjectTypeDesign mergedType,
            ObjectTypeDesign objectType)
        {
            if (objectType.SupportsEventsSpecified)
            {
                mergedType.SupportsEvents = objectType.SupportsEvents;
                mergedType.SupportsEventsSpecified = true;
            }
        }

        private NodeDesign CreateMergedInstance(
            XmlQualifiedName rootId,
            string relativePath,
            NodeDesign source)
        {
            m_logger.LogDebug("Merging Instance: {Root} {Path} {Source}", rootId.Name, relativePath, source.SymbolicId.Name);

            var type = source as TypeDesign;

            if (type != null)
            {
                if (type is ReferenceTypeDesign or DataTypeDesign)
                {
                    return type;
                }

                type = MergeTypeHierarchy(type);
            }

            InstanceDesign mergedInstance = null;

            if (source is InstanceDesign instance)
            {
                mergedInstance = instance.Copy();

                if (instance is MethodDesign method)
                {
                    ((MethodDesign)mergedInstance).MethodDeclarationNode = method;
                }
            }
            else
            {
                if (type is VariableTypeDesign variableType)
                {
                    mergedInstance = CreateMergedInstance(variableType);
                }

                if (type is ObjectTypeDesign objectType)
                {
                    mergedInstance = CreateMergedInstance(objectType);
                }

                mergedInstance.SymbolicName = rootId;
                mergedInstance.NumericId = source.NumericId;
                mergedInstance.NumericIdSpecified = source.NumericIdSpecified;
                mergedInstance.StringId = source.StringId;
                mergedInstance.BrowseName = rootId.Name;
                mergedInstance.DisplayName.Value = rootId.Name;
                mergedInstance.DisplayName.IsAutogenerated = true;
                mergedInstance.TypeDefinition = source.SymbolicId;
                mergedInstance.TypeDefinitionNode = source as TypeDesign;
            }

            string instanceId = rootId.Name;

            if (!string.IsNullOrEmpty(relativePath))
            {
                instanceId = NodeDesign.CreateSymbolicId(instanceId, relativePath);
            }

            mergedInstance.SymbolicId = new XmlQualifiedName(instanceId, rootId.Namespace);

            mergedInstance.References = null;
            mergedInstance.IdentifierRequired = true;
            mergedInstance.OveriddenNode = null;
            mergedInstance.Parent = null;
            mergedInstance.Category = source.Category;
            mergedInstance.Purpose = source.Purpose;
            mergedInstance.ReleaseStatus = source.ReleaseStatus;
            mergedInstance.DesignToolOnly = source is InstanceDesign dto && dto.DesignToolOnly;

            m_logger.LogDebug("Created Merged Instance: {Name}", mergedInstance.SymbolicId.Name);
            return mergedInstance;
        }

        private static ObjectDesign CreateMergedInstance(
            ObjectTypeDesign type)
        {
            var objectd = new ObjectDesign
            {
                Parent = null,
                ReferenceType = null,
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                DisplayName = new LocalizedText(),
                WriteAccess = 0,
                SupportsEvents = type.SupportsEvents,
                SupportsEventsSpecified = true,
                Category = type.Category,
                Purpose = type.Purpose,
                ReleaseStatus = type.ReleaseStatus,
                DefaultRolePermissions = type.DefaultRolePermissions,
                DefaultAccessRestrictions = type.DefaultAccessRestrictions,
                DefaultAccessRestrictionsSpecified = type.DefaultAccessRestrictionsSpecified
            };
            objectd.WriteAccess = 0;

            return objectd;
        }

        private static VariableDesign CreateMergedInstance(
            VariableTypeDesign type)
        {
            var variable = new VariableDesign
            {
                Parent = null,
                ReferenceType = null,
                ModellingRule = ModellingRule.Mandatory,
                ModellingRuleSpecified = true,
                DisplayName = new LocalizedText(),
                WriteAccess = 0,
                DecodedValue = type.DecodedValue,
                DefaultValue = type.DefaultValue,
                DataType = type.DataType,
                DataTypeNode = type.DataTypeNode,
                ValueRank = type.ValueRank,
                ValueRankSpecified = type.ValueRankSpecified,
                ArrayDimensions = type.ArrayDimensions,
                AccessLevel = type.AccessLevel,
                AccessLevelSpecified = type.AccessLevelSpecified,
                MinimumSamplingInterval = type.MinimumSamplingInterval,
                MinimumSamplingIntervalSpecified = type.MinimumSamplingIntervalSpecified,
                Historizing = type.Historizing,
                HistorizingSpecified = type.HistorizingSpecified,
                Category = type.Category,
                Purpose = type.Purpose,
                ReleaseStatus = type.ReleaseStatus,
                DefaultRolePermissions = type.DefaultRolePermissions,
                DefaultAccessRestrictions = type.DefaultAccessRestrictions,
                DefaultAccessRestrictionsSpecified = type.DefaultAccessRestrictionsSpecified
            };
            variable.WriteAccess = 0;

            return variable;
        }

        private void UpdateMergedInstance(
            InstanceDesign mergedInstance,
            NodeDesign source)
        {
            m_logger.LogDebug(
                "Updated Merged Instance: {Instance} {Source}",
                mergedInstance.SymbolicId.Name,
                source.SymbolicId.Name);

            if (source.DisplayName != null && !source.DisplayName.IsAutogenerated)
            {
                mergedInstance.DisplayName = source.DisplayName;
            }

            if (source.Description != null && !source.Description.IsAutogenerated)
            {
                mergedInstance.Description = source.Description;
            }

            if (source.RolePermissions != null)
            {
                mergedInstance.RolePermissions = source.RolePermissions;
            }

            if (source.WriteAccess != 0)
            {
                mergedInstance.WriteAccess = source.WriteAccess;
            }

            if (source.AccessRestrictionsSpecified)
            {
                mergedInstance.AccessRestrictions = source.AccessRestrictions;
                mergedInstance.AccessRestrictionsSpecified = source.AccessRestrictionsSpecified;
            }

            if (source is InstanceDesign instance)
            {
                if (source.NumericId != 0 && source.NumericId != mergedInstance.NumericId)
                {
                    mergedInstance.NumericId = source.NumericId;
                    mergedInstance.NumericIdSpecified = source.NumericIdSpecified;
                }

                if (source.StringId != null && source.StringId != mergedInstance.StringId)
                {
                    mergedInstance.StringId = source.StringId;
                    mergedInstance.NumericIdSpecified = source.NumericIdSpecified;
                }

                if (mergedInstance.SymbolicName != source.SymbolicName)
                {
                    mergedInstance.SymbolicName = source.SymbolicName;
                    mergedInstance.BrowseName = source.BrowseName;
                    mergedInstance.DisplayName = source.DisplayName;
                }

                UpdateMergedInstance(mergedInstance, instance);

                if (source is InstanceDesign dto && dto.DesignToolOnly)
                {
                    mergedInstance.DesignToolOnly = true;
                }
            }
            else
            {
                if (source is VariableTypeDesign variableType)
                {
                    UpdateMergedInstance((VariableDesign)mergedInstance, variableType);
                }

                if (source is ObjectTypeDesign objectType)
                {
                    UpdateMergedInstance((ObjectDesign)mergedInstance, objectType);
                }
            }
        }

        private static void UpdateMergedInstance(
            InstanceDesign mergedInstance,
            InstanceDesign instance)
        {
            mergedInstance.ReferenceType = instance.ReferenceType;

            if (instance.ModellingRuleSpecified)
            {
                mergedInstance.ModellingRule = instance.ModellingRule;
                mergedInstance.ModellingRuleSpecified = true;
            }

            if (instance is VariableDesign variable)
            {
                UpdateMergedInstance((VariableDesign)mergedInstance, variable);
            }

            if (instance is ObjectDesign objectd)
            {
                UpdateMergedInstance((ObjectDesign)mergedInstance, objectd);
            }

            if (instance is MethodDesign method)
            {
                UpdateMergedInstance((MethodDesign)mergedInstance, method);
            }
        }

        private static void UpdateMergedInstance(
            VariableDesign mergedVariable,
            VariableTypeDesign variableType)
        {
            mergedVariable.TypeDefinition = variableType.SymbolicId;
            mergedVariable.TypeDefinitionNode = variableType;

            if (variableType.DecodedValue != null)
            {
                mergedVariable.DecodedValue = variableType.DecodedValue;
            }

            if (variableType.DefaultValue != null)
            {
                mergedVariable.DefaultValue = variableType.DefaultValue;
            }

            if (variableType.DataType != null &&
                variableType.DataType != s_baseDataTypeQn)
            {
                mergedVariable.DataType = variableType.DataType;
                mergedVariable.DataTypeNode = variableType.DataTypeNode;
            }

            if (variableType.ValueRankSpecified)
            {
                mergedVariable.ValueRank = variableType.ValueRank;
                mergedVariable.ValueRankSpecified = true;
            }

            if (!string.IsNullOrEmpty(variableType.ArrayDimensions))
            {
                mergedVariable.ArrayDimensions = variableType.ArrayDimensions;
            }

            if (variableType.AccessLevelSpecified)
            {
                mergedVariable.AccessLevel = variableType.AccessLevel;
                mergedVariable.AccessLevelSpecified = true;
            }

            if (variableType.MinimumSamplingIntervalSpecified)
            {
                mergedVariable.MinimumSamplingInterval =
                    variableType.MinimumSamplingInterval;
                mergedVariable.MinimumSamplingIntervalSpecified = true;
            }

            if (variableType.HistorizingSpecified)
            {
                mergedVariable.Historizing = variableType.Historizing;
                mergedVariable.HistorizingSpecified = true;
            }

            if (variableType.DefaultRolePermissions != null)
            {
                mergedVariable.DefaultRolePermissions =
                    variableType.DefaultRolePermissions;
            }

            if (variableType.DefaultAccessRestrictionsSpecified &&
                !mergedVariable.DefaultAccessRestrictionsSpecified)
            {
                mergedVariable.DefaultAccessRestrictions =
                    variableType.DefaultAccessRestrictions;
                mergedVariable.DefaultAccessRestrictionsSpecified = true;
            }
        }

        private static void UpdateMergedInstance(
            VariableDesign mergedVariable,
            VariableDesign variable)
        {
            if (variable.TypeDefinition != null &&
                variable.TypeDefinition != s_baseDataVariableTypeQn)
            {
                mergedVariable.TypeDefinition = variable.TypeDefinition;
                mergedVariable.TypeDefinitionNode = variable.TypeDefinitionNode;
            }

            if (variable.DecodedValue != null)
            {
                mergedVariable.DecodedValue = variable.DecodedValue;
            }

            if (variable.DefaultValue != null)
            {
                mergedVariable.DefaultValue = variable.DefaultValue;
            }

            if (variable.DataType != null &&
                variable.DataType != s_baseDataTypeQn)
            {
                mergedVariable.DataType = variable.DataType;
                mergedVariable.DataTypeNode = variable.DataTypeNode;
            }

            if (variable.ValueRankSpecified)
            {
                mergedVariable.ValueRank = variable.ValueRank;
                mergedVariable.ValueRankSpecified = true;
            }

            if (!string.IsNullOrEmpty(variable.ArrayDimensions))
            {
                mergedVariable.ArrayDimensions = variable.ArrayDimensions;
            }

            if (variable.AccessLevelSpecified)
            {
                mergedVariable.AccessLevel = variable.AccessLevel;
                mergedVariable.AccessLevelSpecified = true;
            }

            if (variable.MinimumSamplingIntervalSpecified)
            {
                mergedVariable.MinimumSamplingInterval = variable.MinimumSamplingInterval;
                mergedVariable.MinimumSamplingIntervalSpecified = true;
            }

            if (variable.HistorizingSpecified)
            {
                mergedVariable.Historizing = variable.Historizing;
                mergedVariable.HistorizingSpecified = true;
            }
        }

        private static void UpdateMergedInstance(
            ObjectDesign mergedObject,
            ObjectTypeDesign objectType)
        {
            mergedObject.TypeDefinition = objectType.SymbolicId;
            mergedObject.TypeDefinitionNode = objectType;

            if (objectType.SupportsEventsSpecified)
            {
                mergedObject.SupportsEvents = objectType.SupportsEvents;
                mergedObject.SupportsEventsSpecified = true;
            }

            if (objectType.DefaultRolePermissions != null)
            {
                mergedObject.DefaultRolePermissions = objectType.DefaultRolePermissions;
            }

            if (objectType.DefaultAccessRestrictionsSpecified && !mergedObject.DefaultAccessRestrictionsSpecified)
            {
                mergedObject.DefaultAccessRestrictions = objectType.DefaultAccessRestrictions;
                mergedObject.DefaultAccessRestrictionsSpecified = true;
            }
        }

        private static void UpdateMergedInstance(
            MethodDesign mergedMethod,
            MethodDesign method)
        {
            if (method.NonExecutableSpecified)
            {
                mergedMethod.NonExecutable = method.NonExecutable;
                mergedMethod.NonExecutableSpecified = true;
            }

            if (method.DefaultRolePermissions != null)
            {
                mergedMethod.DefaultRolePermissions = method.DefaultRolePermissions;
            }

            if (method.DefaultAccessRestrictionsSpecified &&
                !mergedMethod.DefaultAccessRestrictionsSpecified)
            {
                mergedMethod.DefaultAccessRestrictions = method.DefaultAccessRestrictions;
                mergedMethod.DefaultAccessRestrictionsSpecified = true;
            }
        }

        private static void UpdateMergedInstance(
            ObjectDesign mergedObject,
            ObjectDesign objectd)
        {
            if (objectd.TypeDefinition != null &&
                objectd.TypeDefinition != s_baseObjectTypeQn)
            {
                mergedObject.TypeDefinition = objectd.TypeDefinition;
                mergedObject.TypeDefinitionNode = objectd.TypeDefinitionNode;
            }

            if (objectd.SupportsEventsSpecified)
            {
                mergedObject.SupportsEvents = objectd.SupportsEvents;
                mergedObject.SupportsEventsSpecified = true;
            }
        }

        private void SetOverriddenNodes(
            TypeDesign type,
            string basePath,
            Dictionary<string, InstanceDesign> nodes,
            int depth,
            bool fromInstance)
        {
            if (type.BaseTypeNode != null)
            {
                SetOverriddenNodes(
                    type.BaseTypeNode,
                    basePath,
                    nodes,
                    depth + 1,
                    fromInstance);
            }

            if (type.Children != null && type.Children.Items != null)
            {
                for (int ii = 0; ii < type.Children.Items.Length; ii++)
                {
                    InstanceDesign instance = type.Children.Items[ii];

                    if (instance.ModellingRule == ModellingRule.ExposesItsArray)
                    {
                        continue;
                    }

                    string browsePath = GetBrowsePath(basePath, instance);

                    if (instance.ModellingRule is
                        ModellingRule.MandatoryPlaceholder or
                        ModellingRule.OptionalPlaceholder)
                    {
                        continue;
                    }

                    SetOverriddenNodes(instance, browsePath, nodes, depth + 1);

                    if (nodes.TryGetValue(browsePath, out InstanceDesign overriddenInstance))
                    {
                        bool inPath = false;

                        for (InstanceDesign current = overriddenInstance;
                            current != null;
                            current = current.OveriddenNode)
                        {
                            if (current.SymbolicId == instance.SymbolicId)
                            {
                                inPath = true;
                                break;
                            }
                        }

                        if (!inPath)
                        {
                            m_logger.LogDebug(
                                "OverridingInstance: {Instance} : {Overridden}",
                                instance.SymbolicId.Name,
                                overriddenInstance.SymbolicId.Name);
                            instance.OveriddenNode = overriddenInstance;
                        }
                    }

                    // special handling for built-in properties.
                    var propertyName = new XmlQualifiedName(
                        "EnumStrings",
                        Ua.Types.Namespaces.OpcUa);

                    if (instance is PropertyDesign &&
                        instance.SymbolicName == propertyName)
                    {
                        instance.OveriddenNode = this.FindNode<VariableDesign>(
                            new XmlQualifiedName("EnumStrings", Ua.Types.Namespaces.OpcUa),
                            instance.SymbolicId.Name,
                            propertyName.Name);
                    }

                    m_logger.LogDebug(
                        "IndexingInstance: {Path} : {Instance}",
                        browsePath,
                        instance.SymbolicId.Name);
                    nodes[browsePath] = instance;
                }
            }
        }

        private void SetOverriddenNodes(
            InstanceDesign parent,
            string basePath,
            Dictionary<string, InstanceDesign> nodes,
            int depth)
        {
            if (depth > MaxRecursionDepth)
            {
                throw new InvalidOperationException(
                    $"{parent.SymbolicId.Name} max recursion exceeded. Check model.");
            }

            if (parent.TypeDefinitionNode != null)
            {
                SetOverriddenNodes(
                    parent.TypeDefinitionNode,
                    basePath,
                    nodes,
                    depth + 1,
                    true);
            }

            if (parent.Children != null && parent.Children.Items != null)
            {
                for (int ii = 0; ii < parent.Children.Items.Length; ii++)
                {
                    InstanceDesign instance = parent.Children.Items[ii];

                    if (instance.ModellingRule == ModellingRule.ExposesItsArray)
                    {
                        continue;
                    }

                    string browsePath = GetBrowsePath(basePath, instance);

                    if (instance.ModellingRule is
                        ModellingRule.MandatoryPlaceholder or
                        ModellingRule.OptionalPlaceholder)
                    {
                        continue;
                    }

                    SetOverriddenNodes(instance, browsePath, nodes, depth + 1);

                    if (nodes.TryGetValue(browsePath, out InstanceDesign overriddenInstance))
                    {
                        bool inPath = false;

                        for (InstanceDesign current = overriddenInstance;
                            current != null;
                            current = current.OveriddenNode)
                        {
                            if (current.SymbolicId == instance.SymbolicId)
                            {
                                inPath = true;
                                break;
                            }
                        }

                        if (!inPath)
                        {
                            instance.OveriddenNode = overriddenInstance;
                        }
                    }

                    nodes[browsePath] = instance;
                }
            }
        }

        /// <summary>
        /// Collects all of children for a node.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void SetOverriddenNodes(NodeDesign node, int depth)
        {
            if (node is ReferenceTypeDesign or DataTypeDesign)
            {
                return;
            }

            if (depth > MaxRecursionDepth)
            {
                throw new InvalidOperationException(
                    $"{node.SymbolicId.Name} max recursion exceeded. Check model.");
            }

            var nodes = new Dictionary<string, InstanceDesign>();
            switch (node)
            {
                case TypeDesign type:
                    SetOverriddenNodes(type, string.Empty, nodes, depth + 1, false);
                    break;
                case InstanceDesign instance:
                    SetOverriddenNodes(instance, string.Empty, nodes, depth + 1);
                    break;
            }
        }

        private void TranslateReferences(
            string currentPath,
            NodeDesign source,
            List<HierarchyReference> references,
            bool suppressInverseHierarchicalAtTypeLevel,
            bool inherited)
        {
            if (source.References == null || source.References.Length == 0)
            {
                return;
            }

            for (int ii = 0; ii < source.References.Length; ii++)
            {
                if (source.References[ii].ReferenceType ==
                    new XmlQualifiedName("HasModelParent", Ua.Types.Namespaces.OpcUa))
                {
                    continue;
                }

                // suppress inhierited non-hierarchial references.
                if (inherited &&
                    m_nodes.TryGetValue(source.References[ii].ReferenceType, out NodeDesign target))
                {
                    var referenceType = target as ReferenceTypeDesign;

                    bool found = false;

                    while (referenceType != null)
                    {
                        if (referenceType.SymbolicName ==
                            new XmlQualifiedName("NonHierarchicalReferences", Ua.Types.Namespaces.OpcUa))
                        {
                            found = true;
                            break;
                        }

                        referenceType = referenceType.BaseTypeNode as ReferenceTypeDesign;
                    }

                    if (found)
                    {
                        continue;
                    }
                }

                if (suppressInverseHierarchicalAtTypeLevel &&
                    source.References[ii].IsInverse &&
                    source.References[ii].ReferenceType ==
                        new XmlQualifiedName("Organizes", Ua.Types.Namespaces.OpcUa))
                {
                    continue;
                }

                HierarchyReference reference = TranslateReference(
                    currentPath,
                    source.SymbolicId,
                    source.References[ii]);

                references.Add(reference);

                m_logger.LogDebug(
                    "Translated Reference: {Source} => {Reference} => {Target}",
                    string.IsNullOrEmpty(reference.SourcePath) ?
                        source.SymbolicId.Name :
                        reference.SourcePath,
                    reference.ReferenceType.Name,
                    reference.TargetId != null ?
                        reference.TargetId.Name :
                        reference.TargetPath);
            }
        }

        private static HierarchyReference TranslateReference(
            string currentPath,
            XmlQualifiedName sourceId,
            Reference reference)
        {
            currentPath ??= string.Empty;

            var mergedReference = new HierarchyReference
            {
                SourcePath = currentPath,
                ReferenceType = reference.ReferenceType,
                IsInverse = reference.IsInverse,
                TargetId = reference.TargetId
            };

            if (reference.TargetId == null ||
                sourceId.Namespace != reference.TargetId.Namespace ||
                reference.ReferenceType == new XmlQualifiedName("HasEncoding", Ua.Types.Namespaces.OpcUa))
            {
                return mergedReference;
            }

            string[] currentPathParts = currentPath.Split(
                NodeDesign.PathChars,
                StringSplitOptions.RemoveEmptyEntries);
            string[] sourceIdParts = sourceId.Name.Split(
                NodeDesign.PathChars,
                StringSplitOptions.RemoveEmptyEntries);
            string[] targetIdParts = reference.TargetId.Name.Split(
                NodeDesign.PathChars,
                StringSplitOptions.RemoveEmptyEntries);

            // find the common root in the type declaration.
            string[] targetPath = null;
            string[] sourcePath = null;

            if (sourceIdParts.Length == 0 ||
                targetIdParts.Length == 0 ||
                targetIdParts[0] != sourceIdParts[0])
            {
                return mergedReference;
            }

            for (int ii = 0; ii < sourceIdParts.Length; ii++)
            {
                if (ii >= targetIdParts.Length)
                {
                    sourcePath = new string[sourceIdParts.Length - ii];
                    Array.Copy(sourceIdParts, ii, sourcePath, 0, sourcePath.Length);
                    targetPath = [];
                    break;
                }

                if (targetIdParts[ii] != sourceIdParts[ii])
                {
                    sourcePath = new string[sourceIdParts.Length - ii];
                    Array.Copy(sourceIdParts, ii, sourcePath, 0, sourcePath.Length);
                    targetPath = new string[targetIdParts.Length - ii];
                    Array.Copy(targetIdParts, ii, targetPath, 0, targetPath.Length);
                    break;
                }
            }

            // no common root.
            if (sourcePath == null)
            {
                sourcePath = [];
                targetPath = new string[targetIdParts.Length - sourceIdParts.Length];
                Array.Copy(targetIdParts, sourceIdParts.Length, targetPath, 0, targetPath.Length);
            }

            // find the new root.
            string[] targetRoot = null;

            for (int ii = 1; ii <= sourcePath.Length - 1; ii++)
            {
                if (ii > currentPathParts.Length)
                {
                    return mergedReference;
                }

                if (currentPathParts[^ii] != sourcePath[^ii])
                {
                    targetRoot = new string[currentPathParts.Length - ii];
                    Array.Copy(currentPathParts, 0, targetRoot, 0, targetRoot.Length);
                    break;
                }
            }

            // no common root.
            if (targetRoot == null)
            {
                if (currentPathParts.Length < sourcePath.Length)
                {
                    return mergedReference;
                }

                targetRoot = new string[currentPathParts.Length - sourcePath.Length];
                Array.Copy(currentPathParts, 0, targetRoot, 0, targetRoot.Length);
            }

            var builder = new StringBuilder();

            for (int ii = 0; ii < targetRoot.Length; ii++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(NodeDesign.PathChar);
                }

                builder.Append(targetRoot[ii]);
            }

            if (targetPath != null)
            {
                for (int ii = 0; ii < targetPath.Length; ii++)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(NodeDesign.PathChar);
                    }

                    builder.Append(targetPath[ii]);
                }
            }

            mergedReference.TargetId = null;
            mergedReference.TargetPath = builder.ToString();

            return mergedReference;
        }

        /// <summary>
        /// Collects all of children for a type.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void BuildInstanceHierarchy(
            TypeDesign type,
            string basePath,
            List<HierarchyNode> nodes,
            List<HierarchyReference> references,
            bool suppressInverseHierarchicalAtTypeLevel,
            bool inherited,
            int depth)
        {
            m_logger.LogDebug(
                "BuildHierarchy for Type: {Type} : {Path}",
                type.SymbolicId.Name,
                basePath);

            if (depth > MaxRecursionDepth)
            {
                throw new InvalidOperationException(
                    $"{basePath} max recursion exceeded. Check model.");
            }

            if (type.BaseTypeNode != null &&
                type is VariableTypeDesign or ObjectTypeDesign)
            {
                BuildInstanceHierarchy(
                    type.BaseTypeNode,
                    basePath,
                    nodes,
                    references,
                    true,
                    true,
                    depth + 1);
            }

            TranslateReferences(
                basePath,
                type,
                references,
                suppressInverseHierarchicalAtTypeLevel,
                inherited);

            if (type.Children != null && type.Children.Items != null)
            {
                for (int ii = 0; ii < type.Children.Items.Length; ii++)
                {
                    InstanceDesign instance = type.Children.Items[ii];

                    string browsePath = GetBrowsePath(basePath, instance);

                    if (!string.IsNullOrEmpty(basePath))
                    {
                        if (instance.ModellingRule is
                            ModellingRule.None or
                            ModellingRule.ExposesItsArray or
                            ModellingRule.OptionalPlaceholder)
                        {
                            continue;
                        }

                        if (instance.ModellingRule == ModellingRule.MandatoryPlaceholder)
                        {
                            var reference = new HierarchyReference
                            {
                                SourcePath = basePath,
                                ReferenceType = instance.ReferenceType,
                                IsInverse = false,
                                TargetId = instance.SymbolicId
                            };

                            references.Add(reference);

                            //if (instance.SymbolicId.Namespace != DefaultNamespace)
                            //{
                            //    m_logger.LogDebug("TypeDesign Placeholder: {Source} => {Target}",
                            //        reference.SourcePath, reference.TargetId);
                            //}

                            continue;
                        }
                    }

                    var child = new HierarchyNode
                    {
                        RelativePath = browsePath,
                        Instance = instance,
                        Inherited = inherited
                    };

                    nodes.Add(child);
                    BuildInstanceHierarchy(
                        instance,
                        browsePath,
                        nodes,
                        references,
                        inherited,
                        depth + 1);
                }
            }
        }

        /// <summary>
        /// Collects all of children for an instance.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void BuildInstanceHierarchy(
            InstanceDesign parent,
            string basePath,
            List<HierarchyNode> nodes,
            List<HierarchyReference> references,
            bool inherited,
            int depth)
        {
            m_logger.LogDebug(
                "BuildHierarchy for Instance: {Name} : {Path}",
                parent.SymbolicId.Name,
                basePath);

            if (depth > MaxRecursionDepth)
            {
                throw new InvalidOperationException(
                    $"{basePath} max recursion exceeded. Check model.");
            }

            if (parent.TypeDefinitionNode != null)
            {
                BuildInstanceHierarchy(
                    parent.TypeDefinitionNode,
                    basePath,
                    nodes,
                    references,
                    true,
                    inherited,
                    depth + 1);
            }

            if (parent.TypeDefinition != null && parent is MethodDesign)
            {
                MethodDesign methodType = this.FindNode<MethodDesign>(
                    parent.TypeDefinition,
                    parent.SymbolicId.Name,
                    "MethodType");

                if (methodType != null)
                {
                    BuildInstanceHierarchy(
                        methodType,
                        basePath,
                        nodes,
                        references,
                        inherited,
                        depth + 1);
                }
            }

            TranslateReferences(basePath, parent, references, false, false);

            if (parent.Children != null && parent.Children.Items != null)
            {
                bool isTypeDefinition = parent.Parent is TypeDesign;

                for (int ii = 0; ii < parent.Children.Items.Length; ii++)
                {
                    InstanceDesign instance = parent.Children.Items[ii];

                    string browsePath = GetBrowsePath(basePath, instance);

                    if (inherited && !string.IsNullOrEmpty(basePath))
                    {
                        if (instance.ModellingRule is ModellingRule.None or
                            ModellingRule.ExposesItsArray or
                            ModellingRule.OptionalPlaceholder)
                        {
                            continue;
                        }

                        if (instance.ModellingRule == ModellingRule.MandatoryPlaceholder)
                        {
                            HierarchyReference reference = references
                                .FirstOrDefault(x => x.SourcePath == browsePath);

                            if (reference == null)
                            {
                                reference = new HierarchyReference();
                                references.Add(reference);
                            }

                            reference.SourcePath = basePath;
                            reference.ReferenceType = instance.ReferenceType;
                            reference.IsInverse = false;
                            reference.TargetId = instance.SymbolicId;

                            continue;
                        }
                    }

                    var child = new HierarchyNode
                    {
                        RelativePath = browsePath,
                        Instance = instance,
                        Inherited = inherited
                    };

                    if (isTypeDefinition && parent is not MethodDesign)
                    {
                        if (instance.OveriddenNode == null)
                        {
                            child.AdHocInstance = true;
                        }

                        if (instance.ModellingRule == ModellingRule.None &&
                            instance is VariableDesign &&
                            instance.OveriddenNode != null)
                        {
                            child.StaticValue = true;
                        }
                    }

                    if (instance.ModellingRule
                        is ModellingRule.OptionalPlaceholder
                        or ModellingRule.MandatoryPlaceholder &&
                        depth > 3)
                    {
                        continue;
                    }

                    nodes.Add(child);

                    BuildInstanceHierarchy(
                        instance,
                        browsePath,
                        nodes,
                        references,
                        inherited,
                        depth + 1);
                }
            }
        }

        /// <summary>
        /// Collects all of children for a node.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void BuildInstanceHierarchy(
            NodeDesign node,
            List<HierarchyNode> nodes,
            List<HierarchyReference> references,
            int depth)
        {
            if (depth > MaxRecursionDepth)
            {
                throw new InvalidOperationException(
                    $"{node.SymbolicId.Name} max recursion exceeded. Check model.");
            }

            switch (node)
            {
                case TypeDesign type:
                    BuildInstanceHierarchy(
                        type,
                        string.Empty,
                        nodes,
                        references,
                        false,
                        false,
                        depth + 1);
                    break;
                case InstanceDesign instance:
                    BuildInstanceHierarchy(
                        instance,
                        string.Empty,
                        nodes,
                        references,
                        false,
                        depth + 1);
                    break;
            }
        }

        private Hierarchy BuildInstanceHierarchy(NodeDesign root, int depth)
        {
            m_logger.LogDebug(
                "Building InstanceHierarchy for {Name}",
                root.SymbolicId.Name);
            if (depth > MaxRecursionDepth)
            {
                throw new InvalidOperationException(
                    $"{root.SymbolicId.Name} max recursion exceeded. Check model.");
            }

            bool designToolOnly = root is InstanceDesign dto && dto.DesignToolOnly;

            SetOverriddenNodes(root, 0);

            // collect all of the nodes that define the hierachy.
            var nodes = new List<HierarchyNode>();
            var references = new List<HierarchyReference>();

            if (!designToolOnly)
            {
                BuildInstanceHierarchy(root, nodes, references, depth + 1);
            }

            var hierarchy = new Hierarchy
            {
                References = references
            };

            XmlQualifiedName rootId = root.SymbolicId;

            // add root node.
            var instance = root as InstanceDesign;

            if (instance == null)
            {
                rootId = new XmlQualifiedName(
                    "InstanceOf" + root.SymbolicId.Name,
                    root.SymbolicId.Namespace);
            }

            var rootNode = new HierarchyNode
            {
                RelativePath = string.Empty
            };

            if (instance == null || instance.TypeDefinitionNode == null)
            {
                rootNode.Instance = CreateMergedInstance(
                    rootId,
                    string.Empty,
                    root);
            }
            else
            {
                rootNode.Instance = CreateMergedInstance(
                    rootId,
                    string.Empty,
                    instance.TypeDefinitionNode);

                if (root.SymbolicName == rootNode.Instance.SymbolicName)
                {
                    rootNode.Instance.BrowseName = root.BrowseName;
                    rootNode.Instance.DisplayName = root.DisplayName;
                }

                UpdateMergedInstance((InstanceDesign)rootNode.Instance, root);
            }

            if (designToolOnly)
            {
                rootNode.Instance.Children = new ListOfChildren();
            }

            rootNode.ExplicitlyDefined = false;

            hierarchy.Nodes.Add(string.Empty, rootNode);
            hierarchy.NodeList.Add(rootNode);

            // build instance hierachy.
            if (!designToolOnly)
            {
                for (int ii = 0; ii < nodes.Count; ii++)
                {
                    HierarchyNode node = nodes[ii];

                    bool explicitlyDefined = false;

                    for (NodeDesign parent = node.Instance; parent != null; parent = parent.Parent)
                    {
                        if (parent.SymbolicId == root.SymbolicId)
                        {
                            explicitlyDefined = true;
                            break;
                        }
                    }

                    if (!hierarchy.Nodes.TryGetValue(node.RelativePath, out HierarchyNode mergedNode))
                    {
                        mergedNode = new HierarchyNode
                        {
                            RelativePath = node.RelativePath,
                            Instance = CreateMergedInstance(
                                root.SymbolicId,
                                node.RelativePath,
                                node.Instance),
                            ExplicitlyDefined = false,
                            Inherited = node.Inherited,
                            AdHocInstance = node.AdHocInstance
                        };

                        hierarchy.Nodes.Add(node.RelativePath, mergedNode);
                        hierarchy.NodeList.Add(mergedNode);
                    }
                    else
                    {
                        UpdateMergedInstance((InstanceDesign)mergedNode.Instance, node.Instance);
                        mergedNode.StaticValue = node.StaticValue;
                    }

                    if (explicitlyDefined && node.Instance.Extensions != null)
                    {
                        mergedNode.Instance.Extensions = node.Instance.Extensions;
                    }

                    mergedNode.OverriddenNodes ??= [];
                    mergedNode.OverriddenNodes.Add(node.Instance);

                    if (explicitlyDefined)
                    {
                        mergedNode.ExplicitlyDefined = true;
                    }
                }
            }

            return hierarchy;
        }

        /// <summary>
        /// Load model design xml or json
        /// </summary>
        /// <param name="fileToLoad"></param>
        /// <returns></returns>
        private ModelDesign LoadModelDesign(string fileToLoad)
        {
            string fileExtension = Path.GetExtension(fileToLoad);
            if (string.Equals(fileExtension, "json", StringComparison.OrdinalIgnoreCase))
            {
                // Load from json file
                using Stream stream = OpenRead(fileToLoad);
                return JsonSerializer.Deserialize<ModelDesignJson>(
                    stream)?.ToModelDesign();
            }
            return Load<ModelDesign>(fileToLoad);
        }

        /// <summary>
        /// Allocates identifiers
        /// </summary>
        private sealed class IdAllocator
        {
            /// <summary>
            /// Allocate identifiers
            /// </summary>
            /// <param name="startId"></param>
            public IdAllocator(uint startId)
            {
                m_lastId = startId;
                m_lastBuiltInId = 15000;
                m_lastImplicitlyDefined = 1000000;
            }

            /// <summary>
            /// Find unused identifier
            /// </summary>
            public uint FindUnusedId(string ns, bool isImplicitlyDefined = false)
            {
                if (ns == Ua.Types.Namespaces.OpcUa)
                {
                    return FindUnusedId(ref m_lastBuiltInId);
                }
                else if (isImplicitlyDefined)
                {
                    return FindUnusedId(ref m_lastImplicitlyDefined);
                }
                return FindUnusedId(ref m_lastId);
            }

            /// <summary>
            /// Add identifier
            /// </summary>
            public void Add(uint value)
            {
                m_identifiers.Add(value);
            }

            /// <summary>
            /// Find an unused identifier
            /// </summary>
            private uint FindUnusedId(ref uint lastId)
            {
                uint id = lastId;
                while (m_identifiers.Contains(id))
                {
                    id++;
                }
                lastId = id;
                m_identifiers.Add(id);
                return id;
            }

            private uint m_lastId;
            private uint m_lastBuiltInId;
            private uint m_lastImplicitlyDefined;
            private readonly HashSet<uint> m_identifiers = [];
        }

        private static readonly XmlQualifiedName s_baseDataTypeQn =
            new("BaseDataType", Ua.Types.Namespaces.OpcUa);

        private static readonly XmlQualifiedName s_structureQn =
            new("Structure", Ua.Types.Namespaces.OpcUa);

        private static readonly XmlQualifiedName s_byteStringQn =
            new("ByteString", Ua.Types.Namespaces.OpcUa);

        private static readonly XmlQualifiedName s_baseObjectTypeQn =
            new("BaseObjectType", Ua.Types.Namespaces.OpcUa);

        private static readonly XmlQualifiedName s_baseDataVariableTypeQn =
            new("BaseDataVariableType", Ua.Types.Namespaces.OpcUa);

        private static readonly XmlQualifiedName s_referencesQn =
            new("References", Ua.Types.Namespaces.OpcUa);

        private static readonly XmlQualifiedName s_enumerationQn =
            new("Enumeration", Ua.Types.Namespaces.OpcUa);

        private static readonly XmlQualifiedName s_unionQn =
            new("Union", Ua.Types.Namespaces.OpcUa);

        private uint m_startId;
        private readonly SpecificationVersion m_standardVersion;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private Dictionary<XmlQualifiedName, NodeDesign> m_nodes;
        private Dictionary<string, string[]> m_namespaceTables;
        private Dictionary<NodeId, NodeDesign> m_nodesByNodeId;
        private Dictionary<uint, NodeDesign> m_identifiers;
        private Dictionary<XmlQualifiedName, string> m_browseNames = [];
        private readonly ServiceMessageContext m_context;
        private readonly IReadOnlyList<string> m_exclusions;
        private readonly Dictionary<XmlQualifiedName, NodeId> m_symbolicIdToNodeId = [];
        private ModelDesign m_dictionary;
        private Dictionary<string, ModelTableEntry> m_dependencies = [];
        private readonly Dictionary<string, RolePermissionSet> m_defaultRolePermissions = [];
        private readonly Dictionary<string, AccessRestrictions?> m_defaultAccessRestrictions = [];
        private Dictionary<string, string> m_designFilePaths = [];
    }
}
