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
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotRegistryIdentityLiveTests
    {
        [Test]
        public async Task FederationFollowsLogicalResourceAndPinsDefaultVersionHandles()
        {
            const string source = "urn:federation:thing";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:federation:catalogue", ct).ConfigureAwait(false);
            WotRegistryResourceAllocation first = await group.CreateThingDescriptionResourceAsync(source, "v1", ct: ct)
                .ConfigureAwait(false);
            WotRegistryResourceAllocation second = await group.CreateThingDescriptionResourceAsync(source, "v2", ct: ct)
                .ConfigureAwait(false);
            var firstBytes = ByteString.From(TestMaterialization.Td(source, "first"));
            var secondBytes = ByteString.From(TestMaterialization.Td(source, "second"));
            await first.Version.Proxy.UploadAsync(firstBytes, ct: ct).ConfigureAwait(false);
            await second.Version.Proxy.UploadAsync(secondBytes, ct: ct).ConfigureAwait(false);
            await WaitForFederationDefaultAsync(first.LogicalResource.ResourceNodeId, "v1", ct).ConfigureAwait(false);

            await using var remoteConnection = new ClientFixture(m_telemetry);
            await remoteConnection.LoadClientConfigurationAsync(
                Path.Combine(m_root, "fr"), "FederationRemoteClient").ConfigureAwait(false);
            using ISession remoteSession = await remoteConnection.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"),
                SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            Ua.Server.NodeManagerRegistration? lookupRegistration = null;
            try
            {
                WotRegistryClient remote = await WotRegistryClient.ForServerAsync(remoteSession, m_telemetry, ct)
                    .ConfigureAwait(false);
                XRegistryFederationTarget target = CreateNativeFederationTarget(group, first);
                lookupRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                    new FederationContentLookupFactory(firstBytes), null, ct).ConfigureAwait(false);
                await remoteSession.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
                var contentClient = new GenericXRegistryClient(remoteSession, kFederationContentNamespace, m_telemetry);
                Assert.That(await contentClient.ResolveResourceAsync(firstBytes, ct: ct).ConfigureAwait(false),
                    Is.EqualTo(firstBytes));
                var options = new XRegistryServerOptions
                {
                    PublishFederationProxy = true,
                    RegistryNamespaceUri = kFederationProxyNamespace,
                    FederationTarget = target,
                    FederationProvider = remote,
                    RemoteEndpointUrl = remoteSession.ConfiguredEndpoint.Description.EndpointUrl ??
                        throw new InvalidOperationException("The connected Session has no endpoint locator."),
                    FederatedFormat = "application/td+json"
                };
                var localFixture = new ServerFixture<FederationProxyServer>(
                    telemetry => new FederationProxyServer(telemetry, options))
                {
                    AutoAccept = true
                };
                FederationProxyServer? localServer = null;
                try
                {
                    localServer = await localFixture.StartAsync(Path.Combine(m_root, "fl")).ConfigureAwait(false);
                    await using var localConnection = new ClientFixture(m_telemetry);
                    await localConnection.LoadClientConfigurationAsync(
                        Path.Combine(m_root, "fc"), "FederationLocalClient").ConfigureAwait(false);
                    using ISession localSession = await localConnection.ConnectAsync(
                        new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{localFixture.Port}"),
                        SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                    try
                    {
                        var localNodeId = new NodeId(XRegistryWellKnown.FederationProxyObject,
                            (ushort)localSession.NamespaceUris.GetIndex(kFederationProxyNamespace));
                        ExpandedNodeId before = await ReadFederationReferenceAsync(localSession, localNodeId, ct)
                            .ConfigureAwait(false);
                        DataValue localXid = await ReadFederationPropertyAsync(
                            localSession, localNodeId, XRegistry.BrowseNames.Xid, ct).ConfigureAwait(false);
                        int reads = m_store.Blobs.Reads;
                        ResourceTypeClient resource = await remote.FollowExternalReferenceAsync(
                            localSession, localNodeId, target, ct).ConfigureAwait(false);
                        using ISessionClient resourceSession = resource.Session;
                        ResourceVersionsTypeClient? versions = await resource.GetVersionsAsync(m_telemetry, ct)
                            .ConfigureAwait(false);
                        Assert.That(versions, Is.Not.Null);
                        var browser = new Browser(remoteSession, new BrowserOptions
                        {
                            ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
                        });
                        ArrayOf<ReferenceDescription> versionNodes = await browser.BrowseAsync(versions!.ObjectId, ct)
                            .ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(m_store.Blobs.Reads, Is.EqualTo(reads),
                                "Verification must not read file content.");
                            Assert.That(resource.ObjectId, Is.EqualTo(first.LogicalResource.ResourceNodeId));
                            Assert.That(resource.ObjectId, Is.Not.EqualTo(first.Version.ResourceNodeId));
                            Assert.That(versionNodes.ToArrayOf(reference =>
                                ExpandedNodeId.ToNodeId(reference.NodeId, remoteSession.NamespaceUris)).ToArray(),
                                Does.Contain(first.Version.ResourceNodeId).And.Contain(second.Version.ResourceNodeId));
                            Assert.That(localSession.ServerUris.GetString(before.ServerIndex),
                                Is.EqualTo(m_serverFixture.Config.ApplicationUri));
                            Assert.That(before.ServerIndex, Is.Not.Zero);
                            Assert.That(before.NamespaceUri, Is.EqualTo(Namespaces.WotCon));
                            Assert.That(localSession.NamespaceUris.GetIndex(Namespaces.WotCon), Is.EqualTo(-1));
                            Assert.That(remoteSession.NamespaceUris.GetIndex(Namespaces.WotCon), Is.GreaterThan(0));
                        });

                        uint oldHandle = await resource.OpenAsync(1, ct).ConfigureAwait(false);
                        uint newHandle = 0;
                        ISessionClient? currentBinding = null;
                        try
                        {
                            await m_server.NodeManagerLifecycle.RemoveAsync(lookupRegistration, null, ct)
                                .ConfigureAwait(false);
                            lookupRegistration = null;
                            Assert.That((await contentClient.ResolveResourceAsync(firstBytes, ct: ct)
                                .ConfigureAwait(false)).IsNull, Is.True, "The independent fast path has been retired.");
                            await second.Version.SetDefaultVersionAsync("v2", expectedEpoch: 0, ct: ct)
                                .ConfigureAwait(false);
                            await WaitForFederationDefaultAsync(resource.ObjectId, "v2", ct).ConfigureAwait(false);
                            lookupRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                                new FederationContentLookupFactory(secondBytes), null, ct).ConfigureAwait(false);
                            Assert.That(await contentClient.ResolveResourceAsync(secondBytes, ct: ct)
                                    .ConfigureAwait(false),
                                Is.EqualTo(secondBytes));
                            ResourceTypeClient current = await remote.FollowExternalReferenceAsync(
                                localSession, localNodeId, target, ct).ConfigureAwait(false);
                            currentBinding = current.Session;
                            newHandle = await current.OpenAsync(1, ct).ConfigureAwait(false);
                            ByteString oldContent = await resource.ReadAsync(oldHandle, 4096, ct).ConfigureAwait(false);
                            ByteString newContent = await current.ReadAsync(newHandle, 4096, ct).ConfigureAwait(false);
                            ExpandedNodeId after = await ReadFederationReferenceAsync(localSession, localNodeId, ct)
                                .ConfigureAwait(false);
                            DataValue afterXid = await ReadFederationPropertyAsync(
                                localSession, localNodeId, XRegistry.BrowseNames.Xid, ct).ConfigureAwait(false);
                            Assert.Multiple(() =>
                            {
                                Assert.That(after, Is.EqualTo(before));
                                Assert.That(current.ObjectId, Is.EqualTo(resource.ObjectId));
                                Assert.That(afterXid.WrappedValue, Is.EqualTo(localXid.WrappedValue));
                                Assert.That(oldContent, Is.EqualTo(firstBytes));
                                Assert.That(newContent, Is.EqualTo(secondBytes));
                                Assert.That(newHandle, Is.Not.EqualTo(oldHandle));
                            });
                        }
                        finally
                        {
                            try
                            {
                                try
                                {
                                    await resource.CloseAsync(oldHandle, CancellationToken.None).ConfigureAwait(false);
                                }
                                finally
                                {
                                    if (newHandle != 0)
                                    {
                                        await resource.CloseAsync(newHandle, CancellationToken.None)
                                            .ConfigureAwait(false);
                                    }
                                }
                            }
                            finally
                            {
                                currentBinding?.Dispose();
                            }
                        }

                        await using var alternateConnection = new ClientFixture(m_telemetry);
                        await alternateConnection.LoadClientConfigurationAsync(
                            Path.Combine(m_root, "fa"), "FederationAlternateClient").ConfigureAwait(false);
                        EndpointDescription originalEndpoint = remoteSession.Endpoint;
                        string alternateUrl = new UriBuilder(originalEndpoint.EndpointUrl!)
                        {
                            Host = "127.0.0.1"
                        }.Uri.OriginalString;
                        var alternateEndpoint = new EndpointDescription
                        {
                            EndpointUrl = alternateUrl,
                            Server = originalEndpoint.Server,
                            ServerCertificate = originalEndpoint.ServerCertificate,
                            SecurityMode = originalEndpoint.SecurityMode,
                            SecurityPolicyUri = originalEndpoint.SecurityPolicyUri,
                            UserIdentityTokens = originalEndpoint.UserIdentityTokens,
                            TransportProfileUri = originalEndpoint.TransportProfileUri,
                            SecurityLevel = originalEndpoint.SecurityLevel
                        };
                        using ISession alternateSession = await alternateConnection.ConnectAsync(new ConfiguredEndpoint(
                            null, alternateEndpoint, EndpointConfiguration.Create(alternateConnection.Config)))
                            .ConfigureAwait(false);
                        try
                        {
                            WotRegistryClient alternate = await WotRegistryClient.ForServerAsync(
                                alternateSession, m_telemetry, ct).ConfigureAwait(false);
                            await localServer.Manager.UpdateEndpointAsync(alternateUrl, alternate, ct)
                                .ConfigureAwait(false);
                            ResourceTypeClient relocated = await alternate.FollowExternalReferenceAsync(
                                localSession, localNodeId, target, ct).ConfigureAwait(false);
                            using ISessionClient relocatedSession = relocated.Session;
                            ExpandedNodeId relocatedReference = await ReadFederationReferenceAsync(
                                localSession, localNodeId, ct).ConfigureAwait(false);
                            ByteString relocatedBytes = await relocated.ReadDocumentAsync(ct: ct).ConfigureAwait(false);
                            Assert.Multiple(() =>
                            {
                                Assert.That(alternateSession.Endpoint.EndpointUrl, Is.EqualTo(alternateUrl));
                                Assert.That(alternateUrl, Is.Not.EqualTo(originalEndpoint.EndpointUrl));
                                Assert.That(relocatedReference, Is.EqualTo(before));
                                Assert.That(relocated.ObjectId, Is.EqualTo(resource.ObjectId));
                                Assert.That(relocatedBytes, Is.EqualTo(secondBytes));
                            });
                        }
                        finally
                        {
                            await alternateSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await localSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await localFixture.StopAsync().ConfigureAwait(false);
                    localServer?.Dispose();
                }
            }
            finally
            {
                if (lookupRegistration is not null)
                {
                    await m_server.NodeManagerLifecycle.RemoveAsync(lookupRegistration, null, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await remoteSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [TestCase("missing-resource")]
        [TestCase("content-variable")]
        [TestCase("versions-folder")]
        [TestCase("exact-version")]
        [TestCase("wrong-registry")]
        [TestCase("wrong-application")]
        [TestCase("missing-namespace")]
        [TestCase("conflicting-xid")]
        [TestCase("ambiguous-resource")]
        [TestCase("unsupported-read")]
        public async Task FederationRejectsInvalidRemoteIdentityOrCapabilityBeforeContentReads(string fault)
        {
            StatusCode expectedStatus = fault switch
            {
                "missing-resource" or "missing-namespace" => StatusCodes.BadNodeIdUnknown,
                "content-variable" => StatusCodes.BadNodeClassInvalid,
                "versions-folder" => StatusCodes.BadTypeMismatch,
                "ambiguous-resource" => StatusCodes.BadBrowseNameDuplicated,
                "unsupported-read" => StatusCodes.BadNotSupported,
                _ => StatusCodes.BadSecurityChecksFailed
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:federation:rejection-catalogue", ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                "urn:federation:rejection-thing", "v1", ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget valid = CreateNativeFederationTarget(group, allocation);
            RegistryOriginDataType origin = valid.OriginRegistry;
            ExpandedNodeId resourceId = valid.ResourceNodeId;
            string serverUri = valid.ServerUri;
            ResourceState logical = m_manager.FindPredefinedNode<ResourceState>(
                allocation.LogicalResource.ResourceNodeId)!;
            ResourceState? ambiguousResource = null;
            void PreserveAmbiguity(ISystemContext context, NodeState node, NodeStateChangeMasks changes) =>
                node.BrowseName = logical.BrowseName;
            try
            {
                switch (fault)
                {
                    case "missing-resource":
                        resourceId = new ExpandedNodeId("missing-federation-resource", Namespaces.WotCon);
                        break;
                    case "content-variable":
                        resourceId = NodeId.ToExpandedNodeId(
                            m_manager.FindPredefinedNode<WoTDocumentState>(allocation.Version.ResourceNodeId)!
                                .ContentDigest!.NodeId,
                            m_client.Session.NamespaceUris);
                        break;
                    case "versions-folder":
                        resourceId = NodeId.ToExpandedNodeId(logical.Versions!.NodeId, m_client.Session.NamespaceUris);
                        break;
                    case "exact-version":
                        resourceId = NodeId.ToExpandedNodeId(
                            allocation.Version.ResourceNodeId, m_client.Session.NamespaceUris);
                        break;
                    case "wrong-registry":
                        origin.RegistryNodeId = new ExpandedNodeId("other-registry", Namespaces.WotCon);
                        break;
                    case "wrong-application":
                        origin.ServerUri = serverUri = "urn:untrusted:application";
                        break;
                    case "missing-namespace":
                        resourceId = resourceId.WithNamespaceUri("urn:federation:missing-namespace");
                        break;
                    case "conflicting-xid":
                        logical.Xid!.Value = "/groups/other/resources/other";
                        break;
                    case "ambiguous-resource":
                        WotRegistryResourceAllocation other = await group.CreateThingDescriptionResourceAsync(
                            "urn:federation:other-thing", "v1", ct: ct).ConfigureAwait(false);
                        ambiguousResource = m_manager.FindPredefinedNode<ResourceState>(
                            other.LogicalResource.ResourceNodeId)!;
                        // Queued projection updates must not remove the fault before native verification.
                        ambiguousResource.StateChanged += PreserveAmbiguity;
                        ambiguousResource.BrowseName = logical.BrowseName;
                        await group.CreateThingDescriptionResourceAsync(
                            "urn:federation:reconcile-trigger", "v1", ct: ct).ConfigureAwait(false);
                        break;
                    case "unsupported-read":
                        logical.Read!.Executable = false;
                        break;
                    default:
                        Assert.Fail("Unknown federation fault.");
                        break;
                }
                var target = new XRegistryFederationTarget(origin, serverUri, resourceId, valid.ResourceXid);

                await WithNativeFederationClientAsync(async (session, client) =>
                {
                    int before = m_store.Blobs.Reads;
                    if (ambiguousResource is not null)
                    {
                        var browser = new Browser(session, new BrowserOptions
                        {
                            ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences
                        });
                        ArrayOf<ReferenceDescription> references = await browser.BrowseAsync(group.GroupNodeId, ct)
                            .ConfigureAwait(false);
                        int matchingNames = 0;
                        foreach (ReferenceDescription reference in references)
                        {
                            if (reference.BrowseName == logical.BrowseName)
                            {
                                matchingNames++;
                            }
                        }
                        Assert.Multiple(() =>
                        {
                            Assert.That(ambiguousResource.NodeId, Is.Not.EqualTo(logical.NodeId));
                            Assert.That(matchingNames, Is.EqualTo(2));
                        });
                    }
                    await Assert.ThatAsync(async () => await client.VerifyLogicalResourceAsync(
                            target, session.Endpoint.EndpointUrl!, ct).ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(expectedStatus))
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(m_store.Blobs.Reads, Is.EqualTo(before));
                        Assert.That(logical.OpenCount!.Value, Is.Zero);
                        Assert.That(session.NamespaceUris.GetIndex("urn:federation:missing-namespace"), Is.EqualTo(-1));
                        if (ambiguousResource is not null)
                        {
                            Assert.That(ambiguousResource.BrowseName, Is.EqualTo(logical.BrowseName));
                        }
                    });
                }).ConfigureAwait(false);
            }
            finally
            {
                ambiguousResource?.StateChanged -= PreserveAmbiguity;
            }
        }

        [Test]
        public async Task FederationUsesAuthenticatedChannelInsteadOfMutableEndpointLabels()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:federation:channel-catalogue", ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                "urn:federation:channel-thing", "v1", ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget target = CreateNativeFederationTarget(group, allocation);
            int reads = m_store.Blobs.Reads;

            await Assert.ThatAsync(async () => await m_client.VerifyLogicalResourceAsync(
                    target, m_client.Session.Endpoint.EndpointUrl!, ct).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityModeInsufficient)).ConfigureAwait(false);

            await WithNativeFederationClientAsync(async (session, client) =>
            {
                EndpointDescription authenticated = session.Endpoint;
                EndpointDescription configured = session.ConfiguredEndpoint.Description;
                var misleading = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://not-authorized.invalid:4840",
                    Server = new ApplicationDescription { ApplicationUri = "urn:federation:false-label" },
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                };
                session.ConfiguredEndpoint.Update(misleading);
                try
                {
                    await client.VerifyLogicalResourceAsync(target, authenticated.EndpointUrl!, ct)
                        .ConfigureAwait(false);
                    await Assert.ThatAsync(async () => await client.VerifyLogicalResourceAsync(
                            target, misleading.EndpointUrl, ct).ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    var providerOrigin = new XRegistryFederationTarget(new RegistryOriginDataType
                    {
                        OriginUri = "urn:federation:non-native-origin",
                        ServerUri = string.Empty,
                        RegistryNodeId = ExpandedNodeId.Null
                    }, target.ServerUri, target.ResourceNodeId, target.ResourceXid);
                    await Assert.ThatAsync(async () => await client.VerifyLogicalResourceAsync(
                            providerOrigin, authenticated.EndpointUrl!, ct).ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(session.Endpoint.EndpointUrl, Is.EqualTo(authenticated.EndpointUrl));
                        Assert.That(session.Endpoint.Server.ApplicationUri, Is.EqualTo(target.ServerUri));
                        Assert.That(session.ConfiguredEndpoint.Description.EndpointUrl,
                            Is.EqualTo(misleading.EndpointUrl));
                        Assert.That(m_store.Blobs.Reads, Is.EqualTo(reads));
                        Assert.That(m_manager.FindPredefinedNode<ResourceState>(
                            allocation.LogicalResource.ResourceNodeId)!.OpenCount!.Value, Is.Zero);
                    });
                }
                finally
                {
                    session.ConfiguredEndpoint.Update(configured);
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task FederationKeepsEqualContentInDistinctRemoteEntities()
        {
            const string source = "urn:federation:shared-description";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient firstGroup = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:federation:first-catalogue", ct).ConfigureAwait(false);
            WotRegistryGroupClient secondGroup = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:federation:second-catalogue", ct).ConfigureAwait(false);
            WotRegistryResourceAllocation first = await firstGroup.CreateThingDescriptionResourceAsync(
                source, "v1", ct: ct).ConfigureAwait(false);
            WotRegistryResourceAllocation second = await secondGroup.CreateThingDescriptionResourceAsync(
                source, "v1", ct: ct).ConfigureAwait(false);
            var bytes = ByteString.From(TestMaterialization.Td(source, "identical"));
            await first.Version.Proxy.UploadAsync(bytes, ct: ct).ConfigureAwait(false);
            await second.Version.Proxy.UploadAsync(bytes, ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget firstTarget = CreateNativeFederationTarget(firstGroup, first);
            XRegistryFederationTarget secondTarget = CreateNativeFederationTarget(secondGroup, second);

            await WithNativeFederationClientAsync(async (session, client) =>
            {
                await client.VerifyLogicalResourceAsync(firstTarget, session.Endpoint.EndpointUrl!, ct)
                    .ConfigureAwait(false);
                await client.VerifyLogicalResourceAsync(secondTarget, session.Endpoint.EndpointUrl!, ct)
                    .ConfigureAwait(false);
                ByteString firstContent = await client.GetResource(first.LogicalResource.ResourceNodeId)
                    .ReadDocumentAsync(ct: ct).ConfigureAwait(false);
                ByteString secondContent = await client.GetResource(second.LogicalResource.ResourceNodeId)
                    .ReadDocumentAsync(ct: ct).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(firstTarget.OriginRegistry, Is.EqualTo(secondTarget.OriginRegistry));
                    Assert.That(firstTarget.ResourceNodeId, Is.Not.EqualTo(secondTarget.ResourceNodeId));
                    Assert.That(firstTarget.ResourceXid, Is.Not.EqualTo(secondTarget.ResourceXid));
                    Assert.That(firstContent, Is.EqualTo(bytes));
                    Assert.That(secondContent, Is.EqualTo(bytes));
                    Assert.That(firstGroup.GroupId, Is.Not.EqualTo(secondGroup.GroupId));
                    Assert.That(first.LogicalResource.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
                    Assert.That(second.LogicalResource.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
                });
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task FederationRebasesCurrentNativeNamespaceAndServerTables()
        {
            const string source = "urn:federation:reordered-thing";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:federation:reordered-catalogue", ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                source, "v1", ct: ct).ConfigureAwait(false);
            var bytes = ByteString.From(TestMaterialization.Td(source, "reordered"));
            await allocation.Version.Proxy.UploadAsync(bytes, ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget target = CreateNativeFederationTarget(group, allocation);

            await WithNativeFederationClientAsync(async (remoteSession, remote) =>
                await WithNativeFederationProxyAsync(target, remote, async (localServer, localSession, localNodeId) =>
                {
                    ResourceTypeClient original = await remote.FollowExternalReferenceAsync(
                        localSession, localNodeId, target, ct).ConfigureAwait(false);
                    using ISessionClient originalSession = original.Session;
                    var local = (ResourceState)localServer.Manager.Find(localNodeId)!;
                    ExpandedNodeId originalReference = await ReadFederationReferenceAsync(
                        localSession, localNodeId, ct).ConfigureAwait(false);
                    int originalNamespace = remoteSession.NamespaceUris.GetIndex(Namespaces.WotCon);
                    await WithRelocatedNativeFederationClientAsync(
                        target, group.GroupId, allocation.LogicalResource.ResourceId,
                        async (replacementSession, replacement) =>
                    {
                        int replacementNamespace = replacementSession.NamespaceUris.GetIndex(Namespaces.WotCon);
                        int extraServer = localServer.CurrentInstance.ServerUris.Append("urn:federation:unused-server");
                        string[] serverUris = localServer.CurrentInstance.ServerUris.ToArray();
                        (serverUris[originalReference.ServerIndex], serverUris[extraServer]) =
                            (serverUris[extraServer], serverUris[originalReference.ServerIndex]);
                        localServer.CurrentInstance.ServerUris.Update(serverUris);
                        await localSession.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
                        ExpandedNodeId reordered = await ReadFederationReferenceAsync(localSession, localNodeId, ct)
                            .ConfigureAwait(false);
                        await localServer.Manager.UpdateEndpointAsync(
                            replacementSession.Endpoint.EndpointUrl!, replacement, ct).ConfigureAwait(false);
                        ResourceTypeClient rebased = await replacement.FollowExternalReferenceAsync(
                            localSession, localNodeId, target, ct).ConfigureAwait(false);
                        using ISessionClient rebasedSession = rebased.Session;
                        ByteString content = await rebased.ReadDocumentAsync(ct: ct).ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(replacementNamespace, Is.Not.EqualTo(originalNamespace));
                            Assert.That(rebased.ObjectId.NamespaceIndex, Is.EqualTo(replacementNamespace));
                            Assert.That(rebased.ObjectId, Is.Not.EqualTo(original.ObjectId));
                            Assert.That(NodeId.ToExpandedNodeId(rebased.ObjectId, replacementSession.NamespaceUris),
                                Is.EqualTo(target.ResourceNodeId));
                            Assert.That(reordered.ServerIndex,
                                Is.EqualTo(extraServer).And.Not.EqualTo(originalReference.ServerIndex));
                            Assert.That(localSession.ServerUris.GetString(reordered.ServerIndex),
                                Is.EqualTo(target.ServerUri));
                            Assert.That(content, Is.EqualTo(bytes));
                        });

                        if (localServer.CurrentInstance.NamespaceUris.Count == replacementNamespace)
                        {
                            localServer.CurrentInstance.NamespaceUris.Append("urn:federation:source-index-separator");
                        }
                        int sourceNamespace = localServer.CurrentInstance.NamespaceUris.Append(Namespaces.WotCon);
                        await localSession.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
                        local.ExternalReference!.OnSimpleReadValue = null;
                        local.ExternalReference.Value = new ExpandedNodeId(
                            target.ResourceNodeId.InnerNodeId.WithNamespaceIndex((ushort)sourceNamespace),
                            null, reordered.ServerIndex);
                        ResourceTypeClient indexed = await replacement.FollowExternalReferenceAsync(
                            localSession, localNodeId, target, ct).ConfigureAwait(false);
                        using ISessionClient indexedSession = indexed.Session;
                        Assert.Multiple(() =>
                        {
                            Assert.That(sourceNamespace, Is.Not.EqualTo(replacementNamespace));
                            Assert.That(indexed.ObjectId, Is.EqualTo(rebased.ObjectId),
                                "A source namespace index must not be reused in the remote Session.");
                            Assert.That(local.OriginRegistry!.Value, Is.EqualTo(target.OriginRegistry));
                            Assert.That(local.Xid!.Value,
                                Is.EqualTo("/groups/federated/resources/federated-resource/versions/1"));
                        });
                    }, ct).ConfigureAwait(false);
                }).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task FederationProxyMetadataCannotReplaceTrustedOriginOrEntity()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:federation:metadata-catalogue", ct).ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
                "urn:federation:metadata-thing", "v1", ct: ct).ConfigureAwait(false);
            XRegistryFederationTarget target = CreateNativeFederationTarget(group, allocation);

            await WithNativeFederationClientAsync(async (remoteSession, remote) =>
                await WithNativeFederationProxyAsync(target, remote, async (localServer, localSession, localNodeId) =>
                {
                    var proxy = (ResourceState)localServer.Manager.Find(localNodeId)!;
                    ExpandedNodeId reference = proxy.ExternalReference!.Value;
                    string? endpointUrl = proxy.ResourceUrl!.Value;
                    NodeId originDataType = proxy.OriginRegistry!.DataType;
                    NodeId referenceDataType = proxy.ExternalReference.DataType;
                    NodeId locatorDataType = proxy.ResourceUrl.DataType;
                    ResourceState remoteResource = m_manager.FindPredefinedNode<ResourceState>(
                        allocation.LogicalResource.ResourceNodeId)!;
                    NodeId xidDataType = remoteResource.Xid!.DataType;
                    proxy.ExternalReference.OnSimpleReadValue = null;
                    string[] faults =
                    [
                        "origin", "mixed-origin", "resource", "self-server", "unknown-server", "unknown-namespace",
                        "locator", "origin-data-type", "origin-rank", "reference-data-type", "reference-rank",
                        "locator-data-type", "locator-rank", "xid-data-type", "xid-rank"
                    ];
                    foreach (string fault in faults)
                    {
                        proxy.OriginRegistry.Value = target.OriginRegistry;
                        proxy.OriginRegistry.DataType = originDataType;
                        proxy.OriginRegistry.ValueRank = ValueRanks.Scalar;
                        proxy.ExternalReference.Value = reference;
                        proxy.ExternalReference.DataType = referenceDataType;
                        proxy.ExternalReference.ValueRank = ValueRanks.Scalar;
                        proxy.ResourceUrl.Value = endpointUrl;
                        proxy.ResourceUrl.DataType = locatorDataType;
                        proxy.ResourceUrl.ValueRank = ValueRanks.Scalar;
                        remoteResource.Xid.DataType = xidDataType;
                        remoteResource.Xid.ValueRank = ValueRanks.Scalar;
                        StatusCode expectedStatus = StatusCodes.BadSecurityChecksFailed;
                        switch (fault)
                        {
                            case "origin":
                                proxy.OriginRegistry.Value.ServerUri = "urn:untrusted:origin";
                                break;
                            case "mixed-origin":
                                proxy.OriginRegistry.Value.OriginUri = "urn:untrusted:origin";
                                break;
                            case "resource":
                                proxy.ExternalReference.Value = new ExpandedNodeId(
                                    "untrusted-resource", reference.NamespaceUri, reference.ServerIndex);
                                break;
                            case "self-server":
                                proxy.ExternalReference.Value = reference.WithServerIndex(0);
                                break;
                            case "unknown-server":
                                proxy.ExternalReference.Value = reference.WithServerIndex(uint.MaxValue);
                                break;
                            case "unknown-namespace":
                                proxy.ExternalReference.Value = new ExpandedNodeId(
                                    reference.InnerNodeId.WithNamespaceIndex(ushort.MaxValue),
                                    null, reference.ServerIndex);
                                break;
                            case "locator":
                                proxy.ResourceUrl.Value = "opc.tcp://not-configured.invalid:4840";
                                break;
                            case "origin-data-type":
                                proxy.OriginRegistry.DataType = Ua.DataTypeIds.String;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            case "origin-rank":
                                proxy.OriginRegistry.ValueRank = ValueRanks.OneDimension;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            case "reference-data-type":
                                proxy.ExternalReference.DataType = Ua.DataTypeIds.String;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            case "reference-rank":
                                proxy.ExternalReference.ValueRank = ValueRanks.OneDimension;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            case "locator-data-type":
                                proxy.ResourceUrl.DataType = Ua.DataTypeIds.ExpandedNodeId;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            case "locator-rank":
                                proxy.ResourceUrl.ValueRank = ValueRanks.OneDimension;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            case "xid-data-type":
                                remoteResource.Xid.DataType = Ua.DataTypeIds.UInt32;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            case "xid-rank":
                                remoteResource.Xid.ValueRank = ValueRanks.OneDimension;
                                expectedStatus = StatusCodes.BadTypeMismatch;
                                break;
                            default:
                                Assert.Fail("Unknown proxy fault.");
                                break;
                        }
                        BaseVariableState? declaration = fault switch
                        {
                            "origin-data-type" or "origin-rank" => proxy.OriginRegistry,
                            "reference-data-type" or "reference-rank" => proxy.ExternalReference,
                            "locator-data-type" or "locator-rank" => proxy.ResourceUrl,
                            "xid-data-type" or "xid-rank" => remoteResource.Xid,
                            _ => null
                        };
                        if (declaration is not null)
                        {
                            ISession metadataSession = fault is "xid-data-type" or "xid-rank"
                                ? remoteSession
                                : localSession;
                            Node observed = await metadataSession.ReadNodeAsync(declaration.NodeId, ct)
                                .ConfigureAwait(false);
                            Assert.That(observed, Is.InstanceOf<VariableNode>(), $"Native declaration: {fault}");
                            var variable = (VariableNode)observed;
                            Assert.Multiple(() =>
                            {
                                Assert.That(variable.DataType, Is.EqualTo(declaration.DataType),
                                    $"Native declaration: {fault}");
                                Assert.That(variable.ValueRank, Is.EqualTo(declaration.ValueRank),
                                    $"Native declaration: {fault}");
                            });
                        }
                        int reads = m_store.Blobs.Reads;
                        await Assert.ThatAsync(async () =>
                        {
                            ResourceTypeClient unexpected = await remote.FollowExternalReferenceAsync(
                                localSession, localNodeId, target, ct).ConfigureAwait(false);
                            using ISessionClient binding = unexpected.Session;
                            await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        }, Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(expectedStatus),
                            $"Proxy fault: {fault}").ConfigureAwait(false);
                        Assert.That(m_store.Blobs.Reads, Is.EqualTo(reads), $"Proxy fault: {fault}");
                        Assert.That(remoteSession.Endpoint.EndpointUrl, Is.EqualTo(endpointUrl));
                    }
                }).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task WithNativeFederationProxyAsync(
            XRegistryFederationTarget target,
            WotRegistryClient remote,
            Func<FederationProxyServer, ISession, NodeId, Task> action)
        {
            var options = new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                RegistryNamespaceUri = kFederationProxyNamespace,
                FederationTarget = target,
                FederationProvider = remote,
                RemoteEndpointUrl = remote.Session.Endpoint.EndpointUrl!
            };
            var fixture = new ServerFixture<FederationProxyServer>(
                telemetry => new FederationProxyServer(telemetry, options))
            {
                AutoAccept = true
            };
            FederationProxyServer? server = null;
            try
            {
                server = await fixture.StartAsync(Path.Combine(m_root, "fp")).ConfigureAwait(false);
                await using var connection = new ClientFixture(m_telemetry);
                await connection.LoadClientConfigurationAsync(Path.Combine(m_root, "fpc"), "FederationProxyClient")
                    .ConfigureAwait(false);
                using ISession session = await connection.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"), SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                try
                {
                    var nodeId = new NodeId(XRegistryWellKnown.FederationProxyObject,
                        (ushort)session.NamespaceUris.GetIndex(kFederationProxyNamespace));
                    await action(server, session, nodeId).ConfigureAwait(false);
                }
                finally
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
            }
        }

        private async Task WithNativeFederationClientAsync(Func<ISession, WotRegistryClient, Task> action)
        {
            await using var connection = new ClientFixture(m_telemetry);
            await connection.LoadClientConfigurationAsync(
                Path.Combine(m_root, "fv"), "FederationVerifier").ConfigureAwait(false);
            using ISession session = await connection.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"),
                SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            try
            {
                WotRegistryClient client = await WotRegistryClient.ForServerAsync(session, m_telemetry)
                    .ConfigureAwait(false);
                await action(session, client).ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task WithRelocatedNativeFederationClientAsync(
            XRegistryFederationTarget target,
            string groupId,
            string resourceId,
            Func<ISession, WotRegistryClient, Task> action,
            CancellationToken ct)
        {
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                AutoAccept = true
            };
            ReferenceServer? server = null;
            WotMaterializationCoordinator? coordinator = null;
            WotRegistryService? restoredRegistry = null;
            try
            {
                server = await fixture.StartAsync(Path.Combine(m_root, "fb")).ConfigureAwait(false);
                Assert.That(fixture.Config.ApplicationUri, Is.EqualTo(target.ServerUri));
                server.CurrentInstance.NamespaceUris.Append("urn:federation:before-registry");
                restoredRegistry = new WotRegistryService(m_store, m_options.Bounds, m_options.IdentityBindings);
                await restoredRegistry.InitializeAsync(ct).ConfigureAwait(false);
                WotResource? restored = restoredRegistry.Current.FindResource(groupId, resourceId);
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored!.Xid, Is.EqualTo(target.ResourceXid));
                // Projection registration is separate from the native registry and federation behavior under test.
                var projectionHost = new FakeWotProjectionHost();
                coordinator = new WotMaterializationCoordinator(
                    restoredRegistry, projectionHost,
                    documentConverter: new FakeWotDocumentConverter());
                await server.NodeManagerLifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(m_options, restoredRegistry, coordinator), null, ct)
                    .ConfigureAwait(false);
                Assert.That(projectionHost.AddCount, Is.EqualTo(1));
                await using var connection = new ClientFixture(m_telemetry);
                await connection.LoadClientConfigurationAsync(Path.Combine(m_root, "fbc"), "FederationRebasedClient")
                    .ConfigureAwait(false);
                using ISession session = await connection.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"), SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                try
                {
                    WotRegistryClient client = await WotRegistryClient.ForServerAsync(session, m_telemetry, ct)
                        .ConfigureAwait(false);
                    Assert.That(NodeId.ToExpandedNodeId(client.RegistryNodeId, session.NamespaceUris),
                        Is.EqualTo(target.OriginRegistry.RegistryNodeId));
                    await action(session, client).ConfigureAwait(false);
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
                restoredRegistry?.Dispose();
                server?.Dispose();
            }
        }

        private XRegistryFederationTarget CreateNativeFederationTarget(
            WotRegistryGroupClient group,
            WotRegistryResourceAllocation allocation)
        {
            WotResource resource = m_registry.Current.FindResource(group.GroupId, allocation.Version.ResourceId)!;
            string applicationUri = m_serverFixture.Config.ApplicationUri ??
                throw new InvalidOperationException("The controlled remote server has no application identity.");
            return new XRegistryFederationTarget(
                new RegistryOriginDataType
                {
                    OriginUri = string.Empty,
                    ServerUri = applicationUri,
                    RegistryNodeId = NodeId.ToExpandedNodeId(m_client.RegistryNodeId, m_client.Session.NamespaceUris)
                },
                applicationUri,
                NodeId.ToExpandedNodeId(allocation.LogicalResource.ResourceNodeId, m_client.Session.NamespaceUris),
                resource.Xid);
        }

        private async Task WaitForFederationDefaultAsync(NodeId logicalResource, string versionId, CancellationToken ct)
        {
            while (!string.Equals(
                await ReadStringAsync(logicalResource, XRegistry.BrowseNames.VersionId,
                    XRegistryWellKnown.XRegistryNamespaceUri).ConfigureAwait(false),
                versionId, StringComparison.Ordinal))
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        private static async ValueTask<ExpandedNodeId> ReadFederationReferenceAsync(
            ISession session, NodeId proxy, CancellationToken ct)
        {
            DataValue value = await ReadFederationPropertyAsync(
                session, proxy, XRegistry.BrowseNames.ExternalReference, ct).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out ExpandedNodeId reference), Is.True);
            return reference;
        }

        private static async ValueTask<DataValue> ReadFederationPropertyAsync(
            ISession session, NodeId proxy, string name, CancellationToken ct)
        {
            var browser = new Browser(session, new BrowserOptions
            {
                ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty
            });
            var expectedName = new QualifiedName(name,
                (ushort)session.NamespaceUris.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri));
            ArrayOf<ReferenceDescription> references = await browser.BrowseAsync(proxy, ct).ConfigureAwait(false);
            ReferenceDescription property = references.ToArray()!.Single(
                reference => reference.BrowseName == expectedName);
            DataValue value = await session.ReadValueAsync(
                ExpandedNodeId.ToNodeId(property.NodeId, session.NamespaceUris), ct).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            return value;
        }

        private sealed class FederationProxyServer : ReferenceServer
        {
            public FederationProxyServer(ITelemetryContext telemetry, XRegistryServerOptions options)
                : base(telemetry)
            {
                m_factory = new FederationProxyFactory(options);
                AddNodeManager(m_factory);
            }

            public XRegistryFederationNodeManager Manager => m_factory.Manager!;

            private readonly FederationProxyFactory m_factory;
        }

        private sealed class FederationProxyFactory : Ua.Server.IAsyncNodeManagerFactory
        {
            public FederationProxyFactory(XRegistryServerOptions options)
            {
                m_options = options;
            }

            public ArrayOf<string> NamespacesUris => [kFederationProxyNamespace];

            public XRegistryFederationNodeManager? Manager { get; private set; }

            public ValueTask<Ua.Server.IAsyncNodeManager> CreateAsync(
                Ua.Server.IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new XRegistryFederationNodeManager(server, configuration, m_options);
                return new ValueTask<Ua.Server.IAsyncNodeManager>(Manager);
            }

            private readonly XRegistryServerOptions m_options;
        }

        private sealed class FederationContentLookupFactory
            : Ua.Server.IAsyncNodeManagerFactory, IResourceContentIdProvider
        {
            public FederationContentLookupFactory(ByteString document)
            {
                m_document = document;
            }

            public ArrayOf<string> NamespacesUris => [kFederationContentNamespace];

            public ValueTask<Ua.Server.IAsyncNodeManager> CreateAsync(
                Ua.Server.IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<Ua.Server.IAsyncNodeManager>(new XRegistryFastPathNodeManager(
                    server, configuration, new XRegistryServerOptions
                    {
                        RegistryNamespaceUri = kFederationContentNamespace,
                        PublishSeedResource = true,
                        SeedDocument = m_document,
                        ContentIdProvider = this
                    }));
            }

            public ByteString ComputeContentId(string format, ReadOnlySpan<byte> document)
            {
                return ByteString.From(document.ToArray());
            }

            public string? GetAlgorithm(string format)
            {
                return "test";
            }

            private readonly ByteString m_document;
        }

        private const string kFederationProxyNamespace = "urn:opcua:tests:federation:proxy";
        private const string kFederationContentNamespace = "urn:opcua:tests:federation:content";
    }
}
