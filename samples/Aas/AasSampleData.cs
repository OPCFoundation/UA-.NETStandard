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

using Opc.Ua.Aas.V3;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Aas;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Aas.Server.Packaging;
using Opc.Ua.Aas.Server.Registry;

namespace AasSample
{
    /// <summary>
    /// Creates the small AAS environment, registry documents, and DPP metadata used by the sample.
    /// </summary>
    public static class AasSampleData
    {
        /// <summary>
        /// The materialized AAS instance namespace exposed by the sample server.
        /// </summary>
        public const string InstanceNamespaceUri = "urn:opcfoundation.org:UA:AasSample:BatteryPassport";

        /// <summary>
        /// The sample Asset Administration Shell identifier.
        /// </summary>
        public const string ShellId = "urn:opcfoundation:sample:aas:battery-pack:0001";

        /// <summary>
        /// The nameplate submodel identifier.
        /// </summary>
        public const string NameplateSubmodelId = "urn:opcfoundation:sample:submodel:nameplate:0001";

        /// <summary>
        /// The battery passport submodel identifier.
        /// </summary>
        public const string PassportSubmodelId = "urn:opcfoundation:sample:submodel:battery-passport:0001";

        /// <summary>
        /// The published package identifier.
        /// </summary>
        public const string PackageId = "urn:opcfoundation:sample:aasx:battery-passport:0001";

        /// <summary>
        /// Creates the complete sample data set.
        /// </summary>
        public static async ValueTask<AasSampleDataset> CreateAsync(CancellationToken cancellationToken)
        {
            AasDppIdentifierResult carbonFootprint = AasDppIdentifier.Construct("0173-1#02-AAO677#002");
            AasDisclosureDecision passportDisclosure = AasDppDisclosurePolicy.Map(
                AasDppRegulatoryClass.AvailableToPublic);
            AasDisclosureDecision controlledDisclosure = AasDppDisclosurePolicy.Map(
                AasDppRegulatoryClass.LegitimateInterestAndCommission);
            AasSubmodel nameplate = CreateNameplateSubmodel();
            AasSubmodel passport = CreatePassportSubmodel(carbonFootprint.Iri);
            AasConceptDescription concept = new()
            {
                Id = carbonFootprint.Iri,
                IdShort = Present("CarbonFootprintConcept")
            };
            AasEnvironment environment = new()
            {
                AssetAdministrationShells = Present(new ArrayOf<AasShell>(new[]
                {
                    new AasShell
                    {
                        Id = ShellId,
                        IdShort = Present("BatteryPack0001"),
                        AssetInformation = new AasAssetInformation
                        {
                            AssetKind = AASAssetKindDataType.Instance,
                            GlobalAssetId = Present("urn:epc:id:sgtin:0614141.107346.0001"),
                            SpecificAssetIds = Present(new ArrayOf<AASSpecificAssetIdDataType>(new[]
                            {
                                SpecificAssetId("serialNumber", "BP-2026-0001"),
                                SpecificAssetId("manufacturerPartId", "BATT-MODULE-42")
                            }))
                        }
                    }
                })),
                Submodels = Present(new ArrayOf<AasSubmodel>(new[] { nameplate, passport })),
                ConceptDescriptions = Present(new ArrayOf<AasConceptDescription>(new[] { concept }))
            };

            ByteString environmentDocument = await WriteEnvironmentAsync(environment, cancellationToken)
                .ConfigureAwait(false);
            ByteString nameplateDocument = await WriteEnvironmentAsync(
                new AasEnvironment { Submodels = Present(new ArrayOf<AasSubmodel>(new[] { nameplate })) },
                cancellationToken).ConfigureAwait(false);
            ByteString passportDocument = await WriteEnvironmentAsync(
                new AasEnvironment { Submodels = Present(new ArrayOf<AasSubmodel>(new[] { passport })) },
                cancellationToken).ConfigureAwait(false);
            ByteString conceptDocument = await WriteEnvironmentAsync(
                new AasEnvironment
                {
                    ConceptDescriptions = Present(new ArrayOf<AasConceptDescription>(new[] { concept }))
                },
                cancellationToken).ConfigureAwait(false);
            ByteString package = ByteString.From(Encoding.UTF8.GetBytes(
                "AASX placeholder package for the AAS V3 battery passport sample."));
            string packageDigest = AasPackageIntegrity.ComputeDigest(package, AasPackageIntegrity.Sha256);

            return new AasSampleDataset(
                environment,
                environmentDocument,
                RegistryRequests(
                    nameplateDocument,
                    passportDocument,
                    conceptDocument,
                    package,
                    passportDisclosure,
                    controlledDisclosure,
                    packageDigest),
                MaterializationDocuments(environmentDocument),
                carbonFootprint,
                passportDisclosure,
                controlledDisclosure,
                package,
                packageDigest);
        }

