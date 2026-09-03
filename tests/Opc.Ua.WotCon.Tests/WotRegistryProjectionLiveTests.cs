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
using System.Linq;
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
    public sealed class WotRegistryProjectionLiveTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(WotRegistryProjectionLiveTests),
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
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                }
            };

            m_registry = new WotRegistryService();
            m_converter = new FakeWotDocumentConverter();
            m_projectionHost = new PausableProjectionHost(
                new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_projectionHost,
                documentConverter: m_converter);
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
            // Exercises DeleteAddressSpaceAsync (unregisters events, calls
            // coordinator.RemoveAllAsync → LifecycleWotProjectionHost.RemoveAsync per
            // projection) and WotRegistryProjection.Dispose (clears group entries,
            // disposes the refresh gate).
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
        public async Task ExplicitVersionsHaveDistinctStableProjectedNodeIds()
        {
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);

            (WotRegistryResourceClient v1, string v1Id) =
                await group.CreateResourceAsync("multi", "v1").ConfigureAwait(false);
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
                        "WoTRegistry/groups/thingdescriptions/resources/multi/versions/v1"));
                Assert.That(
                    v2.ResourceNodeId.TryGetValue(out string v2NodeId)
                        ? v2NodeId
                        : string.Empty,
                    Is.EqualTo(
                        "WoTRegistry/groups/thingdescriptions/resources/multi/versions/v2"));
                WotResource stored = m_registry.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "multi")!;
                Assert.That(stored.Versions.Select(version => version.VersionId),
                    Is.EquivalentTo(s_expectedVersionIds));
            });
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

            (_, bool firstCreated) = await client.GetOrCreateGroupAsync("My Group")
                .ConfigureAwait(false);
            (_, bool secondCreated) = await client.GetOrCreateGroupAsync("My Group")
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

            Task[] tasks = Enumerable.Range(0, 12)
                .Select(i => group.Proxy
                    .GetOrCreateResourceAsync(
                        "race-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        string.Empty,
                        requestFileOpen: true)
                    .AsTask())
                .ToArray();

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
            WotResource? stored = m_registry.Current.FindResource(
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
            (_, WotRegistryResourceClient resource) = await CreateGroupAndResourceAsync(client)
                .ConfigureAwait(false);

            NodeId labelsNodeId = await BrowseForChildNodeIdAsync(
                resource.ResourceNodeId,
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

            WotResource stored = m_registry.Current.FindResource(
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
                    .OpenGroupAsync(WotRegistryClient.ThingDescriptionsGroupId)
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
            m_converter.MarkInvalid("bad-doc");

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
                "\"@type\":\"uav:object\",\"id\":\"urn:" + id + "\",\"title\":\"" + id + "\"," +
                "\"description\":\"" + padding + "\"}";
        }

        private static string MakeThingDescriptionStringV2(string id)
        {
            string padding = new('y', 300);
            return
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:object\",\"id\":\"urn:" + id + "\",\"title\":\"" + id + "-v2\"," +
                "\"description\":\"" + padding + "\"}";
        }

        /// <summary>
        /// Resolves the NodeId of a direct <c>HasComponent</c>-referenced child of
        /// <paramref name="parent"/> whose browse name is <paramref name="name"/> in the
        /// xRegistry namespace. Uses <see cref="NamespaceTable.GetIndexOrAppend"/> on the
        /// session's own namespace table (populated by <c>FetchNamespaceTablesAsync</c>)
        /// rather than the transport channel's message context, which may not include
        /// application-level namespaces.
        /// </summary>
        private async ValueTask<NodeId> BrowseForChildNodeIdAsync(NodeId parent, string name)
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
                                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
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

        private string m_pkiRoot = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ReferenceServer m_server = null!;
        private ISession m_session = null!;
        private ITelemetryContext m_telemetry = null!;
        private WotRegistryService m_registry = null!;
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
