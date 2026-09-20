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
using System.Linq;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerRoutingTableTests
    {
        [TestCase("AddInitial", false)]
        [TestCase("AddInitial", true)]
        [TestCase("Initialize", false)]
        [TestCase("Initialize", true)]
        [TestCase("Add", false)]
        [TestCase("Add", true)]
        [TestCase("AddHidden", false)]
        [TestCase("AddHidden", true)]
        [TestCase("Replace", false)]
        [TestCase("Replace", true)]
        [TestCase("ReplaceHidden", false)]
        [TestCase("ReplaceHidden", true)]
        [TestCase("Remove", false)]
        [TestCase("Remove", true)]
        [TestCase("RegisterNamespace", false)]
        [TestCase("RegisterNamespace", true)]
        [TestCase("RegisterHiddenNamespace", false)]
        [TestCase("RegisterHiddenNamespace", true)]
        [TestCase("UnregisterNamespace", false)]
        [TestCase("UnregisterNamespace", true)]
        [TestCase("UnregisterSyncNamespace", false)]
        [TestCase("UnregisterSyncNamespace", true)]
        [TestCase("RemoveNamespaceManager", false)]
        [TestCase("RemoveNamespaceManager", true)]
        [TestCase("SetVisible", false)]
        [TestCase("SetVisible", true)]
        [TestCase("Clear", false)]
        [TestCase("Clear", true)]
        public void PreparedReservationRejectsRoutingMutationsBeforeEffects(string mutation, bool publish)
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent, out IAsyncNodeManager secondPermanent);
            INodeManager synchronous = new Mock<INodeManager>().Object;
            IAsyncNodeManager original = CreateManagerWithSync(synchronous);
            IAsyncNodeManager writer = CreateManager();
            IAsyncNodeManager candidate = CreateManager();
            IAsyncNodeManager unrelatedHidden = CreateManager();
            table.Add(original, InitialNamespaceIndexes);
            table.RegisterNamespace(8, unrelatedHidden, visible: false);
            NodeManagerRoutingTable.RoutingSnapshot previous = table.Revision;
            var initializedTypes = new TypeTable(new NamespaceTable());
            var initializedFactory = (EncodeableFactory)EncodeableFactory.Create();
            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareRoutingCandidate(table, candidate);

            routes.Reserve();
            Assert.Throws<InvalidOperationException>(Mutate);
            Assert.That(table.Revision, Is.SameAs(previous));
            AssertSingleManagerRoute(table.NamespaceManagers, 2, original);
            AssertSingleManagerRoute(table.NamespaceManagers, 3, original);
            Assert.That(table.Revision.HiddenNodeManagers, Is.EqualTo(new[] { unrelatedHidden }));
            if (publish)
            {
                routes.Publish();
                NodeManagerRoutingTable.RoutingSnapshot published = table.Revision;
                Assert.Throws<InvalidOperationException>(Mutate,
                    "Publication alone does not release the reservation before host bookkeeping completes.");
                Assert.That(table.Revision, Is.SameAs(published));
                Assert.That(table.NamespaceManagers[2], Is.EqualTo(new[] { original, candidate }));
            }
            routes.Dispose();
            routes.Dispose();
            Mutate();

            switch (mutation)
            {
                case "AddInitial":
                    Assert.That(table[table.Count - 1], Is.SameAs(writer));
                    Assert.That(table.NamespaceManagers.ContainsKey(9), Is.False);
                    break;
                case "Initialize":
                    Assert.That(table.NamespaceManagers.Keys, Is.EqualTo(ConcurrentNamespaceIndexes));
                    AssertSingleManagerRoute(table.NamespaceManagers, 9, writer);
                    Assert.That(table.TypeTree, Is.SameAs(initializedTypes));
                    Assert.That(table.Factory, Is.SameAs(initializedFactory));
                    break;
                case "Add":
                case "AddHidden":
                case "Replace":
                case "ReplaceHidden":
                    Assert.That(table.Contains(writer), Is.True);
                    AssertSingleManagerRoute(table.Revision.NamespaceManagers, 9, writer);
                    bool visible = mutation is "Add" or "Replace";
                    Assert.That(table.IsVisible(writer), Is.EqualTo(visible));
                    Assert.That(table.NamespaceManagers.ContainsKey(9), Is.EqualTo(visible));
                    Assert.That(table.Contains(original), Is.EqualTo(mutation is "Add" or "AddHidden"));
                    break;
                case "Remove":
                case "RemoveNamespaceManager":
                    Assert.That(table.Contains(original), Is.EqualTo(mutation == "RemoveNamespaceManager"));
                    Assert.That(table.Revision.NamespaceManagers.Values.SelectMany(managers => managers),
                        Does.Not.Contain(original));
                    break;
                case "RegisterNamespace":
                case "RegisterHiddenNamespace":
                    AssertSingleManagerRoute(table.Revision.NamespaceManagers, 9, writer);
                    Assert.That(table.NamespaceManagers.ContainsKey(9), Is.EqualTo(mutation == "RegisterNamespace"));
                    Assert.That(table.Contains(writer), Is.False);
                    break;
                case "UnregisterNamespace":
                case "UnregisterSyncNamespace":
                    Assert.That(table.NamespaceManagers.ContainsKey(3), Is.False);
                    Assert.That(table.Revision.NamespaceManagers[2], Does.Contain(original));
                    break;
                case "SetVisible":
                    Assert.That(table.Contains(original), Is.True);
                    Assert.That(table.IsVisible(original), Is.False);
                    Assert.That(table.NamespaceManagers.ContainsKey(3), Is.False);
                    break;
                case "Clear":
                    Assert.That(table, Is.Empty);
                    Assert.That(table.NamespaceManagers, Is.Empty);
                    Assert.That(table.Revision.HiddenNodeManagers, Is.Empty);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            Assert.That(table.NamespaceManagers.ContainsKey(8), Is.False,
                "Neither publication nor a later write may reveal an unrelated hidden owner.");
            using (table.Capture(previous))
            {
                Assert.That(table.ToArray(), Is.EqualTo(new[] { firstPermanent, secondPermanent, original }));
                AssertSingleManagerRoute(table.NamespaceManagers, 2, original);
                AssertSingleManagerRoute(table.NamespaceManagers, 3, original);
                Assert.That(table.NamespaceManagers.ContainsKey(9), Is.False);
            }

            void Mutate()
            {
                switch (mutation)
                {
                    case "AddInitial":
                        table.AddInitial(writer);
                        break;
                    case "Initialize":
                        table.Initialize(new Dictionary<int, List<IAsyncNodeManager>> { [9] = [writer] },
                            initializedTypes, initializedFactory);
                        break;
                    case "Add":
                    case "AddHidden":
                        table.Add(writer, ConcurrentNamespaceIndexes, visible: mutation == "Add");
                        break;
                    case "Replace":
                    case "ReplaceHidden":
                        table.Replace(original, writer, ConcurrentNamespaceIndexes,
                            replacementVisible: mutation == "Replace");
                        break;
                    case "Remove":
                        table.Remove(original);
                        break;
                    case "RegisterNamespace":
                    case "RegisterHiddenNamespace":
                        table.RegisterNamespace(9, writer, visible: mutation == "RegisterNamespace");
                        break;
                    case "UnregisterNamespace":
                        Assert.That(table.UnregisterNamespace(3, original, null), Is.True);
                        break;
                    case "UnregisterSyncNamespace":
                        Assert.That(table.UnregisterNamespace(3, null, synchronous), Is.True);
                        break;
                    case "RemoveNamespaceManager":
                        table.RemoveNamespaceManager(original);
                        break;
                    case "SetVisible":
                        table.SetVisible(original, visible: false);
                        break;
                    case "Clear":
                        table.Clear();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreparedReservationRejectsReferencePreparationBeforeOwnershipEffects(bool publish)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            NodeManagerRoutingTable other = CreateTable(out _, out _);
            IAsyncNodeManager owner = CreateManager();
            IAsyncNodeManager otherOwner = CreateManager();
            var node = new BaseObjectState(null);
            ExpandedNodeId original = new(9101, 2);
            ExpandedNodeId addition = new(9102, 2);
            node.AddReference(ReferenceTypeIds.Organizes, false, original);
            ArrayOf<IReference> additions = [new NodeStateReference(ReferenceTypeIds.Organizes, false, addition)];
            var next = new Dictionary<NodeState, NodeState.ReferenceSnapshot>();
            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareRoutingCandidate(table, CreateManager());
            routes.Reserve();
            Assert.Throws<InvalidOperationException>(() => table.PrepareReferences(owner, node, additions, [], next));
            Assert.That(next, Is.Empty);
            using (other.PrepareReferences(otherOwner, node, [], [], []))
            {
                other.ReleaseReferences(otherOwner);
            }
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, original), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, addition), Is.False);
            if (publish)
            {
                routes.Publish();
            }
            routes.Dispose();

            using NodeState.ReferenceUpdate accepted = table.PrepareReferences(owner, node, additions, [], next);
            Assert.That(next[node], Is.SameAs(accepted.Next));
            Assert.That(accepted.Next.References.Keys.Select(reference => reference.TargetId),
                Is.EquivalentTo(new[] { original, addition }));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, addition), Is.False,
                "Preparing the private image must not install its references.");
            Assert.Throws<InvalidOperationException>(() => other.PrepareReferences(otherOwner, node, [], [], []));
            table.ReleaseReferences(owner);
            using (other.PrepareReferences(otherOwner, node, [], [], []))
            {
                other.ReleaseReferences(otherOwner);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void PreparedReservationRejectsReferenceReleaseBeforeEffects(bool clear, bool publish)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            NodeManagerRoutingTable other = CreateTable(out _, out _);
            IAsyncNodeManager owner = CreateManager();
            var node = new BaseObjectState(null);
            ExpandedNodeId target = new(9201, 2);
            var images = new Dictionary<NodeState, NodeState.ReferenceSnapshot>();
            using (NodeState.ReferenceUpdate update = table.PrepareReferences(owner, node,
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, target)], [], images))
            {
                using NodeManagerRoutingTable.PreparedRoutes initial = table.PrepareBatch(
                    [], [], table.Revision, _ => BatchNamespaceIndexes,
                    new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create(), images);
                update.Reserve();
                initial.Reserve();
                initial.Publish();
                update.Complete();
            }
            NodeManagerRoutingTable.RoutingSnapshot previous = table.Revision;
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, target), Is.True);
            using NodeManagerRoutingTable.PreparedRoutes routes = PrepareRoutingCandidate(table, CreateManager());
            routes.Reserve();
            Assert.Throws<InvalidOperationException>(Release);
            Assert.That(table.Revision, Is.SameAs(previous));
            Assert.That(table.Revision.References[node], Is.SameAs(images[node]));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, target), Is.True);
            Assert.Throws<InvalidOperationException>(() => other.PrepareReferences(owner, node, [], [], []),
                "A rejected release or clear must not detach the node's existing reference-view owner.");
            if (publish)
            {
                routes.Publish();
            }
            routes.Dispose();
            Release();
            Assert.That(table.Revision.References.ContainsKey(node), Is.False);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, target), Is.True);
            using (other.PrepareReferences(owner, node, [], [], []))
            {
                table.ReleaseReferences(owner);
                Assert.Throws<InvalidOperationException>(() => table.PrepareReferences(owner, node, [], [], []),
                    "Repeated old-owner cleanup must not detach a later reference-view owner.");
                other.ReleaseReferences(owner);
            }

            void Release()
            {
                if (clear)
                {
                    table.Clear();
                }
                else
                {
                    table.ReleaseReferences(owner);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreparedRoutingReservationHasExclusiveDisposableOwnership(bool publish)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager firstCandidate = CreateManager();
            IAsyncNodeManager rejectedCandidate = CreateManager();
            IAsyncNodeManager lastCandidate = CreateManager();
            IAsyncNodeManager writer = CreateManager();
            using NodeManagerRoutingTable.PreparedRoutes first = PrepareRoutingCandidate(table, firstCandidate);
            using NodeManagerRoutingTable.PreparedRoutes rejected = PrepareRoutingCandidate(table, rejectedCandidate);
            NodeManagerRoutingTable.RoutingSnapshot previous = table.Revision;
            Assert.Throws<InvalidOperationException>(first.Publish);
            first.Reserve();
            Assert.Throws<InvalidOperationException>(first.Reserve);
            Assert.Throws<InvalidOperationException>(rejected.Reserve);
            Assert.Throws<InvalidOperationException>(rejected.Publish);
            rejected.Dispose();
            rejected.Dispose();
            Assert.Throws<InvalidOperationException>(() => table.RegisterNamespace(9, writer));
            Assert.That(table.Revision, Is.SameAs(previous));
            if (publish)
            {
                first.Publish();
            }
            first.Dispose();
            first.Dispose();
            Assert.Throws<InvalidOperationException>(first.Reserve);
            Assert.Throws<InvalidOperationException>(rejected.Reserve);

            using NodeManagerRoutingTable.PreparedRoutes last = PrepareRoutingCandidate(table, lastCandidate);
            last.Reserve();
            first.Dispose();
            rejected.Dispose();
            Assert.Throws<InvalidOperationException>(() => table.RegisterNamespace(9, writer));
            last.Publish();
            last.Dispose();
            table.RegisterNamespace(9, writer);
            NodeManagerRoutingTable.RoutingSnapshot afterWrite = table.Revision;
            last.Publish();
            if (publish)
            {
                first.Publish();
            }
            else
            {
                Assert.Throws<InvalidOperationException>(first.Publish);
            }
            Assert.That(table.Revision, Is.SameAs(afterWrite),
                "Repeated publication or disposal must not replay a stale image over accepted later writes.");
            AssertSingleManagerRoute(table.NamespaceManagers, 9, writer);
            Assert.That(table.Contains(firstCandidate), Is.EqualTo(publish));
            Assert.That(table.Contains(rejectedCandidate), Is.False);
            Assert.That(table.Contains(lastCandidate), Is.True);
            Assert.That(table.NamespaceManagers[2],
                Is.EqualTo(publish ? new[] { firstCandidate, lastCandidate } : new[] { lastCandidate }));
        }

        [Test]
        public void PreparedRoutingReservationRejectsChangedRevisionBeforeOwnership()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager candidate = CreateManager();
            IAsyncNodeManager writer = CreateManager();
            NodeManagerRoutingTable.RoutingSnapshot previous = table.Revision;
            using NodeManagerRoutingTable.PreparedRoutes stale = PrepareRoutingCandidate(table, candidate);
            table.RegisterNamespace(9, writer);
            Assert.Throws<InvalidOperationException>(() => table.PrepareBatch(
                [], [], previous, _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create()));
            Assert.Throws<InvalidOperationException>(stale.Reserve);
            using NodeManagerRoutingTable.PreparedRoutes current = PrepareRoutingCandidate(table, candidate);
            current.Reserve();
            stale.Dispose();
            stale.Dispose();
            Assert.Throws<InvalidOperationException>(() => table.UnregisterNamespace(9, writer, null));
            current.Publish();
            current.Dispose();
            AssertSingleManagerRoute(table.NamespaceManagers, 9, writer);
            Assert.That(table.UnregisterNamespace(9, writer, null), Is.True);
            Assert.That(table.NamespaceManagers.ContainsKey(9), Is.False);
            Assert.That(previous.NamespaceManagers.ContainsKey(9), Is.False);
        }

        private static NodeManagerRoutingTable.PreparedRoutes PrepareRoutingCandidate(
            NodeManagerRoutingTable table,
            IAsyncNodeManager candidate)
        {
            var prepared = new PreparedNodeManager(candidate, []) { Staged = true };
            return table.PrepareBatch([prepared], [], table.Revision, _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());
        }
    }
}
