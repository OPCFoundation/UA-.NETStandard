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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
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
    public sealed partial class WotRegistryIdentityLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task FederationRejectsAdaptersWithoutBindingCapabilityAsync(bool followReference)
        {
            const string catalogue = "urn:r09:peer-switch-catalogue";
            const string thing = "urn:r09:peer-switch-thing";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, catalogue, ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                thing, "v1", ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget target = CreateNativeFederationTarget(group, allocation);

            await WithNativeFederationClientAsync(async (trustedSession, trustedClient) =>
                await WithFederationOtherPeerAsync(catalogue, thing, async (
                    otherSession, otherClient, otherStore, otherManager, otherAllocation) =>
                {
                    ResourceState otherResource = otherManager.FindPredefinedNode<ResourceState>(
                        otherAllocation.LogicalResource.ResourceNodeId)!;
                    Assert.Multiple(() =>
                    {
                        Assert.That(otherSession.Endpoint.Server.ApplicationUri, Is.Not.EqualTo(target.ServerUri));
                        Assert.That(otherSession.ServerUris.GetString(0),
                            Is.EqualTo(otherSession.Endpoint.Server.ApplicationUri));
                        Assert.That(NodeId.ToExpandedNodeId(otherClient.RegistryNodeId, otherSession.NamespaceUris),
                            Is.EqualTo(target.OriginRegistry.RegistryNodeId));
                        Assert.That(NodeId.ToExpandedNodeId(
                            otherAllocation.LogicalResource.ResourceNodeId, otherSession.NamespaceUris),
                            Is.EqualTo(target.ResourceNodeId));
                        Assert.That(otherResource.Xid!.Value, Is.EqualTo(target.ResourceXid));
                        Assert.That(otherStore, Is.Not.SameAs(m_store));
                    });
                    await trustedClient.VerifyLogicalResourceAsync(
                        target, trustedSession.Endpoint.EndpointUrl!, ct).ConfigureAwait(false);
                    await Assert.ThatAsync(async () => await otherClient.VerifyLogicalResourceAsync(
                            target, trustedSession.Endpoint.EndpointUrl!, ct).ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);

                    await WithNativeFederationProxyAsync(target, trustedClient, async (
                        localServer, localSession, proxyNodeId) =>
                    {
                        ISession active = trustedSession;
                        bool switched = false;
                        NodeId firstRead = NodeId.Null;
                        Mock<ISession> forwarding = CreateFederationForwardingSession(
                            () => active, ForwardReadAsync);
                        var client = new GenericXRegistryClient(
                            forwarding.Object, Namespaces.WotCon, trustedClient.RegistryNodeId, m_telemetry);
                        int trustedReads = m_store.Blobs.Reads;
                        int otherReads = otherStore.Blobs.Reads;
                        ResourceTypeClient? followed = null;
                        ServiceResultException? rejection = null;

                        try
                        {
                            if (followReference)
                            {
                                followed = await client.FollowExternalReferenceAsync(
                                    localSession, proxyNodeId, target, ct).ConfigureAwait(false);
                                using ISessionClient followedSession = followed.Session;
                                await followedSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                            else
                            {
                                await client.VerifyLogicalResourceAsync(
                                    target, trustedSession.Endpoint.EndpointUrl!, ct).ConfigureAwait(false);
                            }
                        }
                        catch (ServiceResultException exception)
                        {
                            rejection = exception;
                        }

                        var local = (ResourceState)localServer.Manager.Find(proxyNodeId)!;
                        Assert.Multiple(() =>
                        {
                            Assert.That(switched, Is.False);
                            Assert.That(firstRead.IsNull, Is.True);
                            Assert.That(active, Is.SameAs(trustedSession));
                            Assert.That(followed, Is.Null);
                            Assert.That(m_store.Blobs.Reads, Is.EqualTo(trustedReads));
                            Assert.That(otherStore.Blobs.Reads, Is.EqualTo(otherReads));
                            Assert.That(otherResource.OpenCount!.Value, Is.Zero);
                            Assert.That(local.OriginRegistry!.Value, Is.EqualTo(target.OriginRegistry));
                            Assert.That(local.ExternalReference!.Value.WithServerIndex(0),
                                Is.EqualTo(target.ResourceNodeId));
                            Assert.That(rejection, Is.Not.Null,
                                "An adapter without an atomic binding capability cannot promise federation.");
                            if (rejection is not null)
                            {
                                Assert.That(rejection.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                            }
                        });

                        async ValueTask<ReadResponse> ForwardReadAsync(
                            RequestHeader? header,
                            double maxAge,
                            TimestampsToReturn timestamps,
                            ArrayOf<ReadValueId> nodes,
                            CancellationToken token)
                        {
                            ReadResponse response = await active.ReadAsync(
                                header, maxAge, timestamps, nodes, token).ConfigureAwait(false);
                            if (!switched)
                            {
                                firstRead = nodes[0].NodeId;
                                active = otherSession;
                                switched = true;
                            }
                            return response;
                        }
                    }).ConfigureAwait(false);
                }, ct).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task FederationReturnedBindingCannotRetargetContentToAnotherAuthenticatedPeerAsync()
        {
            const string catalogue = "urn:r09:returned-binding-catalogue";
            const string thing = "urn:r09:returned-binding-thing";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, catalogue, ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                thing, "v1", ct: ct).ConfigureAwait(false);
            var bytes = ByteString.From(TestMaterialization.Td(thing, "trusted"));
            await allocation.Version.Proxy.UploadAsync(bytes, ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget target = CreateNativeFederationTarget(group, allocation);

            await WithNativeFederationClientAsync(async (trustedSession, trustedClient) =>
                await WithFederationOtherPeerAsync(catalogue, thing, async (
                    otherSession, otherClient, otherStore, otherManager, otherAllocation) =>
                {
                    var otherBytes = ByteString.From(TestMaterialization.Td(thing, "other"));
                    await otherAllocation.Version.Proxy.UploadAsync(otherBytes, ct: ct).ConfigureAwait(false);
                    await WithNativeFederationProxyAsync(target, trustedClient, async (
                        localServer, localSession, proxyNodeId) =>
                    {
                        ResourceTypeClient resource = await trustedClient.FollowExternalReferenceAsync(
                            localSession, proxyNodeId, target, ct).ConfigureAwait(false);
                        using ISessionClient binding = resource.Session;
                        try
                        {
                            Assert.That(binding, Is.Not.SameAs(trustedSession));
                            Assert.That(binding.Endpoint.Server.ApplicationUri, Is.EqualTo(target.ServerUri));
                            var native = (Session)trustedSession;
                            int trustedReads = m_store.Blobs.Reads;
                            int otherReads = otherStore.Blobs.Reads;
                            ResourceState otherResource = otherManager.FindPredefinedNode<ResourceState>(
                                otherAllocation.LogicalResource.ResourceNodeId)!;
                            await native.RecreateInPlaceAsync(otherSession.ConfiguredEndpoint, ct: ct)
                                .ConfigureAwait(false);
                            Assert.That(native.Endpoint.Server.ApplicationUri,
                                Is.EqualTo(otherSession.Endpoint.Server.ApplicationUri)
                                    .And.Not.EqualTo(target.ServerUri));

                            await Assert.ThatAsync(async () => await resource.OpenAsync(1, ct).ConfigureAwait(false),
                                Throws.TypeOf<ServiceResultException>()
                                    .With.Property(nameof(ServiceResultException.StatusCode))
                                    .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                            Assert.Multiple(() =>
                            {
                                Assert.That(resource.Session.Endpoint.Server.ApplicationUri,
                                    Is.EqualTo(target.ServerUri));
                                Assert.That(m_store.Blobs.Reads, Is.EqualTo(trustedReads));
                                Assert.That(otherStore.Blobs.Reads, Is.EqualTo(otherReads));
                                Assert.That(otherResource.OpenCount!.Value, Is.Zero);
                            });
                        }
                        finally
                        {
                            await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);
                }, ct).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task FederationReturnedBindingRejectsMappingHolderAbaBeforeContentAsync(
            bool namespaceMapping,
            bool sourceContext,
            bool observeInvalidation)
        {
            const string catalogue = "urn:r09:mapping-holder-catalogue";
            const string thing = "urn:r09:mapping-holder-thing";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, catalogue, ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                thing, "v1", ct: ct).ConfigureAwait(false);
            var bytes = ByteString.From(TestMaterialization.Td(thing, "mapping-holder"));
            await allocation.Version.Proxy.UploadAsync(bytes, ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget target = CreateNativeFederationTarget(group, allocation);
            ResourceState logical = m_manager.FindPredefinedNode<ResourceState>(
                allocation.LogicalResource.ResourceNodeId)!;

            await WithNativeFederationClientAsync(async (remoteSession, remote) =>
                await WithNativeFederationProxyAsync(target, remote, async (
                    localServer, localSession, proxyNodeId) =>
                {
                    ResourceTypeClient resource = await remote.FollowExternalReferenceAsync(
                        localSession, proxyNodeId, target, ct).ConfigureAwait(false);
                    using (ISessionClient binding = resource.Session)
                    {
                        var context = (ServiceMessageContext)(sourceContext
                            ? remoteSession.MessageContext
                            : binding.MessageContext);
                        NamespaceTable namespaces = context.NamespaceUris;
                        StringTable servers = context.ServerUris;
                        uint? handle = null;
                        try
                        {
                            DataValue serverStateValue = await binding.ReadValueAsync(
                                Ua.VariableIds.Server_ServerStatus_State, ct).ConfigureAwait(false);
                            Assert.Multiple(() =>
                            {
                                Assert.That(serverStateValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                                Assert.That(serverStateValue.WrappedValue.TryGetValue(out ServerState state), Is.True);
                                Assert.That(state, Is.EqualTo(ServerState.Running));
                            });
                            int reads = m_store.Blobs.Reads;
                            if (namespaceMapping)
                            {
                                context.NamespaceUris = new NamespaceTable(namespaces.ToArray());
                            }
                            else
                            {
                                context.ServerUris = new StringTable(servers.ToArray());
                            }
                            if (observeInvalidation)
                            {
                                await Assert.ThatAsync(() => binding.ReadValueAsync(
                                        Ua.VariableIds.Server_ServerStatus_State, ct),
                                    Throws.TypeOf<ServiceResultException>()
                                        .With.Property(nameof(ServiceResultException.StatusCode))
                                        .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                            }
                            context.NamespaceUris = namespaces;
                            context.ServerUris = servers;
                            await Assert.ThatAsync(
                                async () => handle = await resource.OpenAsync(1, ct).ConfigureAwait(false),
                                Throws.TypeOf<ServiceResultException>()
                                    .With.Property(nameof(ServiceResultException.StatusCode))
                                    .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                            Assert.Multiple(() =>
                            {
                                Assert.That(m_store.Blobs.Reads, Is.EqualTo(reads));
                                Assert.That(logical.OpenCount!.Value, Is.Zero);
                            });
                        }
                        finally
                        {
                            context.NamespaceUris = namespaces;
                            context.ServerUris = servers;
                            try
                            {
                                if (handle is uint openedHandle)
                                {
                                    var owner = new ResourceTypeClient(remoteSession, resource.ObjectId, m_telemetry);
                                    await owner.CloseAsync(openedHandle, CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                    }
                    DataValue ownerState = await remoteSession.ReadValueAsync(
                        Ua.VariableIds.Server_ServerStatus_State, ct).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(ownerState.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(ownerState.WrappedValue.TryGetValue(out ServerState state), Is.True);
                        Assert.That(state, Is.EqualTo(ServerState.Running));
                    });
                    ResourceTypeClient fresh = await remote.FollowExternalReferenceAsync(
                        localSession, proxyNodeId, target, ct).ConfigureAwait(false);
                    using ISessionClient freshBinding = fresh.Session;
                    try
                    {
                        Assert.That(await fresh.ReadDocumentAsync(ct: ct).ConfigureAwait(false), Is.EqualTo(bytes));
                        Assert.That(logical.OpenCount!.Value, Is.Zero);
                    }
                    finally
                    {
                        await freshBinding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static Mock<ISession> CreateFederationForwardingSession(
            Func<ISession> current,
            Func<RequestHeader?, double, TimestampsToReturn, ArrayOf<ReadValueId>,
                CancellationToken, ValueTask<ReadResponse>> read)
        {
            var forwarding = new Mock<ISession>(MockBehavior.Strict);
            forwarding.SetupGet(session => session.Connected).Returns(() => current().Connected);
            forwarding.SetupGet(session => session.Endpoint).Returns(() => current().Endpoint);
            forwarding.SetupGet(session => session.NamespaceUris).Returns(() => current().NamespaceUris);
            forwarding.SetupGet(session => session.ServerUris).Returns(() => current().ServerUris);
            forwarding.SetupGet(session => session.MessageContext).Returns(() => current().MessageContext);
            forwarding.SetupGet(session => session.NodeCache).Returns(() => current().NodeCache);
            forwarding.SetupGet(session => session.OperationLimits).Returns(() => current().OperationLimits);
            forwarding.SetupGet(session => session.ServerCapabilities).Returns(() => current().ServerCapabilities);
            forwarding.SetupGet(session => session.ContinuationPointPolicy)
                .Returns(() => current().ContinuationPointPolicy);
            forwarding.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(read);
            forwarding.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader?, ViewDescription?, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (header, view, max, nodes, token) => current().BrowseAsync(header, view, max, nodes, token));
            forwarding.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader?, bool, ArrayOf<ByteString>, CancellationToken>(
                    (header, release, points, token) => current().BrowseNextAsync(header, release, points, token));
            forwarding.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader?, ArrayOf<BrowsePath>, CancellationToken>(
                    (header, paths, token) => current().TranslateBrowsePathsToNodeIdsAsync(header, paths, token));
            return forwarding;
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FederationStockBindingRejectsPeerRecreationDuringVerificationAsync(bool followReference)
        {
            const string catalogue = "urn:r09:stock-switch-catalogue";
            const string thing = "urn:r09:stock-switch-thing";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, catalogue, ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                thing, "v1", ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget target = CreateNativeFederationTarget(group, allocation);
            ResourceState logical = m_manager.FindPredefinedNode<ResourceState>(
                allocation.LogicalResource.ResourceNodeId)!;

            await WithNativeFederationClientAsync(async (trustedSession, trustedClient) =>
                await WithFederationOtherPeerAsync(catalogue, thing, async (
                    otherSession, otherClient, otherStore, otherManager, otherAllocation) =>
                    await WithNativeFederationProxyAsync(target, trustedClient, async (
                        localServer, localSession, proxyNodeId) =>
                    {
                        var entered = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        var release = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        NodeValueEventHandlerAsync? originalRead = logical.Xid!.OnReadValueAsync;
                        logical.Xid.OnReadValueAsync = async (context, node, range, encoding, token) =>
                        {
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(token).ConfigureAwait(false);
                            return originalRead is not null
                                ? await originalRead(context, node, range, encoding, token).ConfigureAwait(false)
                                : new AttributeReadResult(
                                    ServiceResult.Good, new Variant(logical.Xid.Value),
                                    StatusCodes.Good, DateTimeUtc.Now);
                        };
                        int trustedReads = m_store.Blobs.Reads;
                        int otherReads = otherStore.Blobs.Reads;
                        ResourceTypeClient? returned = null;
                        Task verification = VerifyAsync();
                        try
                        {
                            await entered.Task.WaitAsync(ct).ConfigureAwait(false);
                            Assert.That(verification.IsCompleted, Is.False);
                            var native = (Session)trustedSession;
                            await native.RecreateInPlaceAsync(otherSession.ConfiguredEndpoint, ct: ct)
                                .ConfigureAwait(false);
                            Assert.That(native.Endpoint.Server.ApplicationUri,
                                Is.EqualTo(otherSession.Endpoint.Server.ApplicationUri)
                                    .And.Not.EqualTo(target.ServerUri));
                            release.TrySetResult(true);
                            await Assert.ThatAsync(() => verification,
                                Throws.TypeOf<ServiceResultException>()
                                    .With.Property(nameof(ServiceResultException.StatusCode))
                                    .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                            Assert.Multiple(() =>
                            {
                                Assert.That(returned, Is.Null);
                                Assert.That(m_store.Blobs.Reads, Is.EqualTo(trustedReads));
                                Assert.That(otherStore.Blobs.Reads, Is.EqualTo(otherReads));
                                Assert.That(otherManager.FindPredefinedNode<ResourceState>(
                                    otherAllocation.LogicalResource.ResourceNodeId)!.OpenCount!.Value, Is.Zero);
                            });
                        }
                        finally
                        {
                            release.TrySetResult(true);
                            logical.Xid.OnReadValueAsync = originalRead;
                        }

                        async Task VerifyAsync()
                        {
                            if (followReference)
                            {
                                returned = await trustedClient.FollowExternalReferenceAsync(
                                    localSession, proxyNodeId, target, ct).ConfigureAwait(false);
                                using ISessionClient returnedSession = returned.Session;
                                await returnedSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                            else
                            {
                                await trustedClient.VerifyLogicalResourceAsync(
                                    target, trustedSession.Endpoint.EndpointUrl!, ct).ConfigureAwait(false);
                            }
                        }
                    }).ConfigureAwait(false), ct).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task WithFederationOtherPeerAsync(
            string catalogue,
            string thing,
            Func<ISession, WotRegistryClient, NativeStore, WotRegistryNodeManager,
                WotRegistryResourceAllocation, Task> action,
            CancellationToken ct)
        {
            var fixture = new ServerFixture<FederationOtherPeerServer>(
                telemetry => new FederationOtherPeerServer(telemetry))
            {
                AutoAccept = true
            };
            FederationOtherPeerServer? server = null;
            WotRegistryService? registry = null;
            WotMaterializationCoordinator? coordinator = null;
            try
            {
                server = await fixture.StartAsync(Path.Combine(m_root, "f1b")).ConfigureAwait(false);
                var store = new NativeStore();
                var options = new WotRegistryServerOptions();
                Configure(options);
                registry = new WotRegistryService(store, options.Bounds, options.IdentityBindings);
                coordinator = new WotMaterializationCoordinator(
                    registry, new LifecycleWotProjectionHost(server.NodeManagerLifecycle),
                    documentConverter: new FakeWotDocumentConverter());
                Ua.Server.NodeManagerRegistration registration = await server.NodeManagerLifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(options, registry, coordinator), null, ct).ConfigureAwait(false);
                var manager = (WotRegistryNodeManager)registration.NodeManager;
                await using var connection = new ClientFixture(m_telemetry);
                await connection.LoadClientConfigurationAsync(
                    Path.Combine(m_root, "f1bc"), "FederationOtherPeerClient").ConfigureAwait(false);
                using ISession session = await connection.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                try
                {
                    WotRegistryClient client = await WotRegistryClient.ForServerAsync(session, m_telemetry, ct)
                        .ConfigureAwait(false);
                    WotRegistryGroupClient group = await client.CreateDocumentGroupAsync(
                        WoTDocumentKindEnum.ThingDescription, catalogue, ct).ConfigureAwait(false);
                    WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                        thing, "v1", ct: ct).ConfigureAwait(false);
                    await action(session, client, store, manager, allocation).ConfigureAwait(false);
                }
                finally
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                coordinator?.Dispose();
                registry?.Dispose();
                server?.Dispose();
            }
        }

        private sealed class FederationOtherPeerServer : ReferenceServer
        {
            public FederationOtherPeerServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }
        }
    }
}
