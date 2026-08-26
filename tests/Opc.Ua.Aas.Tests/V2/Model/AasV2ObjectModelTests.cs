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

using NUnit.Framework;
using Opc.Ua.Aas.V2;

namespace Opc.Ua.Aas.Tests.V2.Model
{
    /// <summary>
    /// Tests the hand-written AAS V2 object model used before materialization.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasV2ObjectModelTests
    {
        [Test]
        public void ConstructionRetainsV2TopLevelIdentifiablesAndReferences()
        {
            var reference = CreateReference(AASKeyElementsDataType.Asset, "asset");
            var asset = CreateAsset(AASIdentifierTypeDataType.IRDI);
            var property = CreateProperty("temperature");
            var submodel = new AasSubmodel
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.IRI),
                Administration = CreateAdministration(),
                IdShort = "Process",
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance,
                SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new AasSubmodelElement[] { property })
            };
            var shell = new AasShell
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.Custom),
                Administration = CreateAdministration(),
                IdShort = "Shell",
                Category = "VARIABLE",
                Asset = asset,
                SubmodelReferences = AasOptional<ArrayOf<AasReference>>.Present(
                    new AasReference[] { reference })
            };
            var environment = new AasEnvironment
            {
                AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(new AasShell[] { shell }),
                Assets = AasOptional<ArrayOf<AasAsset>>.Present(new AasAsset[] { asset }),
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new AasSubmodel[] { submodel })
            };

            Assert.Multiple(() =>
            {
                Assert.That(environment.AssetAdministrationShells.Value[0].Asset, Is.SameAs(asset));
                Assert.That(environment.Assets.Value[0].Identification.IdType, Is.EqualTo(AASIdentifierTypeDataType.IRDI));
                Assert.That(environment.Submodels.Value[0].SubmodelElements.Value[0], Is.SameAs(property));
                Assert.That(shell.SubmodelReferences.Value[0].Keys[0].Type, Is.EqualTo(AASKeyElementsDataType.Asset));
                Assert.That(shell.ModelType, Is.EqualTo("AssetAdministrationShell"));
            });
        }

        [Test]
        public void OptionalCollectionsDistinguishAbsentFromPresentEmptyAcrossV2Containers()
        {
            var absentShell = CreateShellWithAsset();
            var presentShell = CreateShellWithAsset() with
            {
                ConceptDictionaries = AasOptional<ArrayOf<AasConceptDictionary>>.Present(
                    ArrayOf<AasConceptDictionary>.Empty),
                DataSpecifications = AasOptional<ArrayOf<AasReference>>.Present(ArrayOf<AasReference>.Empty),
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(ArrayOf<AasSubmodel>.Empty),
                SubmodelReferences = AasOptional<ArrayOf<AasReference>>.Present(ArrayOf<AasReference>.Empty),
                Views = AasOptional<ArrayOf<AasView>>.Present(ArrayOf<AasView>.Empty)
            };
            var absentSubmodel = CreateSubmodel();
            var presentSubmodel = CreateSubmodel() with
            {
                DataSpecifications = AasOptional<ArrayOf<AasReference>>.Present(ArrayOf<AasReference>.Empty),
                Qualifiers = AasOptional<ArrayOf<AasQualifier>>.Present(ArrayOf<AasQualifier>.Empty),
                SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    ArrayOf<AasSubmodelElement>.Empty)
            };

            Assert.Multiple(() =>
            {
                Assert.That(absentShell.Views.IsPresent, Is.False);
                Assert.That(presentShell.Views.IsPresent, Is.True);
                Assert.That(presentShell.Views.Value.Count, Is.Zero);
                Assert.That(presentShell.ConceptDictionaries.Value.Count, Is.Zero);
                Assert.That(absentSubmodel.SubmodelElements.IsPresent, Is.False);
                Assert.That(presentSubmodel.SubmodelElements.IsPresent, Is.True);
                Assert.That(presentSubmodel.SubmodelElements.Value.Count, Is.Zero);
                Assert.That(presentSubmodel.Qualifiers.Value.Count, Is.Zero);
            });
        }

        [Test]
        public void OptionalCollectionsDistinguishAbsentFromPresentEmptyAcrossV2SpecialTypes()
        {
            var reference = CreateReference(AASKeyElementsDataType.ConceptDescription, "concept");
            var presentConceptDictionary = new AasConceptDictionary
            {
                ConceptDescriptions = AasOptional<ArrayOf<AasReference>>.Present(ArrayOf<AasReference>.Empty)
            };
            var presentView = new AasView
            {
                DataSpecifications = AasOptional<ArrayOf<AasReference>>.Present(ArrayOf<AasReference>.Empty),
                Referables = AasOptional<ArrayOf<AasReference>>.Present(ArrayOf<AasReference>.Empty)
            };
            var presentConcept = CreateIriConceptDescription() with
            {
                ConceptDescriptions = AasOptional<ArrayOf<AasReference>>.Present(new AasReference[] { reference }),
                DataSpecifications = AasOptional<ArrayOf<AasReference>>.Present(ArrayOf<AasReference>.Empty)
            };
            var presentEnvironment = new AasEnvironment
            {
                CustomConceptDescriptions = AasOptional<ArrayOf<AasCustomConceptDescription>>.Present(
                    ArrayOf<AasCustomConceptDescription>.Empty),
                IrdiConceptDescriptions = AasOptional<ArrayOf<AasIrdiConceptDescription>>.Present(
                    ArrayOf<AasIrdiConceptDescription>.Empty),
                IriConceptDescriptions = AasOptional<ArrayOf<AasIriConceptDescription>>.Present(
                    new AasIriConceptDescription[] { presentConcept }),
                DataSpecifications = AasOptional<ArrayOf<AasDataSpecification>>.Present(
                    ArrayOf<AasDataSpecification>.Empty)
            };

            Assert.Multiple(() =>
            {
                Assert.That(new AasConceptDictionary().ConceptDescriptions.IsPresent, Is.False);
                Assert.That(presentConceptDictionary.ConceptDescriptions.IsPresent, Is.True);
                Assert.That(presentConceptDictionary.ConceptDescriptions.Value.Count, Is.Zero);
                Assert.That(presentView.Referables.IsPresent, Is.True);
                Assert.That(presentView.Referables.Value.Count, Is.Zero);
                Assert.That(presentConcept.ConceptDescriptions.Value[0], Is.SameAs(reference));
                Assert.That(presentEnvironment.CustomConceptDescriptions.Value.Count, Is.Zero);
                Assert.That(presentEnvironment.IriConceptDescriptions.Value[0], Is.SameAs(presentConcept));
                Assert.That(presentEnvironment.DataSpecifications.Value.Count, Is.Zero);
            });
        }

        [Test]
        public void V2OnlyConceptsRoundTripThroughModel()
        {
            var view = new AasView
            {
                Referables = AasOptional<ArrayOf<AasReference>>.Present(
                    new AasReference[] { CreateReference(AASKeyElementsDataType.Submodel, "submodel") })
            };
            var ordered = new AasOrderedSubmodelElementCollection
            {
                IdShort = "ordered",
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Template,
                AllowDuplicates = AasOptional<bool>.Present(true),
                SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new AasSubmodelElement[] { CreateProperty("first") })
            };
            var shell = CreateShellWithAsset() with
            {
                Views = AasOptional<ArrayOf<AasView>>.Present(new AasView[] { view })
            };

            Assert.Multiple(() =>
            {
                Assert.That(shell.Asset.ModelType, Is.EqualTo("Asset"));
                Assert.That(shell.Asset.Identification.IdType, Is.EqualTo(AASIdentifierTypeDataType.IRDI));
                Assert.That(shell.Views.Value[0].Referables.Value[0].Keys[0].Type,
                    Is.EqualTo(AASKeyElementsDataType.Submodel));
                Assert.That(ordered.AllowDuplicates.IsPresent, Is.True);
                Assert.That(ordered.AllowDuplicates.Value, Is.True);
                Assert.That(ordered.SubmodelElements.Value[0].IdShort, Is.EqualTo("first"));
                Assert.That(ordered.ModelType, Is.EqualTo("OrderedSubmodelElementCollection"));
            });
        }

        [Test]
        public void ConceptDescriptionFlavoursRetainIdentifierKinds()
        {
            var custom = CreateCustomConceptDescription();
            var irdi = CreateIrdiConceptDescription();
            var iri = CreateIriConceptDescription();

            Assert.Multiple(() =>
            {
                Assert.That(custom.Identification.IdType, Is.EqualTo(AASIdentifierTypeDataType.Custom));
                Assert.That(irdi.Identification.IdType, Is.EqualTo(AASIdentifierTypeDataType.IRDI));
                Assert.That(iri.Identification.IdType, Is.EqualTo(AASIdentifierTypeDataType.IRI));
                Assert.That(custom.ModelType, Is.EqualTo("CustomConceptDescription"));
                Assert.That(irdi.ModelType, Is.EqualTo("IrdiConceptDescription"));
                Assert.That(iri.ModelType, Is.EqualTo("IriConceptDescription"));
            });
        }

        [Test]
        public void SubmodelElementMembersRetainNodeSetTypes()
        {
            var reference = CreateReference(AASKeyElementsDataType.Property, "property");
            var bytes = ByteString.From(new byte[] { 1, 2, 3 });
            var blob = new AasBlob
            {
                IdShort = "blob",
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance,
                File = AasOptional<AasFileObject>.Present(new AasFileObject
                {
                    Value = AasOptional<ByteString>.Present(bytes)
                })
            };
            var entity = new AasEntity
            {
                IdShort = "entity",
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance,
                Asset = AasOptional<AasReference>.Present(reference),
                EntityType = AASEntityTypeDataType.SelfManagedEntity,
                Statements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new AasSubmodelElement[] { CreateProperty("statement") })
            };
            var property = CreateProperty("value") with
            {
                Value = AasOptional<Variant>.Present(new Variant(42)),
                ValueId = AasOptional<AasReference>.Present(reference)
            };
            var multiLanguage = new AasMultiLanguageProperty
            {
                IdShort = "mlp",
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance,
                Value = AasOptional<ArrayOf<LocalizedText>>.Present(
                    new LocalizedText[] { new LocalizedText("en", "value") }),
                ValueId = AasOptional<AasReference>.Present(reference)
            };

            Assert.Multiple(() =>
            {
                Assert.That(blob.File.Value.Value.Value.Length, Is.EqualTo(3));
                Assert.That(entity.Asset.Value, Is.SameAs(reference));
                Assert.That(entity.Statements.Value[0].ModelType, Is.EqualTo("Property"));
                Assert.That(property.ValueType, Is.EqualTo(AASValueTypeDataType.Int32));
                Assert.That(property.ValueId.Value, Is.SameAs(reference));
                Assert.That(multiLanguage.Value.Value[0].Text, Is.EqualTo("value"));
            });
        }

        [Test]
        public void DataSpecificationIec61360RetainsMandatoryAndOptionalMembers()
        {
            var reference = CreateReference(AASKeyElementsDataType.GlobalReference, "unit");
            var specification = new AasDataSpecificationIec61360
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.IRI),
                Administration = CreateAdministration(),
                IdShort = "specification",
                Category = "CONSTANT",
                DataSpecificationAdministration = CreateAdministration(),
                DataSpecificationCategory = AasOptional<AASCategoryDataType>.Present(AASCategoryDataType.CONSTANT),
                DataType = AasOptional<AASDataTypeIEC61360DataType>.Present(AASDataTypeIEC61360DataType.REAL_MEASURE),
                DefaultInstanceBrowseName = "DefaultName",
                Definition = AasOptional<ArrayOf<LocalizedText>>.Present(
                    new LocalizedText[] { new LocalizedText("en", "definition") }),
                DataSpecificationIdentification = CreateIdentifier(AASIdentifierTypeDataType.IRDI),
                LevelType = AasOptional<ArrayOf<AASLevelTypeDataType>>.Present(
                    new AASLevelTypeDataType[] { AASLevelTypeDataType.Min, AASLevelTypeDataType.Max }),
                PreferredName = new LocalizedText[] { new LocalizedText("en", "preferred") },
                ShortName = AasOptional<ArrayOf<LocalizedText>>.Present(ArrayOf<LocalizedText>.Empty),
                SourceOfDefinition = AasOptional<string>.Present("source"),
                Symbol = AasOptional<string>.Present("°C"),
                Unit = AasOptional<string>.Present("degree Celsius"),
                UnitId = AasOptional<AasReference>.Present(reference),
                Value = AasOptional<Variant>.Present(new Variant(1.5d)),
                ValueFormat = AasOptional<string>.Present("float"),
                ValueId = AasOptional<AasReference>.Present(reference),
                ValueList = AasOptional<AasReference>.Present(reference)
            };

            Assert.Multiple(() =>
            {
                Assert.That(specification.ModelType, Is.EqualTo("DataSpecificationIEC61360"));
                Assert.That(specification.DataSpecificationCategory.Value, Is.EqualTo(AASCategoryDataType.CONSTANT));
                Assert.That(specification.DataType.Value, Is.EqualTo(AASDataTypeIEC61360DataType.REAL_MEASURE));
                Assert.That(specification.Definition.Value[0].Text, Is.EqualTo("definition"));
                Assert.That(specification.LevelType.Value.Count, Is.EqualTo(2));
                Assert.That(specification.ShortName.Value.Count, Is.Zero);
                Assert.That(specification.UnitId.Value, Is.SameAs(reference));
            });
        }

        private static AasShell CreateShellWithAsset()
        {
            return new AasShell
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.Custom),
                Administration = CreateAdministration(),
                IdShort = "shell",
                Category = "VARIABLE",
                Asset = CreateAsset(AASIdentifierTypeDataType.IRDI)
            };
        }

        private static AasAsset CreateAsset(AASIdentifierTypeDataType idType)
        {
            return new AasAsset
            {
                Identification = CreateIdentifier(idType),
                Administration = CreateAdministration(),
                IdShort = "asset",
                Category = "VARIABLE",
                AssetKind = AASAssetKindDataType.Instance
            };
        }

        private static AasSubmodel CreateSubmodel()
        {
            return new AasSubmodel
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.IRI),
                Administration = CreateAdministration(),
                IdShort = "submodel",
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance
            };
        }

        private static AasProperty CreateProperty(string idShort)
        {
            return new AasProperty
            {
                IdShort = idShort,
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance,
                ValueType = AASValueTypeDataType.Int32
            };
        }

        private static AasCustomConceptDescription CreateCustomConceptDescription()
        {
            return new AasCustomConceptDescription
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.Custom),
                Administration = CreateAdministration(),
                IdShort = "custom",
                Category = "CONSTANT"
            };
        }

        private static AasIrdiConceptDescription CreateIrdiConceptDescription()
        {
            return new AasIrdiConceptDescription
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.IRDI),
                Administration = CreateAdministration(),
                IdShort = "irdi",
                Category = "CONSTANT"
            };
        }

        private static AasIriConceptDescription CreateIriConceptDescription()
        {
            return new AasIriConceptDescription
            {
                Identification = CreateIdentifier(AASIdentifierTypeDataType.IRI),
                Administration = CreateAdministration(),
                IdShort = "iri",
                Category = "CONSTANT"
            };
        }

        private static AasAdministrativeInformation CreateAdministration()
        {
            return new AasAdministrativeInformation
            {
                Revision = "1",
                Version = "2"
            };
        }

        private static AasIdentifier CreateIdentifier(AASIdentifierTypeDataType idType)
        {
            return new AasIdentifier
            {
                Id = "https://example.test/" + idType,
                IdType = idType
            };
        }

        private static AasReference CreateReference(AASKeyElementsDataType type, string value)
        {
            return new AasReference
            {
                Keys = new AASKeyDataType[]
                {
                    new AASKeyDataType
                    {
                        Type = type,
                        Local = true,
                        Value = value,
                        IdType = AASKeyTypeDataType.IdShort
                    }
                }
            };
        }
    }
}
