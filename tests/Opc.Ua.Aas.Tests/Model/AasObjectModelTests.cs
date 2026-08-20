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
using Opc.Ua.Aas.V3;

namespace Opc.Ua.Aas.Tests.Model
{
    /// <summary>
    /// Tests the hand-written AAS V3 object model used before materializing an
    /// Environment into OPC UA nodes.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasObjectModelTests
    {
        [Test]
        public void ConstructionRetainsTopLevelIdentifiablesAndElements()
        {
            var property = new AasProperty
            {
                IdShort = AasOptional<string>.Present("temperature"),
                ValueType = AASDataTypeDefXsdDataType.Double,
                Value = AasOptional<Variant>.Present(new Variant(42.5d))
            };
            var submodel = new AasSubmodel
            {
                Id = "https://example.test/submodel/1",
                IdShort = AasOptional<string>.Present("Process"),
                SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new AasSubmodelElement[] { property })
            };
            var shell = new AasShell
            {
                Id = "https://example.test/shell/1",
                AssetInformation = new AasAssetInformation
                {
                    AssetKind = AASAssetKindDataType.Instance
                }
            };
            var environment = new AasEnvironment
            {
                AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(
                    new AasShell[] { shell }),
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(
                    new AasSubmodel[] { submodel })
            };

            Assert.Multiple(() =>
            {
                Assert.That(environment.AssetAdministrationShells.Value.Count, Is.EqualTo(1));
                Assert.That(environment.Submodels.Value[0].SubmodelElements.Value[0], Is.SameAs(property));
                Assert.That(property.ModelType, Is.EqualTo("Property"));
                Assert.That(shell.ModelType, Is.EqualTo("AssetAdministrationShell"));
            });
        }

        [Test]
        public void OptionalCollectionDistinguishesAbsentFromPresentEmpty()
        {
            var absent = new AasSubmodel
            {
                Id = "https://example.test/submodel/absent"
            };
            var presentEmpty = new AasSubmodel
            {
                Id = "https://example.test/submodel/empty",
                SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    ArrayOf<AasSubmodelElement>.Empty),
                Qualifiers = AasOptional<ArrayOf<AASQualifierDataType>>.Present(
                    ArrayOf<AASQualifierDataType>.Empty)
            };

