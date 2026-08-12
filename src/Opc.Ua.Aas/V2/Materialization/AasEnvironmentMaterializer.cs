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
using Opc.Ua.Export;
using SystemXml = System.Xml;

namespace Opc.Ua.Aas.V2
{
    /// <summary>
    /// Materializes an AAS V2.0.1 <see cref="AasEnvironment"/> into a deterministic OPC UA NodeSet.
    /// </summary>
    public static class AasEnvironmentMaterializer
    {
        /// <summary>
        /// Materializes an AAS Environment according to OPC 30270.
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
                NamespaceUris = [Namespaces.AasV2],
                Aliases = s_aliases,
                Models = [new ModelTableEntry { ModelUri = Namespaces.AasV2 }]
            };

            var environmentNode = new UAObject
            {
                NodeId = EnvironmentNodeId,
                BrowseName = BrowseName("AASEnvironment"),
                DisplayName = Text("AASEnvironment"),
                References =
                [
                    TypeDefinition(CoreObjectType(58))
                ]
            };
            items.Add(environmentNode);

            MaterializeIdentifiables(environment.AssetAdministrationShells, AasNodeKind.Shell, diagnostics, items,
                static (builder, shell) => builder.AddShell(shell));
            MaterializeIdentifiables(environment.Assets, V2IdentifiableKind.Asset, diagnostics, items,
                static (builder, asset) => builder.AddAsset(asset));
            MaterializeIdentifiables(environment.Submodels, AasNodeKind.Submodel, diagnostics, items,
                static (builder, submodel) => builder.AddSubmodel(submodel));
            MaterializeIdentifiables(environment.CustomConceptDescriptions, AasNodeKind.ConceptDescription, diagnostics,
                items, static (builder, concept) => builder.AddConceptDescription(concept));
            MaterializeIdentifiables(environment.IrdiConceptDescriptions, AasNodeKind.ConceptDescription, diagnostics,
                items, static (builder, concept) => builder.AddConceptDescription(concept));
            MaterializeIdentifiables(environment.IriConceptDescriptions, AasNodeKind.ConceptDescription, diagnostics,
                items, static (builder, concept) => builder.AddConceptDescription(concept));
            MaterializeIdentifiables(environment.DataSpecifications, V2IdentifiableKind.DataSpecification, diagnostics,
                items, static (builder, specification) => builder.AddDataSpecification(specification));

