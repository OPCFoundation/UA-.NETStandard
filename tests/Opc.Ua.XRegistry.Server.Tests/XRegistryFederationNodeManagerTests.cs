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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server.Tests
{
    /// <summary>
    /// Verifies the federation proxy: a resource hosted by another registry is represented locally
    /// by a proxy carrying an <c>ExternalReference</c> (an ExpandedNodeId naming the remote server
    /// through the ServerArray) and a <c>ResourceUrl</c>, alongside the content-derived id that
    /// makes the same resource de-duplicable across registries.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryFederationNodeManagerTests
    {
        [Test]
        public void ProxyDisabledPublishesNothing()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                FederatedDocument = s_federatedDocument,
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            Assert.That(nm.Find(ProxyNodeId(nm)), Is.Null);
        }

        [Test]
        public void ProxyWithoutDocumentPublishesNothing()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            Assert.That(nm.Find(ProxyNodeId(nm)), Is.Null);
        }

        [Test]
        public void ProxyWithoutContentIdProviderThrowsInvalidOperationException()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                FederatedDocument = s_federatedDocument
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>()));
            Assert.That(ex.Message, Does.Contain("ContentIdProvider"));
        }

        [Test]
        public void ProxyIsAResourceTypeInstanceCarryingTheFederationLink()
        {
            const string remoteEndpoint = "opc.tcp://remote.example.org:4840";
            const string remoteNamespace = "http://example.org/UA/RemoteRegistry/";
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                FederatedDocument = s_federatedDocument,
                RemoteEndpointUrl = remoteEndpoint,
                RemoteRegistryNamespaceUri = remoteNamespace,
                RemoteServerIndex = 3,
                FederationProxyBrowseName = "RemoteResource",
                FederatedFormat = "application/json",
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

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

                Assert.That(proxy.ResourceUrl!.Value, Is.EqualTo(remoteEndpoint));
                Assert.That(proxy.Format!.Value, Is.EqualTo("application/json"));
                Assert.That(proxy.Xid!.Value, Is.EqualTo(proxy.ResourceId!.Value),
                    "The identity of a federated resource is its content id.");
                Assert.That(proxy.Epoch!.Value, Is.EqualTo(1u));
            });
        }

        [Test]
        public void ProxyIdentityIsTheContentIdOfTheFederatedDocument()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                FederatedDocument = s_federatedDocument,
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            var proxy = (ResourceState?)nm.Find(ProxyNodeId(nm));
            Assert.That(
                proxy!.Xid!.Value,
                Is.EqualTo(ByteString.From(s_federatedDocument).ToHexString()),
                "The content id is derived from the federated document itself.");
        }

        [Test]
        public void CreateAddressSpaceMaterializesTheGeneratedCompanionModel()
        {
            using XRegistryFederationNodeManager nm = CreateNodeManager(new XRegistryServerOptions());

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            Assert.That(
                nm.Find(ExpandedNodeId.ToNodeId(ObjectTypeIds.GroupType, nm.SystemContext.NamespaceUris)),
                Is.Not.Null);
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
            return new XRegistryFederationNodeManager(server.Object, null!, options);
        }

        private static readonly byte[] s_federatedDocument = [0xDE, 0xAD, 0xBE, 0xEF];
    }
}