        private static ArrayOf<AasUpsertResourceRequest> RegistryRequests(
            ByteString nameplateDocument,
            ByteString passportDocument,
            ByteString conceptDocument,
            ByteString package,
            AasDisclosureDecision passportDisclosure,
            AasDisclosureDecision controlledDisclosure,
            string packageDigest)
        {
            return new AasUpsertResourceRequest[]
            {
                SubmodelRequest(
                    NameplateSubmodelId,
                    "Battery nameplate",
                    nameplateDocument,
                    AASDisclosureTierDataType.Public,
                    string.Empty),
                SubmodelRequest(
                    PassportSubmodelId,
                    "EU battery passport",
                    passportDocument,
                    passportDisclosure.Tier,
                    passportDisclosure.Authorization),
                new()
                {
                    GroupSourceIdentity = "urn:opcfoundation:sample:concept-dictionary:battery-passport",
                    ResourceSourceIdentity = "https://rdf.eclass.eu/resource/0173-1_02-AAO677_002",
                    GroupKind = AasRegistryEntityKind.ConceptDictionary,
                    ResourceKind = AasRegistryEntityKind.ConceptDescription,
                    Name = "Carbon footprint concept",
                    Content = conceptDocument
                },
                new()
                {
                    GroupSourceIdentity = "urn:opcfoundation:sample:package-store:battery-passports",
                    ResourceSourceIdentity = PackageId,
                    GroupKind = AasRegistryEntityKind.PackageStore,
                    ResourceKind = AasRegistryEntityKind.Package,
                    Name = "Battery passport AASX package",
                    Content = package,
                    Format = "aasx/3.0",
                    ContentType = "application/asset-administration-shell-package+xml",
                    Description = "Digest " + packageDigest,
                    DisclosureTier = controlledDisclosure.Tier,
                    Authorization = Authorization(controlledDisclosure.Authorization)
                }
            }.ToArrayOf();
        }

        private static AasUpsertResourceRequest SubmodelRequest(
            string submodelId,
            string name,
            ByteString content,
            AASDisclosureTierDataType tier,
            string authorization)
        {
            return new AasUpsertResourceRequest
            {
                GroupSourceIdentity = ShellId,
                ResourceSourceIdentity = submodelId,
                GroupKind = AasRegistryEntityKind.Shell,
                ResourceKind = AasRegistryEntityKind.Submodel,
                Name = name,
                Content = content,
                DisclosureTier = tier,
                Authorization = Authorization(authorization),
                SpecificAssetIds = new AasRegistryAssetLink[]
                {
                    new() { Name = "serialNumber", Value = "BP-2026-0001" },
                    new() { Name = "manufacturerPartId", Value = "BATT-MODULE-42" }
                }.ToArrayOf()
            };
        }

        private static ArrayOf<AasMaterializationDocument> MaterializationDocuments(ByteString environmentDocument)
        {
            return new AasMaterializationDocument[]
            {
                new()
                {
                    Xid = "battery-passport-environment",
                    VersionId = "v1",
                    SourceIdentity = ShellId,
                    Kind = AasMaterializationDocumentKind.Environment,
                    Content = environmentDocument,
                    Format = "aas/3.0+json"
                }
            }.ToArrayOf();
        }

        private static AasSubmodel CreateNameplateSubmodel()
        {
            return new AasSubmodel
            {
                Id = NameplateSubmodelId,
                IdShort = Present("Nameplate"),
                SubmodelElements = Present(new AasSubmodelElement[]
                {
                    Property("ManufacturerName", AASDataTypeDefXsdDataType.String, "Contoso Battery Systems"),
                    Property("SerialNumber", AASDataTypeDefXsdDataType.String, "BP-2026-0001")
                }.ToArrayOf())
            };
        }

        private static AasSubmodel CreatePassportSubmodel(string carbonFootprintSemanticId)
        {
            return new AasSubmodel
            {
                Id = PassportSubmodelId,
                IdShort = Present("BatteryPassport"),
                SubmodelElements = Present(new AasSubmodelElement[]
                {
                    Property("PassportIdentifier", AASDataTypeDefXsdDataType.String, "DPP-BP-2026-0001"),
                    Property(
                        "CarbonFootprint",
                        AASDataTypeDefXsdDataType.String,
                        "87.4",
                        Reference(carbonFootprintSemanticId)),
                    Property("StateOfHealth", AASDataTypeDefXsdDataType.Double, 98.2d),
                    Property("DisclosureClass", AASDataTypeDefXsdDataType.String, "available to the public"),
                    new AasOperation
                    {
                        IdShort = Present("RecalculatePassport"),
                        InputVariables = Present(new AasSubmodelElement[]
                        {
                            Property("Reason", AASDataTypeDefXsdDataType.String, "operator-request")
                        }.ToArrayOf()),
                        OutputVariables = Present(new AasSubmodelElement[]
                        {
                            Property("Result", AASDataTypeDefXsdDataType.String, "pending")
                        }.ToArrayOf())
                    }
                }.ToArrayOf())
            };
        }

