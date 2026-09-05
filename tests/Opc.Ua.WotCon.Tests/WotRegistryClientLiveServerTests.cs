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
using System.IO;
using System.Text;
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
using Opc.Ua.XRegistry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Live-server end-to-end test for <see cref="WotRegistryClient"/>: a
    /// real <see cref="ReferenceServer"/> hosts the WoT Connectivity 1.1
    /// registry NodeManager (the same fixture used by
    /// <c>WotRegistryLifecycleTests</c>/<c>WotRegistryEventIntegrationTests</c>
    /// in <c>Opc.Ua.Server.Tests</c>), a real <see cref="ISession"/>
    /// connects over loopback opc.tcp, and the registry client drives a
    /// complete create-group / create-resource / chunked-upload / refresh
    /// / read-back-metadata workflow against it.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Client")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class WotRegistryClientLiveServerTests
    {
        private string m_pkiRoot = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ReferenceServer m_server = null!;
        private ISession m_session = null!;
        private ITelemetryContext m_telemetry = null!;
        private WotRegistryService m_registry = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private BlockingProjectionHost m_projectionHost = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(), nameof(WotRegistryClientLiveServerTests), Guid.NewGuid().ToString("N"));

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            var options = new WotRegistryServerOptions
            {
                // The test drives Refresh explicitly for a deterministic
                // sequence of events/generations.
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                }
            };
            m_registry = new WotRegistryService();
            m_projectionHost = new BlockingProjectionHost(
                new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_projectionHost,
                documentConverter: new FakeWotDocumentConverter());
            var factory = new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator);
            _ = await m_server.NodeManagerLifecycle
                .AddAsync(factory, callerContext: null)
                .ConfigureAwait(false);

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
        }

        [Test]
        public async Task UploadRefreshAndReadBackResourceMetadataAsync()
        {
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(m_session, m_telemetry)
                .ConfigureAwait(false);

            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            Assert.That(group.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));

            (WotRegistryResourceClient resource, string versionId, bool created) = await group
                .GetOrCreateResourceAsync("sensor01", "1.0.0")
                .ConfigureAwait(false);
            Assert.That(created, Is.True);
            Assert.That(versionId, Is.EqualTo("1.0.0"));

            byte[] content = Encoding.UTF8.GetBytes(ValidThingDescriptionJson("sensor01"));

            // Force several Write calls with a small chunk size to prove
            // the chunked upload / commit-on-close path over a real
            // FileType transfer.
            await resource.UploadNewVersionAsync(ByteString.From(content), chunkSize: 64)
                .ConfigureAwait(false);

            ByteString downloaded = await resource.DownloadAsync(chunkSize: 64).ConfigureAwait(false);
            Assert.That(downloaded.ToArray(), Is.EqualTo(content));

            WotRegistryRefreshResult refresh = await client
                .RefreshAllAsync(requestId: "live-req-1")
                .ConfigureAwait(false);
            var refreshMessages = new List<string>();
            foreach (WoTResourceLoadResultDataType result in refresh.Results)
            {
                refreshMessages.Add(
                    $"{result.ResourceId}: {result.Phase}/{result.Outcome}: {result.Message}");
            }
            string refreshDetails = string.Join("; ", refreshMessages);
            Assert.That(refresh.HasFailures, Is.False, refreshDetails);

            bool hasFailure = false;
            bool hasActiveResource = false;
            foreach (WoTResourceLoadResultDataType result in refresh.Results)
            {
                hasFailure |= result.Outcome == WoTOutcomeEnum.Failed;
                hasActiveResource |=
                    result.ResourceId == "sensor01" &&
                    result.LoadState == WoTLoadStateEnum.Active;
            }
            Assert.That(
                hasFailure, Is.False,
                "The uploaded Thing Description must materialize without failures.");
            Assert.That(
                hasActiveResource, Is.True,
                "Refresh must report an active load state for the uploaded document.");

            // Read back plain resource metadata (VersionId, inherited from
            // the xRegistry ResourceType base) over the real session to
            // prove the uploaded/materialized resource is observable
            // through the same client-resolved NodeId.
            ushort xRegistryNs = m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            NodeId versionIdNodeId = await ResolveChildAsync(
                m_session, resource.ResourceNodeId, xRegistryNs, "VersionId").ConfigureAwait(false);
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId { NodeId = versionIdNodeId, AttributeId = Attributes.Value }
            }.ToArrayOf();
            ReadResponse readResponse = await m_session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, default)
                .ConfigureAwait(false);
            Assert.That(readResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            readResponse.Results[0].WrappedValue.TryGetValue(out string readVersionId);
            Assert.That(readVersionId, Is.Not.Empty,
                "The server must assign a real version id once a version has been committed.");
        }

        [Test]
        public async Task ConcurrentRefreshReturnsServerTooBusyWithoutDeadlockAsync()
        {
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(m_session, m_telemetry)
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);
            (WotRegistryResourceClient resource, _, _) = await group
                .GetOrCreateResourceAsync("concurrent-sensor", "1.0.0")
                .ConfigureAwait(false);
            await resource.UploadNewVersionAsync(
                ByteString.From(Encoding.UTF8.GetBytes(
                    ValidThingDescriptionJson("concurrent-sensor"))))
                .ConfigureAwait(false);

            m_projectionHost.BlockNextActivation();
            Task<WotRegistryRefreshResult> firstRefresh = client
                .RefreshAllAsync(requestId: "first-refresh")
                .AsTask();
            await m_projectionHost.WaitUntilBlockedAsync().ConfigureAwait(false);

            ServiceResultException? secondFailure = null;
            try
            {
                _ = await client
                    .RefreshAllAsync(requestId: "second-refresh")
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

            Assert.That(secondFailure, Is.Not.Null);
            Assert.That(
                secondFailure!.StatusCode,
                Is.EqualTo(StatusCodes.BadServerTooBusy));
            WotRegistryRefreshResult firstResult = await firstRefresh.ConfigureAwait(false);
            Assert.That(firstResult.HasFailures, Is.False);
        }

        private static async ValueTask<NodeId> ResolveChildAsync(
            ISession session, NodeId parent, ushort namespaceIndex, string name)
        {
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
                                ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(name, namespaceIndex)
                            }
                        ]
                    }
                }
            }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, default)
                .ConfigureAwait(false);
            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, session.NamespaceUris);
        }

        private static string ValidThingDescriptionJson(string id)
        {
            // A syntactically valid, minimal WoT 1.1 Thing Description,
            // padded so the upload exercises multiple 64-byte Write calls.
            string padding = new('x', 300);
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:object\",\"id\":\"urn:" +
                id +
                "\",\"title\":\"" +
                id +
                "\"," +
                "\"description\":\"" +
                padding +
                "\"}";
        }

        private sealed class BlockingProjectionHost : IWotProjectionHost
        {
            public BlockingProjectionHost(IWotProjectionHost inner)
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
