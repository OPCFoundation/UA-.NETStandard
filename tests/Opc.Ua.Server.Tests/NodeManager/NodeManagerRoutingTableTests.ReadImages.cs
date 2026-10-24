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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerRoutingTableTests
    {
        /// <summary>
        /// Contextless application-state reads follow publication, not inherited execution-context metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ContextlessReadImagesIgnoreMetadataScopes(bool retire)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager owner = CreateManager();
            table.Add(owner, InitialNamespaceIndexes);
            PublishReadImage(table, new TestReadImage(owner, 1));
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None))
            {
                context.PinRouting(table);
                var next = new TestReadImage(owner, 2);
                using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                    [], retire ? [owner] : [], table.Revision, _ => InitialNamespaceIndexes,
                    new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create(),
                    readImages: retire ? [] : [next]);
                routes.Reserve();
                routes.Publish();
                Assert.That(table.GetReadImage(owner), retire ? Is.Null : Is.SameAs(next));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CapturedReadImagesSurviveReplacementAndOwnerRetirement(bool retire)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager owner = CreateManager();
            table.Add(owner, InitialNamespaceIndexes);
            var previous = new TestReadImage(owner, 1);
            var next = new TestReadImage(owner, 2);
            PublishReadImage(table, previous);
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            NodeManagerRoutingTable.RoutingSnapshot captured = context.RoutingSnapshot!;
            Mock<INodeManagerReadImageSource> source = CreateReadImageSource(table, owner);

            using (NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [], retire ? [owner] : [], table.Revision, _ => InitialNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create(),
                readImages: retire ? [] : [next]))
            {
                routes.Reserve();
                Assert.That(table.GetReadImage(owner), Is.SameAs(previous));
                routes.Publish();
            }

            Assert.That(table.GetReadImage(owner), retire ? Is.Null : Is.SameAs(next));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(source.Object.GetReadImage(context, owner), Is.SameAs(previous));
                Assert.That(table.GetReadImage(owner), retire ? Is.Null : Is.SameAs(next));
                Assert.That(table.GetDispatchView(captured).NodeManagers, Does.Contain(owner));
                Assert.That(table.Contains(owner), Is.EqualTo(!retire),
                    "Metadata images must not change the live owner list.");
                Assert.That(source.Object.GetReadImage(context, owner), Is.SameAs(previous));
            }
        }

        /// <summary>
        /// A pinned view without application state must not fall through to a later live publication.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitReadImagesDistinguishFirstPublicationFromUnpinnedWork(bool firstPublication)
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager owner, out _);
            var previous = new TestReadImage(owner, 1);
            if (!firstPublication)
            {
                PublishReadImage(table, previous);
            }
            using var pinned = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            pinned.PinRouting(table);
            var next = new TestReadImage(owner, 2);
            PublishReadImage(table, next);
            Mock<INodeManagerReadImageSource> source = CreateReadImageSource(table, owner);
            using var unpinned = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(source.Object.GetReadImage(pinned, owner),
                    firstPublication ? Is.Null : Is.SameAs(previous));
                Assert.That(source.Object.GetReadImage(unpinned, owner), Is.SameAs(next));
                Assert.That(source.Object.GetReadImage(null, owner), Is.SameAs(next));
            }
            source.Verify(value => value.GetReadImage(owner), Times.Exactly(2));
        }

        /// <summary>
        /// Invalid provider and owner arguments fail explicitly before attempting a lookup.
        /// </summary>
        [Test]
        public void ExplicitReadImagesRejectNullArguments()
        {
            IAsyncNodeManager owner = CreateManager();
            var source = new Mock<INodeManagerReadImageSource>(MockBehavior.Strict);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => NodeManagerReadImageExtensions.GetReadImage(null!, null, owner),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("source"));
                Assert.That(() => source.Object.GetReadImage(null, null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("owner"));
            }
            source.VerifyNoOtherCalls();
        }

        [TestCase("ForeignOwner")]
        [TestCase("DuplicateOwner")]
        [TestCase("NullImage")]
        public void InvalidReadImagesAreRejectedBeforePublication(string invalid)
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager owner, out _);
            var previous = new TestReadImage(owner, 1);
            PublishReadImage(table, previous);
            NodeManagerRoutingTable.RoutingSnapshot captured = table.Revision;
            ArrayOf<INodeManagerReadImage> images = invalid switch
            {
                "ForeignOwner" => [new TestReadImage(CreateManager(), 2)],
                "DuplicateOwner" => [new TestReadImage(owner, 2), new TestReadImage(owner, 3)],
                "NullImage" => [null!],
                _ => throw new ArgumentOutOfRangeException(nameof(invalid))
            };

            Assert.That(() => table.PrepareBatch(
                [], [], table.Revision, _ => InitialNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create(),
                readImages: images), Throws.InvalidOperationException);

            Assert.That(table.Revision, Is.SameAs(captured));
            Assert.That(table.GetReadImage(owner), Is.SameAs(previous));
        }

        [Test]
        public void DiscardedReadImageIsNeverVisibleToCurrentOrCapturedReaders()
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager owner, out _);
            var previous = new TestReadImage(owner, 1);
            PublishReadImage(table, previous);
            NodeManagerRoutingTable.RoutingSnapshot captured = table.Revision;
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None))
            {
                context.PinRouting(table);
                using (NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                    [], [], table.Revision, _ => InitialNamespaceIndexes,
                    new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create(),
                    readImages: [new TestReadImage(owner, 2)]))
                {
                    routes.Reserve();
                    Assert.That(table.GetReadImage(owner), Is.SameAs(previous));
                }
                Assert.That(table.GetReadImage(owner), Is.SameAs(previous));
            }
            Assert.That(table.Revision, Is.SameAs(captured));
            Assert.That(table.GetReadImage(owner), Is.SameAs(previous));
        }

        /// <summary>
        /// Creates a provider whose live reads follow the routing table.
        /// </summary>
        private static Mock<INodeManagerReadImageSource> CreateReadImageSource(
            NodeManagerRoutingTable table,
            IAsyncNodeManager owner)
        {
            var source = new Mock<INodeManagerReadImageSource>(MockBehavior.Strict);
            source.Setup(value => value.GetReadImage(owner)).Returns(() => table.GetReadImage(owner));
            return source;
        }

        private static void PublishReadImage(NodeManagerRoutingTable table, TestReadImage image)
        {
            using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [], [], table.Revision, _ => InitialNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create(),
                readImages: [image]);
            routes.Reserve();
            routes.Publish();
        }

        private sealed record TestReadImage(IAsyncNodeManager Owner, int Generation) : INodeManagerReadImage;
    }
}