            Assert.Multiple(() =>
            {
                Assert.That(absent.SubmodelElements.IsPresent, Is.False);
                Assert.That(presentEmpty.SubmodelElements.IsPresent, Is.True);
                Assert.That(presentEmpty.SubmodelElements.Value.Count, Is.Zero);
                Assert.That(presentEmpty.Qualifiers.IsPresent, Is.True);
                Assert.That(presentEmpty.Qualifiers.Value.Count, Is.Zero);
            });
        }

        [Test]
        public void OptionalObjectDistinguishesAbsentFromPresentEmptyObject()
        {
            var shell = new AasShell
            {
                Id = "https://example.test/shell/object",
                AssetInformation = new AasAssetInformation
                {
                    AssetKind = AASAssetKindDataType.Instance,
                    DefaultThumbnail = AasOptional<AASResourceDataType>.Present(Generated<AASResourceDataType>())
                }
            };

            Assert.Multiple(() =>
            {
                Assert.That(shell.DerivedFrom.IsPresent, Is.False);
                Assert.That(shell.AssetInformation.DefaultThumbnail.IsPresent, Is.True);
            });
        }

        [Test]
        public void OrderRelevantDefaultsToTrueWhenAbsent()
        {
            var absent = new AasSubmodelElementList
            {
                TypeValueListElement = AASSubmodelElementsDataType.Property
            };
            var explicitFalse = new AasSubmodelElementList
            {
                TypeValueListElement = AASSubmodelElementsDataType.Property,
                OrderRelevant = AasOptional<bool>.Present(false)
            };

            Assert.Multiple(() =>
            {
                Assert.That(absent.EffectiveOrderRelevant, Is.True);
                Assert.That(explicitFalse.EffectiveOrderRelevant, Is.False);
            });
        }

        [Test]
        public void ListMemberCarriesIndexWithoutShortName()
        {
            var member = new AasProperty
            {
                Index = AasOptional<uint>.Present(2u),
                ValueType = AASDataTypeDefXsdDataType.String
            };

            Assert.Multiple(() =>
            {
                Assert.That(member.IdShort.IsPresent, Is.False);
                Assert.That(member.Index.Value, Is.EqualTo(2u));
            });
        }

        [Test]
        public void OperationRolesRetainRoleAndPosition()
        {
            var input = new AasProperty
            {
                Index = AasOptional<uint>.Present(0u),
                IdShort = AasOptional<string>.Present("workpiece"),
                ValueType = AASDataTypeDefXsdDataType.String
            };
            var output = new AasProperty
            {
                Index = AasOptional<uint>.Present(0u),
                IdShort = AasOptional<string>.Present("accepted"),
                ValueType = AASDataTypeDefXsdDataType.Boolean
            };
            var operation = new AasOperation
            {
                IdShort = AasOptional<string>.Present("Inspect"),
                InputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new AasSubmodelElement[] { input }),
                OutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new AasSubmodelElement[] { output }),
                InoutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    ArrayOf<AasSubmodelElement>.Empty)
            };

            Assert.Multiple(() =>
            {
                Assert.That(operation.InputVariables.Value[0], Is.SameAs(input));
                Assert.That(operation.OutputVariables.Value[0], Is.SameAs(output));
                Assert.That(operation.InoutputVariables.Value.Count, Is.Zero);
                Assert.That(operation.InputVariables.Value[0].Index.Value, Is.Zero);
                Assert.That(operation.OutputVariables.Value[0].Index.Value, Is.Zero);
            });
        }

        [Test]
        public void AnnexBFieldsAreRepresentable()
        {
            AASReferenceDataType reference = Generated<AASReferenceDataType>();
            var element = new AasAnnotatedRelationshipElement
            {
                IdShort = AasOptional<string>.Present("relationship"),
                Category = AasOptional<string>.Present("PARAMETER"),
                DisplayName = AasOptional<ArrayOf<AASLangStringDataType>>.Present(
                    ArrayOf<AASLangStringDataType>.Empty),
                Description = AasOptional<ArrayOf<AASLangStringDataType>>.Present(
                    ArrayOf<AASLangStringDataType>.Empty),
                Extensions = AasOptional<ArrayOf<AASExtensionDataType>>.Present(
                    ArrayOf<AASExtensionDataType>.Empty),
                SemanticId = AasOptional<AASReferenceDataType>.Present(reference),
                SupplementalSemanticIds = AasOptional<ArrayOf<AASReferenceDataType>>.Present(
                    ArrayOf<AASReferenceDataType>.Empty),
                Qualifiers = AasOptional<ArrayOf<AASQualifierDataType>>.Present(
                    ArrayOf<AASQualifierDataType>.Empty),
                EmbeddedDataSpecifications =
                    AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>>.Present(
                        ArrayOf<AASEmbeddedDataSpecificationDataType>.Empty),
                First = reference,
                Second = reference,
                Annotations = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new AasSubmodelElement[] { new AasCapability() })
            };
            var shell = CreateCompleteShell(reference);
            var submodel = CreateCompleteSubmodel(element);
            var concept = new AasConceptDescription
            {
                Id = "concept",
                Administration = AasOptional<AASAdministrativeInformationDataType>.Present(
                    Generated<AASAdministrativeInformationDataType>()),
                IsCaseOf = AasOptional<ArrayOf<AASReferenceDataType>>.Present(
                    ArrayOf<AASReferenceDataType>.Empty),
                EmbeddedDataSpecifications =
                    AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>>.Present(
                        ArrayOf<AASEmbeddedDataSpecificationDataType>.Empty)
            };

            Assert.Multiple(() =>
            {
                Assert.That(shell.AssetInformation.SpecificAssetIds.IsPresent, Is.True);
                Assert.That(submodel.SubmodelElements.Value[0], Is.SameAs(element));
                Assert.That(concept.IsCaseOf.IsPresent, Is.True);
                Assert.That(element.Annotations.Value[0].ModelType, Is.EqualTo("Capability"));
            });
        }

        private static AasShell CreateCompleteShell(AASReferenceDataType reference)
        {
            return new AasShell
            {
                Id = "shell",
                Administration = AasOptional<AASAdministrativeInformationDataType>.Present(
                    Generated<AASAdministrativeInformationDataType>()),
                AssetInformation = new AasAssetInformation
                {
                    AssetKind = AASAssetKindDataType.Instance,
                    GlobalAssetId = AasOptional<string>.Present("global"),
                    AssetType = AasOptional<string>.Present("type"),
                    SpecificAssetIds = AasOptional<ArrayOf<AASSpecificAssetIdDataType>>.Present(
                        ArrayOf<AASSpecificAssetIdDataType>.Empty),
                    DefaultThumbnail = AasOptional<AASResourceDataType>.Present(Generated<AASResourceDataType>())
                },
                SubmodelReferences = AasOptional<ArrayOf<AASReferenceDataType>>.Present(
                    new AASReferenceDataType[] { reference }),
                DerivedFrom = AasOptional<AASReferenceDataType>.Present(reference),
                EmbeddedDataSpecifications =
                    AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>>.Present(
                        ArrayOf<AASEmbeddedDataSpecificationDataType>.Empty)
            };
        }

        private static AasSubmodel CreateCompleteSubmodel(AasSubmodelElement element)
        {
            return new AasSubmodel
            {
                Id = "submodel",
                Kind = AasOptional<AASModellingKindDataType>.Present(AASModellingKindDataType.Instance),
                SemanticId = AasOptional<AASReferenceDataType>.Present(Generated<AASReferenceDataType>()),
                SupplementalSemanticIds = AasOptional<ArrayOf<AASReferenceDataType>>.Present(
                    ArrayOf<AASReferenceDataType>.Empty),
                Qualifiers = AasOptional<ArrayOf<AASQualifierDataType>>.Present(
                    ArrayOf<AASQualifierDataType>.Empty),
                EmbeddedDataSpecifications =
                    AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>>.Present(
                        ArrayOf<AASEmbeddedDataSpecificationDataType>.Empty),
                SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new[] { element })
            };
        }

        private static T Generated<T>()
            where T : class, new()
        {
            return new T();
        }
    }
}
