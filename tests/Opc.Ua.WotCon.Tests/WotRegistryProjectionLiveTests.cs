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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Live-server integration tests that exercise the OPC UA method handlers in
    /// <c>WotRegistryProjection</c> and the corresponding <c>WotRegistryNodeManager</c>
    /// paths (event building, refresh gate, address-space lifecycle) as well as the
    /// production <c>LifecycleWotProjectionHost</c>.
    /// A real <see cref="ReferenceServer"/> is started per test, and a real OPC UA session
    /// connects over loopback opc.tcp so that method calls travel the full server-side
    /// call chain.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Client")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed partial class WotRegistryProjectionLiveTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_testOutput.Clear();
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                "ua-rg-wot",
                Guid.NewGuid().ToString("N"));

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            var options = new WotRegistryServerOptions
            {
                IdentityBindings = Registry.WotRegistryTestAuthorities.ForResources(
                    "dup-res", "new-res", "recover-pending", "multi", "reader-alias", "pinned-read",
                    "session-discard", "session-readers", "late-open", "unscoped-pin", "late-create",
                    "cancel-discard", "delete-logical", "delete-version", "collision-delete", "delete-switched",
                    "existing-res", "td-01", "retry-placeholder", "atomic-retry", "bulk-version",
                    "version-titles", "model-titles", "replace", "retained-client", "collision", "validate-version",
                    "bad-doc", "dep-td", "session-create", "race-0", "race-1", "race-2", "race-3", "race-4",
                    "race-5", "race-6", "race-7", "race-8", "race-9", "race-10", "race-11"),
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                }
            };

            m_registry = new WotRegistryService(null, options.Bounds, options.IdentityBindings);
            m_converter = new FakeWotDocumentConverter();
            m_projectionHost = new PausableProjectionHost(
                new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_projectionHost,
                documentConverter: m_converter);
            var factory = new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator);
            Ua.Server.NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(factory, callerContext: null)
                .ConfigureAwait(false);
            m_nodeManager = (WotRegistryNodeManager)registration.NodeManager;

            m_clientFixture = new ClientFixture(false, false, m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            string url = $"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}";
            m_session = await m_clientFixture
                .ConnectAsync(new Uri(url), SecurityPolicies.None)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            // Exercises DeleteAddressSpaceAsync (unregisters events, calls
            // coordinator.RemoveAllAsync → LifecycleWotProjectionHost.RemoveAsync per
            // projection) and WotRegistryProjection.Dispose (clears group entries,
            // disposes the refresh gate).
            bool completed = false;
            try
            {
                try
                {
                    if (m_session != null)
                    {
                        await m_session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_session?.Dispose();
                    m_coordinator?.Dispose();
                    m_registry?.Dispose();
                    m_server?.Dispose();

                    if (m_serverFixture != null)
                    {
                        await m_serverFixture.StopAsync().ConfigureAwait(false);
                    }

                    m_clientFixture?.Dispose();

                    if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
                    {
                        Directory.Delete(m_pkiRoot, recursive: true);
                    }
                }
                completed = true;
            }
            finally
            {
                if (m_testOutput.Length != 0 &&
                    (!completed || TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Passed))
                {
                    TestContext.Out.Write(m_testOutput.ToString());
                }
                m_testOutput.Clear();
            }
        }

        /// <summary>
        /// Covers <c>OnCreateResourceAsync</c> empty-resource-id branch:
        /// the server rejects an empty id with <c>BadInvalidArgument</c>.
        /// </summary>
        [Test]
        public async Task CreateResourceWithEmptyIdReturnsBadInvalidArgument()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);

            // Bypass the client-wrapper validation and call the proxy directly
            // so that the empty string reaches the server-side handler.
            ServiceResultException? ex = null;
            try
            {
                _ = await group.Proxy
                    .CreateResourceAsync(
                        resourceId: string.Empty,
                        versionId: string.Empty,
                        requestFileOpen: false)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Not.Null, "Empty resource id must be rejected by the server.");
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// Covers <c>OnCreateResourceAsync</c> duplicate-Version branch.
        /// </summary>
        [Test]
        public async Task CreateResourceWithDuplicateVersionReturnsBadNodeIdExists()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            _ = await group.CreateResourceAsync("dup-res", "v1").ConfigureAwait(false);

            ServiceResultException? ex = null;
            try
            {
                _ = await group.CreateResourceAsync("dup-res", "v1").ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Not.Null, "A duplicate (ResourceId, VersionId) must be rejected.");
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        /// <summary>
        /// Covers <c>OnGetOrCreateResourceAsync</c> create path: the first call
        /// creates the resource and reports <c>Created == true</c>.
        /// </summary>
        [Test]
        public async Task GetOrCreateResourceCreatesNewResourceOnFirstCall()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);

            (_, _, bool created) = await group
                .GetOrCreateResourceAsync("new-res")
                .ConfigureAwait(false);

            Assert.That(created, Is.True, "First call must report the resource as newly created.");
        }

        [Test]
        public async Task EmptyCreateAndGetOrCreateReusePendingVersion()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient first, string versionId) = await group
                .CreateResourceAsync("recover-pending")
                .ConfigureAwait(false);

            (WotRegistryResourceClient reusedCreate, string createVersionId) = await group
                .CreateResourceAsync("recover-pending")
                .ConfigureAwait(false);
            (WotRegistryResourceClient reusedGet, string getVersionId, bool created) = await group
                .GetOrCreateResourceAsync("recover-pending")
                .ConfigureAwait(false);
            await reusedCreate.UploadNewVersionAsync(
                    ByteString.From(TestMaterialization.Td("urn:recover-pending")))
                .ConfigureAwait(false);
            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "recover-pending")!;

            Assert.Multiple(() =>
            {
                Assert.That(createVersionId, Is.EqualTo(versionId));
                Assert.That(getVersionId, Is.EqualTo(versionId));
                Assert.That(reusedCreate.ResourceNodeId, Is.EqualTo(first.ResourceNodeId));
                Assert.That(reusedGet.ResourceNodeId, Is.EqualTo(first.ResourceNodeId));
                Assert.That(created, Is.False);
                Assert.That(stored.Versions, Has.Length.EqualTo(1));
                Assert.That(stored.FindVersion(versionId)!.HasContent, Is.True);
            });
        }

        [Test]
        public async Task ExplicitVersionsHaveDistinctStableProjectedNodeIds()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);

            (WotRegistryResourceClient v1, string v1Id) =
                await group.CreateResourceAsync("multi", "v1").ConfigureAwait(false);
            await v1.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:multi", "v1")))
                .ConfigureAwait(false);
            (WotRegistryResourceClient v2, string v2Id) =
                await group.CreateResourceAsync("multi", "v2").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(v1Id, Is.EqualTo("v1"));
                Assert.That(v2Id, Is.EqualTo("v2"));
                Assert.That(v1.ResourceNodeId, Is.Not.EqualTo(v2.ResourceNodeId));
                Assert.That(
                    v1.ResourceNodeId.TryGetValue(out string v1NodeId)
                        ? v1NodeId
                        : string.Empty,
                    Is.EqualTo(
                        $"WoTRegistry/groups/{group.GroupId}/resources/{v1.ResourceId}/versions/v1"));
                Assert.That(
                    v2.ResourceNodeId.TryGetValue(out string v2NodeId)
                        ? v2NodeId
                        : string.Empty,
                    Is.EqualTo(
                        $"WoTRegistry/groups/{group.GroupId}/resources/{v2.ResourceId}/versions/v2"));
                WotResource stored = FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "multi")!;
                Assert.That(stored.Versions.Select(version => version.VersionId),
                    Is.EquivalentTo(s_expectedVersionIds));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReturnedCreateHandlesAreUsableOnlyByTheirCreatingSession(bool getOrCreate)
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            NodeId resourceNodeId;
            uint handle;
            if (getOrCreate)
            {
                (resourceNodeId, _, handle, _) = await group.Proxy.GetOrCreateResourceAsync(
                    "session-create", "v1", requestFileOpen: true).ConfigureAwait(false);
            }
            else
            {
                (resourceNodeId, _, handle) = await group.Proxy.CreateResourceAsync(
                    "session-create", "v1", requestFileOpen: true).ConfigureAwait(false);
            }
            var ownerFile = new ThingDescriptionFileTypeClient(m_session, resourceNodeId, m_telemetry);
            using var otherFixture = new ClientFixture(false, false, m_telemetry);
            await otherFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using ISession otherSession = await otherFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                var otherFile = new ThingDescriptionFileTypeClient(otherSession, resourceNodeId, m_telemetry);
                Assert.That(() => otherFile.WriteAsync(handle, ByteString.From([9])).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(() => otherFile.CloseAsync(handle).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadUserAccessDenied));

                byte[] document = TestMaterialization.Td("urn:session-create", "committed by owner");
                await ownerFile.WriteAsync(handle, ByteString.From(document)).ConfigureAwait(false);
                await ownerFile.CloseAsync(handle).ConfigureAwait(false);
                byte[] downloaded = await otherFile.DownloadAllAsync().ConfigureAwait(false);

                Assert.That(downloaded, Is.EqualTo(document));
            }
            finally
            {
                await otherSession.CloseAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task LogicalReaderExcludesAnExactVersionWriterUntilClose()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient version, _) = await group.CreateResourceAsync("reader-alias", "v1")
                .ConfigureAwait(false);
            byte[] document = TestMaterialization.Td("urn:reader-alias", "original");
            await version.Proxy.UploadAsync(ByteString.From(document)).ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("reader-alias"))
                .ConfigureAwait(false);
            uint reader = await logical.Proxy.OpenAsync(1).ConfigureAwait(false);
            try
            {
                Assert.That(() => version.Proxy.OpenAsync(6).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadNotWritable));
                ByteString content = await logical.Proxy.ReadAsync(reader, int.MaxValue).ConfigureAwait(false);
                Assert.That(content.Span.ToArray(), Is.EqualTo(document));
            }
            finally
            {
                await logical.Proxy.CloseAsync(reader).ConfigureAwait(false);
            }
            uint writer = await version.Proxy.OpenAsync(6).ConfigureAwait(false);
            await version.Proxy.CloseAsync(writer).ConfigureAwait(false);
            byte[] unchanged = await logical.Proxy.DownloadAllAsync().ConfigureAwait(false);

            Assert.That(unchanged, Is.EqualTo(document));
        }

        [Test]
        public async Task LogicalReadHandlesKeepTheirVersionsAcrossADefaultSwitch()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient firstVersion, _) = await group.CreateResourceAsync("pinned-read", "v1")
                .ConfigureAwait(false);
            byte[] firstDocument = TestMaterialization.Td("urn:pinned-read", "first");
            await firstVersion.Proxy.UploadAsync(ByteString.From(firstDocument)).ConfigureAwait(false);
            (WotRegistryResourceClient secondVersion, _) = await group.CreateResourceAsync("pinned-read", "v2")
                .ConfigureAwait(false);
            byte[] secondDocument = TestMaterialization.Td("urn:pinned-read", "second");
            await secondVersion.Proxy.UploadAsync(ByteString.From(secondDocument)).ConfigureAwait(false);
            await firstVersion.SetDefaultVersionAsync("v1", expectedEpoch: 0).ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("pinned-read"))
                .ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v1").ConfigureAwait(false);
            uint first = await logical.Proxy.OpenAsync(1).ConfigureAwait(false);
            uint second = 0;
            try
            {
                await secondVersion.SetDefaultVersionAsync("v2", expectedEpoch: 0).ConfigureAwait(false);
                await WaitForPublishedDefaultAsync(logical, "v2").ConfigureAwait(false);
                second = await logical.Proxy.OpenAsync(1).ConfigureAwait(false);
                ByteString firstContent = await logical.Proxy.ReadAsync(first, int.MaxValue).ConfigureAwait(false);
                ByteString secondContent = await logical.Proxy.ReadAsync(second, int.MaxValue).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(first, Is.Not.EqualTo(second));
                    Assert.That(firstContent.Span.ToArray(), Is.EqualTo(firstDocument));
                    Assert.That(secondContent.Span.ToArray(), Is.EqualTo(secondDocument));
                });
            }
            finally
            {
                await logical.Proxy.CloseAsync(first).ConfigureAwait(false);
                if (second != 0)
                {
                    await logical.Proxy.CloseAsync(second).ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ActiveVersionDoesNotRedirectLogicalDefaultOrPinnedReadsAsync(bool rejectDefault)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            m_registry.Bounds.MaxVersionsPerResource = 2;
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct: ct)
                .ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("bad-doc", "v1", ct: ct)
                .ConfigureAwait(false);
            byte[] firstDocument = TestMaterialization.Td("urn:bad-doc", "active-first");
            await first.Proxy.UploadAsync(ByteString.From(firstDocument), ct: ct).ConfigureAwait(false);
            await first.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
            WotRegistryRefreshResult activated = await client.RefreshAllAsync(requestId: "roles-active-v1", ct: ct)
                .ConfigureAwait(false);
            Assert.That(activated.HasFailures, Is.False);
            Assert.That(FindResource(WotRegistryGroups.ThingDescriptions, "bad-doc")!.ActiveVersionId,
                Is.EqualTo("v1"));
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("bad-doc"), ct)
                .ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v1").ConfigureAwait(false);
            uint oldHandle = await logical.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
            uint newHandle = 0;
            try
            {
                (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("bad-doc", "v2", ct: ct)
                    .ConfigureAwait(false);
                byte[] secondDocument = TestMaterialization.Td("urn:bad-doc", "selected-second");
                await second.Proxy.UploadAsync(ByteString.From(secondDocument), ct: ct).ConfigureAwait(false);
                WotResource beforeSelection = FindResource(WotRegistryGroups.ThingDescriptions, "bad-doc")!;
                await second.SetDefaultVersionAsync("v2", checked((uint)beforeSelection.MetaEpoch), ct)
                    .ConfigureAwait(false);
                WotResource afterSelection = FindResource(WotRegistryGroups.ThingDescriptions, "bad-doc")!;
                Assert.Multiple(() =>
                {
                    Assert.That(afterSelection.DefaultVersionId, Is.EqualTo("v2"));
                    Assert.That(afterSelection.ActiveVersionId, Is.EqualTo("v1"));
                    Assert.That(afterSelection.MetaEpoch, Is.EqualTo(beforeSelection.MetaEpoch + 1));
                    Assert.That(afterSelection.FindVersion("v1")!.Epoch,
                        Is.EqualTo(beforeSelection.FindVersion("v1")!.Epoch));
                    Assert.That(afterSelection.FindVersion("v2")!.Epoch,
                        Is.EqualTo(beforeSelection.FindVersion("v2")!.Epoch));
                });
                if (rejectDefault)
                {
                    m_converter.MarkInvalid(AssignedResourceId("bad-doc"));
                    WotRegistryRefreshResult rejected = await client
                        .RefreshAllAsync(requestId: "roles-rejected-v2", ct: ct).ConfigureAwait(false);
                    Assert.That(rejected.HasFailures, Is.True);
                }
                await WaitForPublishedDefaultAsync(logical, "v2").ConfigureAwait(false);
                WotResource selected = FindResource(WotRegistryGroups.ThingDescriptions, "bad-doc")!;
                Assert.That(selected.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(selected.DefaultVersionId, Is.EqualTo("v2"));
                newHandle = await logical.Proxy.OpenAsync(1, ct).ConfigureAwait(false);

                WotRegistrySnapshot beforeRejectedAllocation = m_registry.Current;
                await Assert.ThatAsync(async () =>
                {
                    _ = await group.CreateResourceAsync("bad-doc", "v3", ct: ct).ConfigureAwait(false);
                }, Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(beforeRejectedAllocation));
                Assert.That(selected.Versions, Has.Length.EqualTo(2));
                ByteString oldContent = await logical.Proxy.ReadAsync(oldHandle, int.MaxValue, ct)
                    .ConfigureAwait(false);
                ByteString newContent = await logical.Proxy.ReadAsync(newHandle, int.MaxValue, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(oldHandle, Is.Not.EqualTo(newHandle));
                    Assert.That(oldContent, Is.EqualTo(ByteString.From(firstDocument)));
                    Assert.That(newContent, Is.EqualTo(ByteString.From(secondDocument)));
                });
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await logical.Proxy.CloseAsync(oldHandle, cleanup.Token).ConfigureAwait(false);
                if (newHandle != 0)
                {
                    await logical.Proxy.CloseAsync(newHandle, cleanup.Token).ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LogicalOldReadClosePreservesCurrentDefaultFilePropertiesAsync(bool currentReaderOpen)
        {
            using var output = new StringWriter(m_testOutput, CultureInfo.CurrentCulture);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            using var secureFixture = new ClientFixture(false, false, m_telemetry);
            await secureFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using ISession session = await secureFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"),
                SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(session.SessionId, Is.Not.EqualTo(m_session.SessionId));
                });
                output.WriteLine(
                    $"Runtime: {Environment.Version}; ServerGC: {System.Runtime.GCSettings.IsServerGC}");
                WotRegistryClient client = await WotRegistryClient.ForServerAsync(session, m_telemetry, ct)
                    .ConfigureAwait(false);
                WotRegistryGroupClient group = await client.CreateDocumentGroupAsync(
                    WoTDocumentKindEnum.ThingDescription, "urn:review:catalog", ct).ConfigureAwait(false);
                WotRegistryResourceAllocation first = await group.CreateDocumentResourceAsync(
                    "urn:review:pump", "v1", false, ct).ConfigureAwait(false);
                ByteString oldDocument = PinnedCloseDocument("first");
                ByteString currentDocument = PinnedCloseDocument("a-deliberately-longer-second-version-document");
                Assert.Multiple(() =>
                {
                    Assert.That(oldDocument.Length, Is.EqualTo(167));
                    Assert.That(currentDocument.Length, Is.EqualTo(207));
                });
                await first.Version.Proxy.UploadAsync(oldDocument, ct: ct).ConfigureAwait(false);
                WotRegistryResourceClient logical = first.LogicalResource;
                await WaitForFileViewAsync(session, logical.ResourceNodeId, "v1", 167, ct).ConfigureAwait(false);
                uint oldHandle = await logical.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
                uint currentHandle = 0;
                try
                {
                    WotRegistryResourceAllocation second = await group.CreateDocumentResourceAsync(
                        "urn:review:pump", "v2", false, ct).ConfigureAwait(false);
                    await second.Version.Proxy.UploadAsync(currentDocument, ct: ct).ConfigureAwait(false);
                    await logical.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                    await WaitForFileViewAsync(session, logical.ResourceNodeId, "v2", 207, ct).ConfigureAwait(false);
                    if (currentReaderOpen)
                    {
                        currentHandle = await logical.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
                    }

                    NativeFileView before = await ReadFileViewAsync(session, logical.ResourceNodeId, ct)
                        .ConfigureAwait(false);
                    NativeFileView oldBefore = await ReadFileViewAsync(session, first.Version.ResourceNodeId, ct)
                        .ConfigureAwait(false);
                    ushort xNs = session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
                    ArrayOf<DataValue> metaBefore = await ReadNativePropertiesAsync(
                        session, logical.ResourceNodeId, [new QualifiedName("MetaEpoch", xNs)], ct)
                        .ConfigureAwait(false);
                    Assert.That(metaBefore[0].WrappedValue.TryGetValue(out uint metaEpoch), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(before.VersionId, Is.EqualTo("v2"));
                        Assert.That(before.Size, Is.EqualTo(207ul));
                        Assert.That(before.OpenCount, Is.EqualTo(currentReaderOpen ? (ushort)1 : (ushort)0));
                        Assert.That(oldBefore.VersionId, Is.EqualTo("v1"));
                        Assert.That(oldBefore.Size, Is.EqualTo(167ul));
                        Assert.That(oldBefore.OpenCount, Is.EqualTo((ushort)1));
                        Assert.That(metaEpoch, Is.GreaterThan(0u));
                    });

                    var foreign = new ResourceTypeClient(m_session, logical.ResourceNodeId, m_telemetry);
                    await Assert.ThatAsync(
                        async () => await foreign.CloseAsync(oldHandle, ct).ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
                    ByteString oldBytes = await logical.Proxy.ReadAsync(oldHandle, int.MaxValue, ct)
                        .ConfigureAwait(false);
                    ulong oldPosition = await logical.Proxy.GetPositionAsync(oldHandle, ct).ConfigureAwait(false);
                    uint closedOldHandle = oldHandle;
                    await logical.Proxy.CloseAsync(oldHandle, ct).ConfigureAwait(false);
                    oldHandle = 0;

                    NativeFileView after = await ReadFileViewAsync(session, logical.ResourceNodeId, ct)
                        .ConfigureAwait(false);
                    NativeFileView oldAfter = await ReadFileViewAsync(session, first.Version.ResourceNodeId, ct)
                        .ConfigureAwait(false);
                    ArrayOf<DataValue> metaAfter = await ReadNativePropertiesAsync(
                        session, logical.ResourceNodeId, [new QualifiedName("MetaEpoch", xNs)], ct)
                        .ConfigureAwait(false);
                    Assert.That(metaAfter[0].WrappedValue.TryGetValue(out uint currentMetaEpoch), Is.True);
                    if (currentHandle == 0)
                    {
                        currentHandle = await logical.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
                    }
                    ByteString currentBytes = await logical.Proxy.ReadAsync(currentHandle, int.MaxValue, ct)
                        .ConfigureAwait(false);
                    ulong currentPosition = await logical.Proxy.GetPositionAsync(currentHandle, ct)
                        .ConfigureAwait(false);
                    Assert.That(currentHandle, Is.Not.EqualTo(closedOldHandle));
                    await logical.Proxy.CloseAsync(currentHandle, ct).ConfigureAwait(false);
                    currentHandle = 0;
                    NativeFileView closed = await ReadFileViewAsync(session, logical.ResourceNodeId, ct)
                        .ConfigureAwait(false);
                    output.WriteLine(
                        $"OldClose: currentReaderOpen={currentReaderOpen}; " +
                        $"Size={before.Size}->{after.Size}; OpenCount={before.OpenCount}->{after.OpenCount}");
                    Assert.Multiple(() =>
                    {
                        Assert.That(oldBytes, Is.EqualTo(oldDocument));
                        Assert.That(currentBytes, Is.EqualTo(currentDocument));
                        Assert.That(oldPosition, Is.EqualTo(167ul));
                        Assert.That(currentPosition, Is.EqualTo(207ul));
                        Assert.That(currentMetaEpoch, Is.EqualTo(metaEpoch));
                        Assert.That(oldAfter, Is.EqualTo(oldBefore with { OpenCount = 0 }));
                        Assert.That(after, Is.EqualTo(before),
                            "Observe the default view immediately after old Close, before another Open can repair it.");
                        Assert.That(closed, Is.EqualTo(before with { OpenCount = 0 }));
                    });
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    if (oldHandle != 0)
                    {
                        await logical.Proxy.CloseAsync(oldHandle, cleanup.Token).ConfigureAwait(false);
                    }
                    if (currentHandle != 0)
                    {
                        await logical.Proxy.CloseAsync(currentHandle, cleanup.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                StatusCode status = await session.CloseAsync(10000, true, cleanup.Token).ConfigureAwait(false);
                Assert.That(status, Is.EqualTo(StatusCodes.Good));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ClosingSessionDiscardsVersionAndLogicalWritersWithoutCommitting(bool logical)
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient version, _) = await group.CreateResourceAsync("session-discard", "v1")
                .ConfigureAwait(false);
            byte[] original = TestMaterialization.Td("urn:session-discard", "original");
            await version.Proxy.UploadAsync(ByteString.From(original)).ConfigureAwait(false);
            WotRegistryResourceClient writer = logical
                ? await group.OpenResourceAsync(AssignedResourceId("session-discard")).ConfigureAwait(false)
                : version;
            uint handle = await writer.Proxy.OpenAsync(6).ConfigureAwait(false);
            await writer.Proxy.WriteAsync(
                handle, ByteString.From(TestMaterialization.Td("urn:session-discard", "uncommitted")))
                .ConfigureAwait(false);

            using var otherFixture = new ClientFixture(false, false, m_telemetry);
            await otherFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using ISession otherSession = await otherFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                WotRegistryClient otherClient = await WotRegistryClient.ForServerAsync(otherSession, m_telemetry)
                    .ConfigureAwait(false);
                (WotRegistryGroupClient otherGroup, _) = await otherClient.GetOrCreateThingDescriptionGroupAsync()
                    .ConfigureAwait(false);
                WotRegistryResourceClient survivor = await otherGroup
                    .OpenResourceAsync(AssignedResourceId("session-discard"))
                    .ConfigureAwait(false);

                await m_session.CloseAsync().ConfigureAwait(false);
                byte[]? downloaded = null;
                Assert.That(async () => downloaded = await survivor.Proxy.DownloadAllAsync()
                    .ConfigureAwait(false), Throws.Nothing);
                Assert.That(downloaded, Is.EqualTo(original));
                if (logical)
                {
                    Assert.That(() => survivor.Proxy.CloseAsync(handle).AsTask(),
                        Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadInvalidArgument));
                }
            }
            finally
            {
                await otherSession.CloseAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ClosingSessionDiscardsOldDefaultPinsButPreservesOtherSessionReaders()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient firstVersion, _) = await group.CreateResourceAsync("session-readers", "v1")
                .ConfigureAwait(false);
            byte[] firstDocument = TestMaterialization.Td("urn:session-readers", "first");
            await firstVersion.Proxy.UploadAsync(ByteString.From(firstDocument)).ConfigureAwait(false);
            (WotRegistryResourceClient secondVersion, _) = await group.CreateResourceAsync("session-readers", "v2")
                .ConfigureAwait(false);
            byte[] secondDocument = TestMaterialization.Td("urn:session-readers", "second");
            await secondVersion.Proxy.UploadAsync(ByteString.From(secondDocument)).ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("session-readers"))
                .ConfigureAwait(false);
            await firstVersion.SetDefaultVersionAsync("v1", expectedEpoch: 0).ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v1").ConfigureAwait(false);
            uint firstPin = await logical.Proxy.OpenAsync(1).ConfigureAwait(false);
            uint firstDirect = await firstVersion.Proxy.OpenAsync(1).ConfigureAwait(false);
            await secondVersion.SetDefaultVersionAsync("v2", expectedEpoch: 0).ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v2").ConfigureAwait(false);
            uint secondPin = await logical.Proxy.OpenAsync(1).ConfigureAwait(false);
            uint secondDirect = await secondVersion.Proxy.OpenAsync(1).ConfigureAwait(false);

            using var otherFixture = new ClientFixture(false, false, m_telemetry);
            await otherFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using ISession otherSession = await otherFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                var otherFirst = new ThingDescriptionFileTypeClient(
                    otherSession, firstVersion.ResourceNodeId, m_telemetry);
                var otherSecond = new ThingDescriptionFileTypeClient(
                    otherSession, secondVersion.ResourceNodeId, m_telemetry);
                var otherLogical = new ThingDescriptionFileTypeClient(
                    otherSession, logical.ResourceNodeId, m_telemetry);
                uint retainedFirst = await otherFirst.OpenAsync(1).ConfigureAwait(false);
                uint retainedDefault = await otherLogical.OpenAsync(1).ConfigureAwait(false);

                await m_session.CloseAsync().ConfigureAwait(false);
                Assert.That(() => otherLogical.CloseAsync(firstPin).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(() => otherLogical.CloseAsync(secondPin).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(() => otherFirst.CloseAsync(firstDirect).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(() => otherSecond.CloseAsync(secondDirect).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadInvalidArgument));
                ByteString retainedFirstBytes = await otherFirst.ReadAsync(retainedFirst, int.MaxValue)
                    .ConfigureAwait(false);
                ByteString retainedDefaultBytes = await otherLogical.ReadAsync(retainedDefault, int.MaxValue)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(retainedFirstBytes.Span.ToArray(), Is.EqualTo(firstDocument));
                    Assert.That(retainedDefaultBytes.Span.ToArray(), Is.EqualTo(secondDocument));
                });

                await otherFirst.CloseAsync(retainedFirst).ConfigureAwait(false);
                await otherLogical.CloseAsync(retainedDefault).ConfigureAwait(false);
                uint availableFirst = await otherFirst.OpenAsync(6).ConfigureAwait(false);
                uint availableDefault = await otherLogical.OpenAsync(6).ConfigureAwait(false);
                await otherFirst.CloseAsync(availableFirst).ConfigureAwait(false);
                await otherLogical.CloseAsync(availableDefault).ConfigureAwait(false);
            }
            finally
            {
                await otherSession.CloseAsync().ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LateOpenCannotReserveAHandleAfterSessionDiscard(bool logical)
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient version, _) = await group.CreateResourceAsync("late-open", "v1")
                .ConfigureAwait(false);
            await version.Proxy.UploadAsync(ByteString.From(TestMaterialization.Td("urn:late-open")))
                .ConfigureAwait(false);
            WotRegistryResourceClient resource = logical
                ? await group.OpenResourceAsync(AssignedResourceId("late-open")).ConfigureAwait(false)
                : version;
            FileState file = m_nodeManager.FindPredefinedNode<FileState>(resource.ResourceNodeId)!;
            OpenMethodStateMethodAsyncCallHandler original = file.Open!.OnCallAsync!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            file.Open.OnCall = null;
            file.Open.OnCallAsync = async (context, method, objectId, mode, ct) =>
            {
                entered.TrySetResult(true);
                // Complete the delayed Open after discard, independent of caller cancellation.
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                return await original(context, method, objectId, mode, CancellationToken.None).ConfigureAwait(false);
            };

            Task<uint> opening = resource.Proxy.OpenAsync(1).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            try
            {
                await m_server.CurrentInstance.CloseSessionAsync(
                    null!, m_session.SessionId, deleteSubscriptions: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.That(() => opening,
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSessionClosed));

            using var otherFixture = new ClientFixture(false, false, m_telemetry);
            await otherFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using ISession otherSession = await otherFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                var other = new ThingDescriptionFileTypeClient(otherSession, version.ResourceNodeId, m_telemetry);
                uint writer = await other.OpenAsync(6).ConfigureAwait(false);
                await other.CloseAsync(writer).ConfigureAwait(false);
            }
            finally
            {
                await otherSession.CloseAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task UnscopedCloseCannotConsumeTheOwnersLogicalPin()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient version, _) = await group.CreateResourceAsync("unscoped-pin", "v1")
                .ConfigureAwait(false);
            await version.Proxy.UploadAsync(ByteString.From(TestMaterialization.Td("urn:unscoped-pin")))
                .ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("unscoped-pin"))
                .ConfigureAwait(false);
            uint handle = await logical.Proxy.OpenAsync(6).ConfigureAwait(false);
            FileState file = m_nodeManager.FindPredefinedNode<FileState>(logical.ResourceNodeId)!;

            CloseMethodStateResult unscoped = await file.Close!.OnCallAsync!(
                m_nodeManager.SystemContext, file.Close, file.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(unscoped.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            byte[] document = TestMaterialization.Td("urn:unscoped-pin", "owner committed");
            Assert.That(async () =>
            {
                await logical.Proxy.WriteAsync(handle, ByteString.From(document)).ConfigureAwait(false);
                await logical.Proxy.CloseAsync(handle).ConfigureAwait(false);
            }, Throws.Nothing);
            byte[] downloaded = await logical.Proxy.DownloadAllAsync().ConfigureAwait(false);

            Assert.That(downloaded, Is.EqualTo(document));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LateCreateCannotReturnAnOpenHandleAfterSessionDiscard(bool getOrCreate)
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            GroupState node = m_nodeManager.FindPredefinedNode<GroupState>(group.GroupNodeId)!;
            MethodState method = getOrCreate ? node.GetOrCreateResource! : node.CreateResource!;
            GenericMethodCalledEventHandler2Async original = method.OnCallMethod2Async!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            method.OnCallMethod2Async = async (context, called, objectId, input, output, ct) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                return await original(context, called, objectId, input, output, CancellationToken.None)
                    .ConfigureAwait(false);
            };

            Task creating = getOrCreate
                ? group.Proxy.GetOrCreateResourceAsync("late-create", "v1", requestFileOpen: true).AsTask()
                : group.Proxy.CreateResourceAsync("late-create", "v1", requestFileOpen: true).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            try
            {
                await m_server.CurrentInstance.CloseSessionAsync(
                    null!, m_session.SessionId, deleteSubscriptions: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.That(() => creating,
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSessionClosed));

            using var otherFixture = new ClientFixture(false, false, m_telemetry);
            await otherFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using ISession otherSession = await otherFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                WotRegistryClient otherClient = await WotRegistryClient.ForServerAsync(otherSession, m_telemetry)
                    .ConfigureAwait(false);
                (WotRegistryGroupClient otherGroup, _) = await otherClient.GetOrCreateThingDescriptionGroupAsync()
                    .ConfigureAwait(false);
                (WotRegistryResourceClient resource, _, _) = await otherGroup
                    .GetOrCreateResourceAsync("late-create", "v1")
                    .ConfigureAwait(false);
                uint available = await resource.Proxy.OpenAsync(6).ConfigureAwait(false);
                await resource.Proxy.CloseAsync(available).ConfigureAwait(false);
            }
            finally
            {
                await otherSession.CloseAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SessionClosingCannotBeCancelledBeforeDiscardingItsHandles()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient version, _) = await group.CreateResourceAsync("cancel-discard", "v1")
                .ConfigureAwait(false);
            byte[] original = TestMaterialization.Td("urn:cancel-discard", "original");
            await version.Proxy.UploadAsync(ByteString.From(original)).ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("cancel-discard"))
                .ConfigureAwait(false);
            uint handle = await logical.Proxy.OpenAsync(6).ConfigureAwait(false);
            await logical.Proxy.WriteAsync(
                handle, ByteString.From(TestMaterialization.Td("urn:cancel-discard", "uncommitted")))
                .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(async () => await m_nodeManager.SessionClosingAsync(
                    null!, m_session.SessionId, deleteSubscriptions: true, cancellation.Token)
                        .ConfigureAwait(false), Throws.Nothing).ConfigureAwait(false);
            byte[] downloaded = await logical.Proxy.DownloadAllAsync().ConfigureAwait(false);

            Assert.That(downloaded, Is.EqualTo(original));
            Assert.That(() => logical.Proxy.CloseAsync(handle).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task DeletingLogicalDefaultResourceRemovesAllVersions()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            _ = await CreateCommittedVersionsAsync(group, "delete-logical")
                .ConfigureAwait(false);
            WotRegistryResourceClient logical = await group
                .OpenResourceAsync(AssignedResourceId("delete-logical"))
                .ConfigureAwait(false);
            WotResource before = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-logical")!;

            await logical.DeleteAsync(checked((uint)before.MetaEpoch)).ConfigureAwait(false);

            Assert.That(
                FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-logical"),
                Is.Null);
        }

        [Test]
        public async Task DeletingNonDefaultVersionPreservesLogicalResource()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (_, WotRegistryResourceClient v2) = await CreateCommittedVersionsAsync(
                    group,
                    "delete-version")
                .ConfigureAwait(false);
            WotResource before = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-version")!;

            await v2.DeleteAsync(
                    checked((uint)before.FindVersion("v2")!.Epoch))
                .ConfigureAwait(false);

            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-version")!;
            Assert.Multiple(() =>
            {
                Assert.That(stored.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(stored.Versions, Has.Length.EqualTo(1));
                Assert.That(stored.Versions[0].VersionId, Is.EqualTo("v1"));
            });
        }

        [Test]
        public async Task DeletingVersionWhoseIdMatchesResourceIdUsesExactVersionRole()
        {
            const string ResourceId = "collision-delete";
            const string VersionId = "urn.collision-delete";
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient v1Version, WotRegistryResourceClient exactVersion) =
                await CreateCommittedVersionsAsync(
                    group,
                    ResourceId,
                    secondVersionId: VersionId)
                .ConfigureAwait(false);
            Assert.That(exactVersion.ResourceId, Is.EqualTo(VersionId));
            WotResource before = FindResource(
                WotRegistryGroups.ThingDescriptions,
                ResourceId)!;

            await exactVersion.DeleteAsync(
                    checked((uint)before.FindVersion(VersionId)!.Epoch))
                .ConfigureAwait(false);

            WotResource afterVersionDelete = FindResource(
                WotRegistryGroups.ThingDescriptions,
                ResourceId)!;
            Assert.Multiple(() =>
            {
                Assert.That(afterVersionDelete.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(afterVersionDelete.FindVersion(VersionId), Is.Null);
                Assert.That(afterVersionDelete.FindVersion("v1"), Is.Not.Null);
            });

            // Use the logical resource node (not a version node) for resource-level delete.
            WotRegistryResourceClient logical = await group
                .OpenResourceAsync(AssignedResourceId(ResourceId))
                .ConfigureAwait(false);
            await logical.DeleteAsync(checked((uint)afterVersionDelete.MetaEpoch))
                .ConfigureAwait(false);

            Assert.That(
                FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    ResourceId),
                Is.Null);
        }

        [Test]
        public async Task DeleteRoutingTracksDefaultSwitchForExistingVersionNodes()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient v1, WotRegistryResourceClient v2) =
                await CreateCommittedVersionsAsync(
                    group,
                    "delete-switched")
                .ConfigureAwait(false);
            WotResource beforeSwitch = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-switched")!;

            await v1.SetDefaultVersionAsync(
                    "v2",
                    checked((uint)beforeSwitch.MetaEpoch))
                .ConfigureAwait(false);

            WotResource afterSwitch = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-switched")!;
            WotRegistryResourceClient logical = await group
                .OpenResourceAsync(AssignedResourceId("delete-switched"))
                .ConfigureAwait(false);
            // In the new hierarchy, the logical resource has a stable NodeId
            // that does not change with SetDefaultVersion.
            Assert.That(logical.ResourceNodeId, Is.Not.EqualTo(v2.ResourceNodeId));

            await v1.DeleteAsync(
                    checked((uint)afterSwitch.FindVersion("v1")!.Epoch))
                .ConfigureAwait(false);
            WotResource afterOldDefaultDelete = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-switched")!;
            Assert.Multiple(() =>
            {
                Assert.That(afterOldDefaultDelete.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(afterOldDefaultDelete.Versions, Has.Length.EqualTo(1));
                Assert.That(afterOldDefaultDelete.Versions[0].VersionId, Is.EqualTo("v2"));
            });

            // Delete the logical resource (not the version) to remove all versions.
            await logical.DeleteAsync(checked((uint)afterOldDefaultDelete.MetaEpoch))
                .ConfigureAwait(false);

            Assert.That(
                FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-switched"),
                Is.Null);
        }

        /// <summary>
        /// Covers <c>OnGetOrCreateResourceAsync</c> existing path: the second call
        /// returns the existing resource with <c>Created == false</c>.
        /// </summary>
        [Test]
        public async Task GetOrCreateResourceReturnsExistingOnSecondCall()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            _ = await group.GetOrCreateResourceAsync("existing-res").ConfigureAwait(false);

            (_, _, bool created) = await group
                .GetOrCreateResourceAsync("existing-res")
                .ConfigureAwait(false);

            Assert.That(created, Is.False,
                "Second call must report the resource as pre-existing.");
        }

        [Test]
        public async Task GetOrCreateGroupReturnsExistingWhenNameRequiresSlugification()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);

            (_, bool firstCreated) = await client.GetOrCreateGroupAsync("MyGroup")
                .ConfigureAwait(false);
            (_, bool secondCreated) = await client.GetOrCreateGroupAsync("MyGroup")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(firstCreated, Is.True);
                Assert.That(secondCreated, Is.False);
            });
        }

        [Test]
        public async Task ConcurrentGetOrCreateResourceOpenAndReconcileDoesNotRace()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);

            Task[] tasks = [.. Enumerable.Range(0, 12)
                .Select(i => group.Proxy
                    .GetOrCreateResourceAsync(
                        "race-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        string.Empty,
                        requestFileOpen: true)
                    .AsTask())];

            Assert.That(
                async () => await Task.WhenAll(tasks).ConfigureAwait(false),
                Throws.Nothing);
        }

        /// <summary>
        /// Covers <c>CommitDocumentAsync</c> success path: the chunked upload
        /// triggers commit-on-close, and the content is immediately downloadable.
        /// </summary>
        [Test]
        public async Task UploadNewVersionPersistsDocumentViaClosedFileHandle()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            byte[] expected = MakeThingDescriptionBytes("td-01");
            ByteString downloaded = await resource.DownloadAsync().ConfigureAwait(false);

            Assert.That(downloaded.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public async Task UploadNewVersionAllocatesVersionWithoutSwitchingDefault()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);
            WotResource before = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "td-01")!;
            string defaultVersionId = before.DefaultVersionId!;

            byte[] second = Encoding.UTF8.GetBytes(MakeThingDescriptionStringV2("td-01"));
            await resource.UploadNewVersionAsync(ByteString.From(second)).ConfigureAwait(false);

            WotResource after = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "td-01")!;
            WotResourceVersion newVersion = after.Versions.Single(
                version => !string.Equals(
                    version.VersionId,
                    defaultVersionId,
                    StringComparison.Ordinal));
            ByteString stored = await m_registry.ReadContentAsync(newVersion).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(after.Versions, Has.Length.EqualTo(2));
                Assert.That(after.DefaultVersionId, Is.EqualTo(defaultVersionId));
                Assert.That(after.DesiredVersionId, Is.EqualTo(defaultVersionId));
                Assert.That(stored.ToArray(), Is.EqualTo(second));
            });
        }

        [Test]
        public async Task RetryingContentlessPlaceholderFillsSameDefaultVersion()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient _, string placeholderId, bool firstCreated) =
                await group.GetOrCreateResourceAsync("retry-placeholder")
                    .ConfigureAwait(false);
            (WotRegistryResourceClient retry, string retryId, bool retryCreated) =
                await group.GetOrCreateResourceAsync("retry-placeholder")
                    .ConfigureAwait(false);

            await retry.UploadNewVersionAsync(
                    ByteString.From(MakeThingDescriptionBytes("retry-placeholder")))
                .ConfigureAwait(false);
            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "retry-placeholder")!;

            Assert.Multiple(() =>
            {
                Assert.That(firstCreated, Is.True);
                Assert.That(retryCreated, Is.False);
                Assert.That(retryId, Is.EqualTo(placeholderId));
                Assert.That(stored.Versions, Has.Length.EqualTo(1));
                Assert.That(stored.DefaultVersionId, Is.EqualTo(placeholderId));
                Assert.That(stored.FindVersion(placeholderId)!.HasContent, Is.True);
            });
        }

        [Test]
        public async Task StalePlaceholderRetryPreservesConcurrentFillAndAllocatesNewVersion()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient clientB, string placeholderId, bool created) =
                await group.GetOrCreateResourceAsync("atomic-retry")
                    .ConfigureAwait(false);
            (WotRegistryResourceClient clientA, string observedId, bool observedCreated) =
                await group.GetOrCreateResourceAsync("atomic-retry")
                    .ConfigureAwait(false);
            byte[] contentB = TestMaterialization.Td("urn:atomic-retry", "client-b");
            byte[] contentA = TestMaterialization.Td("urn:atomic-retry", "client-a");

            WotRegistryUploadResult uploadB = await clientB
                .UploadNewVersionAndGetResultAsync(ByteString.From(contentB))
                .ConfigureAwait(false);
            WotRegistryUploadResult uploadA = await clientA
                .UploadNewVersionAndGetResultAsync(ByteString.From(contentA))
                .ConfigureAwait(false);

            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "atomic-retry")!;
            WotResourceVersion placeholder = stored.FindVersion(placeholderId)!;
            WotResourceVersion allocated = stored.FindVersion(uploadA.VersionId)!;
            ByteString storedB = await m_registry.ReadContentAsync(placeholder)
                .ConfigureAwait(false);
            ByteString storedA = await m_registry.ReadContentAsync(allocated)
                .ConfigureAwait(false);
            ushort ns = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            var expectedAllocatedNodeId = new NodeId(
                $"WoTRegistry/groups/{group.GroupId}/resources/" +
                $"{clientA.ResourceId}/versions/{uploadA.VersionId}",
                ns);

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.True);
                Assert.That(observedCreated, Is.False);
                Assert.That(observedId, Is.EqualTo(placeholderId));
                Assert.That(uploadB.VersionId, Is.EqualTo(placeholderId));
                Assert.That(uploadB.ResourceNodeId, Is.EqualTo(clientB.ResourceNodeId));
                Assert.That(uploadA.VersionId, Is.Not.EqualTo(placeholderId));
                Assert.That(uploadA.ResourceNodeId, Is.EqualTo(expectedAllocatedNodeId));
                Assert.That(uploadA.ResourceNodeId, Is.Not.EqualTo(clientA.ResourceNodeId));
                Assert.That(stored.Versions, Has.Length.EqualTo(2));
                Assert.That(stored.DefaultVersionId, Is.EqualTo(placeholderId));
                Assert.That(stored.DesiredVersionId, Is.EqualTo(placeholderId));
                Assert.That(storedB.ToArray(), Is.EqualTo(contentB));
                Assert.That(storedA.ToArray(), Is.EqualTo(contentA));
            });
        }

        [Test]
        public async Task BulkLoadReportsVersionThatActuallyReceivedBytes()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient v1, string v1Id) =
                await group.CreateResourceAsync("bulk-version", "v1")
                .ConfigureAwait(false);
            await v1.Proxy.UploadAsync(
                    ByteString.From(MakeThingDescriptionBytes("bulk-version")))
                .ConfigureAwait(false);

            WotRegistryBulkLoadResult result = await client.LoadDocumentsAsync(
                new[]
                {
                    new WotRegistryDocument(
                        WoTDocumentKindEnum.ThingDescription,
                        WotRegistryGroups.ThingDescriptions,
                        "bulk-version",
                        ByteString.From(Encoding.UTF8.GetBytes(
                            MakeThingDescriptionStringV2("bulk-version"))))
                }.ToArrayOf(),
                refresh: false).ConfigureAwait(false);
            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "bulk-version")!;
            WotResourceVersion uploaded = stored.Versions.Single(version =>
                !string.Equals(version.VersionId, v1Id, StringComparison.Ordinal));
            ushort ns = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            var expectedNodeId = new NodeId(
                $"WoTRegistry/groups/{group.GroupId}/resources/" +
                $"{stored.ResourceId}/versions/{uploaded.VersionId}",
                ns);

            Assert.Multiple(() =>
            {
                Assert.That(stored.Versions, Has.Length.EqualTo(2));
                Assert.That(stored.DefaultVersionId, Is.EqualTo(v1Id));
                Assert.That(uploaded.HasContent, Is.True);
                Assert.That(result.Uploaded, Has.Count.EqualTo(1));
                Assert.That(result.Uploaded[0].VersionId, Is.EqualTo(uploaded.VersionId));
                Assert.That(result.Uploaded[0].ResourceNodeId, Is.EqualTo(expectedNodeId));
                Assert.That(result.Uploaded[0].ResourceNodeId, Is.Not.EqualTo(v1.ResourceNodeId));
            });
        }

        [Test]
        public async Task DefaultSwitchProjectsEachVersionsOwnDocumentTitle()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient v1, _) =
                await group.CreateResourceAsync("version-titles", "v1")
                .ConfigureAwait(false);
            await v1.Proxy.UploadAsync(
                    ByteString.From(ThingDescriptionWithMetadata(
                        "urn:version-titles",
                        "urn:version-titles-first",
                        "https://example.test/first/")))
                .ConfigureAwait(false);
            (WotRegistryResourceClient v2, _) =
                await group.CreateResourceAsync("version-titles", "v2")
                .ConfigureAwait(false);
            await v2.Proxy.UploadAsync(
                    ByteString.From(ThingDescriptionWithMetadata(
                        "urn:version-titles",
                        "urn:version-titles-second",
                        "https://example.test/second/")))
                .ConfigureAwait(false);

            await v2.SetDefaultVersionAsync("v2", expectedEpoch: 0)
                .ConfigureAwait(false);
            string v1Title = await ReadWotChildValueAsync<string>(
                    v1.ResourceNodeId,
                    BrowseNames.ThingTitle)
                .ConfigureAwait(false);
            string v2Title = await ReadWotChildValueAsync<string>(
                    v2.ResourceNodeId,
                    BrowseNames.ThingTitle)
                .ConfigureAwait(false);
            string v1BaseUri = await ReadWotChildValueAsync<string>(
                    v1.ResourceNodeId,
                    BrowseNames.BaseUri)
                .ConfigureAwait(false);
            string v2BaseUri = await ReadWotChildValueAsync<string>(
                    v2.ResourceNodeId,
                    BrowseNames.BaseUri)
                .ConfigureAwait(false);
            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "version-titles")!;

            Assert.Multiple(() =>
            {
                Assert.That(stored.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(stored.Title, Is.EqualTo("urn:version-titles-second"));
                Assert.That(v1Title, Is.EqualTo("urn:version-titles-first"));
                Assert.That(v2Title, Is.EqualTo("urn:version-titles-second"));
                Assert.That(v1BaseUri, Is.EqualTo("https://example.test/first/"));
                Assert.That(v2BaseUri, Is.EqualTo("https://example.test/second/"));
            });
        }

        [Test]
        public async Task ThingModelVersionsProjectTheirOwnModelTitles()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingModelGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient v1, _) =
                await group.CreateResourceAsync("model-titles", "v1")
                .ConfigureAwait(false);
            await v1.Proxy.UploadAsync(
                    ByteString.From(ThingModelWithMetadata(
                        "urn:model-titles",
                        "urn:model-titles-first",
                        "1.0.0")))
                .ConfigureAwait(false);
            (WotRegistryResourceClient v2, _) =
                await group.CreateResourceAsync("model-titles", "v2")
                .ConfigureAwait(false);
            await v2.Proxy.UploadAsync(
                    ByteString.From(ThingModelWithMetadata(
                        "urn:model-titles",
                        "urn:model-titles-second",
                        "2.0.0")))
                .ConfigureAwait(false);

            await v2.SetDefaultVersionAsync("v2", expectedEpoch: 0)
                .ConfigureAwait(false);
            string v1Title = await ReadWotChildValueAsync<string>(
                    v1.ResourceNodeId,
                    BrowseNames.ModelTitle)
                .ConfigureAwait(false);
            string v2Title = await ReadWotChildValueAsync<string>(
                    v2.ResourceNodeId,
                    BrowseNames.ModelTitle)
                .ConfigureAwait(false);
            string v1ModelVersion = await ReadWotChildValueAsync<string>(
                    v1.ResourceNodeId,
                    BrowseNames.ModelVersion)
                .ConfigureAwait(false);
            string v2ModelVersion = await ReadWotChildValueAsync<string>(
                    v2.ResourceNodeId,
                    BrowseNames.ModelVersion)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(v1Title, Is.EqualTo("urn:model-titles-first"));
                Assert.That(v2Title, Is.EqualTo("urn:model-titles-second"));
                Assert.That(v1ModelVersion, Is.EqualTo("1.0.0"));
                Assert.That(v2ModelVersion, Is.EqualTo("2.0.0"));
                Assert.That(
                    FindResource(
                        WotRegistryGroups.ThingModels,
                        "model-titles")!.Title,
                    Is.EqualTo("urn:model-titles-second"));
            });
        }

        [Test]
        public async Task ConcreteVersionWriteReplacesOnlyThatVersionAndPreservesDefault()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient v1, _) = await group
                .CreateResourceAsync("replace", "v1")
                .ConfigureAwait(false);
            byte[] v1Content = TestMaterialization.Td("urn:replace", "v1");
            await v1.Proxy.UploadAsync(ByteString.From(v1Content)).ConfigureAwait(false);
            (WotRegistryResourceClient v2, _) = await group
                .CreateResourceAsync("replace", "v2")
                .ConfigureAwait(false);
            await v2.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:replace", "v2")))
                .ConfigureAwait(false);
            await v2.SetDefaultVersionAsync("v1", expectedEpoch: 0).ConfigureAwait(false);

            byte[] replacement = TestMaterialization.Td("urn:replace", "v2-replaced");
            await v2.Proxy.UploadAsync(ByteString.From(replacement)).ConfigureAwait(false);

            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "replace")!;
            ByteString storedV1 = await m_registry.ReadContentAsync(
                stored.FindVersion("v1")!).ConfigureAwait(false);
            ByteString storedV2 = await m_registry.ReadContentAsync(
                stored.FindVersion("v2")!).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stored.Versions, Has.Length.EqualTo(2));
                Assert.That(stored.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(storedV1.ToArray(), Is.EqualTo(v1Content));
                Assert.That(storedV2.ToArray(), Is.EqualTo(replacement));
            });
        }

        [Test]
        public async Task ClientUploadContinuesPastVersionRetentionLimit()
        {
            m_registry.Bounds.MaxVersionsPerResource = 2;
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group
                .CreateResourceAsync("retained-client", "v1")
                .ConfigureAwait(false);
            await first.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:retained-client", "first")))
                .ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group
                .CreateResourceAsync("retained-client", "v2")
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:retained-client", "second")))
                .ConfigureAwait(false);

            byte[] thirdContent = TestMaterialization.Td("urn:retained-client", "third");
            WotRegistryUploadResult uploaded = await second
                .UploadNewVersionAndGetResultAsync(ByteString.From(thirdContent))
                .ConfigureAwait(false);
            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "retained-client")!;
            ByteString storedContent = await m_registry
                .ReadContentAsync(stored.FindVersion(uploaded.VersionId)!)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(stored.Versions, Has.Length.EqualTo(2));
                Assert.That(stored.FindVersion("v1"), Is.Not.Null);
                Assert.That(stored.FindVersion("v2"), Is.Null);
                Assert.That(stored.FindVersion(uploaded.VersionId), Is.Not.Null);
                Assert.That(storedContent.ToArray(), Is.EqualTo(thirdContent));
            });
        }

        [Test]
        public async Task OpenResourceSelectsDefaultWhenResourceAndVersionBrowseNamesCollide()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient collidingVersion, _) = await group
                .CreateResourceAsync("collision", "urn.collision")
                .ConfigureAwait(false);
            Assert.That(collidingVersion.ResourceId, Is.EqualTo(collidingVersion.VersionId));
            await collidingVersion.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:collision", "old")))
                .ConfigureAwait(false);
            (WotRegistryResourceClient defaultVersion, _) = await group
                .CreateResourceAsync("collision", "v2")
                .ConfigureAwait(false);
            await defaultVersion.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:collision", "default")))
                .ConfigureAwait(false);
            await defaultVersion.SetDefaultVersionAsync("v2", expectedEpoch: 0)
                .ConfigureAwait(false);

            WotRegistryResourceClient opened = await group
                .OpenResourceAsync(AssignedResourceId("collision"))
                .ConfigureAwait(false);

            // In the new hierarchy, OpenResource returns the stable logical
            // resource node, not the default version's version node.
            Assert.That(
                opened.ResourceNodeId,
                Is.Not.EqualTo(collidingVersion.ResourceNodeId));
            Assert.That(
                opened.ResourceNodeId,
                Is.Not.EqualTo(defaultVersion.ResourceNodeId));
        }

        /// <summary>
        /// Covers <c>OnValidateAsync</c> success path: calling Validate after
        /// uploading content returns a non-null outcome.
        /// </summary>
        [Test]
        public async Task ValidateResourceAfterUploadReturnsOutcome()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            WoTValidationOutcomeDataType outcome = await resource
                .ValidateAsync()
                .ConfigureAwait(false);

            Assert.That(outcome, Is.Not.Null);
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.ThingModel)]
        public async Task FullRegistryFileUploadStoresAnExplicitProjectionAsAPlan(WoTDocumentKindEnum kind)
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = kind == WoTDocumentKindEnum.ThingModel
                ? await client.CreateThingModelGroupAsync().ConfigureAwait(false)
                : await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group
                .CreateDocumentResourceAsync("urn:uploaded:plan", "v1").ConfigureAwait(false);
            WotRegistryResourceClient resource = allocation.Version;
            string resultKind = kind == WoTDocumentKindEnum.ThingModel ? "ThingModel" : "ThingDescription";
            string json = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:projection\",\"uav:projectionKind\":\"" +
                resultKind +
                "\"," +
                "\"id\":\"urn:uploaded:plan\",\"title\":\"Plan\",\"uav:scenario\":\"urn:scenario\"," +
                "\"uav:projects\":[{\"uav:sourceName\":\"source\",\"href\":\"urn:source\"," +
                "\"type\":\"application/td+json\",\"uav:selectAll\":true}]}";
            var content = ByteString.From(Encoding.UTF8.GetBytes(json));

            await resource.Proxy.UploadAsync(content).ConfigureAwait(false);

            WotResource stored = m_registry.Current.FindResource(group.GroupId, resource.ResourceId)!;
            WotResourceVersion version = stored.FindVersion("v1")!;
            Assert.That(stored.SourceId, Is.EqualTo("urn:uploaded:plan"));
            Assert.That(stored.Kind, Is.EqualTo(kind));
            Assert.That(version.Format, Is.EqualTo(Wot.WotProjection.Format));
            Assert.That(version.ContentType, Is.EqualTo(Wot.WotProjection.ContentType));
            ByteString downloaded = await resource.DownloadAsync().ConfigureAwait(false);
            Assert.That(downloaded, Is.EqualTo(content));
            WoTValidationOutcomeDataType validation = await resource.ValidateAsync().ConfigureAwait(false);
            Assert.That(validation.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Skipped));
        }

        [Test]
        public async Task FullRegistryFileUploadDoesNotImplicitlySelectLegacyPlanProcessing()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group
                .CreateThingDescriptionResourceAsync("urn:draft-plan", "v1").ConfigureAwait(false);
            WotRegistryResourceClient resource = allocation.Version;
            var content = ByteString.From(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                {
                  "@context":"https://www.w3.org/2022/wot/td/v1.1","@type":["Thing","uav:projection"],
                  "id":"urn:draft-plan","title":"Plan","uav:scenario":"urn:scenario",
                  "uav:projects":[
                    {"uav:sourceName":"source","href":"urn:source","type":"application/td+json","uav:selectAll":true}
                  ]
                }
                """));

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await resource.Proxy.UploadAsync(content).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            WotResource stored = m_registry.Current.FindResource(group.GroupId, resource.ResourceId)!;
            Assert.That(stored.SourceId, Is.EqualTo("urn:draft-plan"));
            WotResourceVersion version = stored.FindVersion("v1")!;
            Assert.That(version.HasContent, Is.False);
            Assert.That(version.Digest.IsEmpty, Is.True);
        }

        [Test]
        public async Task ValidateOnConcreteNonDefaultVersionTargetsThatVersion()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient v1, _) = await group
                .CreateResourceAsync("validate-version", "v1")
                .ConfigureAwait(false);
            await v1.Proxy.UploadAsync(
                ByteString.From(MakeThingDescriptionBytes("validate-version")))
                .ConfigureAwait(false);
            (WotRegistryResourceClient v2, _) = await group
                .CreateResourceAsync("validate-version", "v2")
                .ConfigureAwait(false);
            await v2.Proxy.UploadAsync(
                ByteString.From(Encoding.UTF8.GetBytes(MakeThingDescriptionStringV2("validate-version"))))
                .ConfigureAwait(false);
            await v2.SetDefaultVersionAsync("v1", expectedEpoch: 0).ConfigureAwait(false);

            WoTValidationOutcomeDataType outcome = await v2.ValidateAsync().ConfigureAwait(false);
            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "validate-version")!;

            Assert.Multiple(() =>
            {
                Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Skipped));
                Assert.That(stored.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(stored.FindVersion("v1")!.Validation, Is.Null);
                Assert.That(
                    stored.FindVersion("v2")!.Validation!.FormatOutcome,
                    Is.EqualTo(WoTOutcomeEnum.Skipped));
            });
        }

        /// <summary>
        /// Records whether the session namespace table and the transport
        /// channel's message-context namespace table are the same instance, and
        /// whether both actually contain the application-level xRegistry
        /// namespace after <c>FetchNamespaceTablesAsync</c>.
        /// <see cref="ObjectTypeClient.ResolveChildNodeIdAsync"/> resolves child
        /// browse names through <c>Session.MessageContext.NamespaceUris</c>, so
        /// this is the invariant the source-generated typed child accessors
        /// depend on.
        /// </summary>
        [Test]
        public void SessionAndMessageContextShareTheNamespaceTable()
        {
            NamespaceTable sessionTable = m_session.NamespaceUris;
            NamespaceTable channelTable = m_session.MessageContext.NamespaceUris;

            int sessionIndex = sessionTable.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri);
            int channelIndex = channelTable.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri);

            Assert.Multiple(() =>
            {
                Assert.That(
                    ReferenceEquals(sessionTable, channelTable),
                    Is.True,
                    "Session.NamespaceUris and Session.MessageContext.NamespaceUris must be the " +
                    "same table, otherwise FetchNamespaceTablesAsync updates only one of them and " +
                    "ObjectTypeClient.ResolveChildNodeIdAsync cannot resolve application namespaces.");
                Assert.That(
                    sessionIndex,
                    Is.GreaterThanOrEqualTo(0),
                    "the session namespace table must contain the xRegistry namespace");
                Assert.That(
                    channelIndex,
                    Is.EqualTo(sessionIndex),
                    "both tables must resolve the xRegistry namespace to the same index");
            });
        }

        /// <summary>
        /// Covers the Version-label success path and the
        /// <c>ToServiceResult(Success)</c> arm. Uses direct session Call so that
        /// the xRegistry namespace index is resolved from the session namespace table
        /// (populated by <c>FetchNamespaceTablesAsync</c>) rather than the transport
        /// channel's message context (which may not have been updated).
        /// </summary>
        [Test]
        public async Task AddVersionLabelOverOpcUaSucceeds()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            NodeId labelsNodeId = await BrowseForChildNodeIdAsync(resource.ResourceNodeId, "Labels")
                .ConfigureAwait(false);
            Assert.That(labelsNodeId.IsNull, Is.False, "Labels node must be browsable via HasComponent.");

            ushort xNs = m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            ServiceResultException? ex = null;
            try
            {
                // AddAttribute type-declaration NodeId is ns=xRegistry;i=63501.
                // Epoch 0 means no epoch check.
                await m_session.CallAsync(
                    labelsNodeId, new NodeId(63501u, xNs), default,
                    new Variant("env"), new Variant("prod"), new Variant(0u))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Null, "A valid label add must succeed without exception.");
            WotResource? stored = FindResource(
                WotRegistryGroups.ThingDescriptions, "td-01");
            Assert.That(stored, Is.Not.Null);
            WotResourceVersion version = stored!.DefaultVersion!;
            Assert.That(version.Labels.ContainsKey("env"), Is.True);
            Assert.That(version.Labels["env"], Is.EqualTo("prod"));
        }

        [Test]
        public async Task AddResourceMetaLabelOverOpcUaSucceeds()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group;
            (group, _) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            // MetaLabels lives on the logical Resource node, not the Version
            // node returned by CreateResource. Open the logical resource.
            WotRegistryResourceClient logical = await group
                .OpenResourceAsync(AssignedResourceId("td-01"))
                .ConfigureAwait(false);

            NodeId labelsNodeId = await BrowseForChildNodeIdAsync(
                logical.ResourceNodeId,
                "MetaLabels").ConfigureAwait(false);
            Assert.That(labelsNodeId.IsNull, Is.False);

            ushort xNs = m_session.NamespaceUris.GetIndexOrAppend(
                XRegistryWellKnown.XRegistryNamespaceUri);
            await m_session.CallAsync(
                labelsNodeId,
                new NodeId(63501u, xNs),
                default,
                new Variant("owner"),
                new Variant("plant-1"),
                new Variant(0u)).ConfigureAwait(false);

            WotResource stored = FindResource(
                WotRegistryGroups.ThingDescriptions,
                "td-01")!;
            Assert.That(stored.MetaLabels["owner"], Is.EqualTo("plant-1"));
            Assert.That(stored.DefaultVersion!.Labels.ContainsKey("owner"), Is.False);
        }

        /// <summary>
        /// Covers the <c>catch (ServiceResultException ex)</c> branch in
        /// <c>OnAddResourceLabelAsync</c>: an empty key is rejected by
        /// <c>WotLabelValidator</c> which throws a <c>ServiceResultException</c>
        /// that is caught and surfaced as a service error.
        /// </summary>
        [Test]
        public async Task AddResourceLabelWithInvalidKeyPropagatesAsServiceError()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            NodeId labelsNodeId = await BrowseForChildNodeIdAsync(resource.ResourceNodeId, "Labels")
                .ConfigureAwait(false);
            Assert.That(labelsNodeId.IsNull, Is.False, "Labels node must be browsable via HasComponent.");

            ushort xNs = m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            ServiceResultException? ex = null;
            try
            {
                await m_session.CallAsync(
                    labelsNodeId, new NodeId(63501u, xNs), default,
                    new Variant(string.Empty), new Variant("value"), new Variant(0u))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Not.Null, "Empty label key must be rejected by the server.");
        }

        /// <summary>
        /// Covers <c>ToServiceResult(Rejected)</c> arm: an epoch mismatch causes
        /// the registry to return <c>WoTOutcomeEnum.Rejected</c>, which maps to
        /// <c>BadInvalidState</c>.
        /// </summary>
        [Test]
        public async Task AddResourceLabelWithWrongEpochReturnsBadInvalidState()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            NodeId labelsNodeId = await BrowseForChildNodeIdAsync(resource.ResourceNodeId, "Labels")
                .ConfigureAwait(false);
            Assert.That(labelsNodeId.IsNull, Is.False, "Labels node must be browsable via HasComponent.");

            ushort xNs = m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            ServiceResultException? ex = null;
            try
            {
                // Epoch 999999 will not match the current resource epoch (which starts at 1).
                await m_session.CallAsync(
                    labelsNodeId, new NodeId(63501u, xNs), default,
                    new Variant("env"), new Variant("value"), new Variant(999999u))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Not.Null, "Epoch mismatch must result in an error.");
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Covers <c>ToServiceResult</c> default/Failed arm: removing a label that
        /// does not exist causes <c>RemoveResourceLabelAsync</c> to return
        /// <c>WoTOutcomeEnum.Failed</c> which maps to <c>BadNodeIdUnknown</c>.
        /// </summary>
        [Test]
        public async Task RemoveResourceLabelWithMissingKeyReturnsBadNodeIdUnknown()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            NodeId labelsNodeId = await BrowseForChildNodeIdAsync(resource.ResourceNodeId, "Labels")
                .ConfigureAwait(false);
            Assert.That(labelsNodeId.IsNull, Is.False, "Labels node must be browsable via HasComponent.");

            ushort xNs = m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            ServiceResultException? ex = null;
            try
            {
                // RemoveAttribute type-declaration NodeId is ns=xRegistry;i=63503.
                await m_session.CallAsync(
                    labelsNodeId, new NodeId(63503u, xNs), default,
                    new Variant("no-such-label"), new Variant(0u))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Not.Null,
                "Removing a non-existent label must fail with a service error.");
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Covers <c>RemoveGroupNodeAsync</c>: deleting a group through
        /// <c>OnDeleteGroupAsync</c> → <c>ReconcileAsync</c> removes the group
        /// node and all its resource children from the NodeManager address space.
        /// </summary>
        [Test]
        public async Task DeleteGroupRemovesGroupNodeFromAddressSpace()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (WotRegistryGroupClient group, _) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            await group.DeleteAsync(expectedEpoch: 0u).ConfigureAwait(false);

            // The group node must have been removed; browsing to it must now fail.
            ServiceResultException? ex = null;
            try
            {
                _ = await client
                    .OpenGroupAsync(group.GroupId)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Not.Null,
                "Group node must be absent from the address space after deletion.");
        }

        /// <summary>
        /// Covers <c>OnRefreshAsync</c> success path,
        /// <c>CreateAddressSpaceAsync</c> (via fixture startup) and
        /// <c>SafeRefreshAsync</c> (called at startup as fire-and-forget).
        /// Also covers <c>LifecycleWotProjectionHost.AddAsync</c> (first projection).
        /// </summary>
        [Test]
        public async Task RefreshAllSucceedsForValidUploadedResource()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            _ = await CreateGroupAndResourceAsync(client).ConfigureAwait(false);

            WotRegistryRefreshResult result = await client
                .RefreshAllAsync(requestId: "proj-live-success")
                .ConfigureAwait(false);

            Assert.That(result.HasFailures, Is.False,
                "Refresh must succeed for a valid uploaded document.");
        }

        /// <summary>
        /// Covers <c>OnRefreshAsync</c> <c>BadServerTooBusy</c> path: a second
        /// concurrent refresh call finds the refresh gate taken and fails immediately.
        /// Covers <c>SafeReconcileAsync</c> via registry-changed callbacks.
        /// </summary>
        [Test]
        public async Task ConcurrentRefreshReturnsBadServerTooBusy()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            // Upload a second version so the first refresh has real work to do.
            await resource
                .UploadNewVersionAsync(ByteString.From(MakeThingDescriptionBytes("td-01")))
                .ConfigureAwait(false);

            m_projectionHost.BlockNextActivation();
            Task<WotRegistryRefreshResult> firstRefresh = client
                .RefreshAllAsync(requestId: "busy-first")
                .AsTask();
            await m_projectionHost.WaitUntilBlockedAsync().ConfigureAwait(false);

            ServiceResultException? secondFailure = null;
            try
            {
                _ = await client
                    .RefreshAllAsync(requestId: "busy-second")
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                secondFailure = ex;
            }
            finally
            {
                m_projectionHost.ReleaseActivation();
            }

            Assert.That(secondFailure, Is.Not.Null,
                "A second concurrent refresh must fail with BadServerTooBusy.");
            Assert.That(
                secondFailure!.StatusCode,
                Is.EqualTo(StatusCodes.BadServerTooBusy));
            WotRegistryRefreshResult firstResult = await firstRefresh.ConfigureAwait(false);
            Assert.That(firstResult.HasFailures, Is.False);
        }

        /// <summary>
        /// Covers <c>BuildEvent RefreshCompleted</c> arm and the
        /// <c>BuildEvent Resource</c> (default) arm: a successful refresh emits
        /// both event kinds via <c>OnCoordinatorEvent → BuildEvent</c>.
        /// </summary>
        [Test]
        public async Task RefreshEmitsRefreshCompletedAndResourceEvents()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            _ = await CreateGroupAndResourceAsync(client).ConfigureAwait(false);

            var observed = new HashSet<WotMaterializationEventKind>();
            m_coordinator.Event += (_, e) =>
            {
                lock (observed)
                {
                    observed.Add(e.Kind);
                }
            };

            WotRegistryRefreshResult result = await client
                .RefreshAllAsync(requestId: "events-ok")
                .ConfigureAwait(false);

            Assert.That(result.HasFailures, Is.False);

            bool hasRefreshCompleted;
            bool hasResource;
            lock (observed)
            {
                hasRefreshCompleted = observed.Contains(WotMaterializationEventKind.RefreshCompleted);
                hasResource = observed.Contains(WotMaterializationEventKind.Resource);
            }

            Assert.That(hasRefreshCompleted, Is.True,
                "A successful refresh must emit a RefreshCompleted event.");
            Assert.That(hasResource, Is.True,
                "A successfully projected document must emit a Resource event.");
        }

        /// <summary>
        /// Covers <c>BuildEvent ValidationFailure</c> arm: marking a resource
        /// invalid in the converter causes the coordinator to raise a
        /// <c>ValidationFailure</c> event through <c>BuildEvent</c>.
        /// </summary>
        [Test]
        public async Task RefreshEmitsValidationFailureEventWhenConverterFails()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            _ = await CreateGroupAndResourceAsync(client, resourceId: "bad-doc")
                .ConfigureAwait(false);
            m_converter.MarkInvalid(AssignedResourceId("bad-doc"));

            var observed = new HashSet<WotMaterializationEventKind>();
            m_coordinator.Event += (_, e) =>
            {
                lock (observed)
                {
                    observed.Add(e.Kind);
                }
            };

            WotRegistryRefreshResult result = await client
                .RefreshAllAsync(requestId: "val-fail")
                .ConfigureAwait(false);

            Assert.That(result.HasFailures, Is.True,
                "Refresh must report failure when the converter rejects the document.");

            bool hasValidationFailure;
            lock (observed)
            {
                hasValidationFailure =
                    observed.Contains(WotMaterializationEventKind.ValidationFailure);
            }

            Assert.That(hasValidationFailure, Is.True,
                "A converter rejection must emit a ValidationFailure event.");
        }

        /// <summary>
        /// Covers <c>BuildEvent LoadFailure</c> arm: a TD with a missing
        /// <c>tm:extends</c> dependency causes the dependency graph to mark the
        /// closure as non-projectable, which raises a <c>LoadFailure</c> event
        /// through <c>BuildEvent</c>.
        /// </summary>
        [Test]
        public async Task RefreshEmitsLoadFailureEventWhenDependencyIsMissing()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient resource, _) = await group
                .CreateResourceAsync("dep-td")
                .ConfigureAwait(false);

            byte[] tdWithMissingDep = TestMaterialization.Td(
                "urn:dep-td", "1", "urn:nonexistent-tm");
            await resource
                .UploadNewVersionAsync(ByteString.From(tdWithMissingDep))
                .ConfigureAwait(false);

            var observed = new HashSet<WotMaterializationEventKind>();
            m_coordinator.Event += (_, e) =>
            {
                lock (observed)
                {
                    observed.Add(e.Kind);
                }
            };

            WotRegistryRefreshResult result = await client
                .RefreshAllAsync(requestId: "dep-fail")
                .ConfigureAwait(false);

            Assert.That(result.HasFailures, Is.True,
                "Refresh must report failure for a document with a missing dependency.");

            bool hasLoadFailure;
            lock (observed)
            {
                hasLoadFailure = observed.Contains(WotMaterializationEventKind.LoadFailure);
            }

            Assert.That(hasLoadFailure, Is.True,
                "A missing dependency must emit a LoadFailure event.");
        }

        /// <summary>
        /// Covers <c>LifecycleWotProjectionHost.ShadowReloadAsync</c>: the second
        /// refresh after uploading a new document version updates the live projection
        /// via shadow-reload rather than a fresh add.
        /// </summary>
        [Test]
        public async Task ShadowReloadCalledWhenResourceContentChanges()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            // First refresh activates the AddAsync path in LifecycleWotProjectionHost.
            WotRegistryRefreshResult first = await client
                .RefreshAllAsync(requestId: "shadow-1")
                .ConfigureAwait(false);
            Assert.That(first.HasFailures, Is.False);

            // Upload a different version so the digest changes, triggering ShadowReloadAsync.
            byte[] v2 = Encoding.UTF8.GetBytes(MakeThingDescriptionStringV2("td-01"));
            await resource
                .UploadNewVersionAsync(ByteString.From(v2))
                .ConfigureAwait(false);

            // Second refresh must complete without failures via the shadow-reload path.
            WotRegistryRefreshResult second = await client
                .RefreshAllAsync(requestId: "shadow-2")
                .ConfigureAwait(false);

            Assert.That(second.HasFailures, Is.False,
                "A shadow-reload after content update must succeed.");
        }

        /// <summary>
        /// Covers <c>DeleteAddressSpaceAsync</c> and <c>WotRegistryProjection.Dispose</c>:
        /// shutting the server down triggers the full cleanup chain. NUnit enforces
        /// this via TearDown; this test adds a refresh so projections are active at
        /// shutdown time.
        /// </summary>
        [Test]
        public async Task DisposeRunsCleanlyWhenActiveProjectionsExist()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            _ = await CreateGroupAndResourceAsync(client).ConfigureAwait(false);

            WotRegistryRefreshResult result = await client
                .RefreshAllAsync(requestId: "dispose-test")
                .ConfigureAwait(false);

            Assert.That(result.HasFailures, Is.False);
            // TearDown exercises DeleteAddressSpaceAsync → Dispose.
        }

        private ValueTask<WotRegistryClient> OpenClientAsync()
        {
            return WotRegistryClient.ForServerAsync(m_session, m_telemetry);
        }

        private WotResource? FindResource(string groupId, string resourceAlias)
        {
            return Registry.WotRegistryTestAuthorities.FindResource(m_registry.Current, groupId, resourceAlias);
        }

        private string AssignedResourceId(string resourceAlias)
        {
            return FindResource(WotRegistryGroups.ThingDescriptions, resourceAlias)?.ResourceId ??
                throw new InvalidOperationException("The fixture resource authority has not been provisioned.");
        }

        private static async ValueTask<(
            WotRegistryResourceClient V1,
            WotRegistryResourceClient V2)> CreateCommittedVersionsAsync(
            WotRegistryGroupClient group,
            string resourceId,
            string secondVersionId = "v2")
        {
            (WotRegistryResourceClient v1, _) = await group
                .CreateResourceAsync(resourceId, "v1")
                .ConfigureAwait(false);
            await v1.UploadNewVersionAsync(
                    ByteString.From(MakeThingDescriptionBytes(resourceId)))
                .ConfigureAwait(false);
            (WotRegistryResourceClient v2, _) = await group
                .CreateResourceAsync(resourceId, secondVersionId)
                .ConfigureAwait(false);
            await v2.UploadNewVersionAsync(
                    ByteString.From(
                        Encoding.UTF8.GetBytes(
                            MakeThingDescriptionStringV2(resourceId))))
                .ConfigureAwait(false);
            return (v1, v2);
        }

        private async ValueTask<(WotRegistryGroupClient Group, WotRegistryResourceClient Resource)>
            CreateGroupAndResourceAsync(
                WotRegistryClient client,
                string resourceId = "td-01")
        {
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient resource, _) = await group
                .CreateResourceAsync(resourceId)
                .ConfigureAwait(false);
            byte[] content = MakeThingDescriptionBytes(resourceId);
            await resource
                .UploadNewVersionAsync(ByteString.From(content))
                .ConfigureAwait(false);
            return (group, resource);
        }

        private static byte[] MakeThingDescriptionBytes(string id)
        {
            return Encoding.UTF8.GetBytes(MakeThingDescriptionString(id));
        }

        private static string MakeThingDescriptionString(string id)
        {
            string padding = new('x', 300);
            return
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:object\",\"id\":\"urn:" +
                id +
                "\",\"title\":\"" +
                id +
                "\"," +
                "\"description\":\"" +
                padding +
                "\"}";
        }

        private static string MakeThingDescriptionStringV2(string id)
        {
            string padding = new('y', 300);
            return
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:object\",\"id\":\"urn:" +
                id +
                "\",\"title\":\"" +
                id +
                "-v2\"," +
                "\"description\":\"" +
                padding +
                "\"}";
        }

        private static byte[] ThingDescriptionWithMetadata(
            string id,
            string title,
            string baseUri)
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:object\",\"id\":\"" +
                id +
                "\"," +
                "\"title\":\"" +
                title +
                "\",\"base\":\"" +
                baseUri +
                "\"}");
        }

        private static byte[] ThingModelWithMetadata(
            string id,
            string title,
            string modelVersion)
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"tm:ThingModel\",\"id\":\"" +
                id +
                "\"," +
                "\"title\":\"" +
                title +
                "\",\"version\":{\"model\":\"" +
                modelVersion +
                "\"}}");
        }

        /// <summary>
        /// Resolves the NodeId of a direct <c>HasComponent</c>-referenced child of
        /// <paramref name="parent"/> whose browse name is <paramref name="name"/> in the
        /// xRegistry namespace. Uses <see cref="NamespaceTable.GetIndexOrAppend"/> on the
        /// session's own namespace table (populated by <c>FetchNamespaceTablesAsync</c>)
        /// rather than the transport channel's message context, which may not include
        /// application-level namespaces.
        /// </summary>
        private async ValueTask<NodeId> BrowseForChildNodeIdAsync(
            NodeId parent,
            string name,
            NodeId referenceType = default)
        {
            ushort xNs = m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            ArrayOf<BrowsePath> paths = new[]
            {
                new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = referenceType.IsNull
                                    ? Ua.ReferenceTypeIds.HasComponent
                                    : referenceType,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(name, xNs)
                            }
                        ]
                    }
                }
            }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await m_session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, default)
                .ConfigureAwait(false);
            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, m_session.NamespaceUris);
        }

        private static ByteString PinnedCloseDocument(string title)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"id\":\"urn:review:pump\"," +
                "\"title\":\"" + title + "\",\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"security\":[\"nosec_sc\"]}"));
        }

        private static async Task WaitForFileViewAsync(
            ISession session,
            NodeId resourceId,
            string versionId,
            ulong size,
            CancellationToken ct)
        {
            await Assert.ThatAsync(async () =>
            {
                NativeFileView view = await ReadFileViewAsync(session, resourceId, ct).ConfigureAwait(false);
                return (view.VersionId, view.Size);
            }, Is.EqualTo((versionId, size)).After(5000, 10)).ConfigureAwait(false);
        }

        private static async Task<NativeFileView> ReadFileViewAsync(
            ISession session,
            NodeId resourceId,
            CancellationToken ct)
        {
            ushort xNs = session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            ArrayOf<DataValue> values = await ReadNativePropertiesAsync(
                session,
                resourceId,
                [
                    new QualifiedName("Size"),
                    new QualifiedName("OpenCount"),
                    new QualifiedName("Writable"),
                    new QualifiedName("UserWritable"),
                    new QualifiedName("VersionId", xNs),
                    new QualifiedName("Epoch", xNs),
                    new QualifiedName("CreatedAt", xNs),
                    new QualifiedName("ModifiedAt", xNs)
                ],
                ct).ConfigureAwait(false);
            Assert.That(values[0].WrappedValue.TryGetValue(out ulong size), Is.True);
            Assert.That(values[1].WrappedValue.TryGetValue(out ushort openCount), Is.True);
            Assert.That(values[2].WrappedValue.TryGetValue(out bool writable), Is.True);
            Assert.That(values[3].WrappedValue.TryGetValue(out bool userWritable), Is.True);
            Assert.That(values[4].WrappedValue.TryGetValue(out string versionId), Is.True);
            Assert.That(values[5].WrappedValue.TryGetValue(out uint epoch), Is.True);
            Assert.That(values[6].WrappedValue.TryGetValue(out DateTimeUtc createdAt), Is.True);
            Assert.That(values[7].WrappedValue.TryGetValue(out DateTimeUtc modifiedAt), Is.True);
            return new NativeFileView(
                size, openCount, writable, userWritable, versionId, epoch, createdAt, modifiedAt);
        }

        private static async Task<ArrayOf<DataValue>> ReadNativePropertiesAsync(
            ISession session,
            NodeId resourceId,
            ArrayOf<QualifiedName> names,
            CancellationToken ct)
        {
            TranslateBrowsePathsToNodeIdsResponse paths = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                names.ConvertAll(name => new BrowsePath
                {
                    StartingNode = resourceId,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                                IncludeSubtypes = true,
                                TargetName = name
                            }
                        ]
                    }
                }),
                ct).ConfigureAwait(false);
            Assert.That(paths.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(paths.Results.Count, Is.EqualTo(names.Count));
            var nodes = new List<ReadValueId>();
            foreach (BrowsePathResult result in paths.Results)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Targets.Count, Is.EqualTo(1));
                Assert.That(result.Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
                nodes.Add(new ReadValueId
                {
                    NodeId = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris),
                    AttributeId = Attributes.Value
                });
            }
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, [.. nodes], ct).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results.Count, Is.EqualTo(names.Count));
            foreach (DataValue value in response.Results)
            {
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            return response.Results;
        }

        private async ValueTask WaitForPublishedDefaultAsync(WotRegistryResourceClient logical, string expected)
        {
            NodeId versionId = await BrowseForChildNodeIdAsync(
                logical.ResourceNodeId, XRegistry.BrowseNames.VersionId, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false);
            Assert.That(versionId.IsNull, Is.False);

            // Reconciliation is queued; observe the published default before testing its file forwarding.
            await Assert.ThatAsync(async () =>
            {
                DataValue value = await m_session.ReadValueAsync(versionId).ConfigureAwait(false);
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

        private async ValueTask<T> ReadWotChildValueAsync<T>(NodeId parent, string name)
        {
            ushort wotNs = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            var path = new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(name, wotNs)
                        }
                    ]
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await m_session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    new[] { path }.ToArrayOf(),
                    default)
                .ConfigureAwait(false);
            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoMatch,
                    $"Child '{name}' was not found below '{parent}'.");
            }
            var nodeId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                m_session.NamespaceUris);
            DataValue value = await m_session.ReadValueAsync(nodeId).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode) ||
                value.WrappedValue.AsBoxedObject(Variant.BoxingBehavior.Legacy) is not T result)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Child '{name}' did not return {typeof(T).Name}.");
            }
            return result;
        }

        private sealed record NativeFileView(
            ulong Size,
            ushort OpenCount,
            bool Writable,
            bool UserWritable,
            string VersionId,
            uint Epoch,
            DateTimeUtc CreatedAt,
            DateTimeUtc ModifiedAt);

        private readonly StringBuilder m_testOutput = new();
        private string m_pkiRoot = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ReferenceServer m_server = null!;
        private ISession m_session = null!;
        private ITelemetryContext m_telemetry = null!;
        private WotRegistryService m_registry = null!;
        private WotRegistryNodeManager m_nodeManager = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private PausableProjectionHost m_projectionHost = null!;
        private static readonly string[] s_expectedVersionIds = ["v1", "v2"];

        /// <summary>
        /// A pausing wrapper around <see cref="IWotProjectionHost"/> that can optionally
        /// hold its next activation call until released. Mirrors the pattern in
        /// <c>WotRegistryClientLiveServerTests.BlockingProjectionHost</c> to allow
        /// the concurrent-refresh race condition to be reproduced deterministically.
        /// </summary>
        private sealed class PausableProjectionHost : IWotProjectionHost
        {
            public PausableProjectionHost(IWotProjectionHost inner)
            {
                m_inner = inner;
            }

            public async ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                await WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
                return await m_inner.AddAsync(document, cancellationToken).ConfigureAwait(false);
            }

            public async ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current,
                WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                await WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
                return await m_inner
                    .ShadowReloadAsync(current, document, cancellationToken)
                    .ConfigureAwait(false);
            }

            public async ValueTask<WotProjectionHandle> ImmediateReloadAsync(
                WotProjectionHandle current,
                WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                await WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
                return await m_inner
                    .ImmediateReloadAsync(current, document, cancellationToken)
                    .ConfigureAwait(false);
            }

            public ValueTask RemoveAsync(
                WotProjectionHandle handle,
                CancellationToken cancellationToken = default)
            {
                return m_inner.RemoveAsync(handle, cancellationToken);
            }

            public void BlockNextActivation()
            {
                lock (m_gate)
                {
                    m_entered = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    m_release = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            public Task<bool> WaitUntilBlockedAsync()
            {
                lock (m_gate)
                {
                    return m_entered?.Task ??
                        throw new InvalidOperationException("No activation is blocked.");
                }
            }

            public void ReleaseActivation()
            {
                lock (m_gate)
                {
                    m_release?.TrySetResult(true);
                }
            }

            private async ValueTask WaitIfBlockedAsync(CancellationToken cancellationToken)
            {
                TaskCompletionSource<bool>? entered;
                TaskCompletionSource<bool>? release;
                lock (m_gate)
                {
                    entered = m_entered;
                    release = m_release;
                }

                if (entered is null || release is null)
                {
                    return;
                }

                entered.TrySetResult(true);
                using CancellationTokenRegistration registration = cancellationToken.Register(
                    static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
                    release);
                await release.Task.ConfigureAwait(false);
                lock (m_gate)
                {
                    if (ReferenceEquals(m_release, release))
                    {
                        m_entered = null;
                        m_release = null;
                    }
                }
            }

            private readonly IWotProjectionHost m_inner;
            private readonly Lock m_gate = new();
            private TaskCompletionSource<bool>? m_entered;
            private TaskCompletionSource<bool>? m_release;
        }
    }
}
