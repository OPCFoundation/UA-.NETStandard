// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Reflection;
using NUnit.Framework;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Guards the alignment of the proof to the revised spec model: the
    /// <c>Opc.Ua.WotCon</c> generated model is now produced once from the
    /// combined <c>Opc.Ua.WotCon</c> NodeSet2 (incorporating the OPC 10100-1
    /// v1.02 surface plus the additive registry nodes in one namespace) instead
    /// of the standalone 1.02 ModelDesign and a separate <c>Opc.Ua.WotCon.V2</c>
    /// model. These tests prove that switching the generation source preserved
    /// the exact 1.02 NodeIds, the typed method state/result surface and the
    /// generated client API, and that the registry types now coexist in the
    /// same namespace.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public class CombinedModelPreservationTests
    {
        [Test]
        public void Incorporated102NodeIdsArePreservedExactly()
        {
            Assert.Multiple(() =>
            {
                // Well-known ObjectType NodeIds (OPC 10100-1 v1.02).
                Assert.That(ObjectTypes.WoTAssetConnectionManagementType, Is.EqualTo(1u));
                Assert.That(ObjectTypes.IWoTAssetType, Is.EqualTo(42u));
                Assert.That(ObjectTypes.WoTAssetFileType, Is.EqualTo(110u));

                // The well-known WoTAssetConnectionManagement Object.
                Assert.That(Objects.WoTAssetConnectionManagement, Is.EqualTo(31u));

                // The deprecated method-type declarations keep their NodeIds.
                Assert.That(Methods.CreateAssetMethodType, Is.EqualTo(90u));
            });
        }

        [Test]
        public void Incorporated102BrowseNamesAndMethodsArePreserved()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BrowseNames.WoTAssetConnectionManagement,
                    Is.EqualTo("WoTAssetConnectionManagement"));
                Assert.That(BrowseNames.CreateAsset, Is.EqualTo("CreateAsset"));
                Assert.That(BrowseNames.DeleteAsset, Is.EqualTo("DeleteAsset"));
                Assert.That(BrowseNames.DiscoverAssets, Is.EqualTo("DiscoverAssets"));
                Assert.That(BrowseNames.ConnectionTest, Is.EqualTo("ConnectionTest"));
                Assert.That(BrowseNames.HasWoTComponent, Is.EqualTo("HasWoTComponent"));
            });
        }

        [Test]
        public void TypedMethodResultsRetainTheirArguments()
        {
            // Generating the 1.02 methods from the NodeSet2 must still emit the
            // typed method state result structures with their arguments (these
            // were lost until the NodeSet2 argument-value decoder was fixed).
            var create = new CreateAssetMethodStateResult();
            create.AssetId = new NodeId(1);
            Assert.That(create.AssetId, Is.EqualTo(new NodeId(1)));

            var conn = new ConnectionTestMethodStateResult
            {
                Success = true,
                Status = "ok"
            };
            Assert.That(conn.Success, Is.True);
            Assert.That(conn.Status, Is.EqualTo("ok"));

            var discover = new DiscoverAssetsMethodStateResult();
            _ = discover.AssetEndpoints;
            Assert.That(
                typeof(DiscoverAssetsMethodStateResult).GetProperty(
                    nameof(DiscoverAssetsMethodStateResult.AssetEndpoints)),
                Is.Not.Null);
        }

        [Test]
        public void GeneratedClientApiExposesTypedAssetMethods()
        {
            // WoTAssetConnectionManagementTypeClient is the generated client
            // proxy the WotConnectivityClient facade delegates to.
            MethodInfo? createAsset = typeof(WoTAssetConnectionManagementTypeClient)
                .GetMethod("CreateAssetAsync");
            Assert.That(createAsset, Is.Not.Null,
                "generated client proxy must expose CreateAssetAsync");

            Assert.That(
                typeof(WoTAssetConnectionManagementTypeClient).GetMethod("ConnectionTestAsync"),
                Is.Not.Null);

            // Public client facade surface is preserved.
            Assert.That(typeof(WotConnectivityClient).GetMethod("CreateAssetAsync"),
                Is.Not.Null);
            Assert.That(typeof(WotConnectivityClient).GetMethod("DiscoverAssetsAsync"),
                Is.Not.Null);
        }

        [Test]
        public void AdditiveRegistryTypesCoexistInTheWotConNamespace()
        {
            Assert.Multiple(() =>
            {
                // Additive registry types now live in the same generated
                // Opc.Ua.WotCon namespace (previously Opc.Ua.WotCon.V2), at
                // their provisional 64000+ NodeIds.
                Assert.That(ObjectTypes.WoTRegistryType, Is.EqualTo(64000u));
                Assert.That(ObjectTypes.ThingDescriptionGroupType, Is.EqualTo(64001u));
                Assert.That(Objects.WoTRegistry, Is.EqualTo(64100u));

                // Registry DataTypes and enums are generated in Opc.Ua.WotCon.
                Assert.That(WoTDocumentKindEnum.ThingDescription,
                    Is.Not.EqualTo(WoTDocumentKindEnum.ThingModel));
                Assert.That(new WoTValidationOutcomeDataType(), Is.Not.Null);
            });
        }

        [Test]
        public void ModelDeclaresOneWotConNamespace()
        {
            Assert.That(Namespaces.WotCon,
                Is.EqualTo("http://opcfoundation.org/UA/WoT-Con/"));
        }
    }
}
