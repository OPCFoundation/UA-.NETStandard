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
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the Opaque content-id fast path: a seeded resource is addressable by an Opaque
    /// NodeId whose Identifier is the raw content-id bytes, so a decoder that received the id on
    /// the wire reaches the document in a single Read.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryFastPathNodeManagerTests
    {
        [Test]
        public void SeedDisabledPublishesNoResource()
        {
            using XRegistryFastPathNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            Assert.That(nm.Find(FastPathNodeId(nm, s_seedDocument)), Is.Null);
        }

        [Test]
        public void SeedWithoutDocumentPublishesNoResource()
        {
            using XRegistryFastPathNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                PublishSeedResource = true,
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            Assert.That(nm.Find(FastPathNodeId(nm, s_seedDocument)), Is.Null);
        }

        [Test]
        public void SeedWithoutContentIdProviderThrowsInvalidOperationException()
        {
            using XRegistryFastPathNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                PublishSeedResource = true,
                SeedDocument = ByteString.From(s_seedDocument)
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>()));
            Assert.That(ex.Message, Does.Contain("ContentIdProvider"));
        }

        [Test]
        public void SeedIsPublishedUnderItsOpaqueContentIdNodeId()
        {
            using XRegistryFastPathNodeManager nm = CreateNodeManager(new XRegistryServerOptions
            {
                PublishSeedResource = true,
                SeedDocument = ByteString.From(s_seedDocument),
                SeedBrowseName = "SeededResource",
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            NodeState? node = nm.Find(FastPathNodeId(nm, s_seedDocument));
            Assert.That(node, Is.Not.Null);
            var resource = (BaseDataVariableState)node!;
            Assert.Multiple(() =>
            {
                Assert.That(resource.BrowseName.Name, Is.EqualTo("SeededResource"));
                Assert.That(resource.WrappedValue.TryGetValue(out ByteString document), Is.True);
                Assert.That(
                    resource.WrappedValue.TryGetValue(out ByteString published) && !published.IsNull
                        ? published.Span.ToArray()
                        : [],
                    Is.EqualTo(s_seedDocument));
                Assert.That(resource.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                Assert.That(document.IsNull, Is.False);
            });
        }

        [Test]
        public void CreateAddressSpaceMaterializesTheGeneratedCompanionModel()
        {
            using XRegistryFastPathNodeManager nm = CreateNodeManager(new XRegistryServerOptions());

            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            Assert.That(
                nm.Find(ExpandedNodeId.ToNodeId(ObjectTypeIds.ResourceType, nm.SystemContext.NamespaceUris)),
                Is.Not.Null);
        }

        private static NodeId FastPathNodeId(XRegistryFastPathNodeManager nm, byte[] document)
        {
            ushort ns = (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
            return new NodeId(ByteString.From(document), ns);
        }

        private static XRegistryFastPathNodeManager CreateNodeManager(XRegistryServerOptions options)
        {
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            return new XRegistryFastPathNodeManager(server.Object, null!, options);
        }

        private static readonly byte[] s_seedDocument = [0x11, 0x22, 0x33];
    }
}
