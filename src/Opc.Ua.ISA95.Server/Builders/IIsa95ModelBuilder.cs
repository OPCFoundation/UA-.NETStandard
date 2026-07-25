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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Providers;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Server.Builders
{
    /// <summary>
    /// Typed authoring surface for the OPC-10030 ISA-95 common model. The
    /// builder materialises the generated state types beneath a supplied root
    /// node, assigns deterministic child NodeIds through the injected
    /// <see cref="ISystemContext"/> NodeId factory, wires the normative
    /// relationships (with correct forward/inverse references) and registers or
    /// removes nodes through the asynchronous callbacks supplied at
    /// construction. It intentionally does not depend on a concrete node
    /// manager.
    /// </summary>
    public interface IIsa95ModelBuilder
    {
        /// <summary>
        /// The system context used to materialise nodes and assign NodeIds.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// The root node new instances are created beneath by default.
        /// </summary>
        NodeState Root { get; }

        /// <summary>
        /// Creates a personnel class beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created personnel class.</returns>
        ValueTask<PersonnelClassState> CreatePersonnelClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a person beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created person.</returns>
        ValueTask<PersonState> CreatePersonAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an equipment class beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created equipment class.</returns>
        ValueTask<EquipmentClassState> CreateEquipmentClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an equipment instance beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created equipment.</returns>
        ValueTask<EquipmentState> CreateEquipmentAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a physical asset class beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created physical asset class.</returns>
        ValueTask<PhysicalAssetClassState> CreatePhysicalAssetClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a physical asset beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created physical asset.</returns>
        ValueTask<PhysicalAssetState> CreatePhysicalAssetAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a material class beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created material class.</returns>
        ValueTask<MaterialClassState> CreateMaterialClassAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a material definition beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created material definition.</returns>
        ValueTask<MaterialDefinitionState> CreateMaterialDefinitionAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a material lot beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created material lot.</returns>
        ValueTask<MaterialLotState> CreateMaterialLotAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a material sublot beneath <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created material sublot.</returns>
        ValueTask<MaterialSublotState> CreateMaterialSublotAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an equipment capability test specification.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created test specification.</returns>
        ValueTask<EquipmentCapabilityTestSpecificationState>
            CreateEquipmentTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a physical asset capability test specification.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created test specification.</returns>
        ValueTask<PhysicalAssetCapabilityTestSpecificationState>
            CreatePhysicalAssetTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a qualification test specification.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created test specification.</returns>
        ValueTask<QualificationTestSpecificationState>
            CreateQualificationTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a material test specification.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created test specification.</returns>
        ValueTask<MaterialTestSpecificationState>
            CreateMaterialTestSpecificationAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an ISA-95 test result variable beneath
        /// <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new node.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created test result.</returns>
        ValueTask<ISA95TestResultState> CreateTestResultAsync(
            NodeState parent,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an OPC-10030 <c>GeoSpatialLocationType</c> variable beneath
        /// <paramref name="parent"/> and, when a provider is supplied, binds it so
        /// reads and optional updates flow through the provider. This avoids
        /// reaching into the generated factories directly.
        /// </summary>
        /// <param name="parent">The owning node.</param>
        /// <param name="name">The browse name of the new variable.</param>
        /// <param name="provider">
        /// The optional provider to bind. When supplied, the returned binding
        /// serves reads asynchronously and applies optional push updates; the
        /// update loop stops when <paramref name="cancellationToken"/> is
        /// cancelled or the binding is disposed.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created variable and its optional provider binding.</returns>
        ValueTask<Isa95GeoSpatialLocationBinding> CreateGeoSpatialLocationAsync(
            NodeState parent,
            string name,
            IIsa95GeoSpatialLocationProvider? provider = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds an ISA-95 class property to <paramref name="owner"/>, wiring the
        /// <c>HasISA95ClassProperty</c> reference.
        /// </summary>
        /// <param name="owner">The owning ISA-95 class.</param>
        /// <param name="name">The browse name of the property.</param>
        /// <param name="value">The initial value.</param>
        /// <param name="dataType">The optional data type of the property.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created class property.</returns>
        ValueTask<ISA95ClassPropertyState> AddClassPropertyAsync(
            ISA95ClassState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a person property and wires the
        /// <c>HasISA95Property</c> reference.
        /// </summary>
        /// <param name="owner">The owning person.</param>
        /// <param name="name">The browse name of the property.</param>
        /// <param name="value">The initial value.</param>
        /// <param name="dataType">The optional data type of the property.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The created person property.</returns>
        ValueTask<PersonPropertyState> AddPropertyAsync(
            PersonState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds an equipment property and wires the
        /// <c>HasISA95Property</c> reference.
        /// </summary>
        ValueTask<EquipmentPropertyState> AddPropertyAsync(
            EquipmentState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a physical asset property and wires the
        /// <c>HasISA95Property</c> reference.
        /// </summary>
        ValueTask<PhysicalAssetPropertyState> AddPropertyAsync(
            PhysicalAssetState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a material definition property and wires the
        /// <c>HasISA95Property</c> reference.
        /// </summary>
        /// <param name="owner">The owning material definition.</param>
        /// <param name="name">The browse name of the property.</param>
        /// <param name="value">The initial value.</param>
        /// <param name="dataType">The optional data type of the property.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask<MaterialDefinitionPropertyState> AddPropertyAsync(
            MaterialDefinitionState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a material lot property and wires the
        /// <c>HasISA95Property</c> reference.
        /// </summary>
        ValueTask<MaterialLotPropertyState> AddPropertyAsync(
            MaterialLotState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a material sublot property using the standard
        /// <c>MaterialLotPropertyType</c> and wires the
        /// <c>HasISA95Property</c> reference.
        /// </summary>
        ValueTask<MaterialLotPropertyState> AddPropertyAsync(
            MaterialSublotState owner,
            string name,
            Variant value = default,
            NodeId? dataType = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a <c>DefinedByPersonnelClass</c> relationship.
        /// </summary>
        /// <param name="person">The person.</param>
        /// <param name="personnelClass">The defining personnel class.</param>
        void DefinedByPersonnelClass(
            PersonState person,
            PersonnelClassState personnelClass);

        /// <summary>
        /// Adds a <c>DefinedByEquipmentClass</c> relationship.
        /// </summary>
        /// <param name="equipment">The equipment.</param>
        /// <param name="equipmentClass">The defining equipment class.</param>
        void DefinedByEquipmentClass(
            EquipmentState equipment,
            EquipmentClassState equipmentClass);

        /// <summary>
        /// Adds a <c>DefinedByPhysicalAssetClass</c> relationship.
        /// </summary>
        /// <param name="asset">The physical asset.</param>
        /// <param name="assetClass">The defining physical asset class.</param>
        void DefinedByPhysicalAssetClass(
            PhysicalAssetState asset,
            PhysicalAssetClassState assetClass);

        /// <summary>
        /// Adds a <c>DefinedByMaterialClass</c> relationship.
        /// </summary>
        /// <param name="definition">The material definition.</param>
        /// <param name="materialClass">The defining material class.</param>
        void DefinedByMaterialClass(
            MaterialDefinitionState definition,
            MaterialClassState materialClass);

        /// <summary>
        /// Adds a <c>DefinedByMaterialDefinition</c> relationship.
        /// </summary>
        /// <param name="lot">The material lot.</param>
        /// <param name="definition">The defining material definition.</param>
        void DefinedByMaterialDefinition(
            MaterialLotState lot,
            MaterialDefinitionState definition);

        /// <summary>
        /// Adds a <c>MadeUpOfEquipment</c> aggregation.
        /// </summary>
        /// <param name="whole">The composite equipment.</param>
        /// <param name="part">The contained equipment.</param>
        void MadeUpOfEquipment(EquipmentState whole, EquipmentState part);

        /// <summary>
        /// Adds a <c>MadeUpOfPhysicalAsset</c> aggregation.
        /// </summary>
        /// <param name="whole">The composite physical asset.</param>
        /// <param name="part">The contained physical asset.</param>
        void MadeUpOfPhysicalAsset(
            PhysicalAssetState whole,
            PhysicalAssetState part);

        /// <summary>
        /// Adds a <c>MadeUpOfMaterialSublot</c> aggregation.
        /// </summary>
        /// <param name="lot">The material lot.</param>
        /// <param name="sublot">The contained material sublot.</param>
        void MadeUpOfMaterialSublot(
            MaterialLotState lot,
            MaterialSublotState sublot);

        /// <summary>
        /// Adds an <c>AssembledFromClass</c> relationship between material
        /// classes (OPC-10030 §9.6.6).
        /// </summary>
        /// <param name="materialClass">The assembled material class.</param>
        /// <param name="component">The component material class.</param>
        void AssembledFromClass(
            MaterialClassState materialClass,
            MaterialClassState component);

        /// <summary>
        /// Adds an <c>AssembledFromClass</c> relationship from a material class
        /// to a material class property.
        /// </summary>
        void AssembledFromClass(
            MaterialClassState materialClass,
            MaterialClassPropertyState component);

        /// <summary>
        /// Adds an <c>AssembledFromClass</c> relationship from a material class
        /// property to a material class.
        /// </summary>
        void AssembledFromClass(
            MaterialClassPropertyState property,
            MaterialClassState component);

        /// <summary>
        /// Adds an <c>AssembledFromClass</c> relationship between material
        /// class properties.
        /// </summary>
        void AssembledFromClass(
            MaterialClassPropertyState property,
            MaterialClassPropertyState component);

        /// <summary>
        /// Adds an <c>AssembledFromDefinition</c> relationship: a material
        /// definition is assembled from another material definition
        /// (OPC-10030 §9.6.5).
        /// </summary>
        /// <param name="definition">The assembled material definition.</param>
        /// <param name="component">The source material definition.</param>
        void AssembledFromDefinition(
            MaterialDefinitionState definition,
            MaterialDefinitionState component);

        /// <summary>
        /// Adds an <c>AssembledFromDefinition</c> relationship from a material
        /// definition to a material definition property.
        /// </summary>
        void AssembledFromDefinition(
            MaterialDefinitionState definition,
            MaterialDefinitionPropertyState component);

        /// <summary>
        /// Adds an <c>AssembledFromDefinition</c> relationship from a material
        /// definition property to a material definition.
        /// </summary>
        void AssembledFromDefinition(
            MaterialDefinitionPropertyState property,
            MaterialDefinitionState component);

        /// <summary>
        /// Adds an <c>AssembledFromDefinition</c> relationship between material
        /// definition properties.
        /// </summary>
        void AssembledFromDefinition(
            MaterialDefinitionPropertyState property,
            MaterialDefinitionPropertyState component);

        /// <summary>
        /// Adds an <c>AssembledFromLot</c> relationship from a material lot to
        /// a material lot (OPC-10030 §9.6.7).
        /// </summary>
        /// <param name="lot">The assembled material lot.</param>
        /// <param name="component">The source material lot.</param>
        void AssembledFromLot(MaterialLotState lot, MaterialLotState component);

        /// <summary>
        /// Adds an <c>AssembledFromLot</c> relationship from a material lot to
        /// a material sublot.
        /// </summary>
        void AssembledFromLot(
            MaterialLotState lot,
            MaterialSublotState component);

        /// <summary>
        /// Adds an <c>AssembledFromSublot</c> relationship from a material
        /// sublot to a material lot (OPC-10030 §9.6.8).
        /// </summary>
        void AssembledFromSublot(
            MaterialSublotState sublot,
            MaterialLotState component);

        /// <summary>
        /// Adds an <c>AssembledFromSublot</c> relationship: a material sublot is
        /// assembled from another material sublot (OPC-10030 §9.6.8).
        /// </summary>
        /// <param name="sublot">The assembled material sublot.</param>
        /// <param name="component">The source material sublot.</param>
        void AssembledFromSublot(
            MaterialSublotState sublot,
            MaterialSublotState component);

        /// <summary>
        /// Adds a <c>TestedByEquipmentTest</c> relationship.
        /// </summary>
        /// <param name="equipmentClass">The tested equipment class.</param>
        /// <param name="specification">The test specification.</param>
        void TestedByEquipmentTest(
            EquipmentClassState equipmentClass,
            EquipmentCapabilityTestSpecificationState specification);

        /// <summary>
        /// Adds a <c>TestedByPhysicalAssetTest</c> relationship.
        /// </summary>
        /// <param name="assetClass">The tested physical asset class.</param>
        /// <param name="specification">The test specification.</param>
        void TestedByPhysicalAssetTest(
            PhysicalAssetClassState assetClass,
            PhysicalAssetCapabilityTestSpecificationState specification);

        /// <summary>
        /// Adds a <c>TestedByQualificationTest</c> relationship.
        /// </summary>
        /// <param name="personnelClass">The tested personnel class.</param>
        /// <param name="specification">The test specification.</param>
        void TestedByQualificationTest(
            PersonnelClassState personnelClass,
            QualificationTestSpecificationState specification);

        /// <summary>
        /// Adds a <c>TestedByMaterialTest</c> relationship.
        /// </summary>
        /// <param name="materialClass">The tested material class.</param>
        /// <param name="specification">The test specification.</param>
        void TestedByMaterialTest(
            MaterialClassState materialClass,
            MaterialTestSpecificationState specification);

        /// <summary>
        /// Adds a <c>LocatedIn</c> relationship.
        /// </summary>
        /// <param name="instance">The located variable.</param>
        /// <param name="location">The location node.</param>
        void LocatedIn(
            BaseVariableState instance,
            GeoSpatialLocationState location);

        /// <summary>
        /// Adds an <c>ImplementedBy</c> relationship.
        /// </summary>
        /// <param name="equipment">The equipment.</param>
        /// <param name="asset">The implementing physical asset.</param>
        void ImplementedBy(
            EquipmentState equipment,
            PhysicalAssetState asset);

        /// <summary>
        /// Adds an <c>ImplementedBy</c> relationship from a physical asset to
        /// equipment.
        /// </summary>
        void ImplementedBy(
            PhysicalAssetState asset,
            EquipmentState equipment);

        /// <summary>
        /// Adds a <c>HasTestResult</c> relationship.
        /// </summary>
        /// <param name="owner">The owning ISA-95 property.</param>
        /// <param name="result">The test result.</param>
        void HasTestResult(
            ISA95PropertyState owner,
            ISA95TestResultState result);

        /// <summary>
        /// Adds a <c>ResultsForSpecification</c> relationship.
        /// </summary>
        /// <param name="result">The test result.</param>
        /// <param name="specification">The test specification.</param>
        void ResultsForSpecification(
            ISA95TestResultState result,
            ISA95TestSpecificationState specification);

        /// <summary>
        /// Adds a forward reference from <paramref name="source"/> to
        /// <paramref name="target"/> together with its inverse on the target.
        /// </summary>
        /// <param name="source">The source node.</param>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="target">The target node.</param>
        void Relate(NodeState source, NodeId referenceTypeId, NodeState target);

        /// <summary>
        /// Binds a geospatial location variable to a provider, serving reads
        /// asynchronously and applying optional push updates.
        /// </summary>
        /// <param name="state">The geospatial location variable.</param>
        /// <param name="provider">The backing provider.</param>
        /// <param name="cancellationToken">
        /// A token that stops the optional update loop when cancelled.
        /// </param>
        /// <returns>A handle that stops the update loop when disposed.</returns>
        IDisposable BindGeoSpatialLocation(
            GeoSpatialLocationState state,
            IIsa95GeoSpatialLocationProvider provider,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers an externally created node through the supplied callback.
        /// </summary>
        /// <param name="node">The node to register.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that completes when the node is registered.</returns>
        ValueTask RegisterAsync(
            NodeState node,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a previously registered node through the supplied callback.
        /// </summary>
        /// <param name="node">The node to remove.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that completes when the node is removed.</returns>
        ValueTask RemoveAsync(
            NodeState node,
            CancellationToken cancellationToken = default);
    }
}
