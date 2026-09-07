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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Server.Builders
{
    /// <summary>
    /// Default <see cref="IIsa95ModelBuilder"/> implementation. Materialises the
    /// generated OPC-10030 state types with deterministic child NodeIds and
    /// wires the normative relationships. The builder is decoupled from the node
    /// manager through the asynchronous register and remove callbacks supplied
    /// at construction.
    /// </summary>
    public sealed class Isa95ModelBuilder : IIsa95ModelBuilder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Isa95ModelBuilder"/>
        /// class.
        /// </summary>
        /// <param name="context">
        /// The system context that supplies the NodeId factory.
        /// </param>
        /// <param name="root">
        /// The root node new instances are created beneath by default.
        /// </param>
        /// <param name="namespaceIndex">
        /// The namespace index used for the browse names of created nodes.
        /// </param>
        /// <param name="register">
        /// The asynchronous callback used to register created nodes.
        /// </param>
        /// <param name="remove">
        /// The optional asynchronous callback used to remove nodes.
        /// </param>
        public Isa95ModelBuilder(
            ISystemContext context,
            NodeState root,
            ushort namespaceIndex,
            Isa95RegisterNodeAsync register,
            Isa95RemoveNodeAsync? remove = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            m_register = register ?? throw new ArgumentNullException(nameof(register));
            m_namespaceIndex = namespaceIndex;
            m_remove = remove;
        }

        /// <summary>
        /// Creates a new <see cref="Isa95ModelBuilder"/>.
        /// </summary>
        /// <param name="context">
        /// The system context that supplies the NodeId factory.
        /// </param>
        /// <param name="root">
        /// The root node new instances are created beneath by default.
        /// </param>
        /// <param name="namespaceIndex">
        /// The namespace index used for the browse names of created nodes.
        /// </param>
        /// <param name="register">
        /// The asynchronous callback used to register created nodes.
        /// </param>
        /// <param name="remove">
        /// The optional asynchronous callback used to remove nodes.
        /// </param>
        /// <returns>The created builder.</returns>
        public static Isa95ModelBuilder Create(
            ISystemContext context,
            NodeState root,
            ushort namespaceIndex,
            Isa95RegisterNodeAsync register,
            Isa95RemoveNodeAsync? remove = null)
        {
            return new Isa95ModelBuilder(context, root, namespaceIndex, register, remove);
        }

        /// <inheritdoc/>
        public ISystemContext Context { get; }

        /// <inheritdoc/>
        public NodeState Root { get; }

        /// <inheritdoc/>
        public ValueTask<PersonnelClassState> CreatePersonnelClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfPersonnelClassType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<PersonState> CreatePersonAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfPersonType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<EquipmentClassState> CreateEquipmentClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfEquipmentClassType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<EquipmentState> CreateEquipmentAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfEquipmentType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<PhysicalAssetClassState> CreatePhysicalAssetClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfPhysicalAssetClassType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<PhysicalAssetState> CreatePhysicalAssetAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfPhysicalAssetType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialClassState> CreateMaterialClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfMaterialClassType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialDefinitionState> CreateMaterialDefinitionAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfMaterialDefinitionType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialLotState> CreateMaterialLotAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfMaterialLotType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialSublotState> CreateMaterialSublotAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfMaterialSublotType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<EquipmentCapabilityTestSpecificationState>
            CreateEquipmentTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions
                    .CreateInstanceOfEquipmentCapabilityTestSpecificationType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<PhysicalAssetCapabilityTestSpecificationState>
            CreatePhysicalAssetTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions
                    .CreateInstanceOfPhysicalAssetCapabilityTestSpecificationType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<QualificationTestSpecificationState>
            CreateQualificationTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions
                    .CreateInstanceOfQualificationTestSpecificationType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialTestSpecificationState>
            CreateMaterialTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfMaterialTestSpecificationType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ISA95TestResultState> CreateTestResultAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfISA95TestResultType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<Isa95GeoSpatialLocationBinding> CreateGeoSpatialLocationAsync(
            NodeState parent,
            string name,
            IGeoLocationProvider? provider = null,
            string? sourceId = null,
            IGeoLocationTextFormatter? formatter = null,
            CancellationToken cancellationToken = default)
        {
            GeoSpatialLocationState state = await CreateAsync(
                parent,
                name,
                OpcUaISA95Extensions.CreateInstanceOfGeoSpatialLocationType,
                cancellationToken).ConfigureAwait(false);
            IDisposable? binding = provider != null
                ? BindGeoSpatialLocation(
                    state,
                    provider,
                    sourceId ?? throw new ArgumentException(
                        "A source identifier is required when a provider is supplied.",
                        nameof(sourceId)),
                    formatter,
                    cancellationToken)
                : null;
            return new Isa95GeoSpatialLocationBinding(state, binding);
        }

        /// <inheritdoc/>
        public async ValueTask<ISA95ClassPropertyState> AddClassPropertyAsync(
            ISA95ClassState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default)
        {
            ValidateParent(owner);
            ValidateName(name);

            return owner switch
            {
                PersonnelClassState personnelClass =>
                    await CreatePropertyAsync(
                        personnelClass,
                        name,
                        OpcUaISA95Extensions
                            .CreateInstanceOfPersonnelClassPropertyType,
                        ReferenceTypeIds.HasISA95ClassProperty,
                        value,
                        dataType,
                        cancellationToken).ConfigureAwait(false),
                EquipmentClassState equipmentClass =>
                    await CreatePropertyAsync(
                        equipmentClass,
                        name,
                        OpcUaISA95Extensions
                            .CreateInstanceOfEquipmentClassPropertyType,
                        ReferenceTypeIds.HasISA95ClassProperty,
                        value,
                        dataType,
                        cancellationToken).ConfigureAwait(false),
                PhysicalAssetClassState assetClass =>
                    await CreatePropertyAsync(
                        assetClass,
                        name,
                        OpcUaISA95Extensions
                            .CreateInstanceOfPhysicalAssetClassPropertyType,
                        ReferenceTypeIds.HasISA95ClassProperty,
                        value,
                        dataType,
                        cancellationToken).ConfigureAwait(false),
                MaterialClassState materialClass =>
                    await CreatePropertyAsync(
                        materialClass,
                        name,
                        OpcUaISA95Extensions
                            .CreateInstanceOfMaterialClassPropertyType,
                        ReferenceTypeIds.HasISA95ClassProperty,
                        value,
                        dataType,
                        cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException(
                    "The class type does not define a concrete ISA-95 class " +
                    "property type.",
                    nameof(owner))
            };
        }

        /// <inheritdoc/>
        public ValueTask<PersonPropertyState> AddPropertyAsync(
            PersonState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default)
        {
            ValidateParent(owner);
            ValidateName(name);
            return CreatePropertyAsync(
                owner,
                name,
                OpcUaISA95Extensions.CreateInstanceOfPersonPropertyType,
                ReferenceTypeIds.HasISA95Property,
                value,
                dataType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<EquipmentPropertyState> AddPropertyAsync(
            EquipmentState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default)
        {
            ValidateParent(owner);
            ValidateName(name);
            return CreatePropertyAsync(
                owner,
                name,
                OpcUaISA95Extensions.CreateInstanceOfEquipmentPropertyType,
                ReferenceTypeIds.HasISA95Property,
                value,
                dataType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<PhysicalAssetPropertyState> AddPropertyAsync(
            PhysicalAssetState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default)
        {
            ValidateParent(owner);
            ValidateName(name);
            return CreatePropertyAsync(
                owner,
                name,
                OpcUaISA95Extensions.CreateInstanceOfPhysicalAssetPropertyType,
                ReferenceTypeIds.HasISA95Property,
                value,
                dataType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialDefinitionPropertyState> AddPropertyAsync(
            MaterialDefinitionState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default)
        {
            ValidateParent(owner);
            ValidateName(name);
            return CreatePropertyAsync(
                owner,
                name,
                OpcUaISA95Extensions
                    .CreateInstanceOfMaterialDefinitionPropertyType,
                ReferenceTypeIds.HasISA95Property,
                value,
                dataType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialLotPropertyState> AddPropertyAsync(
            MaterialLotState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default)
        {
            ValidateParent(owner);
            ValidateName(name);
            return CreatePropertyAsync(
                owner,
                name,
                OpcUaISA95Extensions.CreateInstanceOfMaterialLotPropertyType,
                ReferenceTypeIds.HasISA95Property,
                value,
                dataType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MaterialLotPropertyState> AddPropertyAsync(
            MaterialSublotState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default)
        {
            ValidateParent(owner);
            ValidateName(name);
            return CreatePropertyAsync(
                owner,
                name,
                OpcUaISA95Extensions.CreateInstanceOfMaterialLotPropertyType,
                ReferenceTypeIds.HasISA95Property,
                value,
                dataType,
                cancellationToken);
        }

        /// <inheritdoc/>
        public void DefinedByPersonnelClass(
            PersonState person,
            PersonnelClassState personnelClass)
        {
            AddForwardInverse(
                person,
                ReferenceTypeIds.DefinedByPersonnelClass,
                personnelClass);
        }

        /// <inheritdoc/>
        public void DefinedByEquipmentClass(
            EquipmentState equipment,
            EquipmentClassState equipmentClass)
        {
            AddForwardInverse(
                equipment,
                ReferenceTypeIds.DefinedByEquipmentClass,
                equipmentClass);
        }

        /// <inheritdoc/>
        public void DefinedByPhysicalAssetClass(
            PhysicalAssetState asset,
            PhysicalAssetClassState assetClass)
        {
            AddForwardInverse(
                asset,
                ReferenceTypeIds.DefinedByPhysicalAssetClass,
                assetClass);
        }

        /// <inheritdoc/>
        public void DefinedByMaterialClass(
            MaterialDefinitionState definition,
            MaterialClassState materialClass)
        {
            AddForwardInverse(
                definition,
                ReferenceTypeIds.DefinedByMaterialClass,
                materialClass);
        }

        /// <inheritdoc/>
        public void DefinedByMaterialDefinition(
            MaterialLotState lot,
            MaterialDefinitionState definition)
        {
            AddForwardInverse(
                lot,
                ReferenceTypeIds.DefinedByMaterialDefinition,
                definition);
        }

        /// <inheritdoc/>
        public void MadeUpOfEquipment(EquipmentState whole, EquipmentState part)
        {
            AddForwardInverse(whole, ReferenceTypeIds.MadeUpOfEquipment, part);
        }

        /// <inheritdoc/>
        public void MadeUpOfPhysicalAsset(
            PhysicalAssetState whole,
            PhysicalAssetState part)
        {
            AddForwardInverse(whole, ReferenceTypeIds.MadeUpOfPhysicalAsset, part);
        }

        /// <inheritdoc/>
        public void MadeUpOfMaterialSublot(
            MaterialLotState lot,
            MaterialSublotState sublot)
        {
            AddForwardInverse(lot, ReferenceTypeIds.MadeUpOfMaterialSublot, sublot);
        }

        /// <inheritdoc/>
        public void AssembledFromClass(
            MaterialClassState materialClass,
            MaterialClassState component)
        {
            AddForwardInverse(
                materialClass,
                ReferenceTypeIds.AssembledFromClass,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromClass(
            MaterialClassState materialClass,
            MaterialClassPropertyState component)
        {
            AddForwardInverse(
                materialClass,
                ReferenceTypeIds.AssembledFromClass,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromClass(
            MaterialClassPropertyState property,
            MaterialClassState component)
        {
            AddForwardInverse(
                property,
                ReferenceTypeIds.AssembledFromClass,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromClass(
            MaterialClassPropertyState property,
            MaterialClassPropertyState component)
        {
            AddForwardInverse(
                property,
                ReferenceTypeIds.AssembledFromClass,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromDefinition(
            MaterialDefinitionState definition,
            MaterialDefinitionState component)
        {
            AddForwardInverse(
                definition,
                ReferenceTypeIds.AssembledFromDefinition,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromDefinition(
            MaterialDefinitionState definition,
            MaterialDefinitionPropertyState component)
        {
            AddForwardInverse(
                definition,
                ReferenceTypeIds.AssembledFromDefinition,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromDefinition(
            MaterialDefinitionPropertyState property,
            MaterialDefinitionState component)
        {
            AddForwardInverse(
                property,
                ReferenceTypeIds.AssembledFromDefinition,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromDefinition(
            MaterialDefinitionPropertyState property,
            MaterialDefinitionPropertyState component)
        {
            AddForwardInverse(
                property,
                ReferenceTypeIds.AssembledFromDefinition,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromLot(
            MaterialLotState lot,
            MaterialLotState component)
        {
            AddForwardInverse(lot, ReferenceTypeIds.AssembledFromLot, component);
        }

        /// <inheritdoc/>
        public void AssembledFromLot(
            MaterialLotState lot,
            MaterialSublotState component)
        {
            AddForwardInverse(lot, ReferenceTypeIds.AssembledFromLot, component);
        }

        /// <inheritdoc/>
        public void AssembledFromSublot(
            MaterialSublotState sublot,
            MaterialLotState component)
        {
            AddForwardInverse(
                sublot,
                ReferenceTypeIds.AssembledFromSublot,
                component);
        }

        /// <inheritdoc/>
        public void AssembledFromSublot(
            MaterialSublotState sublot,
            MaterialSublotState component)
        {
            AddForwardInverse(sublot, ReferenceTypeIds.AssembledFromSublot, component);
        }

        /// <inheritdoc/>
        public void TestedByEquipmentTest(
            EquipmentClassState equipmentClass,
            EquipmentCapabilityTestSpecificationState specification)
        {
            AddForwardInverse(
                equipmentClass,
                ReferenceTypeIds.TestedByEquipmentTest,
                specification);
        }

        /// <inheritdoc/>
        public void TestedByPhysicalAssetTest(
            PhysicalAssetClassState assetClass,
            PhysicalAssetCapabilityTestSpecificationState specification)
        {
            AddForwardInverse(
                assetClass,
                ReferenceTypeIds.TestedByPhysicalAssetTest,
                specification);
        }

        /// <inheritdoc/>
        public void TestedByQualificationTest(
            PersonnelClassState personnelClass,
            QualificationTestSpecificationState specification)
        {
            AddForwardInverse(
                personnelClass,
                ReferenceTypeIds.TestedByQualificationTest,
                specification);
        }

        /// <inheritdoc/>
        public void TestedByMaterialTest(
            MaterialClassState materialClass,
            MaterialTestSpecificationState specification)
        {
            AddForwardInverse(
                materialClass,
                ReferenceTypeIds.TestedByMaterialTest,
                specification);
        }

        /// <inheritdoc/>
        public void LocatedIn(
            BaseVariableState instance,
            GeoSpatialLocationState location)
        {
            AddForwardInverse(instance, ReferenceTypeIds.LocatedIn, location);
        }

        /// <inheritdoc/>
        public void ImplementedBy(
            EquipmentState equipment,
            PhysicalAssetState asset)
        {
            AddForwardInverse(
                equipment,
                ReferenceTypeIds.ImplementedBy,
                asset);
        }

        /// <inheritdoc/>
        public void ImplementedBy(
            PhysicalAssetState asset,
            EquipmentState equipment)
        {
            AddForwardInverse(
                asset,
                ReferenceTypeIds.ImplementedBy,
                equipment);
        }

        /// <inheritdoc/>
        public void HasTestResult(
            ISA95PropertyState owner,
            ISA95TestResultState result)
        {
            AddForwardInverse(owner, ReferenceTypeIds.HasTestResult, result);
        }

        /// <inheritdoc/>
        public void ResultsForSpecification(
            ISA95TestResultState result,
            ISA95TestSpecificationState specification)
        {
            AddForwardInverse(
                result,
                ReferenceTypeIds.ResultsForSpecification,
                specification);
        }

        /// <inheritdoc/>
        public void Relate(NodeState source, NodeId referenceTypeId, NodeState target)
        {
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentException(
                    "A reference type id is required.",
                    nameof(referenceTypeId));
            }
            AddForwardInverse(source, referenceTypeId, target);
        }

        /// <inheritdoc/>
        public IDisposable BindGeoSpatialLocation(
            GeoSpatialLocationState state,
            IGeoLocationProvider provider,
            string sourceId,
            IGeoLocationTextFormatter? formatter = null,
            CancellationToken cancellationToken = default)
        {
            return Isa95GeoSpatialLocationBinder.Bind(
                Context,
                state,
                provider,
                sourceId,
                formatter,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask RegisterAsync(
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            return m_register(node, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask RemoveAsync(
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (m_remove == null)
            {
                throw new InvalidOperationException(
                    "No remove callback was supplied to the builder.");
            }
            return m_remove(node, cancellationToken);
        }

        private async ValueTask<TState> CreatePropertyAsync<TState>(
            NodeState owner,
            string name,
            Func<ISystemContext, NodeState, QualifiedName, TState> factory,
            ExpandedNodeId referenceTypeId,
            Variant value,
            NodeId? dataType,
            CancellationToken cancellationToken)
            where TState : BaseVariableState
        {
            TState property = factory(Context, owner, BrowseName(name));
            property.ReferenceTypeId = ResolveReferenceType(referenceTypeId);
            ApplyPropertyValue(property, value, dataType);
            owner.AddChild(property);
            await m_register(property, cancellationToken).ConfigureAwait(false);
            return property;
        }

        private async ValueTask<TState> CreateAsync<TState>(
            NodeState parent,
            string name,
            Func<ISystemContext, NodeState, QualifiedName, TState> factory,
            CancellationToken cancellationToken)
            where TState : BaseInstanceState
        {
            ValidateParent(parent);
            ValidateName(name);

            TState instance = factory(Context, parent, BrowseName(name));
            if (instance.ReferenceTypeId.IsNull)
            {
                instance.ReferenceTypeId = parent is FolderState
                    ? Ua.ReferenceTypeIds.Organizes
                    : Ua.ReferenceTypeIds.HasComponent;
            }
            parent.AddChild(instance);
            await m_register(instance, cancellationToken).ConfigureAwait(false);
            return instance;
        }

        private void AddForwardInverse(
            NodeState source,
            ExpandedNodeId referenceTypeId,
            NodeState target)
        {
            AddForwardInverse(source, ResolveReferenceType(referenceTypeId), target);
        }

        private void AddForwardInverse(
            NodeState source,
            NodeId referenceTypeId,
            NodeState target)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (source.NodeId.IsNull || target.NodeId.IsNull)
            {
                throw new ArgumentException(
                    "Both source and target must have a NodeId before they can " +
                    "be related.");
            }
            if (source.ReferenceExists(referenceTypeId, false, target.NodeId))
            {
                throw new InvalidOperationException(
                    "The relationship already exists between the two nodes.");
            }
            source.AddReference(referenceTypeId, false, target.NodeId);
            target.AddReference(referenceTypeId, true, source.NodeId);
        }

        private NodeId ResolveReferenceType(ExpandedNodeId referenceTypeId)
        {
            var resolved = ExpandedNodeId.ToNodeId(referenceTypeId, Context.NamespaceUris);
            if (resolved.IsNull)
            {
                throw new InvalidOperationException(
                    "The ISA-95 namespace is not present in the context namespace " +
                    "table; the reference type could not be resolved.");
            }
            return resolved;
        }

        private static void ApplyPropertyValue(
            BaseVariableState property,
            Variant value,
            NodeId? dataType)
        {
            property.Value = value;
            if (dataType.HasValue && !dataType.Value.IsNull)
            {
                property.DataType = dataType.Value;
            }
        }

        private void ValidateParent(NodeState parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (parent.NodeId.IsNull)
            {
                throw new ArgumentException(
                    "The parent must have a NodeId so deterministic child NodeIds " +
                    "can be assigned.",
                    nameof(parent));
            }
        }

        private static void ValidateName(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (name.Length == 0)
            {
                throw new ArgumentException(
                    "A non-empty name is required.",
                    nameof(name));
            }
        }

        private QualifiedName BrowseName(string name)
        {
            return new QualifiedName(name, m_namespaceIndex);
        }

        private readonly Isa95RegisterNodeAsync m_register;
        private readonly Isa95RemoveNodeAsync? m_remove;
        private readonly ushort m_namespaceIndex;
    }
}