        private static AasProperty Property(
            string idShort,
            AASDataTypeDefXsdDataType valueType,
            Variant value,
            AASReferenceDataType? semanticId = null)
        {
            return new AasProperty
            {
                IdShort = Present(idShort),
                ValueType = valueType,
                Value = Present(value),
                SemanticId = semanticId is null ? default : Present(semanticId)
            };
        }

        private static AASSpecificAssetIdDataType SpecificAssetId(string name, string value)
        {
            return new AASSpecificAssetIdDataType { Name = name, Value = value };
        }

        private static AASReferenceDataType Reference(string value)
        {
            return new AASReferenceDataType
            {
                Type = AASReferenceTypesDataType.ExternalReference,
                Keys = new AASKeyDataType[]
                {
                    new() { Type = AASKeyTypesDataType.GlobalReference, Value = value }
                }.ToArrayOf()
            };
        }

        private static ArrayOf<AASAuthorizationOptionDataType> Authorization(string authorization)
        {
            return string.IsNullOrEmpty(authorization)
                ? ArrayOf<AASAuthorizationOptionDataType>.Empty
                : new AASAuthorizationOptionDataType[]
                {
                    new() { Type = "DPP", ResourceUri = authorization }
                }.ToArrayOf();
        }

        private static AasOptional<T> Present<T>(T value) where T : notnull
        {
            return AasOptional<T>.Present(value);
        }

        private static async ValueTask<ByteString> WriteEnvironmentAsync(
            AasEnvironment environment,
            CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream();
            await new AasJsonWriter().WriteAsync(stream, environment, cancellationToken).ConfigureAwait(false);
            return ByteString.From(stream.ToArray());
        }
    }

    /// <summary>
    /// Complete sample data produced before the server starts.
    /// </summary>
    public sealed class AasSampleDataset
    {
        /// <summary>
        /// Initializes the sample data set.
        /// </summary>
        public AasSampleDataset(
            AasEnvironment environment,
            ByteString environmentDocument,
            ArrayOf<AasUpsertResourceRequest> registryRequests,
            ArrayOf<AasMaterializationDocument> materializationDocuments,
            AasDppIdentifierResult carbonFootprintIdentifier,
            AasDisclosureDecision passportDisclosure,
            AasDisclosureDecision controlledDisclosure,
            ByteString package,
            string packageDigest)
        {
            Environment = environment;
            EnvironmentDocument = environmentDocument;
            RegistryRequests = registryRequests;
            MaterializationDocuments = materializationDocuments;
            CarbonFootprintIdentifier = carbonFootprintIdentifier;
            PassportDisclosure = passportDisclosure;
            ControlledDisclosure = controlledDisclosure;
            Package = package;
            PackageDigest = packageDigest;
        }

        /// <summary>
        /// Gets the AAS environment used for metamodel materialization.
        /// </summary>
        public AasEnvironment Environment { get; }

        /// <summary>
        /// Gets the serialized complete AAS environment.
        /// </summary>
        public ByteString EnvironmentDocument { get; }

        /// <summary>
        /// Gets the registry seed requests.
        /// </summary>
        public ArrayOf<AasUpsertResourceRequest> RegistryRequests { get; }

        /// <summary>
        /// Gets the coordinator document set.
        /// </summary>
        public ArrayOf<AasMaterializationDocument> MaterializationDocuments { get; }

        /// <summary>
        /// Gets the DPP semantic identifier construction result.
        /// </summary>
        public AasDppIdentifierResult CarbonFootprintIdentifier { get; }

        /// <summary>
        /// Gets the passport disclosure mapping.
        /// </summary>
        public AasDisclosureDecision PassportDisclosure { get; }

        /// <summary>
        /// Gets the controlled disclosure mapping.
        /// </summary>
        public AasDisclosureDecision ControlledDisclosure { get; }

        /// <summary>
        /// Gets the sample package bytes.
        /// </summary>
        public ByteString Package { get; }

        /// <summary>
        /// Gets the sample package digest.
        /// </summary>
        public string PackageDigest { get; }
    }

}
