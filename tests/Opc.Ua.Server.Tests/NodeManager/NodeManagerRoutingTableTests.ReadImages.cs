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
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerRoutingTableTests
    {
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
            NodeManagerRoutingTable.RoutingSnapshot captured = table.Revision;

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
            using (table.Capture(captured))
            {
                Assert.That(table.GetReadImage(owner), Is.SameAs(previous));
                Assert.That(table.ToArray(), Does.Contain(owner));
                using (table.UseLiveRouting())
                {
                    Assert.That(table.GetReadImage(owner), retire ? Is.Null : Is.SameAs(next));
                }
                Assert.That(table.GetReadImage(owner), Is.SameAs(previous));
            }
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
            using (table.Capture())
            {
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
