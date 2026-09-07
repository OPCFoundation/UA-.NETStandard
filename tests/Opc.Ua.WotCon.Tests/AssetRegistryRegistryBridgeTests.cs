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
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Verifies the optional WoT Connectivity asset-to-xRegistry bridge.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class AssetRegistryRegistryBridgeTests
    {
        private string m_tempFolder = null!;

        [SetUp]
        public void SetUp()
        {
            m_tempFolder = Path.Combine(
                Path.GetTempPath(),
                "wotcon-registry-bridge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_tempFolder))
            {
                try
                {
                    Directory.Delete(m_tempFolder, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        [Test]
        public async Task RebuildMirrorsCreatedThingDescriptionToRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(requests, Has.Count.EqualTo(1));
            AssertUpsertRequest(requests[0], WotRegistryGroups.ThingDescriptions, "asset-001", td);
        }

        [Test]
        public async Task RebuildMirrorsUpdatedThingDescriptionToRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription original = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");
            ThingDescription updated = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001-updated");

            await harness.Registry.RebuildAsync(
                entry,
                original,
                persistOnSuccess: true,
                CancellationToken.None).ConfigureAwait(false);
            ServiceResult status = await harness.Registry.RebuildAsync(
                entry,
                updated,
                persistOnSuccess: true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(requests, Has.Count.EqualTo(2));
            AssertUpsertRequest(requests[1], WotRegistryGroups.ThingDescriptions, "asset-001", updated);
        }

        [Test]
        public async Task DeleteRemovesMirroredThingDescriptionFromRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            var deletes = new List<(string GroupId, string ResourceId)>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests, deletes);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");
            await harness.Registry.RebuildAsync(
                entry,
                td,
                persistOnSuccess: true,
                CancellationToken.None).ConfigureAwait(false);

            ServiceResult status = await harness.Registry
                .DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(deletes, Has.Count.EqualTo(1));
            Assert.That(deletes[0].GroupId, Is.EqualTo(WotRegistryGroups.ThingDescriptions));
            Assert.That(deletes[0].ResourceId, Is.EqualTo("asset-001"));
            Assert.That(registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "asset-001"), Is.Null);
        }

        [Test]
        public async Task NonPersistedRebuildMirrorsThingDescriptionToRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(requests, Has.Count.EqualTo(1));
            AssertUpsertRequest(requests[0], WotRegistryGroups.ThingDescriptions, "asset-001", td);
        }

        [Test]
        public async Task NullRegistryBridgePerformsNoRegistryCalls()
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            using var harness = new ManagerHarness(m_tempFolder, registryBridge: null);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult rebuild = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None)
                .ConfigureAwait(false);
            ServiceResult delete = await harness.Registry
                .DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(rebuild), Is.True);
            Assert.That(ServiceResult.IsGood(delete), Is.True);
            bridge.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegistryRejectionDoesNotFailAssetRebuild()
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            bridge.Setup(r => r.UpsertResourceAsync(
                    It.IsAny<WotUpsertResourceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotRegistryMutationResult>(
                    new WotRegistryMutationResult(
                        WoTOutcomeEnum.Rejected,
                        resource: null,
                        generation: 0,
                        diagnostics: ImmutableArray.Create("invalid"),
                        message: "rejected")));
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(harness.Registry.AssetNames, Has.Member("asset-001"));
            bridge.Verify(r => r.UpsertResourceAsync(
                It.IsAny<WotUpsertResourceRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
            bridge.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegistryExceptionDoesNotFailAssetRebuild()
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            bridge.Setup(r => r.UpsertResourceAsync(
                    It.IsAny<WotUpsertResourceRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("registry unavailable"));
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(harness.Registry.AssetNames, Has.Member("asset-001"));
            bridge.Verify(r => r.UpsertResourceAsync(
                It.IsAny<WotUpsertResourceRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
            bridge.VerifyNoOtherCalls();
        }

        private static async Task<AssetEntry> CreateAssetEntryAsync(ManagerHarness harness, string assetName)
        {
            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync(assetName, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(status), Is.True);
            AssetEntry? entry = harness.Registry.FindByNodeId(assetId);
            Assert.That(entry, Is.Not.Null);
            return entry!;
        }

        private static Mock<IWotRegistryService> CreateRecordingBridge(
            WotRegistryService registry,
            List<WotUpsertResourceRequest> requests,
            List<(string GroupId, string ResourceId)>? deletes = null)
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            bridge.Setup(r => r.UpsertResourceAsync(
                    It.IsAny<WotUpsertResourceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<WotUpsertResourceRequest, CancellationToken>((request, _) => requests.Add(request))
                .Returns((WotUpsertResourceRequest request, CancellationToken ct) =>
                    registry.UpsertResourceAsync(request, ct));
            bridge.Setup(r => r.DeleteResourceAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, long?, CancellationToken>((groupId, resourceId, _, _) =>
                    deletes?.Add((groupId, resourceId)))
                .Returns((string groupId, string resourceId, long? expectedEpoch, CancellationToken ct) =>
                    registry.DeleteResourceAsync(groupId, resourceId, expectedEpoch, ct));
            return bridge;
        }

        private static ThingDescription CreateThingDescription(string name, string endpoint)
        {
            return new ThingDescription
            {
                Name = name,
                Base = endpoint
            };
        }

        private static void AssertUpsertRequest(
            WotUpsertResourceRequest request,
            string groupId,
            string resourceId,
            ThingDescription expected)
        {
            Assert.That(request.GroupId, Is.EqualTo(groupId));
            Assert.That(request.ResourceId, Is.EqualTo(resourceId));
            Assert.That(request.ContentType, Is.EqualTo("application/td+json"));
            Assert.That(request.Format, Is.EqualTo("WoT-TD/1.1"));
            ThingDescription? roundtrip = JsonSerializer.Deserialize(
                request.Content.Span,
                ThingDescriptionJsonContext.Default.ThingDescription);
            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip!.Name, Is.EqualTo(expected.Name));
            Assert.That(roundtrip.Base, Is.EqualTo(expected.Base));
            Assert.That(roundtrip.Properties, Is.EqualTo(expected.Properties));
        }

        private sealed class ManagerHarness : IDisposable
        {
            private const string AssetNamespace = "http://opcfoundation.org/UA/WoT-Con/Assets/";

            public ManagerHarness(string thingDescriptionFolder, IWotRegistryService? registryBridge)
            {
                MockServer = new Mock<IServerInternal>();

                var namespaceTable = new NamespaceTable();
                namespaceTable.Append(Namespaces.WotCon);
                namespaceTable.Append(AssetNamespace);

                MockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
                MockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                var typeTable = new TypeTable(namespaceTable);
                SeedStandardTypeTree(typeTable);
                MockServer.Setup(s => s.TypeTree).Returns(typeTable);
                MockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());

                var mockMaster = new Mock<IMasterNodeManager>();
                var mockConfig = new Mock<IConfigurationNodeManager>();
                mockMaster.Setup(m => m.ConfigurationNodeManager).Returns(mockConfig.Object);
                MockServer.Setup(s => s.NodeManager).Returns(mockMaster.Object);

                var mockTelemetry = new Mock<ITelemetryContext>();
                MockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

                m_monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
                MockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_monitoredItemQueueFactory);

                m_serverSystemContext = new ServerSystemContext(MockServer.Object);
                MockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

                m_configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 200
                    }
                };

                Options = new WotConnectivityServerOptions
                {
                    AssetNamespaceUri = AssetNamespace,
                    ThingDescriptionStorageFolder = thingDescriptionFolder,
                    RegistryBridge = registryBridge
                };
                Options.AssetEndpointPolicy.AllowedSchemes.Add("sim");
                Options.Bindings.Add(new SimulatedWotAssetProviderFactory());

                Manager = new WotConnectivityNodeManager(
                    MockServer.Object,
                    m_configuration,
                    Options);
            }

            public Mock<IServerInternal> MockServer { get; }
            public WotConnectivityServerOptions Options { get; }
            public WotConnectivityNodeManager Manager { get; }

            public AssetRegistry Registry
                => (AssetRegistry)typeof(WotConnectivityNodeManager)
                    .GetField(
                        "m_registry",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(Manager)!;

            public async Task StartAsync()
            {
                IDictionary<NodeId, IList<IReference>> externalReferences =
                    new Dictionary<NodeId, IList<IReference>>();
                await Manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            }

            public void Dispose()
            {
                Manager.Dispose();
                m_monitoredItemQueueFactory.Dispose();
            }

            private static void SeedStandardTypeTree(TypeTable typeTable)
            {
                NodeId baseObject = Ua.ObjectTypeIds.BaseObjectType;
                NodeId baseVariable = VariableTypeIds.BaseVariableType;
                NodeId baseDataVariable = VariableTypeIds.BaseDataVariableType;
                NodeId propertyType = VariableTypeIds.PropertyType;
                NodeId fileType = Ua.ObjectTypeIds.FileType;
                NodeId namespaceMetadataType = Ua.ObjectTypeIds.NamespaceMetadataType;
                NodeId baseInterfaceType = Ua.ObjectTypeIds.BaseInterfaceType;

                typeTable.AddSubtype(baseObject, NodeId.Null);
                typeTable.AddSubtype(fileType, baseObject);
                typeTable.AddSubtype(namespaceMetadataType, baseObject);
                typeTable.AddSubtype(baseInterfaceType, baseObject);

                typeTable.AddSubtype(baseVariable, NodeId.Null);
                typeTable.AddSubtype(baseDataVariable, baseVariable);
                typeTable.AddSubtype(propertyType, baseVariable);

                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.References,
                    NodeId.Null,
                    new QualifiedName("References"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    Ua.ReferenceTypeIds.References,
                    new QualifiedName("HierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasChild,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("HasChild"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.Aggregates,
                    Ua.ReferenceTypeIds.HasChild,
                    new QualifiedName("Aggregates"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasComponent,
                    Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasComponent"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasProperty,
                    Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasProperty"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.Organizes,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("Organizes"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    Ua.ReferenceTypeIds.References,
                    new QualifiedName("NonHierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasInterface,
                    Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    new QualifiedName("HasInterface"));
            }

            private readonly ApplicationConfiguration m_configuration;
            private readonly ServerSystemContext m_serverSystemContext;
            private readonly MonitoredItemQueueFactory m_monitoredItemQueueFactory;
        }
    }
}
