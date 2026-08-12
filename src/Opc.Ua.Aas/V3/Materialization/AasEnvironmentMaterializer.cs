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
using System.Runtime.Serialization;
using Opc.Ua.Export;
using SystemXml = System.Xml;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Materializes an AAS V3 <see cref="AasEnvironment"/> into a deterministic OPC UA NodeSet.
    /// </summary>
    public static class AasEnvironmentMaterializer
    {
        /// <summary>
        /// Materializes an AAS Environment according to clause 6.1.6.
        /// </summary>
        /// <param name="environment">The source AAS Environment.</param>
        /// <returns>The produced NodeSet and structured diagnostics.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="environment"/> is <c>null</c>.</exception>
        public static AasMaterializationResult Materialize(AasEnvironment environment)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            var diagnostics = new List<AasMaterializationDiagnostic>();
            var items = new List<UANode>();
            var nodeSet = new UANodeSet
            {
                NamespaceUris = [Namespaces.AasV3],
                Aliases = s_aliases,
                Models = [new ModelTableEntry { ModelUri = Namespaces.AasV3 }]
            };

            var environmentNode = new UAObject
            {
                NodeId = EnvironmentNodeId,
                BrowseName = BrowseName("AASEnvironment"),
                DisplayName = Text("AASEnvironment"),
                References =
                [
                    TypeDefinition(ObjectTypeIds.AASEnvironmentType)
                ]
            };
            items.Add(environmentNode);

            Dictionary<string, string> browseNames = AllocateTopLevelBrowseNames(environment, diagnostics);
            HashSet<string> duplicateShells = FindDuplicates(environment.AssetAdministrationShells);
            HashSet<string> duplicateSubmodels = FindDuplicates(environment.Submodels);
            HashSet<string> duplicateConcepts = FindDuplicates(environment.ConceptDescriptions);

            MaterializeTopLevel(
                environment.AssetAdministrationShells,
                AasNodeKind.Shell,
                duplicateShells,
                browseNames,
                diagnostics,
                items,
                static (builder, shell) => builder.AddShell(shell));
            MaterializeTopLevel(
                environment.Submodels,
                AasNodeKind.Submodel,
                duplicateSubmodels,
                browseNames,
                diagnostics,
                items,
                static (builder, submodel) => builder.AddSubmodel(submodel));
            MaterializeTopLevel(
                environment.ConceptDescriptions,
                AasNodeKind.ConceptDescription,
                duplicateConcepts,
                browseNames,
                diagnostics,
                items,
                static (builder, concept) => builder.AddConceptDescription(concept));

            nodeSet.Items = [.. items];
            return new AasMaterializationResult(nodeSet, diagnostics);
        }

        private static void MaterializeTopLevel<T>(
            AasOptional<ArrayOf<T>> source,
            AasNodeKind kind,
            HashSet<string> duplicates,
            Dictionary<string, string> browseNames,
            List<AasMaterializationDiagnostic> diagnostics,
            List<UANode> items,
            Action<IdentifiableBuilder, T> add)
            where T : AasIdentifiable
        {
            if (!source.IsPresent)
            {
                return;
            }

            foreach (T identifiable in source.Value.Span)
            {
                if (duplicates.Contains(identifiable.Id))
                {
                    diagnostics.Add(new AasMaterializationDiagnostic(
                        AasMaterializationDiagnosticSeverity.Error,
                        AasMaterializationDiagnosticCode.DuplicateIdentifier,
                        "Duplicate identifiers within one identifiable kind produce the same NodeId and are rejected.",
                        new AasMaterializationLocation(kind, identifiable.Id)));
                    continue;
                }

                string nodeId = NodeId(AasNodeIdEncoding.CreateIdentifiableId(kind, identifiable.Id));
                var builder = new IdentifiableBuilder(kind, identifiable.Id, browseNames[identifiable.Id], diagnostics);
                add(builder, identifiable);

                if (builder.HasRejectedIdentifier)
                {
                    continue;
                }

                items.AddRange(builder.Nodes);
                Link(EnvironmentNodeId, nodeId, "Organizes", items);
            }
        }

        private static Dictionary<string, string> AllocateTopLevelBrowseNames(
            AasEnvironment environment,
            List<AasMaterializationDiagnostic> diagnostics)
        {
            var allocator = new AasBrowseNameAllocator();
            ReserveOrRegister(environment.AssetAdministrationShells, AasNodeKind.Shell, allocator);
            ReserveOrRegister(environment.Submodels, AasNodeKind.Submodel, allocator);
            ReserveOrRegister(environment.ConceptDescriptions, AasNodeKind.ConceptDescription, allocator);

            IReadOnlyDictionary<string, string> derived = allocator.Allocate();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            AddBrowseNames(environment.AssetAdministrationShells, AasNodeKind.Shell, derived, result, diagnostics);
            AddBrowseNames(environment.Submodels, AasNodeKind.Submodel, derived, result, diagnostics);
            AddBrowseNames(environment.ConceptDescriptions, AasNodeKind.ConceptDescription, derived, result, diagnostics);
            return result;
        }

        private static void ReserveOrRegister<T>(
            AasOptional<ArrayOf<T>> source,
            AasNodeKind kind,
            AasBrowseNameAllocator allocator)
            where T : AasIdentifiable
        {
            if (!source.IsPresent)
            {
                return;
            }

            foreach (T identifiable in source.Value.Span)
            {
                if (identifiable.IdShort.IsPresent && !string.IsNullOrEmpty(identifiable.IdShort.Value))
                {
                    allocator.Reserve(identifiable.IdShort.Value);
                }
                else
                {
                    allocator.RegisterDerived(kind, identifiable.Id);
                }
            }
        }

        private static void AddBrowseNames<T>(
            AasOptional<ArrayOf<T>> source,
            AasNodeKind kind,
            IReadOnlyDictionary<string, string> derived,
            Dictionary<string, string> result,
            List<AasMaterializationDiagnostic> diagnostics)
            where T : AasIdentifiable
        {
            if (!source.IsPresent)
            {
                return;
            }

            foreach (T identifiable in source.Value.Span)
            {
                string name = identifiable.IdShort.IsPresent && !string.IsNullOrEmpty(identifiable.IdShort.Value)
                    ? identifiable.IdShort.Value
                    : derived[identifiable.Id];
                result[identifiable.Id] = name;
                _ = kind;
                _ = diagnostics;
            }
        }

        private static HashSet<string> FindDuplicates<T>(AasOptional<ArrayOf<T>> source)
            where T : AasIdentifiable
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            if (!source.IsPresent)
            {
                return duplicates;
            }

            foreach (T identifiable in source.Value.Span)
            {
                if (!seen.Add(identifiable.Id))
                {
                    duplicates.Add(identifiable.Id);
                }
            }
            return duplicates;
        }

        private sealed class IdentifiableBuilder
        {
            public IdentifiableBuilder(
                AasNodeKind kind,
                string ownerId,
                string browseName,
                List<AasMaterializationDiagnostic> diagnostics)
            {
                Kind = kind;
                OwnerId = ownerId;
                BrowseName = browseName;
                Diagnostics = diagnostics;
                Nodes = [];
            }

            public AasNodeKind Kind { get; }

            public string OwnerId { get; }

            public string BrowseName { get; }

            public bool HasRejectedIdentifier { get; private set; }

            public List<UANode> Nodes { get; }

            public void AddShell(AasShell shell)
            {
                string nodeId = NodeId(AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, shell.Id));
                if (!CheckNodeId(nodeId, string.Empty))
                {
                    return;
                }
                var node = ObjectNode(nodeId, BrowseName, ObjectTypeIds.AASType, shell.IdShort, BrowseName);
                Nodes.Add(node);
                AddReferable(nodeId, shell);
                AddIdentifiable(nodeId, shell);
                AddObject(nodeId, "AssetInformation", ObjectTypeIds.AASAssetInformationType, asset =>
                {
                    AddProperty(asset, "AssetKind", DataTypes.AASAssetKindDataType,
                        new Variant((int)shell.AssetInformation.AssetKind));
                    AddOptionalString(asset, "GlobalAssetId", shell.AssetInformation.GlobalAssetId);
                    AddOptionalString(asset, "AssetType", shell.AssetInformation.AssetType);
                    AddOptionalStructureArray(asset, "SpecificAssetIds", DataTypes.AASSpecificAssetIdDataType,
                        shell.AssetInformation.SpecificAssetIds);
                    AddOptionalStructure(asset, "DefaultThumbnail", DataTypes.AASResourceDataType,
                        shell.AssetInformation.DefaultThumbnail);
                });
                AddOptionalStructureArray(nodeId, "SubmodelReferences", DataTypes.AASReferenceDataType,
                    shell.SubmodelReferences);
                AddOptionalStructure(nodeId, "DerivedFrom", DataTypes.AASReferenceDataType, shell.DerivedFrom);
                AddOptionalStructureArray(nodeId, "EmbeddedDataSpecifications",
                    DataTypes.AASEmbeddedDataSpecificationDataType, shell.EmbeddedDataSpecifications);
            }

            public void AddSubmodel(AasSubmodel submodel)
            {
                string nodeId = NodeId(AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Submodel, submodel.Id));
                if (!CheckNodeId(nodeId, string.Empty))
                {
                    return;
                }
                var node = ObjectNode(nodeId, BrowseName, ObjectTypeIds.AASSubmodelType, submodel.IdShort, BrowseName);
                Nodes.Add(node);
                AddReferable(nodeId, submodel);
                AddIdentifiable(nodeId, submodel);
                AddOptionalEnum(nodeId, "Kind", DataTypes.AASModellingKindDataType, submodel.Kind);
                AddHasSemantics(nodeId, submodel);
                AddOptionalStructureArray(nodeId, "Qualifiers", DataTypes.AASQualifierDataType, submodel.Qualifiers);
                AddOptionalStructureArray(nodeId, "EmbeddedDataSpecifications",
                    DataTypes.AASEmbeddedDataSpecificationDataType, submodel.EmbeddedDataSpecifications);
                if (submodel.SubmodelElements.IsPresent)
                {
                    foreach (AasSubmodelElement element in submodel.SubmodelElements.Value.Span)
                    {
                        AddElement(nodeId, string.Empty, element, false, 0, "HasComponent");
                    }
                }
            }

            public void AddConceptDescription(AasConceptDescription concept)
            {
                string nodeId = NodeId(AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.ConceptDescription, concept.Id));
                if (!CheckNodeId(nodeId, string.Empty))
                {
                    return;
                }
                var node = ObjectNode(
                    nodeId,
                    BrowseName,
                    ObjectTypeIds.AASConceptDescriptionType,
                    concept.IdShort,
                    BrowseName);
                Nodes.Add(node);
                AddReferable(nodeId, concept);
                AddIdentifiable(nodeId, concept);
                AddOptionalStructureArray(nodeId, "IsCaseOf", DataTypes.AASReferenceDataType, concept.IsCaseOf);
                AddOptionalStructureArray(nodeId, "EmbeddedDataSpecifications",
                    DataTypes.AASEmbeddedDataSpecificationDataType, concept.EmbeddedDataSpecifications);
            }

            private void AddElement(
                string parentNodeId,
                string parentPath,
                AasSubmodelElement element,
                bool isListMember,
                int index,
                string referenceType)
            {
                if (HasRejectedIdentifier)
                {
                    return;
                }

                if (!TryGetElementName(element, isListMember, index, out string browseName, out string path))
                {
                    return;
                }

                string nodeId = NodeId(AasNodeIdEncoding.CreateElementId(OwnerId, path));
                if (!CheckNodeId(nodeId, path))
                {
                    return;
                }

                ExpandedNodeId typeId = ObjectTypeOf(element);
                var node = ObjectNode(nodeId, browseName, typeId, isListMember ? AasOptional<string>.Absent : element.IdShort, browseName);
                Nodes.Add(node);
                Link(parentNodeId, nodeId, referenceType, Nodes);
                AddReferable(nodeId, element, suppressIdShort: isListMember);
                AddHasSemantics(nodeId, element);
                AddOptionalStructureArray(nodeId, "Qualifiers", DataTypes.AASQualifierDataType, element.Qualifiers);
                AddOptionalStructureArray(nodeId, "EmbeddedDataSpecifications",
                    DataTypes.AASEmbeddedDataSpecificationDataType, element.EmbeddedDataSpecifications);
                if (isListMember || element.Index.IsPresent)
                {
                    AddProperty(nodeId, "Index", Opc.Ua.DataTypes.UInt32, new Variant(isListMember ? (uint)index : element.Index.Value));
                }

                AddElementMembers(nodeId, path, element);
            }

            private bool TryGetElementName(
                AasSubmodelElement element,
                bool isListMember,
                int index,
                out string browseName,
                out string path)
            {
                if (isListMember)
                {
                    browseName = AasBrowseNameAllocator.ForListMember(index);
                    path = AasIdShortPath.AppendIndex(string.Empty, index);
                    return true;
                }

                if (!element.IdShort.IsPresent || string.IsNullOrEmpty(element.IdShort.Value))
                {
                    Diagnostics.Add(new AasMaterializationDiagnostic(
                        AasMaterializationDiagnosticSeverity.Error,
                        AasMaterializationDiagnosticCode.MissingIdShort,
                        "A non-list SubmodelElement requires an idShort to derive its idShortPath.",
                        new AasMaterializationLocation(Kind, OwnerId)));
                    browseName = string.Empty;
                    path = string.Empty;
                    return false;
                }

                browseName = element.IdShort.Value;
                path = AasIdShortPath.AppendName(string.Empty, element.IdShort.Value);
                return true;
            }

            private void AddChildElement(
                string parentNodeId,
                string parentPath,
                AasSubmodelElement element,
                string referenceType)
            {
                if (!element.IdShort.IsPresent || string.IsNullOrEmpty(element.IdShort.Value))
                {
                    Diagnostics.Add(new AasMaterializationDiagnostic(
                        AasMaterializationDiagnosticSeverity.Error,
                        AasMaterializationDiagnosticCode.MissingIdShort,
                        "A contained SubmodelElement outside a list requires an idShort to derive its idShortPath.",
                        new AasMaterializationLocation(Kind, OwnerId, parentPath)));
                    return;
                }

                string childPath = AasIdShortPath.AppendName(parentPath, element.IdShort.Value);
                AddElementAtPath(parentNodeId, childPath, element, element.IdShort.Value, referenceType);
            }

            private void AddListElement(string parentNodeId, string parentPath, AasSubmodelElement element, int index, string referenceType)
            {
                string browseName = AasBrowseNameAllocator.ForListMember(index);
                string childPath = AasIdShortPath.AppendIndex(parentPath, index);
                AddElementAtPath(parentNodeId, childPath, element, browseName, referenceType, isListMember: true, index: index);
            }

            private void AddOperationElement(
                string operationNodeId,
                string operationPath,
                AasSubmodelElement element,
                AasOperationVariableRole role,
                int index)
            {
                if (!element.IdShort.IsPresent || string.IsNullOrEmpty(element.IdShort.Value))
                {
                    Diagnostics.Add(new AasMaterializationDiagnostic(
                        AasMaterializationDiagnosticSeverity.Error,
                        AasMaterializationDiagnosticCode.MissingIdShort,
                        "An OperationVariable value element requires its own idShort for BrowseName and IdShort.",
                        new AasMaterializationLocation(Kind, OwnerId, operationPath)));
                    return;
                }

                string childPath = AasIdShortPath.AppendOperationVariable(operationPath, role, index);
                AddElementAtPath(operationNodeId, childPath, element, element.IdShort.Value, "HasComponent", index: index);
            }

            private void AddElementAtPath(
                string parentNodeId,
                string path,
                AasSubmodelElement element,
                string browseName,
                string referenceType,
                bool isListMember = false,
                int index = 0)
            {
                if (HasRejectedIdentifier)
                {
                    return;
                }

                string nodeId = NodeId(AasNodeIdEncoding.CreateElementId(OwnerId, path));
                if (!CheckNodeId(nodeId, path))
                {
                    return;
                }

                var node = ObjectNode(nodeId, browseName, ObjectTypeOf(element),
                    isListMember ? AasOptional<string>.Absent : element.IdShort, browseName);
                Nodes.Add(node);
                Link(parentNodeId, nodeId, referenceType, Nodes);
                AddReferable(nodeId, element, suppressIdShort: isListMember);
                AddHasSemantics(nodeId, element);
                AddOptionalStructureArray(nodeId, "Qualifiers", DataTypes.AASQualifierDataType, element.Qualifiers);
                AddOptionalStructureArray(nodeId, "EmbeddedDataSpecifications",
                    DataTypes.AASEmbeddedDataSpecificationDataType, element.EmbeddedDataSpecifications);
                if (isListMember || element.Index.IsPresent || IsOperationVariablePath(path))
                {
                    AddProperty(nodeId, "Index", Opc.Ua.DataTypes.UInt32, new Variant((uint)index));
                }
                AddElementMembers(nodeId, path, element);
            }

            private void AddElementMembers(string nodeId, string path, AasSubmodelElement element)
            {
                switch (element)
                {
                    case AasProperty property:
                        AddProperty(nodeId, "ValueType", DataTypes.AASDataTypeDefXsdDataType, new Variant((int)property.ValueType));
                        if (property.Value.IsPresent)
                        {
                            AddValueProperty(nodeId, "Value", property.ValueType, property.Value.Value);
                        }
                        AddOptionalStructure(nodeId, "ValueId", DataTypes.AASReferenceDataType, property.ValueId);
                        break;
                    case AasMultiLanguageProperty multiLanguage:
                        AddOptionalStructureArray(nodeId, "Value", DataTypes.AASLangStringDataType, multiLanguage.Value);
                        AddOptionalStructure(nodeId, "ValueId", DataTypes.AASReferenceDataType, multiLanguage.ValueId);
                        break;
                    case AasRange range:
                        AddProperty(nodeId, "ValueType", DataTypes.AASDataTypeDefXsdDataType, new Variant((int)range.ValueType));
                        if (range.Min.IsPresent)
                        {
                            AddValueProperty(nodeId, "Min", range.ValueType, range.Min.Value);
                        }
                        if (range.Max.IsPresent)
                        {
                            AddValueProperty(nodeId, "Max", range.ValueType, range.Max.Value);
                        }
                        break;
                    case AasBlob blob:
                        AddOptionalByteString(nodeId, "Value", blob.Value);
                        AddProperty(nodeId, "ContentType", Opc.Ua.DataTypes.String, new Variant(blob.ContentType));
                        break;
                    case AasFile file:
                        AddOptionalString(nodeId, "Value", file.Value);
                        AddProperty(nodeId, "ContentType", Opc.Ua.DataTypes.String, new Variant(file.ContentType));
                        break;
                    case AasReferenceElement referenceElement:
                        AddOptionalStructure(nodeId, "Value", DataTypes.AASReferenceDataType, referenceElement.Value);
                        break;
                    case AasRelationshipElementBase relationship:
                        AddProperty(nodeId, "First", DataTypes.AASReferenceDataType, StructureVariant(relationship.First));
                        AddProperty(nodeId, "Second", DataTypes.AASReferenceDataType, StructureVariant(relationship.Second));
                        if (relationship is AasAnnotatedRelationshipElement annotated && annotated.Annotations.IsPresent)
                        {
                            foreach (AasSubmodelElement annotation in annotated.Annotations.Value.Span)
                            {
                                AddChildElement(nodeId, path, annotation, "HasComponent");
                            }
                        }
                        break;
                    case AasSubmodelElementCollection collection:
                        if (collection.Value.IsPresent)
                        {
                            foreach (AasSubmodelElement child in collection.Value.Value.Span)
                            {
                                AddChildElement(nodeId, path, child, "HasComponent");
                            }
                        }
                        break;
                    case AasSubmodelElementList list:
                        AddProperty(nodeId, "TypeValueListElement", DataTypes.AASSubmodelElementsDataType,
                            new Variant((int)list.TypeValueListElement));
                        AddOptionalStructure(nodeId, "SemanticIdListElement", DataTypes.AASReferenceDataType,
                            list.SemanticIdListElement);
                        AddOptionalEnum(nodeId, "ValueTypeListElement", DataTypes.AASDataTypeDefXsdDataType,
                            list.ValueTypeListElement);
                        if (list.Value.IsPresent)
                        {
                            string referenceType = list.EffectiveOrderRelevant ? "HasOrderedComponent" : "HasComponent";
                            for (int ii = 0; ii < list.Value.Value.Count; ii++)
                            {
                                AddListElement(nodeId, path, list.Value.Value[ii], ii, referenceType);
                            }
                        }
                        break;
                    case AasEntity entity:
                        AddProperty(nodeId, "EntityType", DataTypes.AASEntityTypeDataType, new Variant((int)entity.EntityType));
                        AddOptionalString(nodeId, "GlobalAssetId", entity.GlobalAssetId);
                        AddOptionalStructureArray(nodeId, "SpecificAssetIds", DataTypes.AASSpecificAssetIdDataType,
                            entity.SpecificAssetIds);
                        if (entity.Statements.IsPresent)
                        {
                            foreach (AasSubmodelElement statement in entity.Statements.Value.Span)
                            {
                                AddChildElement(nodeId, path, statement, "HasComponent");
                            }
                        }
                        break;
                    case AasBasicEventElement basicEvent:
                        AddProperty(nodeId, "Observed", DataTypes.AASReferenceDataType, StructureVariant(basicEvent.Observed));
                        AddProperty(nodeId, "Direction", DataTypes.AASDirectionDataType, new Variant((int)basicEvent.Direction));
                        AddProperty(nodeId, "State", DataTypes.AASStateOfEventDataType, new Variant((int)basicEvent.State));
                        AddOptionalString(nodeId, "MessageTopic", basicEvent.MessageTopic);
                        AddOptionalStructure(nodeId, "MessageBroker", DataTypes.AASReferenceDataType, basicEvent.MessageBroker);

                        // The NodeSet declares these three with the DataTypes
                        // clause 6.3.1 assigns their metamodel xsd types, so an
                        // instance must carry the same: xs:dateTime is DateTime
                        // and xs:duration is DurationString. Annex B briefly
                        // said AASValueString, which clause 6.3.2 forbids as a
                        // Variable DataType outright; that has been corrected
                        // in the specification.
                        AddOptionalVariant(nodeId, "LastUpdate", CoreDataType(Opc.Ua.DataTypes.DateTime),
                            basicEvent.LastUpdate);
                        AddOptionalVariant(nodeId, "MinInterval", CoreDataType(Opc.Ua.DataTypes.DurationString),
                            basicEvent.MinInterval);
                        AddOptionalVariant(nodeId, "MaxInterval", CoreDataType(Opc.Ua.DataTypes.DurationString),
                            basicEvent.MaxInterval);
                        break;
                    case AasOperation operation:
                        AddOperationRole(nodeId, path, "InputVariables", operation.InputVariables, AasOperationVariableRole.Input);
                        AddOperationRole(nodeId, path, "OutputVariables", operation.OutputVariables, AasOperationVariableRole.Output);
                        AddOperationRole(nodeId, path, "InoutputVariables", operation.InoutputVariables,
                            AasOperationVariableRole.Inoutput);
                        AddInvokeMethod(nodeId);
                        break;
                }
            }

            /// <summary>
            /// Adds the Invoke Method of clause 6.2.5. AASOperationType
            /// declares it Mandatory, and a NodeSet is imported as written
            /// rather than instantiated from its type, so an Operation that
            /// does not carry the Method cannot be called.
            /// </summary>
            private void AddInvokeMethod(string parentNodeId)
            {
                if (HasRejectedIdentifier)
                {
                    return;
                }

                string nodeId = MemberNodeId(parentNodeId, "Invoke");
                if (!CheckNodeId(nodeId, "Invoke"))
                {
                    return;
                }

                var method = new UAMethod
                {
                    NodeId = nodeId,
                    BrowseName = BrowseName("Invoke"),
                    DisplayName = Text("Invoke"),
                    ParentNodeId = parentNodeId,
                    // The declaration carries the InputArguments and
                    // OutputArguments a Client needs to build the Call.
                    MethodDeclarationId = InvokeDeclarationId,
                    Executable = true,
                    UserExecutable = true,
                    References = [Inverse("HasComponent", parentNodeId)]
                };
                Nodes.Add(method);
                AddForward(parentNodeId, "HasComponent", nodeId, Nodes);

                // A Server validates a Call against the argument definitions on
                // the Method it resolved, not on the declaration, so an
                // instance without them rejects every call as
                // BadTooManyArguments. Their BrowseNames are the standard ones
                // in namespace zero, which is how MethodState binds them.
                AddArguments(nodeId, "InputArguments", s_invokeInputArguments);
                AddArguments(nodeId, "OutputArguments", s_invokeOutputArguments);
            }

            private void AddArguments(string methodNodeId, string browseName, Argument[] arguments)
            {
                var extensions = new ExtensionObject[arguments.Length];
                for (int ii = 0; ii < arguments.Length; ii++)
                {
                    extensions[ii] = new ExtensionObject(arguments[ii]);
                }

                AddProperty(
                    methodNodeId,
                    browseName,
                    CoreDataType(Opc.Ua.DataTypes.Argument),
                    new Variant(new ArrayOf<ExtensionObject>(extensions.AsMemory())),
                    valueRank: 1,
                    standardBrowseName: true);
            }

            private void AddOperationRole(
                string nodeId,
                string path,
                string propertyName,
                AasOptional<ArrayOf<AasSubmodelElement>> variables,
                AasOperationVariableRole role)
            {
                if (!variables.IsPresent)
                {
                    return;
                }

                var descriptors = new AASOperationVariableDataType[variables.Value.Count];
                for (int ii = 0; ii < variables.Value.Count; ii++)
                {
                    string variablePath = AasIdShortPath.AppendOperationVariable(path, role, ii);
                    string variableNodeId = NodeId(AasNodeIdEncoding.CreateElementId(OwnerId, variablePath));
                    AASOperationVariableDataType descriptor = CreateOperationVariableDescriptor();
                    descriptor.ValueNodeId = new NodeId(
                        AasNodeIdEncoding.CreateElementId(OwnerId, variablePath),
                        AasNamespaceIndex);
                    descriptors[ii] = descriptor;
                    AddOperationElement(nodeId, path, variables.Value[ii], role, ii);
                    _ = variableNodeId;
                }

                AddStructureArray(nodeId, propertyName, DataTypes.AASOperationVariableDataType, descriptors);
            }

            private void AddReferable(string nodeId, AasReferable referable, bool suppressIdShort = false)
            {
                if (!suppressIdShort)
                {
                    AddOptionalString(nodeId, "IdShort", referable.IdShort);
                }
                AddOptionalString(nodeId, "Category", referable.Category);
                AddOptionalStructureArray(nodeId, "DisplayNameSet", DataTypes.AASLangStringDataType, referable.DisplayName);
                AddOptionalStructureArray(nodeId, "DescriptionSet", DataTypes.AASLangStringDataType, referable.Description);
                AddOptionalStructureArray(nodeId, "Extensions", DataTypes.AASExtensionDataType, referable.Extensions);
                AddProperty(nodeId, "ModelType", Opc.Ua.DataTypes.String, new Variant(referable.ModelType));
            }

            private void AddIdentifiable(string nodeId, AasIdentifiable identifiable)
            {
                AddProperty(nodeId, "Id", Opc.Ua.DataTypes.String, new Variant(identifiable.Id));
                AddOptionalStructure(nodeId, "Administration", DataTypes.AASAdministrativeInformationDataType,
                    identifiable.Administration);
            }

            private void AddHasSemantics(string nodeId, IAasHasSemantics semantics)
            {
                AddOptionalStructure(nodeId, "SemanticId", DataTypes.AASReferenceDataType, semantics.SemanticId);
                AddOptionalStructureArray(nodeId, "SupplementalSemanticIds", DataTypes.AASReferenceDataType,
                    semantics.SupplementalSemanticIds);
            }

            private void AddOptionalString(string nodeId, string browseName, AasOptional<string> value)
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, Opc.Ua.DataTypes.String, new Variant(value.Value));
                }
            }

            private void AddOptionalByteString(string nodeId, string browseName, AasOptional<ByteString> value)
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, Opc.Ua.DataTypes.ByteString, new Variant(value.Value));
                }
            }

            private void AddOptionalEnum<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                AasOptional<T> value)
                where T : struct
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, dataTypeId, new Variant(Convert.ToInt32(value.Value, CultureInfo.InvariantCulture)));
                }
            }

            private void AddOptionalVariant(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                AasOptional<Variant> value)
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, dataTypeId, value.Value);
                }
            }

            private void AddOptionalStructure<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                AasOptional<T> value)
                where T : class, IEncodeable
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, dataTypeId, StructureVariant(value.Value));
                }
            }

            private void AddOptionalStructureArray<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                AasOptional<ArrayOf<T>> value)
                where T : class, IEncodeable
            {
                if (value.IsPresent)
                {
                    AddStructureArray(nodeId, browseName, dataTypeId, value.Value);
                }
            }

            private void AddStructureArray<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                ArrayOf<T> values)
                where T : class, IEncodeable
            {
                var extensions = new ExtensionObject[values.Count];
                for (int ii = 0; ii < values.Count; ii++)
                {
                    extensions[ii] = new ExtensionObject(values[ii]);
                }

                AddProperty(
                    nodeId,
                    browseName,
                    dataTypeId,
                    new Variant(new ArrayOf<ExtensionObject>(extensions.AsMemory())),
                    valueRank: 1);
            }

            private void AddStructureArray<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                T[] values)
                where T : class, IEncodeable
            {
                AddStructureArray(nodeId, browseName, dataTypeId, new ArrayOf<T>(values.AsMemory()));
            }

            private void AddValueProperty(
                string nodeId,
                string browseName,
                AASDataTypeDefXsdDataType valueType,
                Variant value)
            {
                ExpandedNodeId dataType = AasXsdTypeMap.ToDataTypeId(valueType);
                AasValueStringGuard.AssertVariableDataTypeAllowed(dataType, browseName);
                Variant materialized = value;
                if (value.TryGetValue(out string? lexical) &&
                    AasLexicalCanonicalizer.TryParse(lexical, valueType, out Variant parsed, out _))
                {
                    materialized = parsed;
                }

                AddProperty(nodeId, browseName, dataType, materialized);
            }

            private void AddObject(
                string parentNodeId,
                string browseName,
                ExpandedNodeId typeId,
                Action<string> populate)
            {
                if (HasRejectedIdentifier)
                {
                    return;
                }

                string nodeId = MemberNodeId(parentNodeId, browseName);
                if (!CheckNodeId(nodeId, browseName))
                {
                    return;
                }
                var node = ObjectNode(nodeId, browseName, typeId, AasOptional<string>.Absent, browseName);
                Nodes.Add(node);
                Link(parentNodeId, nodeId, "HasComponent", Nodes);
                populate(nodeId);
            }

            private void AddProperty(
                string parentNodeId,
                string browseName,
                uint dataTypeId,
                Variant value,
                int valueRank = -1)
            {
                AddProperty(parentNodeId, browseName, CoreDataType(dataTypeId), value, valueRank);
            }

            private void AddProperty(
                string parentNodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                Variant value,
                int valueRank = -1,
                bool standardBrowseName = false)
            {
                if (HasRejectedIdentifier)
                {
                    return;
                }

                string nodeId = MemberNodeId(parentNodeId, browseName);
                if (!CheckNodeId(nodeId, browseName))
                {
                    return;
                }
                string dataType = ToNodeSetId(dataTypeId);
                AasValueStringGuard.AssertVariableDataTypeAllowed(dataTypeId, browseName);
                var variable = new UAVariable
                {
                    NodeId = nodeId,
                    BrowseName = standardBrowseName ? browseName : BrowseName(browseName),
                    DisplayName = Text(browseName),
                    ParentNodeId = parentNodeId,
                    DataType = dataType,
                    ValueRank = valueRank,
                    Value = EncodeValue(value)
                };
                variable.References =
                [
                    TypeDefinition(VariableTypeIds.PropertyType),
                    Inverse("HasProperty", parentNodeId)
                ];
                Nodes.Add(variable);
                AddForward(parentNodeId, "HasProperty", nodeId, Nodes);
            }

            private bool CheckNodeId(string nodeId, string path)
            {
                if (TryGetStringIdentifier(nodeId, out string identifier) &&
                    AasNodeIdEncoding.IsWithinLengthLimit(identifier))
                {
                    return true;
                }

                HasRejectedIdentifier = true;
                Diagnostics.Add(new AasMaterializationDiagnostic(
                    AasMaterializationDiagnosticSeverity.Error,
                    AasMaterializationDiagnosticCode.NodeIdTooLong,
                    "The identifiable was rejected whole because a derived String NodeId exceeds 4096 characters.",
                    new AasMaterializationLocation(Kind, OwnerId, path, nodeId)));
                Nodes.Clear();
                return false;
            }

            private List<AasMaterializationDiagnostic> Diagnostics { get; }
        }

        private static UAObject ObjectNode(
            string nodeId,
            string browseName,
        ExpandedNodeId typeId,
            AasOptional<string> idShort,
            string allocatedBrowseName)
        {
            return new UAObject
            {
                NodeId = nodeId,
                BrowseName = BrowseName(browseName),
                DisplayName = Text(AasBrowseNameAllocator.DisplayNameFor(
                    idShort.IsPresent ? idShort.Value : null,
                    allocatedBrowseName)),
                References =
                [
                    TypeDefinition(typeId)
                ]
            };
        }

        private static ExpandedNodeId ObjectTypeOf(AasSubmodelElement element)
        {
            return element switch
            {
                AasProperty => ObjectTypeIds.AASPropertyType,
                AasMultiLanguageProperty => ObjectTypeIds.AASMultiLanguagePropertyType,
                AasRange => ObjectTypeIds.AASRangeType,
                AasBlob => ObjectTypeIds.AASBlobType,
                AasFile => ObjectTypeIds.AASFileType,
                AasReferenceElement => ObjectTypeIds.AASReferenceElementType,
                AasAnnotatedRelationshipElement => ObjectTypeIds.AASAnnotatedRelationshipElementType,
                AasRelationshipElement => ObjectTypeIds.AASRelationshipElementType,
                AasSubmodelElementCollection => ObjectTypeIds.AASSubmodelElementCollectionType,
                AasSubmodelElementList => ObjectTypeIds.AASSubmodelElementListType,
                AasEntity => ObjectTypeIds.AASEntityType,
                AasBasicEventElement => ObjectTypeIds.AASBasicEventElementType,
                AasOperation => ObjectTypeIds.AASOperationType,
                AasCapability => ObjectTypeIds.AASCapabilityType,
                _ => throw new ArgumentOutOfRangeException(nameof(element))
            };
        }

        private static Variant StructureVariant(IEncodeable value)
        {
            return new Variant(new ExtensionObject(value));
        }

        private static AASOperationVariableDataType CreateOperationVariableDescriptor()
        {
#pragma warning disable SYSLIB0050 // TODO: remove when recursive generated default constructors are fixed.
            return (AASOperationVariableDataType)FormatterServices.GetUninitializedObject(
                typeof(AASOperationVariableDataType));
#pragma warning restore SYSLIB0050
        }

        private static SystemXml.XmlElement EncodeValue(Variant value)
        {
            using var encoder = new XmlEncoder(ServiceMessageContext.CreateEmpty(null!));
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.AasV3);
            encoder.SetMappingTables(namespaceUris, new StringTable());
            encoder.WriteVariantValue(null, value);

            var document = new SystemXml.XmlDocument();
            document.LoadInnerXml(encoder.CloseAndReturnText()!);
            return document.DocumentElement!;
        }

        private static void Link(string parentNodeId, string childNodeId, string referenceType, List<UANode> nodes)
        {
            AddForward(parentNodeId, referenceType, childNodeId, nodes);
            UANode? child = Find(nodes, childNodeId);
            if (child is not null)
            {
                AddReference(child, Inverse(referenceType, parentNodeId));
                if (child is UAInstance instance)
                {
                    instance.ParentNodeId = parentNodeId;
                }
            }
        }

        private static void AddForward(string parentNodeId, string referenceType, string childNodeId, List<UANode> nodes)
        {
            UANode? parent = Find(nodes, parentNodeId);
            if (parent is not null)
            {
                AddReference(parent, new Reference { ReferenceType = referenceType, IsForward = true, Value = childNodeId });
            }
        }

        private static UANode? Find(List<UANode> nodes, string nodeId)
        {
            for (int ii = nodes.Count - 1; ii >= 0; ii--)
            {
                if (string.Equals(nodes[ii].NodeId, nodeId, StringComparison.Ordinal))
                {
                    return nodes[ii];
                }
            }
            return null;
        }

        private static void AddReference(UANode node, Reference reference)
        {
            var references = new List<Reference>();
            if (node.References is not null)
            {
                references.AddRange(node.References);
            }
            references.Add(reference);
            node.References = [.. references];
        }

        private static Reference TypeDefinition(ExpandedNodeId typeId)
        {
            return new Reference
            {
                ReferenceType = "HasTypeDefinition",
                IsForward = true,
                Value = ToNodeSetId(typeId)
            };
        }

        private static Reference TypeDefinition(NodeId typeId)
        {
            return new Reference
            {
                ReferenceType = "HasTypeDefinition",
                IsForward = true,
                Value = ToNodeSetId(typeId)
            };
        }

        private static Reference Inverse(string referenceType, string target)
        {
            return new Reference { ReferenceType = referenceType, IsForward = false, Value = target };
        }

        private static string NodeId(string identifier)
        {
            return "ns=1;s=" + identifier;
        }

        private static string MemberNodeId(string parentNodeId, string browseName)
        {
            return parentNodeId + "." + AasNodeIdEncoding.Escape(browseName);
        }

        private static string BrowseName(string name)
        {
            return "1:" + name;
        }

        private static Opc.Ua.Export.LocalizedText[] Text(string value)
        {
            return [new Opc.Ua.Export.LocalizedText { Value = value }];
        }

        private static bool TryGetStringIdentifier(string nodeId, out string identifier)
        {
            const string prefix = "ns=1;s=";
            if (nodeId.StartsWith(prefix, StringComparison.Ordinal))
            {
                identifier = nodeId.Substring(prefix.Length);
                return true;
            }

            identifier = string.Empty;
            return false;
        }

        private static string ToNodeSetId(ExpandedNodeId dataTypeId)
        {
            if (string.Equals(dataTypeId.NamespaceUri, Namespaces.AasV3, StringComparison.Ordinal) &&
                dataTypeId.TryGetValue(out uint aasId))
            {
                return "ns=1;i=" + aasId.ToString(CultureInfo.InvariantCulture);
            }

            if (dataTypeId.TryGetValue(out uint coreId))
            {
                return "i=" + coreId.ToString(CultureInfo.InvariantCulture);
            }

            return "i=24";
        }

        private static string ToNodeSetId(NodeId nodeId)
        {
            if (nodeId.NamespaceIndex == 0 && nodeId.TryGetValue(out uint coreId))
            {
                return "i=" + coreId.ToString(CultureInfo.InvariantCulture);
            }

            if (nodeId.TryGetValue(out uint id))
            {
                return "ns=" + nodeId.NamespaceIndex.ToString(CultureInfo.InvariantCulture) +
                    ";i=" + id.ToString(CultureInfo.InvariantCulture);
            }

            return nodeId.ToString();
        }

        private static ExpandedNodeId CoreDataType(uint dataTypeId)
        {
            return new ExpandedNodeId(dataTypeId, 0, Opc.Ua.Namespaces.OpcUa, 0);
        }

        private static bool IsOperationVariablePath(string path)
        {
            return ContainsOrdinal(path, ".inputVariables[") ||
                ContainsOrdinal(path, ".outputVariables[") ||
                ContainsOrdinal(path, ".inoutputVariables[");
        }

        private static bool ContainsOrdinal(string value, string text)
        {
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            return value.Contains(text, StringComparison.Ordinal);
#else
#pragma warning disable CA2249 // string.Contains with StringComparison is unavailable on older target frameworks.
            return value.IndexOf(text, StringComparison.Ordinal) >= 0;
#pragma warning restore CA2249
#endif
        }

        /// <summary>
        /// The reference type aliases the emitted NodeSet uses. A NodeSet that
        /// names a reference type by alias has to declare that alias, or a
        /// loader cannot resolve it and rejects the whole document. The values
        /// are the standard NodeIds OPC 10000-5 assigns to these reference
        /// types, spelled literally so the model assembly stays independent of
        /// the generated identifier classes.
        /// </summary>
        private static readonly NodeIdAlias[] s_aliases =
        [
            Alias("HasComponent", "i=47"),
            Alias("HasOrderedComponent", "i=49"),
            Alias("HasProperty", "i=46"),
            Alias("HasTypeDefinition", "i=40"),
            Alias("Organizes", "i=35")
        ];

        private static NodeIdAlias Alias(string alias, string nodeId)
        {
            return new NodeIdAlias { Alias = alias, Value = nodeId };
        }

        /// <summary>
        /// The Invoke arguments of clause 6.2.5, matching the declaration on
        /// AASOperationType. They are repeated on every materialized instance
        /// because a Server validates a Call against the Method it resolved.
        /// </summary>
        private static readonly Argument[] s_invokeInputArguments =
        [
            ArrayArgument("InputValues",
                "Values for the operation's input variables, positionally matching InputVariables."),
            ArrayArgument("InoutputValues",
                "Values for the operation's in-out variables, positionally matching InoutputVariables."),
            ScalarArgument("ClientTimeout", new NodeId(Opc.Ua.DataTypes.Duration),
                "How long the caller will wait. Zero means the Server's default. " +
                "Corresponds to clientTimeoutDuration of the AAS API request.")
        ];

        private static readonly Argument[] s_invokeOutputArguments =
        [
            ArrayArgument("OutputValues", "Results, positionally matching OutputVariables."),
            ArrayArgument("InoutputResults",
                "The in-out variables after execution, positionally matching InoutputVariables."),
            ScalarArgument("Success", new NodeId(Opc.Ua.DataTypes.Boolean),
                "Whether the operation executed successfully. A false result is an executed " +
                "operation that failed, not a failed Call."),
            ScalarArgument("Diagnostic", new NodeId(Opc.Ua.DataTypes.String),
                "Why the operation failed, where it did.")
        ];

        private static Argument ArrayArgument(string name, string description)
        {
            return new Argument
            {
                Name = name,
                DataType = new NodeId(Opc.Ua.DataTypes.BaseDataType),
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = new ArrayOf<uint>(new uint[] { 0 }),
                Description = new LocalizedText(description)
            };
        }

        private static Argument ScalarArgument(string name, NodeId dataType, string description)
        {
            return new Argument
            {
                Name = name,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                Description = new LocalizedText(description)
            };
        }

        private const ushort AasNamespaceIndex = 1;
        private const string EnvironmentNodeId = "ns=1;s=i4aas3:Environment";

        /// <summary>
        /// The NodeId of the Invoke Method AASOperationType declares. The
        /// emitted NodeSet declares the AAS namespace first, so the
        /// declaration is addressed at that index rather than the index the
        /// pinned companion NodeSet happens to use.
        /// </summary>
        private const string InvokeDeclarationId = "ns=1;i=5103";
    }
}
