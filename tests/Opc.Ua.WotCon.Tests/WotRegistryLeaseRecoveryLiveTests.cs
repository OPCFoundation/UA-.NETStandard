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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.WotCon.Tests.Registry;
using Opc.Ua.XRegistry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Client")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class WotRegistryLeaseRecoveryLiveTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_root = Path.Combine(Path.GetTempPath(), "ua-ln", Guid.NewGuid().ToString("N"));
            string registryRoot = Path.Combine(m_root, "registry");
            Directory.CreateDirectory(registryRoot);
            m_store = new FileWotRegistryStore(registryRoot);
            m_serverFixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_serverFixture.StartAsync(m_root).ConfigureAwait(false);
            var options = new WotRegistryServerOptions
            {
                IdentityBindings = WotRegistryTestAuthorities.ForResources("rehydrated-native"),
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                }
            };
            options.Bounds.MaxVersionsPerResource = 2;
            m_registry = new WotRegistryService(m_store, options.Bounds, options.IdentityBindings);
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: new FakeWotDocumentConverter());
            await m_server.NodeManagerLifecycle.AddAsync(
                new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            m_clientFixture = new ClientFixture(false, false, m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_root).ConfigureAwait(false);
            m_session = await m_clientFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"),
                SecurityPolicies.None).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_session is not null)
                {
                    await m_session.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    m_session?.Dispose();
                }
                finally
                {
                    try
                    {
                        m_coordinator?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            m_registry?.Dispose();
                        }
                        finally
                        {
                            try
                            {
                                m_server?.Dispose();
                            }
                            finally
                            {
                                try
                                {
                                    if (m_serverFixture is not null)
                                    {
                                        await m_serverFixture.StopAsync().ConfigureAwait(false);
                                    }
                                }
                                finally
                                {
                                    try
                                    {
                                        if (m_clientFixture is not null)
                                        {
                                            await m_clientFixture.DisposeAsync().ConfigureAwait(false);
                                        }
                                    }
                                    finally
                                    {
                                        try
                                        {
                                            m_store?.Dispose();
                                        }
                                        finally
                                        {
                                            if (m_root is not null && Directory.Exists(m_root))
                                            {
                                                Directory.Delete(m_root, recursive: true);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FileStoreReloadKeepsNativeHandlesLeasedUntilTheirLastReleaseAsync(bool writer)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            WotRegistryClient client = await WotRegistryClient.ForServerAsync(m_session, m_telemetry, ct: ct)
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("rehydrated-native", "v1", ct)
                .ConfigureAwait(false);
            ByteString original = ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "v1"));
            await first.Proxy.UploadAsync(original, ct: ct).ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("rehydrated-native", "v2", ct)
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "v2")), ct: ct).ConfigureAwait(false);
            WotResource resource = FindResource();
            WotRegistryResourceClient logical = await group.OpenResourceAsync(resource.ResourceId, ct)
                .ConfigureAwait(false);
            await WaitForDefaultVersionAsync(logical.ResourceNodeId, "v1", ct).ConfigureAwait(false);
            ByteString updated = ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "written-v1"));
            uint exactHandle = 0;
            uint logicalHandle = await logical.Proxy.OpenAsync(writer ? (byte)6 : (byte)1, ct).ConfigureAwait(false);
            IWotRegistryVersionLease? peer = null;
            try
            {
                if (writer)
                {
                    await logical.Proxy.WriteAsync(logicalHandle, updated, ct).ConfigureAwait(false);
                    peer = await m_registry.AcquireVersionLeaseAsync(
                        resource.GroupId, resource.ResourceId, resource.FindVersion("v1")!, ct).ConfigureAwait(false);
                }
                else
                {
                    exactHandle = await first.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
                }
                await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
                Assert.That((await client.RefreshAllAsync(requestId: "rehydrated-native-v2", ct: ct)
                    .ConfigureAwait(false)).HasFailures, Is.False);
                AssertSelectors(FindResource());
                await m_registry.InitializeAsync(ct).ConfigureAwait(false);
                await AssertAllocationBlockedAsync(group, ct).ConfigureAwait(false);

                if (writer)
                {
                    await logical.Proxy.CloseAsync(logicalHandle, ct).ConfigureAwait(false);
                    Assert.That(await m_registry.ReadContentAsync(FindResource().FindVersion("v1")!, ct)
                        .ConfigureAwait(false), Is.EqualTo(updated));
                }
                else
                {
                    Assert.That(await first.Proxy.ReadAsync(exactHandle, int.MaxValue, ct).ConfigureAwait(false),
                        Is.EqualTo(original));
                    Assert.That(await logical.Proxy.ReadAsync(logicalHandle, int.MaxValue, ct).ConfigureAwait(false),
                        Is.EqualTo(original));
                    await first.Proxy.CloseAsync(exactHandle, ct).ConfigureAwait(false);
                }
                await m_registry.InitializeAsync(ct).ConfigureAwait(false);
                await AssertAllocationBlockedAsync(group, ct).ConfigureAwait(false);
                if (writer)
                {
                    peer!.Dispose();
                }
                else
                {
                    await logical.Proxy.CloseAsync(logicalHandle, ct).ConfigureAwait(false);
                }
                (WotRegistryResourceClient third, _) = await group.CreateResourceAsync("rehydrated-native", "v3", ct)
                    .ConfigureAwait(false);
                ByteString thirdBytes = ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "v3"));
                await third.Proxy.UploadAsync(thirdBytes, ct: ct).ConfigureAwait(false);
                WotResource after = FindResource();
                WotResource stored = (await ReadStoredSnapshotAsync(ct).ConfigureAwait(false))
                    .FindResource(resource.GroupId, resource.ResourceId)!;
                Assert.Multiple(() =>
                {
                    Assert.That(after.Versions.Select(version => version.VersionId),
                        Is.EquivalentTo(s_remainingVersionIds));
                    Assert.That(stored.Versions.Select(version => version.VersionId),
                        Is.EquivalentTo(s_remainingVersionIds));
                    Assert.That(stored.MetaEpoch, Is.EqualTo(after.MetaEpoch));
                });
                AssertSelectors(after);
                AssertSelectors(stored);
                uint thirdHandle = await third.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
                Assert.That(await third.Proxy.ReadAsync(thirdHandle, int.MaxValue, ct).ConfigureAwait(false),
                    Is.EqualTo(thirdBytes));
                await third.Proxy.CloseAsync(thirdHandle, ct).ConfigureAwait(false);
            }
            finally
            {
                peer?.Dispose();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StaleNativeWriterCannotChangeRecreatedIncarnationAfterReloadAsync(bool deleteResource)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            WotRegistryClient client = await WotRegistryClient.ForServerAsync(m_session, m_telemetry, ct: ct)
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("rehydrated-native", "v1", ct)
                .ConfigureAwait(false);
            ByteString original = ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "v1"));
            ByteString secondBytes = ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "v2"));
            await first.Proxy.UploadAsync(original, ct: ct).ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("rehydrated-native", "v2", ct)
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(secondBytes, ct: ct).ConfigureAwait(false);
            await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
            await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
            Assert.That((await client.RefreshAllAsync(requestId: "old-incarnation-v2", ct: ct).ConfigureAwait(false))
                .HasFailures, Is.False);
            WotResource resource = FindResource();
            AssertSelectors(resource);
            using IWotRegistryVersionLease staleLease = await m_registry.AcquireVersionLeaseAsync(
                resource.GroupId, resource.ResourceId, resource.FindVersion("v1")!, ct).ConfigureAwait(false);
            uint staleHandle = await first.Proxy.OpenAsync(6, ct).ConfigureAwait(false);
            await first.Proxy.WriteAsync(
                staleHandle,
                ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "stale-write")),
                ct).ConfigureAwait(false);
            WotRegistryMutationResult deleted = deleteResource
                ? await m_registry.DeleteResourceAsync(
                    resource.GroupId, resource.ResourceId, cancellationToken: ct).ConfigureAwait(false)
                : await m_registry.DeleteVersionAsync(
                    resource.GroupId, resource.ResourceId, "v1", cancellationToken: ct).ConfigureAwait(false);
            Assert.That(deleted.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            await Assert.ThatAsync(async () =>
            {
                ReadResponse response = await m_session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    new[]
                    {
                        new ReadValueId { NodeId = first.ResourceNodeId, AttributeId = Attributes.NodeClass }
                    }.ToArrayOf(),
                    ct).ConfigureAwait(false);
                return response.Results[0].StatusCode;
            }, Is.EqualTo(StatusCodes.BadNodeIdUnknown).After(5000, 10)).ConfigureAwait(false);
            await m_registry.InitializeAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient recreated, _) = await group.CreateResourceAsync("rehydrated-native", "v1", ct)
                .ConfigureAwait(false);
            Assert.That(recreated.ResourceNodeId, Is.EqualTo(first.ResourceNodeId));
            await recreated.Proxy.UploadAsync(original, ct: ct).ConfigureAwait(false);
            if (deleteResource)
            {
                (second, _) = await group.CreateResourceAsync("rehydrated-native", "v2", ct).ConfigureAwait(false);
                await second.Proxy.UploadAsync(secondBytes, ct: ct).ConfigureAwait(false);
                await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
                Assert.That((await client.RefreshAllAsync(requestId: "new-incarnation-v2", ct: ct)
                    .ConfigureAwait(false))
                    .HasFailures, Is.False);
            }
            await m_registry.InitializeAsync(ct).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            AssertSelectors(FindResource());
            await Assert.ThatAsync(async () =>
                await first.Proxy.CloseAsync(staleHandle, ct).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidArgument)).ConfigureAwait(false);
            Assert.That(m_registry.Current, Is.SameAs(before));
            WotResourceVersion replacement = FindResource().FindVersion("v1")!;
            Assert.That(replacement.Digest, Is.EqualTo(staleLease.Version.Digest));
            Assert.That(await m_registry.ReadContentAsync(replacement, ct).ConfigureAwait(false), Is.EqualTo(original));
            WotResource stored = (await ReadStoredSnapshotAsync(ct).ConfigureAwait(false))
                .FindResource(resource.GroupId, resource.ResourceId)!;
            Assert.That(stored.FindVersion("v1")!.Digest, Is.EqualTo(replacement.Digest));
            Assert.That(stored.MetaEpoch, Is.EqualTo(FindResource().MetaEpoch));
            (WotRegistryResourceClient third, _) = await group.CreateResourceAsync("rehydrated-native", "v3", ct)
                .ConfigureAwait(false);
            await third.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:rehydrated-native", "v3")), ct: ct).ConfigureAwait(false);
            Assert.That(FindResource().FindVersion("v1"), Is.Null,
                "The still-live old lease must not protect the replacement incarnation.");
        }

        private WotResource FindResource()
        {
            return WotRegistryTestAuthorities.FindResource(
                m_registry.Current, WotRegistryGroups.ThingDescriptions, "rehydrated-native")!;
        }

        private async ValueTask<WotRegistrySnapshot> ReadStoredSnapshotAsync(CancellationToken ct)
        {
            using var observer = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            return await observer.LoadAsync(ct).ConfigureAwait(false);
        }

        private async Task AssertAllocationBlockedAsync(WotRegistryGroupClient group, CancellationToken ct)
        {
            WotRegistrySnapshot before = m_registry.Current;
            WotResource resource = FindResource();
            AssertSelectors(resource);
            await Assert.ThatAsync(async () =>
            {
                _ = await group.CreateResourceAsync("rehydrated-native", "v3", ct).ConfigureAwait(false);
            }, Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
            Assert.That(m_registry.Current, Is.SameAs(before));
            WotRegistrySnapshot stored = await ReadStoredSnapshotAsync(ct).ConfigureAwait(false);
            Assert.That(stored.Generation, Is.EqualTo(before.Generation));
            Assert.That(stored.FindResource(resource.GroupId, resource.ResourceId)!.FindVersion("v1"), Is.Not.Null);
        }

        private async Task WaitForDefaultVersionAsync(NodeId resource, string expected, CancellationToken ct)
        {
            ushort ns = m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            TranslateBrowsePathsToNodeIdsResponse response = await m_session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                new[]
                {
                    new BrowsePath
                    {
                        StartingNode = resource,
                        RelativePath = new RelativePath
                        {
                            Elements =
                            [
                                new RelativePathElement
                                {
                                    ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                                    IncludeSubtypes = true,
                                    TargetName = new QualifiedName(XRegistry.BrowseNames.VersionId, ns)
                                }
                            ]
                        }
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            NodeId node = ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, m_session.NamespaceUris);
            await Assert.ThatAsync(async () =>
            {
                DataValue value = await m_session.ReadValueAsync(node, ct).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw new ServiceResultException(value.StatusCode);
                }
                if (!value.WrappedValue.TryGetValue(out string selected))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                }
                return selected;
            }, Is.EqualTo(expected).After(5000, 10)).ConfigureAwait(false);
        }

        private static void AssertSelectors(WotResource resource)
        {
            Assert.Multiple(() =>
            {
                Assert.That(resource.ActiveVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DesiredVersionId, Is.EqualTo("v2"));
            });
        }

        private string m_root = null!;
        private ITelemetryContext m_telemetry = null!;
        private FileWotRegistryStore m_store = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ReferenceServer m_server = null!;
        private ISession m_session = null!;
        private WotRegistryService m_registry = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private static readonly string[] s_remainingVersionIds = ["v2", "v3"];
    }
}
