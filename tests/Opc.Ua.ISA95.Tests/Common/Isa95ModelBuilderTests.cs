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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Builders;

namespace Opc.Ua.ISA95.Tests.Common
{
    /// <summary>
    /// Tests for the ISA-95 common-model builder: typed creation, deterministic
    /// NodeIds, mandatory children, normative relationship references and
    /// argument guards.
    /// </summary>
    [TestFixture]
    public class Isa95ModelBuilderTests
    {
        [Test]
        public async Task CreateEquipmentAssignsTypeDefinitionAndDeterministicId()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Pump").ConfigureAwait(false);

            Assert.That(equipment, Is.TypeOf<EquipmentState>());
            Assert.That(
                equipment.TypeDefinitionId,
                Is.EqualTo(fixture.Resolve(ObjectTypeIds.EquipmentType)));
            Assert.That(equipment.NodeId, Is.EqualTo(fixture.ExpectedChildId("Pump")));
            Assert.That(equipment.Parent, Is.SameAs(fixture.Root));
            Assert.That(
                ReferenceEquals(
                    fixture.Root.FindChild(fixture.Context, equipment.BrowseName),
                    equipment),
                Is.True);
        }

        [Test]
        public async Task CreateInvokesRegisterCallbackOncePerNode()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            await builder.CreatePersonAsync(fixture.Root, "Alice").ConfigureAwait(false);
            await builder.CreateEquipmentAsync(fixture.Root, "Robot").ConfigureAwait(false);

