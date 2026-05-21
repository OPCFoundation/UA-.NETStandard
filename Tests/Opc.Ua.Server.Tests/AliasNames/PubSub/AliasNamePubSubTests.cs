/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.AliasNames.PubSub;

namespace Opc.Ua.Server.Tests.AliasNames.PubSub
{
    /// <summary>
    /// Coverage tests for the Part 17 Annex D PubSub server-side
    /// surface — DataSet metadata shape, PortableNodeId encoding, and
    /// publisher event production.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNamePubSubTests
    {
        [Test]
        public void DataSetFactoryProducesPart17AnnexDSchema()
        {
            var classId = Guid.NewGuid();
            DataSetMetaDataType metadata = AliasUpdateDataSetFactory.Create(classId);

            Assert.That(metadata.Name, Is.EqualTo("AliasUpdate"));
            Assert.That(metadata.DataSetClassId, Is.EqualTo(new Uuid(classId)));
            Assert.That(metadata.Fields.Count, Is.EqualTo(2));
            Assert.That(metadata.Fields[0].Name, Is.EqualTo("ApplicationUri"));
            Assert.That(metadata.Fields[1].Name, Is.EqualTo("Categories"));
            Assert.That(metadata.Fields[1].DataType,
                Is.EqualTo(DataTypeIds.AliasCategoryUpdateDataType));
            Assert.That(metadata.Fields[1].ValueRank,
                Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void PortableResolverRoundTripsAcrossNamespaces()
        {
            var ns = new NamespaceTable();
            ns.Append("http://example.org/MyServer/");
            var servers = new StringTable();
            servers.Append("urn:example:server");

            var serverMock = new Moq.Mock<IServerInternal>();
            serverMock.SetupGet(s => s.NamespaceUris).Returns(ns);
            serverMock.SetupGet(s => s.ServerUris).Returns(servers);

            var resolver = new ServerPortableNodeIdResolver(serverMock.Object);
            PortableNodeId portable = resolver.ToPortable(new NodeId("MyCat", 1));

            Assert.That(portable, Is.Not.Null);
            Assert.That(portable.NamespaceUri,
                Is.EqualTo("http://example.org/MyServer/"));
            Assert.That(portable.Identifier.NamespaceIndex, Is.EqualTo((ushort)0));
        }

        [Test]
        public void PortableResolverPreservesNumericIdentifier()
        {
            ServerPortableNodeIdResolver resolver = NewResolver();
            PortableNodeId portable = resolver.ToPortable(new NodeId(42u, 1));

            Assert.That(portable, Is.Not.Null);
            Assert.That(portable.NamespaceUri,
                Is.EqualTo("http://example.org/MyServer/"));
            Assert.That(portable.Identifier.NamespaceIndex, Is.EqualTo((ushort)0));
            Assert.That(portable.Identifier.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(portable.Identifier.TryGetValue(out uint numeric), Is.True);
            Assert.That(numeric, Is.EqualTo(42u));
        }

        [Test]
        public void PortableResolverPreservesGuidIdentifier()
        {
            ServerPortableNodeIdResolver resolver = NewResolver();
            var guid = Guid.NewGuid();
            PortableNodeId portable = resolver.ToPortable(new NodeId(guid, 1));

            Assert.That(portable, Is.Not.Null);
            Assert.That(portable.Identifier.NamespaceIndex, Is.EqualTo((ushort)0));
            Assert.That(portable.Identifier.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(portable.Identifier.TryGetValue(out Guid g), Is.True);
            Assert.That(g, Is.EqualTo(guid));
        }

        [Test]
        public void PortableResolverPreservesOpaqueIdentifier()
        {
            ServerPortableNodeIdResolver resolver = NewResolver();
            var bytes = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
            PortableNodeId portable = resolver.ToPortable(new NodeId((ByteString)bytes, 1));

            Assert.That(portable, Is.Not.Null);
            Assert.That(portable.Identifier.NamespaceIndex, Is.EqualTo((ushort)0));
            Assert.That(portable.Identifier.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(portable.Identifier.TryGetValue(out ByteString opaque), Is.True);
            Assert.That(opaque.Span.ToArray(), Is.EqualTo(bytes));
        }

        [Test]
        public void PortableResolverReturnsNullForNullNodeId()
        {
            ServerPortableNodeIdResolver resolver = NewResolver();
            PortableNodeId portable = resolver.ToPortable(NodeId.Null);
            Assert.That(portable, Is.Null,
                "Null NodeId must short-circuit to null without throwing.");
        }

        [Test]
        public void PortableResolverReturnsNullForUnknownNamespaceIndex()
        {
            ServerPortableNodeIdResolver resolver = NewResolver();
            // Namespace index 99 is not registered — the resolver must
            // gracefully return null rather than emit a PortableNodeId
            // with a null/empty namespace URI.
            PortableNodeId portable = resolver.ToPortable(new NodeId("X", 99));
            Assert.That(portable, Is.Null);
        }

        private static ServerPortableNodeIdResolver NewResolver()
        {
            var ns = new NamespaceTable();
            ns.Append("http://example.org/MyServer/");
            var servers = new StringTable();
            servers.Append("urn:example:server");
            var serverMock = new Moq.Mock<IServerInternal>();
            serverMock.SetupGet(s => s.NamespaceUris).Returns(ns);
            serverMock.SetupGet(s => s.ServerUris).Returns(servers);
            return new ServerPortableNodeIdResolver(serverMock.Object);
        }

        [Test]
        public void PublisherProducesAliasUpdateOnRegistryChange()
        {
            using var registry = new AliasNameStoreRegistry();
            using var store = new InMemoryAliasNameStore(
                [new AliasNameCategoryDescriptor(
                    new NodeId("Cat", 1),
                    new QualifiedName("Cat", 1),
                    AliasNameCapabilities.AddAliasesToCategory |
                    AliasNameCapabilities.LastChange)]);
            registry.Register(store);

            var ns = new NamespaceTable();
            ns.Append("http://example.org/MyServer/");
            var servers = new StringTable();
            servers.Append("urn:example:server");
            var serverMock = new Moq.Mock<IServerInternal>();
            serverMock.SetupGet(s => s.NamespaceUris).Returns(ns);
            serverMock.SetupGet(s => s.ServerUris).Returns(servers);

            var resolver = new ServerPortableNodeIdResolver(serverMock.Object);
            using var publisher = new AliasNamePublisher(
                registry,
                resolver,
                applicationUri: "urn:example:publisher");

            AliasUpdateDataType captured = null;
            publisher.AliasUpdateProduced += (_, e) => captured = e.Update;

            store.AddAliasesAsync(
                new NodeId("Cat", 1),
                [new AliasAddRequest("X", new ExpandedNodeId("T", 1), null,
                    ReferenceTypeIds.AliasFor)],
                System.Threading.CancellationToken.None).AsTask().GetAwaiter().GetResult();

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.ApplicationUri, Is.EqualTo("urn:example:publisher"));
            Assert.That(captured.Categories.Count, Is.EqualTo(1));
            Assert.That(captured.Categories[0].LastChange, Is.EqualTo((uint)1));
            Assert.That(captured.Categories[0].Category.NamespaceUri,
                Is.EqualTo("http://example.org/MyServer/"));
        }
    }
}
