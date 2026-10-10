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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Server;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the federation proxy: a resource hosted by another registry is represented locally
    /// by a proxy carrying an <c>ExternalReference</c> (an ExpandedNodeId naming the remote server
    /// through the ServerArray) and a <c>ResourceUrl</c>, while retaining structural xRegistry
    /// Resource and Version identity independently of the opaque content lookup.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryFederationNodeManagerTests
    {
        [Test]
        public async Task ProxyDisabledPublishesNothingAsync()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                FederatedDocument = ByteString.From(s_federatedDocument),
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(nm.Find(ProxyNodeId(nm)), Is.Null);
        }

        [Test]
        public async Task VerifiedProxyDoesNotRequireDocumentOrContentProviderAsync()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(CreateFederationOptions());

            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(nm.Find(ProxyNodeId(nm)), Is.TypeOf<ResourceState>());
        }

        [Test]
        public void ProxyWithoutTrustedLogicalIdentityRejectsConfiguration()
        {
            var options = new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                FederatedDocument = ByteString.From(s_federatedDocument)
            };

            Assert.That(
                () => CreateNodeManager(options),
                Throws.ArgumentException.With.Message.Contains("FederationTarget"));
        }

        [Test]
        public async Task ProxyIsAResourceTypeInstanceCarryingTheFederationLinkAsync()
        {
            const string remoteEndpoint = "opc.tcp://remote.example.org:4840";
            const string remoteNamespace = "http://example.org/UA/RemoteRegistry/";
            XRegistryServerOptions options = CreateFederationOptions();
            options.RemoteEndpointUrl = remoteEndpoint;
            options.FederationProxyBrowseName = "RemoteResource";
            options.FederatedFormat = "application/json";
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);

            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);

            // The proxy has to be a real ResourceType instance so a generic xRegistry client drives
            // it through the same generated proxy as a locally hosted resource.
            var proxy = (ResourceState?)nm.Find(ProxyNodeId(nm));
            Assert.That(proxy, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(proxy!.BrowseName.Name, Is.EqualTo("RemoteResource"));
                Assert.That(
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.ResourceType, nm.SystemContext.NamespaceUris),
                    Is.EqualTo(proxy.TypeDefinitionId));

                Assert.That(proxy.ExternalReference, Is.Not.Null);
                ExpandedNodeId reference = proxy.ExternalReference!.Value;
                Assert.That(reference.NamespaceUri, Is.EqualTo(remoteNamespace));
                Assert.That(reference.ServerIndex, Is.EqualTo(3u));
                Assert.That(reference.WithServerIndex(0), Is.EqualTo(options.FederationTarget!.ResourceNodeId));
                Assert.That(proxy.OriginRegistry!.Value, Is.EqualTo(options.FederationTarget.OriginRegistry));
                Assert.That(proxy.OriginRegistry.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                Assert.That(proxy.Versions, Is.Null);

                Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo(remoteEndpoint));
                Assert.That(proxy.Format!.Value, Is.EqualTo("application/json"));
                Assert.That(proxy.ResourceId!.Value, Is.EqualTo("federated-resource"));
                Assert.That(proxy.VersionId!.Value, Is.EqualTo("1"));
                Assert.That(
                    proxy.Xid!.Value,
                    Is.EqualTo(
                        "/groups/federated/resources/federated-resource/versions/1"));
                Assert.That(proxy.Epoch!.Value, Is.EqualTo(1u));
            });
        }

        [Test]
        public async Task ProxyContentLookupDoesNotReplaceLogicalResourceIdentityAsync()
        {
            XRegistryServerOptions options = CreateFederationOptions();
            options.FederatedDocument = ByteString.From(s_federatedDocument);
            options.ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider();
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);

            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);

            var proxy = (ResourceState?)nm.Find(ProxyNodeId(nm));
            Assert.Multiple(() =>
            {
                Assert.That(
                    proxy!.Xid!.Value,
                    Is.EqualTo(
                        "/groups/federated/resources/federated-resource/versions/1"));
                Assert.That(
                    proxy.ExternalReference!.Value.TryGetValue(out ByteString identifier)
                        ? identifier
                        : ByteString.Empty,
                    Is.Not.EqualTo(ByteString.From(s_federatedDocument)),
                    "ExternalReference must identify the remote logical Resource, not its content lookup.");
                Assert.That(
                    proxy.ExternalReference.Value.WithServerIndex(0),
                    Is.EqualTo(new ExpandedNodeId("remote-logical-resource", "http://example.org/UA/RemoteRegistry/")));
            });
        }

        [Test]
        public async Task CreateAddressSpaceMaterializesTheGeneratedCompanionModelAsync()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions());

            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                nm.Find(ExpandedNodeId.ToNodeId(ObjectTypeIds.GroupType, nm.SystemContext.NamespaceUris)),
                Is.Not.Null);
        }

        [Test]
        public void NativeRegistryClientProvidesLogicalFederationVerification()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(XRegistryWellKnown.XRegistryNamespaceUri);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            XRegistryClient client = new GenericXRegistryClient(session.Object, NUnitTelemetryContext.Create());

            Assert.That(client, Is.InstanceOf<IXRegistryFederationProvider>());
        }

        [Test]
        public async Task ProxyEndpointUpdateRetainsTrustedLogicalIdentityAsync()
        {
            XRegistryServerOptions options = CreateFederationOptions();
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            await nm.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var proxy = (ResourceState)nm.Find(ProxyNodeId(nm))!;
            ExpandedNodeId reference = proxy.ExternalReference!.Value;
            RegistryOriginDataType origin = proxy.OriginRegistry!.Value;
            string? xid = proxy.Xid!.Value;

            await nm.UpdateEndpointAsync("opc.tcp://alternate.example.org:4841", options.FederationProvider!)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(ProxyNodeId(nm)), Is.SameAs(proxy));
                Assert.That(proxy.ExternalReference.Value, Is.EqualTo(reference));
                Assert.That(proxy.OriginRegistry.Value, Is.EqualTo(origin));
                Assert.That(proxy.Xid.Value, Is.EqualTo(xid));
                Assert.That(proxy.ResourceId!.Value, Is.EqualTo("federated-resource"));
                Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo("opc.tcp://alternate.example.org:4841"));
            });
        }

        [Test]
        public void MissingFederationProviderRejectsBeforePublication()
        {
            XRegistryServerOptions options = CreateFederationOptions();
            options.FederationProvider = null;

            Assert.That(() => CreateNodeManager(options),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("relative/path")]
        [TestCase(" opc.tcp://localhost:4840")]
        [TestCase("opc.tcp://localhost:4840#fragment")]
        [TestCase("opc.tcp://user@localhost:4840")]
        public void InvalidLocatorRejectsBeforeProviderEffects(string? endpointUrl)
        {
            XRegistryServerOptions options = CreateFederationOptions();
            options.RemoteEndpointUrl = endpointUrl!;
            var provider = Mock.Get(options.FederationProvider!);

            Assert.That(() => CreateNodeManager(options), Throws.ArgumentException);
            provider.VerifyNoOtherCalls();
        }

        [Test]
        public void UnsupportedProviderPublishesNoProxyOrCompanionNodes()
        {
            XRegistryServerOptions options = CreateFederationOptions();
            var provider = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            provider.Setup(p => p.VerifyLogicalResourceAsync(
                    options.FederationTarget!, options.RemoteEndpointUrl, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNotSupported));
            options.FederationProvider = provider.Object;
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            var references = new Dictionary<NodeId, IList<IReference>>();

            Assert.That(async () => await nm.CreateAddressSpaceAsync(references).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported));
            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(ProxyNodeId(nm)), Is.Null);
                Assert.That(nm.Find(ExpandedNodeId.ToNodeId(ObjectTypeIds.RegistryType,
                    nm.SystemContext.NamespaceUris)), Is.Null);
                Assert.That(references, Is.Empty);
            });
        }

        [Test]
        public void DependencyInjectionRetainsTheConfiguredFederationProvider()
        {
            XRegistryServerOptions expected = CreateFederationOptions();
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton(expected.FederationProvider!)
                .AddXRegistryServer(options =>
                {
                    options.PublishFederationProxy = true;
                    options.FederationTarget = expected.FederationTarget;
                    options.RemoteEndpointUrl = expected.RemoteEndpointUrl;
                }).BuildServiceProvider();
            XRegistryServerOptions actual = services.GetRequiredService<XRegistryServerOptions>();

            Assert.Multiple(() =>
            {
                Assert.That(actual.FederationProvider, Is.SameAs(expected.FederationProvider));
                Assert.That(actual.FederationTarget, Is.SameAs(expected.FederationTarget));
                Assert.That(actual.Validate, Throws.Nothing);
            });
        }

        [Test]
        public void TrustedOriginIsDefensivelyPinnedAndComparedExactly()
        {
            RegistryOriginDataType origin = CreateFederationOptions().FederationTarget!.OriginRegistry;
            var target = new XRegistryFederationTarget(origin, "urn:example:remote",
                new ExpandedNodeId("entity", "https://Example.org/Registry%2FOne#Nodes"),
                "/groups/remote/resources/entity");
            origin.ServerUri = "urn:example:untrusted";
            RegistryOriginDataType copy = target.OriginRegistry;
            copy.RegistryNodeId = new ExpandedNodeId("other-registry", "http://example.org/UA/RemoteRegistry/");

            Assert.Multiple(() =>
            {
                Assert.That(target.OriginRegistry.ServerUri, Is.EqualTo("urn:example:remote"));
                Assert.That(target.ResourceNodeId.NamespaceUri, Is.EqualTo("https://Example.org/Registry%2FOne#Nodes"));
                Assert.That(() => target.VerifyOrigin(origin),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(() => target.VerifyOrigin(copy),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(() => target.VerifyOrigin(target.OriginRegistry), Throws.Nothing);
            });
        }

        [TestCase("missing-origin")]
        [TestCase("missing-origin-field")]
        [TestCase("mixed-origin")]
        [TestCase("missing-root")]
        [TestCase("indexed-root")]
        [TestCase("root-is-resource")]
        [TestCase("different-application")]
        [TestCase("invalid-application-uri")]
        [TestCase("missing-resource")]
        [TestCase("indexed-resource")]
        [TestCase("resource-server-index")]
        [TestCase("invalid-namespace-uri")]
        [TestCase("missing-xid")]
        [TestCase("version-xid")]
        [TestCase("relative-xid")]
        public void InvalidFederationIdentityRejectsAtTheConfigurationBoundary(string fault)
        {
            XRegistryFederationTarget valid = CreateFederationOptions().FederationTarget!;
            RegistryOriginDataType? origin = valid.OriginRegistry;
            ExpandedNodeId nodeId = valid.ResourceNodeId;
            string serverUri = valid.ServerUri;
            string xid = valid.ResourceXid;
            switch (fault)
            {
                case "missing-origin":
                    origin = null;
                    break;
                case "missing-origin-field":
                    origin.OriginUri = null;
                    break;
                case "mixed-origin":
                    origin.OriginUri = "urn:configured:origin";
                    break;
                case "missing-root":
                    origin.RegistryNodeId = ExpandedNodeId.Null;
                    break;
                case "indexed-root":
                    origin.RegistryNodeId = new ExpandedNodeId(new NodeId("registry", 3));
                    break;
                case "root-is-resource":
                    origin.RegistryNodeId = nodeId;
                    break;
                case "different-application":
                    serverUri = "urn:other:application";
                    break;
                case "invalid-application-uri":
                    serverUri = "relative";
                    break;
                case "missing-resource":
                    nodeId = ExpandedNodeId.Null;
                    break;
                case "indexed-resource":
                    nodeId = new ExpandedNodeId(new NodeId("resource", 3));
                    break;
                case "resource-server-index":
                    nodeId = nodeId.WithServerIndex(2);
                    break;
                case "invalid-namespace-uri":
                    nodeId = nodeId.WithNamespaceUri("relative");
                    break;
                case "missing-xid":
                    xid = string.Empty;
                    break;
                case "version-xid":
                    xid += "/versions/v1";
                    break;
                case "relative-xid":
                    xid = "/groups/../resources/one";
                    break;
                default:
                    Assert.Fail("Unknown identity fault.");
                    break;
            }

            Assert.That(() => new XRegistryFederationTarget(origin!, serverUri, nodeId, xid),
                Throws.InstanceOf<ArgumentException>());
        }

        [TestCase("https://Example.org/Registry%2fA", "https://example.org/Registry%2fA")]
        [TestCase("https://Example.org/Registry%2fA", "https://Example.org/Registry%2FA")]
        [TestCase("urn:Registry:One", "urn:registry:One")]
        public void OriginUriSpellingIsNeverNormalizedIntoTrust(string expected, string observed)
        {
            var target = new XRegistryFederationTarget(new RegistryOriginDataType
            {
                OriginUri = expected,
                ServerUri = string.Empty,
                RegistryNodeId = ExpandedNodeId.Null
            }, "urn:example:remote", new ExpandedNodeId("resource", "urn:example:nodes"),
                "/groups/one/resources/one");
            RegistryOriginDataType claimedOrigin = target.OriginRegistry;
            claimedOrigin.OriginUri = observed;

            Assert.Multiple(() =>
            {
                Assert.That(target.OriginRegistry.OriginUri, Is.EqualTo(expected));
                Assert.That(() => target.VerifyOrigin(claimedOrigin),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadSecurityChecksFailed));
            });
        }

        [Test]
        public async Task ServerArrayReorderingChangesOnlyThePublishedTransportIndexAsync()
        {
            XRegistryServerOptions options = CreateFederationOptions();
            options.RemoteServerIndex = 99;
            options.RemoteRegistryNamespaceUri = "urn:example:content-lookup-only";
            Mock<IServerInternal> server = XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            server.Object.ServerUris.Append("urn:example:local");
            server.Object.ServerUris.Append("urn:example:other");
            using var nm = new XRegistryFederationNodeManager(server.Object, null!, options);
            await nm.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var proxy = (ResourceState)nm.Find(ProxyNodeId(nm))!;
            ExpandedNodeId original = proxy.ExternalReference!.Value;
            var reordered = new StringTable();
            reordered.Append("urn:example:local");
            reordered.Append(options.FederationTarget!.ServerUri);
            reordered.Append("urn:example:other");
            server.SetupGet(s => s.ServerUris).Returns(reordered);
            Variant value = Variant.Null;
            ServiceResult status = proxy.ExternalReference.OnSimpleReadValue!(
                nm.SystemContext, proxy.ExternalReference, ref value);
            Assert.That(value.TryGetValue(out ExpandedNodeId current), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(ServiceResult.Good));
                Assert.That(original.ServerIndex, Is.EqualTo(2u));
                Assert.That(current.ServerIndex, Is.EqualTo(1u));
                Assert.That(current.WithServerIndex(0), Is.EqualTo(original.WithServerIndex(0)));
                Assert.That(current.NamespaceUri, Is.EqualTo("http://example.org/UA/RemoteRegistry/"));
                Assert.That(proxy.OriginRegistry!.Value, Is.EqualTo(options.FederationTarget.OriginRegistry));
            });
        }

        [TestCase("untrusted")]
        [TestCase("unsupported")]
        [TestCase("missing")]
        [TestCase("wrong-class")]
        public async Task RejectedEndpointUpdateLeavesPublishedBindingUnchangedAsync(string fault)
        {
            StatusCode statusCode = fault switch
            {
                "untrusted" => StatusCodes.BadSecurityChecksFailed,
                "unsupported" => StatusCodes.BadNotSupported,
                "missing" => StatusCodes.BadNodeIdUnknown,
                "wrong-class" => StatusCodes.BadNodeClassInvalid,
                _ => throw new ArgumentException("Unknown provider fault.", nameof(fault))
            };
            XRegistryServerOptions options = CreateFederationOptions();
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            await nm.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var proxy = (ResourceState)nm.Find(ProxyNodeId(nm))!;
            var rejected = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            rejected.Setup(provider => provider.VerifyLogicalResourceAsync(
                    options.FederationTarget!, "opc.tcp://untrusted.example.org:4840", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(statusCode));

            Assert.That(async () => await nm.UpdateEndpointAsync(
                    "opc.tcp://untrusted.example.org:4840", rejected.Object).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(statusCode));
            Assert.Multiple(() =>
            {
                Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo(options.RemoteEndpointUrl));
                Assert.That(proxy.ExternalReference!.Value.WithServerIndex(0),
                    Is.EqualTo(options.FederationTarget!.ResourceNodeId));
                Assert.That(proxy.OriginRegistry!.Value, Is.EqualTo(options.FederationTarget.OriginRegistry));
            });
        }

        [Test]
        public async Task EndpointUpdateCancellationPreservesBindingAndSerializesVerificationAsync()
        {
            XRegistryServerOptions options = CreateFederationOptions();
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            await nm.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var proxy = (ResourceState)nm.Find(ProxyNodeId(nm))!;
            ExpandedNodeId reference = proxy.ExternalReference!.Value;
            RegistryOriginDataType origin = proxy.OriginRegistry!.Value;
            string? xid = proxy.Xid!.Value;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            blocked.Setup(provider => provider.VerifyLogicalResourceAsync(
                    options.FederationTarget!, "opc.tcp://blocked.example.org:4840", It.IsAny<CancellationToken>()))
                .Returns((XRegistryFederationTarget _, string _, CancellationToken ct) =>
                    new ValueTask(BlockAsync(ct)));
            var queued = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            queued.Setup(provider => provider.VerifyLogicalResourceAsync(
                    It.IsAny<XRegistryFederationTarget>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            using var blockedCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var queuedCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            Task blockedUpdate = nm.UpdateEndpointAsync(
                "opc.tcp://blocked.example.org:4840", blocked.Object, blockedCancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Task queuedUpdate = nm.UpdateEndpointAsync(
                "opc.tcp://queued.example.org:4840", queued.Object, queuedCancellation.Token).AsTask();
            Assert.That(queuedUpdate.IsCompleted, Is.False, "Only one provider may verify an update at a time.");
            queuedCancellation.Cancel();
            await Assert.ThatAsync(async () => await queuedUpdate.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            queued.VerifyNoOtherCalls();
            blockedCancellation.Cancel();
            await Assert.ThatAsync(async () => await blockedUpdate.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(ProxyNodeId(nm)), Is.SameAs(proxy));
                Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo(options.RemoteEndpointUrl));
                Assert.That(proxy.ExternalReference.Value, Is.EqualTo(reference));
                Assert.That(proxy.OriginRegistry.Value, Is.EqualTo(origin));
                Assert.That(proxy.Xid.Value, Is.EqualTo(xid));
            });
            await nm.UpdateEndpointAsync(
                "opc.tcp://recovered.example.org:4840", options.FederationProvider!).ConfigureAwait(false);
            Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo("opc.tcp://recovered.example.org:4840"));

            async Task BlockAsync(CancellationToken ct)
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CancelledVerificationPublishesNothingAsync()
        {
            XRegistryServerOptions options = CreateFederationOptions();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            provider.Setup(p => p.VerifyLogicalResourceAsync(
                    It.IsAny<XRegistryFederationTarget>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((XRegistryFederationTarget _, string _, CancellationToken ct) =>
                    new ValueTask(BlockAsync(ct)));
            options.FederationProvider = provider.Object;
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var references = new Dictionary<NodeId, IList<IReference>>();
            Task startup = nm.CreateAddressSpaceAsync(references, cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            cancellation.Cancel();

            Assert.That(async () => await startup.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(ProxyNodeId(nm)), Is.Null);
                Assert.That(references, Is.Empty);
            });

            async Task BlockAsync(CancellationToken ct)
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelledStartupCannotPublishEvenWhenProviderReturnsSuccessfullyAsync(
            bool cancelDuringVerification)
        {
            XRegistryServerOptions options = CreateFederationOptions();
            using var cancellation = new CancellationTokenSource();
            var provider = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            provider.Setup(p => p.VerifyLogicalResourceAsync(
                    options.FederationTarget!, options.RemoteEndpointUrl, cancellation.Token))
                .Callback(cancellation.Cancel)
                .Returns(default(ValueTask));
            options.FederationProvider = provider.Object;
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            var references = new Dictionary<NodeId, IList<IReference>>();
            if (!cancelDuringVerification)
            {
                cancellation.Cancel();
            }

            await Assert.ThatAsync(async () => await nm.CreateAddressSpaceAsync(references, cancellation.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(ProxyNodeId(nm)), Is.Null);
                Assert.That(nm.Find(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.RegistryType, nm.SystemContext.NamespaceUris)), Is.Null);
                Assert.That(references, Is.Empty);
            });
            provider.Verify(p => p.VerifyLogicalResourceAsync(
                    options.FederationTarget!, options.RemoteEndpointUrl, cancellation.Token),
                Times.Exactly(cancelDuringVerification ? 1 : 0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelledEndpointUpdateCannotPublishEvenWhenProviderReturnsSuccessfullyAsync(
            bool cancelDuringVerification)
        {
            XRegistryServerOptions options = CreateFederationOptions();
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            await nm.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var proxy = (ResourceState)nm.Find(ProxyNodeId(nm))!;
            ExpandedNodeId reference = proxy.ExternalReference!.Value;
            RegistryOriginDataType origin = proxy.OriginRegistry!.Value;
            string? xid = proxy.Xid!.Value;
            using var cancellation = new CancellationTokenSource();
            var provider = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            provider.Setup(p => p.VerifyLogicalResourceAsync(
                    options.FederationTarget!, "opc.tcp://cancelled.example.org:4840", cancellation.Token))
                .Callback(cancellation.Cancel)
                .Returns(default(ValueTask));
            if (!cancelDuringVerification)
            {
                cancellation.Cancel();
            }

            await Assert.ThatAsync(async () => await nm.UpdateEndpointAsync(
                    "opc.tcp://cancelled.example.org:4840", provider.Object, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(ProxyNodeId(nm)), Is.SameAs(proxy));
                Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo(options.RemoteEndpointUrl));
                Assert.That(proxy.ExternalReference.Value, Is.EqualTo(reference));
                Assert.That(proxy.OriginRegistry.Value, Is.EqualTo(origin));
                Assert.That(proxy.Xid.Value, Is.EqualTo(xid));
            });
            provider.Verify(p => p.VerifyLogicalResourceAsync(
                    options.FederationTarget!, "opc.tcp://cancelled.example.org:4840", cancellation.Token),
                Times.Exactly(cancelDuringVerification ? 1 : 0));
            await nm.UpdateEndpointAsync(
                "opc.tcp://recovered.example.org:4840", options.FederationProvider!).ConfigureAwait(false);
            Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo("opc.tcp://recovered.example.org:4840"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MutableOptionsCannotReplaceThePublishedOriginOrRemoteEntityAsync(bool mutateBeforePublication)
        {
            XRegistryServerOptions options = CreateFederationOptions();
            XRegistryFederationTarget pinned = options.FederationTarget!;
            IXRegistryFederationProvider provider = options.FederationProvider!;
            string endpointUrl = options.RemoteEndpointUrl;
            using XRegistryFederationNodeManager nm = CreateNodeManager(options);
            if (!mutateBeforePublication)
            {
                await nm.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            }
            options.FederationTarget = new XRegistryFederationTarget(pinned.OriginRegistry,
                pinned.ServerUri, new ExpandedNodeId("other-entity", pinned.ResourceNodeId.NamespaceUri),
                "/groups/remote/resources/other");
            options.FederatedDocument = ByteString.From([1, 2, 3]);
            options.RemoteEndpointUrl = "opc.tcp://untrusted.example.org:4840";
            options.FederationProxyResourceId = "other-local-entity";
            var replacementProvider = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            options.FederationProvider = replacementProvider.Object;
            if (mutateBeforePublication)
            {
                await nm.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            }
            var proxy = (ResourceState)nm.Find(ProxyNodeId(nm))!;
            Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo(endpointUrl));
            await nm.UpdateEndpointAsync("opc.tcp://alternate.example.org:4841", provider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(proxy.ExternalReference!.Value.WithServerIndex(0), Is.EqualTo(pinned.ResourceNodeId));
                Assert.That(proxy.OriginRegistry!.Value, Is.EqualTo(pinned.OriginRegistry));
                Assert.That(proxy.Xid!.Value, Is.EqualTo("/groups/federated/resources/federated-resource/versions/1"));
                Assert.That(proxy.ResourceId!.Value, Is.EqualTo("federated-resource"));
            });
            Mock.Get(provider).Verify(p => p.VerifyLogicalResourceAsync(
                pinned, "opc.tcp://alternate.example.org:4841", It.IsAny<CancellationToken>()), Times.Once);
            replacementProvider.VerifyNoOtherCalls();
        }

        private static ushort RegistryNamespaceIndex(XRegistryFederationNodeManager nm)
        {
            return (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
        }

        private static NodeId ProxyNodeId(XRegistryFederationNodeManager nm)
        {
            return new NodeId(XRegistryWellKnown.FederationProxyObject, RegistryNamespaceIndex(nm));
        }

        private static XRegistryFederationNodeManager CreateNodeManager(XRegistryServerOptions options)
        {
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            server.Object.ServerUris.Append("urn:example:local");
            server.Object.ServerUris.Append("urn:example:unrelated-one");
            server.Object.ServerUris.Append("urn:example:unrelated-two");
            return new XRegistryFederationNodeManager(server.Object, null!, options);
        }

        private static XRegistryServerOptions CreateFederationOptions()
        {
            var provider = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            provider.Setup(p => p.VerifyLogicalResourceAsync(
                It.IsAny<XRegistryFederationTarget>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            return new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                RemoteEndpointUrl = "opc.tcp://remote.example.org:4840",
                FederationTarget = new XRegistryFederationTarget(
                    new RegistryOriginDataType
                    {
                        OriginUri = string.Empty,
                        ServerUri = "urn:example:remote",
                        RegistryNodeId = new ExpandedNodeId("remote-registry", "http://example.org/UA/RemoteRegistry/")
                    },
                    "urn:example:remote",
                    new ExpandedNodeId("remote-logical-resource", "http://example.org/UA/RemoteRegistry/"),
                    "/groups/remote/resources/original"),
                FederationProvider = provider.Object
            };
        }

        private static readonly byte[] s_federatedDocument = [0xDE, 0xAD, 0xBE, 0xEF];
    }
}