            Assert.That(fixture.RegisterCount, Is.EqualTo(2));
        }

        [Test]
        public Task CreateAllPrimaryFamiliesProducesExpectedStateTypes()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            NodeState root = fixture.Root;

            Assert.Multiple(async () =>
            {
                Assert.That(
                    await builder.CreatePersonnelClassAsync(root, "PClass").ConfigureAwait(false),
                    Is.TypeOf<PersonnelClassState>());
                Assert.That(
                    await builder.CreatePersonAsync(root, "Person").ConfigureAwait(false),
                    Is.TypeOf<PersonState>());
                Assert.That(
                    await builder.CreateEquipmentClassAsync(root, "EClass").ConfigureAwait(false),
                    Is.TypeOf<EquipmentClassState>());
                Assert.That(
                    await builder.CreateEquipmentAsync(root, "Equip").ConfigureAwait(false),
                    Is.TypeOf<EquipmentState>());
                Assert.That(
                    await builder.CreatePhysicalAssetClassAsync(root, "AClass").ConfigureAwait(false),
                    Is.TypeOf<PhysicalAssetClassState>());
                Assert.That(
                    await builder.CreatePhysicalAssetAsync(root, "Asset").ConfigureAwait(false),
                    Is.TypeOf<PhysicalAssetState>());
                Assert.That(
                    await builder.CreateMaterialClassAsync(root, "MClass").ConfigureAwait(false),
                    Is.TypeOf<MaterialClassState>());
                Assert.That(
                    await builder.CreateMaterialDefinitionAsync(root, "MDef").ConfigureAwait(false),
                    Is.TypeOf<MaterialDefinitionState>());
                Assert.That(
                    await builder.CreateMaterialLotAsync(root, "Lot").ConfigureAwait(false),
                    Is.TypeOf<MaterialLotState>());
                Assert.That(
                    await builder.CreateMaterialSublotAsync(root, "Sublot").ConfigureAwait(false),
                    Is.TypeOf<MaterialSublotState>());
                Assert.That(
                    await builder.CreateEquipmentTestSpecificationAsync(root, "ES").ConfigureAwait(false),
                    Is.TypeOf<EquipmentCapabilityTestSpecificationState>());
                Assert.That(
                    await builder.CreatePhysicalAssetTestSpecificationAsync(root, "AS").ConfigureAwait(false),
                    Is.TypeOf<PhysicalAssetCapabilityTestSpecificationState>());
                Assert.That(
                    await builder.CreateQualificationTestSpecificationAsync(root, "QS").ConfigureAwait(false),
                    Is.TypeOf<QualificationTestSpecificationState>());
                Assert.That(
                    await builder.CreateMaterialTestSpecificationAsync(root, "MS").ConfigureAwait(false),
                    Is.TypeOf<MaterialTestSpecificationState>());
                Assert.That(
                    await builder.CreateTestResultAsync(root, "Result").ConfigureAwait(false),
                    Is.TypeOf<ISA95TestResultState>());
            });
            return Task.CompletedTask;
        }

        [Test]
        public async Task TestResultExposesMandatoryChildren()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            ISA95TestResultState result =
                await builder.CreateTestResultAsync(fixture.Root, "Result").ConfigureAwait(false);

            Assert.That(result.Id, Is.Not.Null);
            Assert.That(result.TestDate, Is.Not.Null);
            Assert.That(result.Result, Is.Not.Null);
            Assert.That(result.ResultDescription, Is.Not.Null);
            Assert.That(result.ResultUnitOfMeasure, Is.Not.Null);
        }

        [Test]
        public async Task TestSpecificationExposesMandatoryVersion()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            EquipmentCapabilityTestSpecificationState specification =
                await builder.CreateEquipmentTestSpecificationAsync(fixture.Root, "Spec").ConfigureAwait(false);

            Assert.That(specification.Version, Is.Not.Null);
        }

        [Test]
        public async Task AddClassPropertyWiresHasIsa95ClassPropertyReference()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentClassState owner =
                await builder.CreateEquipmentClassAsync(fixture.Root, "EClass").ConfigureAwait(false);

            ISA95ClassPropertyState property = await builder.AddClassPropertyAsync(
                owner,
                "MaxSpeed",
                new Variant(42)).ConfigureAwait(false);

            Assert.That(property, Is.TypeOf<EquipmentClassPropertyState>());
            Assert.That(
                property.ReferenceTypeId,
                Is.EqualTo(fixture.Resolve(ReferenceTypeIds.HasISA95ClassProperty)));
            Assert.That(property.Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(
                ReferenceEquals(
                    owner.FindChild(fixture.Context, property.BrowseName),
                    property),
                Is.True);
        }

        [Test]
        public async Task AddPropertyWiresHasIsa95PropertyReference()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentState owner =
                await builder.CreateEquipmentAsync(fixture.Root, "Equip").ConfigureAwait(false);

            ISA95PropertyState property = await builder.AddPropertyAsync(
                owner,
                "SerialNumber",
                new Variant("SN-1")).ConfigureAwait(false);

            Assert.That(property, Is.TypeOf<EquipmentPropertyState>());
            Assert.That(
                property.ReferenceTypeId,
                Is.EqualTo(fixture.Resolve(ReferenceTypeIds.HasISA95Property)));
        }

        [Test]
        public async Task AddClassPropertyCreatesEachConcretePropertyType()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            PersonnelClassState personnel =
                await builder.CreatePersonnelClassAsync(fixture.Root, "Personnel")
                    .ConfigureAwait(false);
            EquipmentClassState equipment =
                await builder.CreateEquipmentClassAsync(fixture.Root, "Equipment")
                    .ConfigureAwait(false);
            PhysicalAssetClassState asset =
                await builder.CreatePhysicalAssetClassAsync(fixture.Root, "Asset")
                    .ConfigureAwait(false);
            MaterialClassState material =
                await builder.CreateMaterialClassAsync(fixture.Root, "Material")
                    .ConfigureAwait(false);

            ISA95ClassPropertyState personnelProperty =
                await builder.AddClassPropertyAsync(personnel, "PersonnelProperty")
                    .ConfigureAwait(false);
            ISA95ClassPropertyState equipmentProperty =
                await builder.AddClassPropertyAsync(equipment, "EquipmentProperty")
                    .ConfigureAwait(false);
            ISA95ClassPropertyState assetProperty =
                await builder.AddClassPropertyAsync(asset, "AssetProperty")
                    .ConfigureAwait(false);
            ISA95ClassPropertyState materialProperty =
                await builder.AddClassPropertyAsync(material, "MaterialProperty")
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    personnelProperty,
                    Is.TypeOf<PersonnelClassPropertyState>());
                Assert.That(
                    equipmentProperty,
                    Is.TypeOf<EquipmentClassPropertyState>());
                Assert.That(
                    assetProperty,
                    Is.TypeOf<PhysicalAssetClassPropertyState>());
                Assert.That(
                    materialProperty,
                    Is.TypeOf<MaterialClassPropertyState>());
            });
        }

        [Test]
        public async Task AddPropertyCreatesEachConcretePropertyType()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            PersonState person =
                await builder.CreatePersonAsync(fixture.Root, "Person")
                    .ConfigureAwait(false);
            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Equipment")
                    .ConfigureAwait(false);
            PhysicalAssetState asset =
                await builder.CreatePhysicalAssetAsync(fixture.Root, "Asset")
                    .ConfigureAwait(false);
            MaterialDefinitionState definition =
                await builder.CreateMaterialDefinitionAsync(fixture.Root, "Definition")
                    .ConfigureAwait(false);
            MaterialLotState lot =
                await builder.CreateMaterialLotAsync(fixture.Root, "Lot")
                    .ConfigureAwait(false);
            MaterialSublotState sublot =
                await builder.CreateMaterialSublotAsync(fixture.Root, "Sublot")
                    .ConfigureAwait(false);

            PersonPropertyState personProperty =
                await builder.AddPropertyAsync(person, "PersonProperty")
                    .ConfigureAwait(false);
            EquipmentPropertyState equipmentProperty =
                await builder.AddPropertyAsync(equipment, "EquipmentProperty")
                    .ConfigureAwait(false);
            PhysicalAssetPropertyState assetProperty =
                await builder.AddPropertyAsync(asset, "AssetProperty")
                    .ConfigureAwait(false);
            MaterialDefinitionPropertyState definitionProperty =
                await builder.AddPropertyAsync(definition, "DefinitionProperty")
                    .ConfigureAwait(false);
            MaterialLotPropertyState lotProperty =
                await builder.AddPropertyAsync(lot, "LotProperty")
                    .ConfigureAwait(false);
            MaterialLotPropertyState sublotProperty =
                await builder.AddPropertyAsync(sublot, "SublotProperty")
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(personProperty, Is.TypeOf<PersonPropertyState>());
                Assert.That(equipmentProperty, Is.TypeOf<EquipmentPropertyState>());
                Assert.That(assetProperty, Is.TypeOf<PhysicalAssetPropertyState>());
                Assert.That(
                    definitionProperty,
                    Is.TypeOf<MaterialDefinitionPropertyState>());
                Assert.That(lotProperty, Is.TypeOf<MaterialLotPropertyState>());
                Assert.That(sublotProperty, Is.TypeOf<MaterialLotPropertyState>());
            });
        }

        [Test]
        public async Task DefinedByEquipmentClassAddsForwardAndInverseReferences()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Equip").ConfigureAwait(false);
            EquipmentClassState equipmentClass =
                await builder.CreateEquipmentClassAsync(fixture.Root, "EClass").ConfigureAwait(false);

            builder.DefinedByEquipmentClass(equipment, equipmentClass);

            NodeId referenceType = fixture.Resolve(ReferenceTypeIds.DefinedByEquipmentClass);
            Assert.That(
                equipment.ReferenceExists(referenceType, false, equipmentClass.NodeId),
                Is.True);
            Assert.That(
                equipmentClass.ReferenceExists(referenceType, true, equipment.NodeId),
                Is.True);
        }

        [Test]
        public async Task MadeUpOfMaterialSublotUsesGeneratedReferenceType()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            MaterialLotState lot = await builder.CreateMaterialLotAsync(fixture.Root, "Lot").ConfigureAwait(false);
            MaterialSublotState sublot =
                await builder.CreateMaterialSublotAsync(fixture.Root, "Sublot").ConfigureAwait(false);

            builder.MadeUpOfMaterialSublot(lot, sublot);

            NodeId referenceType = fixture.Resolve(ReferenceTypeIds.MadeUpOfMaterialSublot);
            Assert.That(lot.ReferenceExists(referenceType, false, sublot.NodeId), Is.True);
            Assert.That(sublot.ReferenceExists(referenceType, true, lot.NodeId), Is.True);
        }

        [Test]
        public async Task AssembledFromReferencesUseMaterialSources()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            NodeState root = fixture.Root;
            MaterialClassState materialClass =
                await builder.CreateMaterialClassAsync(root, "MClass").ConfigureAwait(false);
            MaterialClassState componentClass =
                await builder.CreateMaterialClassAsync(root, "MClass2").ConfigureAwait(false);
            MaterialDefinitionState definition =
                await builder.CreateMaterialDefinitionAsync(root, "MDef").ConfigureAwait(false);
            MaterialDefinitionState componentDefinition =
                await builder.CreateMaterialDefinitionAsync(root, "MDef2").ConfigureAwait(false);
            MaterialLotState lot = await builder.CreateMaterialLotAsync(root, "Lot").ConfigureAwait(false);
            MaterialLotState componentLot =
                await builder.CreateMaterialLotAsync(root, "Lot2").ConfigureAwait(false);
            MaterialSublotState sublot =
                await builder.CreateMaterialSublotAsync(root, "Sublot").ConfigureAwait(false);
            MaterialSublotState componentSublot =
                await builder.CreateMaterialSublotAsync(root, "Sublot2").ConfigureAwait(false);

            builder.AssembledFromClass(materialClass, componentClass);
            builder.AssembledFromDefinition(definition, componentDefinition);
            builder.AssembledFromLot(lot, componentLot);
            builder.AssembledFromLot(lot, componentSublot);
            builder.AssembledFromSublot(sublot, componentLot);
            builder.AssembledFromSublot(sublot, componentSublot);

            NodeId fromClass = fixture.Resolve(ReferenceTypeIds.AssembledFromClass);
            NodeId fromDefinition = fixture.Resolve(ReferenceTypeIds.AssembledFromDefinition);
            NodeId fromLot = fixture.Resolve(ReferenceTypeIds.AssembledFromLot);
            NodeId fromSublot = fixture.Resolve(ReferenceTypeIds.AssembledFromSublot);
            Assert.Multiple(() =>
            {
                Assert.That(
                    materialClass.ReferenceExists(
                        fromClass,
                        false,
                        componentClass.NodeId),
                    Is.True);
                Assert.That(
                    componentClass.ReferenceExists(
                        fromClass,
                        true,
                        materialClass.NodeId),
                    Is.True);
                Assert.That(
                    definition.ReferenceExists(
                        fromDefinition,
                        false,
                        componentDefinition.NodeId),
                    Is.True);
                Assert.That(
                    lot.ReferenceExists(fromLot, false, componentLot.NodeId),
                    Is.True);
                Assert.That(
                    componentLot.ReferenceExists(fromLot, true, lot.NodeId),
                    Is.True);
                Assert.That(
                    lot.ReferenceExists(fromLot, false, componentSublot.NodeId),
                    Is.True);
                Assert.That(
                    sublot.ReferenceExists(fromSublot, false, componentLot.NodeId),
                    Is.True);
                Assert.That(
                    sublot.ReferenceExists(
                        fromSublot,
                        false,
                        componentSublot.NodeId),
                    Is.True);
            });
        }

        [Test]
        public async Task AssembledFromClassAndDefinitionSupportPropertyEndpoints()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            MaterialClassState materialClass =
                await builder.CreateMaterialClassAsync(fixture.Root, "Class")
                    .ConfigureAwait(false);
            MaterialClassState componentClass =
                await builder.CreateMaterialClassAsync(fixture.Root, "ComponentClass")
                    .ConfigureAwait(false);
            var classProperty =
                (MaterialClassPropertyState)await builder.AddClassPropertyAsync(
                    materialClass,
                    "ClassProperty").ConfigureAwait(false);
            var componentClassProperty =
                (MaterialClassPropertyState)await builder.AddClassPropertyAsync(
                    componentClass,
                    "ComponentClassProperty").ConfigureAwait(false);
            MaterialDefinitionState definition =
                await builder.CreateMaterialDefinitionAsync(fixture.Root, "Definition")
                    .ConfigureAwait(false);
            MaterialDefinitionState componentDefinition =
                await builder.CreateMaterialDefinitionAsync(
                    fixture.Root,
                    "ComponentDefinition").ConfigureAwait(false);
            MaterialDefinitionPropertyState definitionProperty =
                await builder.AddPropertyAsync(
                    definition,
                    "DefinitionProperty").ConfigureAwait(false);
            MaterialDefinitionPropertyState componentDefinitionProperty =
                await builder.AddPropertyAsync(
                    componentDefinition,
                    "ComponentDefinitionProperty").ConfigureAwait(false);

            builder.AssembledFromClass(materialClass, componentClassProperty);
            builder.AssembledFromClass(classProperty, componentClass);
            builder.AssembledFromClass(classProperty, componentClassProperty);
            builder.AssembledFromDefinition(
                definition,
                componentDefinitionProperty);
            builder.AssembledFromDefinition(
                definitionProperty,
                componentDefinition);
            builder.AssembledFromDefinition(
                definitionProperty,
                componentDefinitionProperty);

            NodeId classReference =
                fixture.Resolve(ReferenceTypeIds.AssembledFromClass);
            NodeId definitionReference =
                fixture.Resolve(ReferenceTypeIds.AssembledFromDefinition);
            Assert.Multiple(() =>
            {
                Assert.That(
                    materialClass.ReferenceExists(
                        classReference,
                        false,
                        componentClassProperty.NodeId),
                    Is.True);
                Assert.That(
                    classProperty.ReferenceExists(
                        classReference,
                        false,
                        componentClass.NodeId),
                    Is.True);
                Assert.That(
                    classProperty.ReferenceExists(
                        classReference,
                        false,
                        componentClassProperty.NodeId),
                    Is.True);
                Assert.That(
                    definition.ReferenceExists(
                        definitionReference,
                        false,
                        componentDefinitionProperty.NodeId),
                    Is.True);
                Assert.That(
                    definitionProperty.ReferenceExists(
                        definitionReference,
                        false,
                        componentDefinition.NodeId),
                    Is.True);
                Assert.That(
                    definitionProperty.ReferenceExists(
                        definitionReference,
                        false,
                        componentDefinitionProperty.NodeId),
                    Is.True);
            });
        }

        [Test]
        public async Task LocatedInAndImplementedByUseNormativeEndpoints()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Equipment")
                    .ConfigureAwait(false);
            PhysicalAssetState asset =
                await builder.CreatePhysicalAssetAsync(fixture.Root, "Asset")
                    .ConfigureAwait(false);
            PhysicalAssetPropertyState locationProperty =
                await builder.AddPropertyAsync(asset, "Location")
                    .ConfigureAwait(false);
            Isa95GeoSpatialLocationBinding location =
                await builder.CreateGeoSpatialLocationAsync(
                    fixture.Root,
                    "GeoLocation").ConfigureAwait(false);

            builder.LocatedIn(locationProperty, location.State);
            builder.ImplementedBy(equipment, asset);
            builder.ImplementedBy(asset, equipment);

            Assert.Multiple(() =>
            {
                Assert.That(
                    locationProperty.ReferenceExists(
                        fixture.Resolve(ReferenceTypeIds.LocatedIn),
                        false,
                        location.State.NodeId),
                    Is.True);
                Assert.That(
                    equipment.ReferenceExists(
                        fixture.Resolve(ReferenceTypeIds.ImplementedBy),
                        false,
                        asset.NodeId),
                    Is.True);
                Assert.That(
                    asset.ReferenceExists(
                        fixture.Resolve(ReferenceTypeIds.ImplementedBy),
                        false,
                        equipment.NodeId),
                    Is.True);
            });
            location.Dispose();
        }

        [Test]
        public async Task HasTestResultAndResultsForSpecificationAreWired()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Equip").ConfigureAwait(false);
            ISA95PropertyState property = await builder.AddPropertyAsync(
                equipment,
                "Inspection",
                new Variant("Passed")).ConfigureAwait(false);
            ISA95TestResultState result =
                await builder.CreateTestResultAsync(fixture.Root, "Result").ConfigureAwait(false);
            EquipmentCapabilityTestSpecificationState specification =
                await builder.CreateEquipmentTestSpecificationAsync(fixture.Root, "Spec").ConfigureAwait(false);

            builder.HasTestResult(property, result);
            builder.ResultsForSpecification(result, specification);

            NodeId hasTestResult = fixture.Resolve(ReferenceTypeIds.HasTestResult);
            NodeId resultsFor = fixture.Resolve(ReferenceTypeIds.ResultsForSpecification);
            Assert.That(
                property.ReferenceExists(hasTestResult, false, result.NodeId),
                Is.True);
            Assert.That(
                result.ReferenceExists(resultsFor, false, specification.NodeId),
                Is.True);
        }

        [Test]
        public async Task RelateAllowsCustomReferenceTypeWithInverse()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentState a = await builder.CreateEquipmentAsync(fixture.Root, "A").ConfigureAwait(false);
            EquipmentState b = await builder.CreateEquipmentAsync(fixture.Root, "B").ConfigureAwait(false);
            NodeId referenceType = fixture.Resolve(ReferenceTypeIds.MadeUpOfEquipment);

            builder.Relate(a, referenceType, b);

            Assert.That(a.ReferenceExists(referenceType, false, b.NodeId), Is.True);
            Assert.That(b.ReferenceExists(referenceType, true, a.NodeId), Is.True);
        }

        [Test]
        public async Task DuplicateRelationshipThrows()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Equip").ConfigureAwait(false);
            EquipmentClassState equipmentClass =
                await builder.CreateEquipmentClassAsync(fixture.Root, "EClass").ConfigureAwait(false);
            builder.DefinedByEquipmentClass(equipment, equipmentClass);

            Assert.That(
                () => builder.DefinedByEquipmentClass(equipment, equipmentClass),
                Throws.InvalidOperationException);
        }

        [Test]
        public void RelationshipWithNullArgumentThrows()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            Assert.That(
                () => builder.DefinedByEquipmentClass(null!, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void CreateWithParentMissingNodeIdThrows()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            var orphan = new FolderState(null)
            {
                BrowseName = new QualifiedName("Orphan", fixture.InstanceNamespaceIndex)
            };

            Assert.That(
                async () => await builder.CreateEquipmentAsync(orphan, "Equip").ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public void CreateWithNullParentThrows()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            Assert.That(
                async () => await builder.CreateEquipmentAsync(null!, "Equip").ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task RemoveInvokesRemoveCallback()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder(withRemove: true);
            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Equip").ConfigureAwait(false);

            await builder.RemoveAsync(equipment).ConfigureAwait(false);

            Assert.That(fixture.RemoveCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RemoveWithoutCallbackThrows()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            EquipmentState equipment =
                await builder.CreateEquipmentAsync(fixture.Root, "Equip").ConfigureAwait(false);

            Assert.That(
                async () => await builder.RemoveAsync(equipment).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task ChildNodeIdsAreDeterministicAcrossBuilders()
        {
            var first = new Isa95CommonTestContext();
            EquipmentState equipmentA =
                await first.CreateBuilder().CreateEquipmentAsync(first.Root, "Equip").ConfigureAwait(false);
            var second = new Isa95CommonTestContext();
            EquipmentState equipmentB =
                await second.CreateBuilder().CreateEquipmentAsync(second.Root, "Equip").ConfigureAwait(false);

            Assert.That(equipmentA.NodeId, Is.EqualTo(equipmentB.NodeId));
            Assert.That(equipmentA.NodeId, Is.EqualTo(first.ExpectedChildId("Equip")));
        }
    }
}