            nodeSet.Items = [.. items];
            return new AasMaterializationResult(nodeSet, diagnostics);
        }

        private static void MaterializeIdentifiables<T>(
            AasOptional<ArrayOf<T>> source,
            AasNodeKind kind,
            List<AasMaterializationDiagnostic> diagnostics,
            List<UANode> items,
            Action<IdentifiableBuilder, T> add)
            where T : AasIdentifiable
        {
            MaterializeIdentifiables(source, V2IdentifiableKind.FromAasNodeKind(kind), diagnostics, items, add);
        }

        private static void MaterializeIdentifiables<T>(
            AasOptional<ArrayOf<T>> source,
            V2IdentifiableKind kind,
            List<AasMaterializationDiagnostic> diagnostics,
            List<UANode> items,
            Action<IdentifiableBuilder, T> add)
            where T : AasIdentifiable
        {
            if (!source.IsPresent)
            {
                return;
            }

            HashSet<string> duplicates = FindDuplicates(source);
            foreach (T identifiable in source.Value.Span)
            {
                if (duplicates.Contains(identifiable.Identification.Id))
                {
                    diagnostics.Add(new AasMaterializationDiagnostic(
                        AasMaterializationDiagnosticSeverity.Error,
                        AasMaterializationDiagnosticCode.DuplicateIdentifier,
                        "Duplicate identifiers within one identifiable kind produce the same NodeId and are rejected.",
                        new AasMaterializationLocation(kind.DiagnosticKind, identifiable.Identification.Id)));
                    continue;
                }

                var builder = new IdentifiableBuilder(kind, identifiable.Identification.Id, diagnostics);
                add(builder, identifiable);
                if (builder.HasRejectedIdentifier)
                {
                    continue;
                }

                items.AddRange(builder.Nodes);
                Link(EnvironmentNodeId, builder.RootNodeId, "Organizes", items);
            }
        }

        private static HashSet<string> FindDuplicates<T>(AasOptional<ArrayOf<T>> source)
            where T : AasIdentifiable
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (T identifiable in source.Value.Span)
            {
                if (!seen.Add(identifiable.Identification.Id))
                {
                    duplicates.Add(identifiable.Identification.Id);
                }
            }
            return duplicates;
        }

        private sealed class IdentifiableBuilder
        {
            public IdentifiableBuilder(
                V2IdentifiableKind kind,
                string ownerId,
                List<AasMaterializationDiagnostic> diagnostics)
            {
                Kind = kind;
                OwnerId = ownerId;
                Diagnostics = diagnostics;
                RootNodeId = NodeId(kind.CreateIdentifier(ownerId));
                Nodes = [];
            }

            public string RootNodeId { get; }

            public bool HasRejectedIdentifier { get; private set; }

            public List<UANode> Nodes { get; }

            public void AddShell(AasShell shell)
            {
                if (!CheckRootNodeId())
                {
                    return;
                }

                var node = ObjectNode(RootNodeId, shell.IdShort, ObjectTypeIds.AASAssetAdministrationShellType);
                Nodes.Add(node);
                AddReferable(RootNodeId, shell);
                AddIdentifiable(RootNodeId, shell);
                AddDataSpecifications(RootNodeId, shell.DataSpecifications);

                AddEmbeddedIdentifiable(RootNodeId, "Asset", shell.Asset, ObjectTypeIds.AASAssetType, AddAssetMembers);
                AddReferenceObjects(RootNodeId, "SubmodelReference", shell.SubmodelReferences, "HasComponent");
                AddOptionalReferenceObject(RootNodeId, "DerivedFrom", shell.DerivedFrom);

                if (shell.Submodels.IsPresent)
                {
                    foreach (AasSubmodel submodel in shell.Submodels.Value.Span)
                    {
                        AddEmbeddedIdentifiable(RootNodeId, submodel.IdShort, submodel, ObjectTypeIds.AASSubmodelType,
                            AddSubmodelMembers);
                    }
                }

                if (shell.Views.IsPresent)
                {
                    int index = 0;
                    foreach (AasView view in shell.Views.Value.Span)
                    {
                        string browseName = "View" + index.ToString(CultureInfo.InvariantCulture);
                        AddView(RootNodeId, browseName, view);
                        index++;
                    }
                }
            }

            public void AddAsset(AasAsset asset)
            {
                if (!CheckRootNodeId())
                {
                    return;
                }

                var node = ObjectNode(RootNodeId, asset.IdShort, ObjectTypeIds.AASAssetType);
                Nodes.Add(node);
                AddReferable(RootNodeId, asset);
                AddIdentifiable(RootNodeId, asset);
                AddAssetMembers(RootNodeId, asset);
            }

            public void AddSubmodel(AasSubmodel submodel)
            {
                if (!CheckRootNodeId())
                {
                    return;
                }

                var node = ObjectNode(RootNodeId, submodel.IdShort, ObjectTypeIds.AASSubmodelType);
                Nodes.Add(node);
                AddReferable(RootNodeId, submodel);
                AddIdentifiable(RootNodeId, submodel);
                AddSubmodelMembers(RootNodeId, submodel);
            }

            public void AddConceptDescription(AasConceptDescription concept)
            {
                if (!CheckRootNodeId())
                {
                    return;
                }

                ExpandedNodeId typeId = concept switch
                {
                    AasCustomConceptDescription => ObjectTypeIds.AASCustomConceptDescriptionType,
                    AasIrdiConceptDescription => ObjectTypeIds.AASIrdiConceptDescriptionType,
                    AasIriConceptDescription => ObjectTypeIds.AASIriConceptDescriptionType,
                    _ => ObjectTypeIds.AASCustomConceptDescriptionType
                };
                var node = ObjectNode(RootNodeId, concept.IdShort, typeId);
                Nodes.Add(node);
                AddReferable(RootNodeId, concept);
                AddIdentifiable(RootNodeId, concept);
                AddReferenceObjects(RootNodeId, "ConceptDescription", concept.ConceptDescriptions, "HasComponent");
                AddDataSpecifications(RootNodeId, concept.DataSpecifications);
            }

            public void AddDataSpecification(AasDataSpecification specification)
            {
                if (!CheckRootNodeId())
                {
                    return;
                }

                ExpandedNodeId typeId = specification is AasDataSpecificationIec61360
                    ? ObjectTypeIds.AASDataSpecificationIEC61360Type
                    : ObjectTypeIds.AASDataSpecificationType;
                var node = ObjectNode(RootNodeId, specification.IdShort, typeId);
                Nodes.Add(node);
                AddReferable(RootNodeId, specification);
                AddIdentifiable(RootNodeId, specification);

                if (specification is AasDataSpecificationIec61360 iec)
                {
                    AddObject(RootNodeId, "DataSpecificationAdministration",
                        ObjectTypeIds.AASAdministrativeInformationType,
                        id => AddAdministrationMembers(id, iec.DataSpecificationAdministration));
                    AddOptionalEnum(RootNodeId, "Category", DataTypeIds.AASCategoryDataType,
                        iec.DataSpecificationCategory);
                    AddOptionalEnum(RootNodeId, "DataType", DataTypeIds.AASDataTypeIEC61360DataType, iec.DataType);
                    AddProperty(RootNodeId, "DefaultInstanceBrowseName", Opc.Ua.DataTypes.String,
                        new Variant(iec.DefaultInstanceBrowseName));
                    AddOptionalLocalizedTextArray(RootNodeId, "Definition", iec.Definition);
                    AddObject(RootNodeId, "DataSpecificationIdentification", ObjectTypeIds.AASIdentifierType,
                        id => AddIdentifierMembers(id, iec.DataSpecificationIdentification));
                    AddOptionalEnumArray(RootNodeId, "LevelType", DataTypeIds.AASLevelTypeDataType, iec.LevelType);
                    AddLocalizedTextArray(RootNodeId, "PreferredName", iec.PreferredName);
                    AddOptionalLocalizedTextArray(RootNodeId, "ShortName", iec.ShortName);
                    AddOptionalString(RootNodeId, "SourceOfDefinition", iec.SourceOfDefinition);
                    AddOptionalString(RootNodeId, "Symbol", iec.Symbol);
                    AddOptionalString(RootNodeId, "Unit", iec.Unit);
                    AddOptionalReferenceObject(RootNodeId, "UnitId", iec.UnitId);
                    AddOptionalVariant(RootNodeId, "Value", CoreDataType(Opc.Ua.DataTypes.BaseDataType), iec.Value);
                    AddOptionalString(RootNodeId, "ValueFormat", iec.ValueFormat);
                    AddOptionalReferenceObject(RootNodeId, "ValueId", iec.ValueId);
                    AddOptionalReferenceObject(RootNodeId, "ValueList", iec.ValueList);
                }
            }

            private void AddAssetMembers(string nodeId, AasAsset asset)
            {
                AddDataSpecifications(nodeId, asset.DataSpecifications);
                AddOptionalReferenceObject(nodeId, "AssetIdentificationModel", asset.AssetIdentificationModel);
                AddProperty(nodeId, "AssetKind", DataTypeIds.AASAssetKindDataType, new Variant((int)asset.AssetKind));
                AddOptionalReferenceObject(nodeId, "BillOfMaterial", asset.BillOfMaterial);
            }

            private void AddSubmodelMembers(string nodeId, AasSubmodel submodel)
            {
                AddDataSpecifications(nodeId, submodel.DataSpecifications);
                AddQualifiers(nodeId, submodel.Qualifiers);
                AddProperty(nodeId, "ModelingKind", DataTypeIds.AASModelingKindDataType,
                    new Variant((int)submodel.ModelingKind));

                if (submodel.SubmodelElements.IsPresent)
                {
                    foreach (AasSubmodelElement element in submodel.SubmodelElements.Value.Span)
                    {
                        AddElement(nodeId, string.Empty, element, "HasComponent");
                    }
                }
            }

            private void AddElement(
                string parentNodeId,
                string parentPath,
                AasSubmodelElement element,
                string referenceType)
            {
                if (HasRejectedIdentifier)
                {
                    return;
                }

                if (string.IsNullOrEmpty(element.IdShort))
                {
                    Diagnostics.Add(new AasMaterializationDiagnostic(
                        AasMaterializationDiagnosticSeverity.Error,
                        AasMaterializationDiagnosticCode.MissingIdShort,
                        "A non-list SubmodelElement requires an idShort to derive its idShortPath.",
                        new AasMaterializationLocation(Kind.DiagnosticKind, OwnerId, parentPath)));
                    return;
                }

                string path = AasIdShortPath.AppendName(parentPath, element.IdShort);
                AddElementAtPath(parentNodeId, path, element, element.IdShort, referenceType);
            }

            private void AddCollectionElement(
                string parentNodeId,
                string parentPath,
                AasSubmodelElement element,
                int index,
                string referenceType)
            {
                if (referenceType == "HasOrderedComponent")
                {
                    string path = AasIdShortPath.AppendIndex(parentPath, index);
                    AddElementAtPath(parentNodeId, path, element, AasBrowseNameAllocator.ForListMember(index),
                        referenceType);
                    return;
                }

                AddElement(parentNodeId, parentPath, element, referenceType);
            }

            private void AddElementAtPath(
                string parentNodeId,
                string path,
                AasSubmodelElement element,
                string browseName,
                string referenceType)
            {
                string nodeId = NodeId(AasNodeIdEncoding.CreateElementId(OwnerId, path));
                if (!CheckNodeId(nodeId, path))
                {
                    return;
                }

                var node = ObjectNode(nodeId, browseName, ObjectTypeOf(element));
                Nodes.Add(node);
                Link(parentNodeId, nodeId, referenceType, Nodes);
                AddReferable(nodeId, element);
                AddProperty(nodeId, "ModelingKind", DataTypeIds.AASModelingKindDataType,
                    new Variant((int)element.ModelingKind));
                AddDataSpecifications(nodeId, element.DataSpecifications);
                AddQualifiers(nodeId, element.Qualifiers);
                AddElementMembers(nodeId, path, element);
            }

            private void AddElementMembers(string nodeId, string path, AasSubmodelElement element)
            {
                switch (element)
                {
                    case AasBlob blob:
                        AddFileObject(nodeId, blob.File);
                        break;
                    case AasEntity entity:
                        AddOptionalReferenceObject(nodeId, "Asset", entity.Asset);
                        AddProperty(nodeId, "EntityType", DataTypeIds.AASEntityTypeDataType,
                            new Variant((int)entity.EntityType));
                        AddElements(nodeId, path, entity.Statements, "HasComponent");
                        break;
                    case AasFile file:
                        AddFileObject(nodeId, file.File);
                        AddProperty(nodeId, "MimeType", Opc.Ua.DataTypes.String, new Variant(file.MimeType));
                        AddProperty(nodeId, "Value", Opc.Ua.DataTypes.String, new Variant(file.Value));
                        break;
                    case AasMultiLanguageProperty multiLanguage:
                        AddOptionalLocalizedTextArray(nodeId, "Value", multiLanguage.Value);
                        AddOptionalReferenceObject(nodeId, "ValueId", multiLanguage.ValueId);
                        break;
                    case AasOperation:
                        AddOperationMethod(nodeId);
                        break;
                    case AasProperty property:
                        AddOptionalVariant(nodeId, "Value", CoreDataType(Opc.Ua.DataTypes.BaseDataType),
                            property.Value);
                        AddOptionalReferenceObject(nodeId, "ValueId", property.ValueId);
                        AddProperty(nodeId, "ValueType", DataTypeIds.AASValueTypeDataType,
                            new Variant((int)property.ValueType));
                        break;
                    case AasRange range:
                        AddOptionalVariant(nodeId, "Max", CoreDataType(Opc.Ua.DataTypes.BaseDataType), range.Max);
                        AddOptionalVariant(nodeId, "Min", CoreDataType(Opc.Ua.DataTypes.BaseDataType), range.Min);
                        AddProperty(nodeId, "ValueType", DataTypeIds.AASValueTypeDataType,
                            new Variant((int)range.ValueType));
                        break;
                    case AasReferenceElement referenceElement:
                        AddReferenceObject(nodeId, "Value", referenceElement.Value, "HasComponent");
                        break;
                    case AasRelationshipElementBase relationship:
                        AddReferenceObject(nodeId, "First", relationship.First, "HasComponent");
                        AddReferenceObject(nodeId, "Second", relationship.Second, "HasComponent");
                        if (relationship is AasAnnotatedRelationshipElement annotated)
                        {
                            AddElements(nodeId, path, annotated.DataElements, "HasComponent");
                        }
                        break;
                    case AasSubmodelElementCollectionBase collection:
                        AddOptionalBool(nodeId, "AllowDuplicates", collection.AllowDuplicates);
                        string referenceType = collection is AasOrderedSubmodelElementCollection
                            ? "HasOrderedComponent"
                            : "HasComponent";
                        AddElements(nodeId, path, collection.SubmodelElements, referenceType);
                        break;
                }
            }

            private void AddElements(
                string nodeId,
                string path,
                AasOptional<ArrayOf<AasSubmodelElement>> elements,
                string referenceType)
            {
                if (!elements.IsPresent)
                {
                    return;
                }

                for (int ii = 0; ii < elements.Value.Count; ii++)
                {
                    AddCollectionElement(nodeId, path, elements.Value[ii], ii, referenceType);
                }
            }

            private void AddOperationMethod(string parentNodeId)
            {
                string nodeId = MemberNodeId(parentNodeId, "Operation");
                if (!CheckNodeId(nodeId, "Operation"))
                {
                    return;
                }

                var method = new UAMethod
                {
                    NodeId = nodeId,
                    BrowseName = BrowseName("Operation"),
                    DisplayName = Text("Operation"),
                    ParentNodeId = parentNodeId,
                    MethodDeclarationId = OperationDeclarationId,
                    Executable = true,
                    UserExecutable = true,
                    References = [Inverse("HasComponent", parentNodeId)]
                };
                Nodes.Add(method);
                AddForward(parentNodeId, "HasComponent", nodeId, Nodes);
                AddArguments(nodeId, "InputArguments", []);
                AddArguments(nodeId, "OutputArguments", []);
            }

            private void AddFileObject(string parentNodeId, AasOptional<AasFileObject> file)
            {
                string nodeId = MemberNodeId(parentNodeId, "File");
                if (!CheckNodeId(nodeId, "File"))
                {
                    return;
                }

                var fileNode = ObjectNode(nodeId, "File", CoreObjectType(11575));
                Nodes.Add(fileNode);
                Link(parentNodeId, nodeId, "HasComponent", Nodes);

                // OPC 30270 declares the File Object with the standard FileType
                // and no modifications, and every one of that type's six
                // Methods and four Properties is Mandatory in the pinned
                // NodeSet. OPC 10000-3 6.4.4 requires an instance to carry all
                // of them, and without Size and OpenCount a Client cannot size
                // a read or notice an abandoned handle at all.
                AddFileMethod(nodeId, "Open", "i=11580", s_openInputArguments, s_openOutputArguments);
                AddFileMethod(nodeId, "Read", "i=11585", s_readInputArguments, s_readOutputArguments);
                AddFileMethod(nodeId, "Close", "i=11583", s_closeInputArguments, []);
                AddFileMethod(nodeId, "Write", "i=11588", s_writeInputArguments, []);
                AddFileMethod(nodeId, "GetPosition", "i=11590",
                    s_getPositionInputArguments, s_getPositionOutputArguments);
                AddFileMethod(nodeId, "SetPosition", "i=11593", s_setPositionInputArguments, []);
                AddProperty(nodeId, "Size", Opc.Ua.DataTypes.UInt64,
                    new Variant((ulong)(file.IsPresent && file.Value.Value.IsPresent
                        ? file.Value.Value.Value.Length
                        : 0)));
                AddProperty(nodeId, "Writable", Opc.Ua.DataTypes.Boolean, new Variant(true));
                AddProperty(nodeId, "UserWritable", Opc.Ua.DataTypes.Boolean, new Variant(true));
                AddProperty(nodeId, "OpenCount", Opc.Ua.DataTypes.UInt16, new Variant((ushort)0));
                if (file.IsPresent)
                {
                    AddOptionalByteString(nodeId, "Value", file.Value.Value);
                }
            }

            private void AddFileMethod(
                string parentNodeId,
                string browseName,
                string declarationId,
                Argument[] inputArguments,
                Argument[] outputArguments)
            {
                string nodeId = MemberNodeId(parentNodeId, browseName);
                if (!CheckNodeId(nodeId, browseName))
                {
                    return;
                }

                var method = new UAMethod
                {
                    NodeId = nodeId,
                    BrowseName = browseName,
                    DisplayName = Text(browseName),
                    ParentNodeId = parentNodeId,
                    MethodDeclarationId = declarationId,
                    Executable = true,
                    UserExecutable = true,
                    References = [Inverse("HasComponent", parentNodeId)]
                };
                Nodes.Add(method);
                AddForward(parentNodeId, "HasComponent", nodeId, Nodes);
                AddArguments(nodeId, "InputArguments", inputArguments);
                if (outputArguments.Length > 0)
                {
                    AddArguments(nodeId, "OutputArguments", outputArguments);
                }
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

            private void AddEmbeddedIdentifiable<T>(
                string parentNodeId,
                string browseName,
                T identifiable,
                ExpandedNodeId typeId,
                Action<string, T> addMembers)
                where T : AasIdentifiable
            {
                string nodeId = MemberNodeId(parentNodeId, browseName);
                if (!CheckNodeId(nodeId, browseName))
                {
                    return;
                }

                var node = ObjectNode(nodeId, browseName, typeId);
                Nodes.Add(node);
                Link(parentNodeId, nodeId, "HasComponent", Nodes);
                AddReferable(nodeId, identifiable);
                AddIdentifiable(nodeId, identifiable);
                addMembers(nodeId, identifiable);
            }

            private void AddView(string parentNodeId, string browseName, AasView view)
            {
                AddObject(parentNodeId, browseName, ObjectTypeIds.AASViewType, nodeId =>
                {
                    AddDataSpecifications(nodeId, view.DataSpecifications);
                    AddReferenceObjects(nodeId, "Referable", view.Referables, "HasComponent");
                });
            }

            private void AddReferable(string nodeId, AasReferable referable)
            {
                AddReference(Find(Nodes, nodeId)!, new Reference
                {
                    ReferenceType = "HasInterface",
                    IsForward = true,
                    Value = ToNodeSetId(ObjectTypeIds.IAASReferableType)
                });
                AddProperty(nodeId, "IdShort", Opc.Ua.DataTypes.String, new Variant(referable.IdShort));
                AddProperty(nodeId, "Category", Opc.Ua.DataTypes.String, new Variant(referable.Category));
                AddProperty(nodeId, "ModelType", Opc.Ua.DataTypes.String, new Variant(referable.ModelType));
            }

            private void AddIdentifiable(string nodeId, AasIdentifiable identifiable)
            {
                AddReference(Find(Nodes, nodeId)!, new Reference
                {
                    ReferenceType = "HasInterface",
                    IsForward = true,
                    Value = ToNodeSetId(ObjectTypeIds.IAASIdentifiableType)
                });
                AddObject(nodeId, "Identification", ObjectTypeIds.AASIdentifierType,
                    id => AddIdentifierMembers(id, identifiable.Identification));
                AddObject(nodeId, "Administration", ObjectTypeIds.AASAdministrativeInformationType,
                    id => AddAdministrationMembers(id, identifiable.Administration));
            }

            private void AddIdentifierMembers(string nodeId, AasIdentifier identifier)
            {
                AddProperty(nodeId, "Id", Opc.Ua.DataTypes.String, new Variant(identifier.Id));
                AddProperty(nodeId, "IdType", DataTypeIds.AASIdentifierTypeDataType,
                    new Variant((int)identifier.IdType));
            }

            private void AddAdministrationMembers(string nodeId, AasAdministrativeInformation administration)
            {
                AddProperty(nodeId, "Revision", Opc.Ua.DataTypes.String, new Variant(administration.Revision));
                AddProperty(nodeId, "Version", Opc.Ua.DataTypes.String, new Variant(administration.Version));
            }

            private void AddDataSpecifications(string nodeId, AasOptional<ArrayOf<AasReference>> references)
            {
                AddReferenceObjects(nodeId, "DataSpecification", references, "HasComponent");
            }

            private void AddQualifiers(string nodeId, AasOptional<ArrayOf<AasQualifier>> qualifiers)
            {
                if (!qualifiers.IsPresent)
                {
                    return;
                }

                for (int ii = 0; ii < qualifiers.Value.Count; ii++)
                {
                    string browseName = "Qualifier" + ii.ToString(CultureInfo.InvariantCulture);
                    AddObject(nodeId, browseName, ObjectTypeIds.AASQualifierType, qualifierNodeId =>
                    {
                        AasQualifier qualifier = qualifiers.Value[ii];
                        AddProperty(qualifierNodeId, "Type", Opc.Ua.DataTypes.String, new Variant(qualifier.Type));
                        AddOptionalVariant(qualifierNodeId, "Value", CoreDataType(Opc.Ua.DataTypes.BaseDataType),
                            qualifier.Value);
                        AddOptionalReferenceObject(qualifierNodeId, "ValueId", qualifier.ValueId);
                        AddProperty(qualifierNodeId, "ValueType", DataTypeIds.AASValueTypeDataType,
                            new Variant((int)qualifier.ValueType));
                    });
                }
            }

            private void AddOptionalReferenceObject(
                string parentNodeId,
                string browseName,
                AasOptional<AasReference> reference)
            {
                if (reference.IsPresent)
                {
                    AddReferenceObject(parentNodeId, browseName, reference.Value, "HasComponent");
                }
            }

            private void AddReferenceObjects(
                string parentNodeId,
                string browseName,
                AasOptional<ArrayOf<AasReference>> references,
                string referenceType)
            {
                if (!references.IsPresent)
                {
                    return;
                }

                for (int ii = 0; ii < references.Value.Count; ii++)
                {
                    string childName = references.Value.Count == 1
                        ? browseName
                        : browseName + ii.ToString(CultureInfo.InvariantCulture);
                    AddReferenceObject(parentNodeId, childName, references.Value[ii], referenceType);
                }
            }

            private void AddReferenceObject(
                string parentNodeId,
                string browseName,
                AasReference reference,
                string referenceType)
            {
                AddObject(parentNodeId, browseName, ObjectTypeIds.AASReferenceType, nodeId =>
                {
                    AddStructureArray(nodeId, "Keys", DataTypeIds.AASKeyDataType, reference.Keys, "AASReference");
                    if (reference.Referables.IsPresent)
                    {
                        int index = 0;
                        foreach (AasReferable referable in reference.Referables.Value.Span)
                        {
                            AddProperty(nodeId, "Referable" + index.ToString(CultureInfo.InvariantCulture),
                                Opc.Ua.DataTypes.String, new Variant(referable.IdShort));
                            index++;
                        }
                    }
                }, referenceType);
            }

            private void AddObject(
                string parentNodeId,
                string browseName,
                ExpandedNodeId typeId,
                Action<string> populate,
                string referenceType = "HasComponent")
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

                var node = ObjectNode(nodeId, browseName, typeId);
                Nodes.Add(node);
                Link(parentNodeId, nodeId, referenceType, Nodes);
                populate(nodeId);
            }

            private void AddOptionalString(string nodeId, string browseName, AasOptional<string> value)
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, Opc.Ua.DataTypes.String, new Variant(value.Value));
                }
            }

            private void AddOptionalBool(string nodeId, string browseName, AasOptional<bool> value)
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, Opc.Ua.DataTypes.Boolean, new Variant(value.Value));
                }
            }

            private void AddOptionalByteString(string nodeId, string browseName, AasOptional<ByteString> value)
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, Opc.Ua.DataTypes.ByteString, new Variant(value.Value));
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

            private void AddOptionalEnum<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                AasOptional<T> value)
                where T : struct
            {
                if (value.IsPresent)
                {
                    AddProperty(nodeId, browseName, dataTypeId,
                        new Variant(Convert.ToInt32(value.Value, CultureInfo.InvariantCulture)));
                }
            }

            private void AddOptionalEnumArray<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                AasOptional<ArrayOf<T>> value)
                where T : struct
            {
                if (!value.IsPresent)
                {
                    return;
                }

                var values = new int[value.Value.Count];
                for (int ii = 0; ii < value.Value.Count; ii++)
                {
                    values[ii] = Convert.ToInt32(value.Value[ii], CultureInfo.InvariantCulture);
                }
                AddProperty(nodeId, browseName, dataTypeId, new Variant(values), valueRank: 1);
            }

            private void AddOptionalLocalizedTextArray(
                string nodeId,
                string browseName,
                AasOptional<ArrayOf<Opc.Ua.LocalizedText>> value)
            {
                if (value.IsPresent)
                {
                    AddLocalizedTextArray(nodeId, browseName, value.Value);
                }
            }

            private void AddLocalizedTextArray(string nodeId, string browseName, ArrayOf<Opc.Ua.LocalizedText> value)
            {
                AddProperty(nodeId, browseName, CoreDataType(Opc.Ua.DataTypes.LocalizedText),
                    new Variant(value), valueRank: 1);
            }

            private void AddStructureArray<T>(
                string nodeId,
                string browseName,
                ExpandedNodeId dataTypeId,
                ArrayOf<T> values,
                string variableReferenceType = "HasProperty",
                bool omitValue = false)
                where T : class, IEncodeable
            {
                if (omitValue)
                {
                    AddProperty(
                        nodeId,
                        browseName,
                        dataTypeId,
                        Variant.Null,
                        valueRank: 1,
                        variableReferenceType: variableReferenceType,
                        omitValue: true);
                    return;
                }

                var extensions = new ExtensionObject[values.Count];
                for (int ii = 0; ii < values.Count; ii++)
                {
                    ExpandedNodeId encodingId = typeof(T) == typeof(AASKeyDataType)
                        ? new ExpandedNodeId(Objects.AASKeyDataType_Encoding_DefaultXml, 0, Namespaces.AasV2, 0)
                        : values[ii].XmlEncodingId;
                    extensions[ii] = new ExtensionObject(encodingId, values[ii]);
                }

                AddProperty(
                    nodeId,
                    browseName,
                    dataTypeId,
                    new Variant(new ArrayOf<ExtensionObject>(extensions.AsMemory())),
                    valueRank: 1,
                    variableReferenceType: variableReferenceType,
                    omitValue: omitValue);
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
                bool standardBrowseName = false,
                string variableReferenceType = "HasProperty",
                bool omitValue = false)
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

                var variable = new UAVariable
                {
                    NodeId = nodeId,
                    BrowseName = standardBrowseName ? browseName : BrowseName(browseName),
                    DisplayName = Text(browseName),
                    ParentNodeId = parentNodeId,
                    DataType = ToNodeSetId(dataTypeId),
                    ValueRank = valueRank,
                    References =
                    [
                        TypeDefinition(VariableTypeIds.PropertyType),
                        Inverse(variableReferenceType, parentNodeId)
                    ]
                };
                if (!omitValue)
                {
                    variable.Value = EncodeValue(value);
                }
                Nodes.Add(variable);
                AddForward(parentNodeId, variableReferenceType, nodeId, Nodes);
            }

            private bool CheckRootNodeId()
            {
                return CheckNodeId(RootNodeId, string.Empty);
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
                    new AasMaterializationLocation(Kind.DiagnosticKind, OwnerId, path, nodeId)));
                Nodes.Clear();
                return false;
            }

            private V2IdentifiableKind Kind { get; }

            private string OwnerId { get; }

            private List<AasMaterializationDiagnostic> Diagnostics { get; }
        }

        private readonly struct V2IdentifiableKind
        {
            private V2IdentifiableKind(AasNodeKind diagnosticKind, string discriminator)
            {
                DiagnosticKind = diagnosticKind;
                Discriminator = discriminator;
            }

            public AasNodeKind DiagnosticKind { get; }

            public static V2IdentifiableKind Asset { get; } = new(AasNodeKind.Shell, "V2Asset");

            public static V2IdentifiableKind DataSpecification { get; } =
                new(AasNodeKind.ConceptDescription, "V2DataSpecification");

            public static V2IdentifiableKind FromAasNodeKind(AasNodeKind kind)
            {
                return new V2IdentifiableKind(kind, kind.ToString());
            }

            public string CreateIdentifier(string id)
            {
                if (Discriminator == nameof(AasNodeKind.Shell))
                {
                    return AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, id);
                }

                if (Discriminator == nameof(AasNodeKind.Submodel))
                {
                    return AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Submodel, id);
                }

                if (Discriminator == nameof(AasNodeKind.ConceptDescription))
                {
                    return AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.ConceptDescription, id);
                }

                string escaped = AasNodeIdEncoding.Escape(id);
                return AasNodeIdEncoding.Prefix + Discriminator + ":" +
                    escaped.Length.ToString(CultureInfo.InvariantCulture) + ":" + escaped;
            }

            private string Discriminator { get; }
        }

        private static UAObject ObjectNode(string nodeId, string browseName, ExpandedNodeId typeId)
        {
            return new UAObject
            {
                NodeId = nodeId,
                BrowseName = BrowseName(browseName),
                DisplayName = Text(browseName),
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
                AasBlob => ObjectTypeIds.AASBlobType,
                AasCapability => ObjectTypeIds.AASCapabilityType,
                AasEntity => ObjectTypeIds.AASEntityType,
                AasEvent => ObjectTypeIds.AASEventType,
                AasFile => ObjectTypeIds.AASFileType,
                AasMultiLanguageProperty => ObjectTypeIds.AASMultiLanguagePropertyType,
                AasOperation => ObjectTypeIds.AASOperationType,
                AasProperty => ObjectTypeIds.AASPropertyType,
                AasRange => ObjectTypeIds.AASRangeType,
                AasReferenceElement => ObjectTypeIds.AASReferenceElementType,
                AasAnnotatedRelationshipElement => ObjectTypeIds.AASAnnotatedRelationshipElementType,
                AasRelationshipElement => ObjectTypeIds.AASRelationshipElementType,
                AasOrderedSubmodelElementCollection => ObjectTypeIds.AASOrderedSubmodelElementCollectionType,
                AasSubmodelElementCollection => ObjectTypeIds.AASSubmodelElementCollectionType,
                _ => throw new ArgumentOutOfRangeException(nameof(element))
            };
        }

        private static SystemXml.XmlElement EncodeValue(Variant value)
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(Opc.Ua.Namespaces.OpcUa);
            namespaceUris.Append(Namespaces.AasV2);
            var context = ServiceMessageContext.CreateEmpty(null!);
            context.NamespaceUris = namespaceUris;
            using var encoder = new XmlEncoder(context);
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

        private static void AddForward(
            string parentNodeId,
            string referenceType,
            string childNodeId,
            List<UANode> nodes)
        {
            UANode? parent = Find(nodes, parentNodeId);
            if (parent is not null)
            {
                AddReference(parent, new Reference
                {
                    ReferenceType = referenceType,
                    IsForward = true,
                    Value = childNodeId
                });
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

        private static string ToNodeSetId(ExpandedNodeId nodeId)
        {
            if (string.Equals(nodeId.NamespaceUri, Namespaces.AasV2, StringComparison.Ordinal) &&
                nodeId.TryGetValue(out uint aasId))
            {
                return "ns=1;i=" + aasId.ToString(CultureInfo.InvariantCulture);
            }

            if (nodeId.TryGetValue(out uint coreId))
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

        private static ExpandedNodeId CoreObjectType(uint objectTypeId)
        {
            return new ExpandedNodeId(objectTypeId, 0, Opc.Ua.Namespaces.OpcUa, 0);
        }

        private static Argument ScalarArgument(string name, uint dataTypeId, string description = "")
        {
            return new Argument
            {
                Name = name,
                DataType = new NodeId(dataTypeId),
                ValueRank = ValueRanks.Scalar,
                Description = new Opc.Ua.LocalizedText(description)
            };
        }

        private static NodeIdAlias Alias(string alias, string nodeId)
        {
            return new NodeIdAlias { Alias = alias, Value = nodeId };
        }

        private static readonly NodeIdAlias[] s_aliases =
        [
            Alias("AASReference", "ns=1;i=4003"),
            Alias("HasComponent", "i=47"),
            Alias("HasInterface", "i=17603"),
            Alias("HasOrderedComponent", "i=49"),
            Alias("HasProperty", "i=46"),
            Alias("HasTypeDefinition", "i=40"),
            Alias("Organizes", "i=35")
        ];

        private static readonly Argument[] s_openInputArguments =
        [
            ScalarArgument("Mode", Opc.Ua.DataTypes.Byte)
        ];

        private static readonly Argument[] s_openOutputArguments =
        [
            ScalarArgument("FileHandle", Opc.Ua.DataTypes.UInt32)
        ];

        private static readonly Argument[] s_readInputArguments =
        [
            ScalarArgument("FileHandle", Opc.Ua.DataTypes.UInt32),
            ScalarArgument("Length", Opc.Ua.DataTypes.Int32)
        ];

        private static readonly Argument[] s_readOutputArguments =
        [
            ScalarArgument("Data", Opc.Ua.DataTypes.ByteString)
        ];

        private static readonly Argument[] s_closeInputArguments =
        [
            ScalarArgument("FileHandle", Opc.Ua.DataTypes.UInt32)
        ];

        private static readonly Argument[] s_writeInputArguments =
        [
            ScalarArgument("FileHandle", Opc.Ua.DataTypes.UInt32),
            ScalarArgument("Data", Opc.Ua.DataTypes.ByteString)
        ];

        private static readonly Argument[] s_getPositionInputArguments =
        [
            ScalarArgument("FileHandle", Opc.Ua.DataTypes.UInt32)
        ];

        private static readonly Argument[] s_getPositionOutputArguments =
        [
            ScalarArgument("Position", Opc.Ua.DataTypes.UInt64)
        ];

        private static readonly Argument[] s_setPositionInputArguments =
        [
            ScalarArgument("FileHandle", Opc.Ua.DataTypes.UInt32),
            ScalarArgument("Position", Opc.Ua.DataTypes.UInt64)
        ];

        private const string EnvironmentNodeId = "ns=1;s=i4aas3:V2:Environment";
        private const string OperationDeclarationId = "ns=1;i=7001";
    }
}
