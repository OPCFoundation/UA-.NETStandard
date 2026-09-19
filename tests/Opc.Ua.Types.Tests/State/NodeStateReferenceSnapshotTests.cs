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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Parallelizable]
    public sealed class NodeStateReferenceSnapshotTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PreparedReferencesKeepEveryReaderOnTheSelectedImage(bool publish)
        {
            var node = CreateNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));
            node.AddReference(ReferenceTypeIds.HasComponent, true, new NodeId(300));
            NodeState.ReferenceSnapshot selected = null;
            NodeState.ReferenceSnapshot Select(NodeState _) => selected;
            using NodeState.ReferenceUpdate baseline = node.PrepareReferences(Select, [], []);
            baseline.Reserve();
            selected = baseline.Next;
            baseline.Complete();
            NodeState.ReferenceSnapshot old = selected;
            var added = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(200));
            using NodeState.ReferenceUpdate update = node.PrepareReferences(
                Select, [added, added], [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(100))]);
            int callbacks = 0;
            node.OnReferenceAdded = (_, _, _, _) => callbacks++;
            node.OnReferenceRemoved = (_, _, _, _) => callbacks++;
            AssertReaders(node, 100);
            Assert.That(callbacks, Is.Zero);
            update.Reserve();
            AssertReaders(node, 100);
            if (publish)
            {
                selected = update.Next;
                update.Complete();
                update.Notify(failure => Assert.Fail(failure.Message));
                AssertReaders(node, 200);
                selected = old;
                AssertReaders(node, 100);
                selected = update.Next;
            }
            else
            {
                update.Dispose();
            }
            AssertReaders(node, publish ? 200u : 100u);
            Assert.That(callbacks, Is.EqualTo(publish ? 2 : 0));
        }

        [TestCase("Add")]
        [TestCase("AddIfMissing")]
        [TestCase("AddMany")]
        [TestCase("Remove")]
        [TestCase("RemoveAll")]
        [TestCase("Remap")]
        [TestCase("Binary")]
        [TestCase("Xml")]
        [TestCase("Initialize")]
        public void PendingAndRetiredReferenceImagesRejectEveryMutation(string operation)
        {
            var node = CreateNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));
            SystemContext context = CreateContext();
            NodeState.ReferenceSnapshot selected = null;
            NodeState.ReferenceSnapshot Select(NodeState _) => selected;
            using NodeState.ReferenceUpdate baseline = node.PrepareReferences(Select, [], []);
            baseline.Reserve();
            selected = baseline.Next;
            baseline.Complete();
            NodeState.ReferenceSnapshot old = selected;
            using NodeState.ReferenceUpdate update = node.PrepareReferences(
                Select, [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(200))], []);
            update.Reserve();
            int callbacks = 0;
            node.OnReferenceAdded = (_, _, _, _) => callbacks++;
            node.OnReferenceRemoved = (_, _, _, _) => callbacks++;
            Assert.Throws<InvalidOperationException>(Mutate);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(100)), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(200)), Is.False);
            selected = update.Next;
            update.Complete();
            update.Dispose();
            selected = old;
            Assert.Throws<InvalidOperationException>(Mutate);
            Assert.That(callbacks, Is.Zero);
            selected = update.Next;
            Assert.DoesNotThrow(Mutate);

            void Mutate()
            {
                switch (operation)
                {
                    case "Add":
                        node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(400));
                        break;
                    case "AddIfMissing":
                        node.AddReferenceIfMissing(ReferenceTypeIds.Organizes, false, new NodeId(100));
                        break;
                    case "AddMany":
                        node.AddReferences(
                            [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(400))]);
                        break;
                    case "Remove":
                        node.RemoveReference(ReferenceTypeIds.Organizes, false, new NodeId(100));
                        break;
                    case "RemoveAll":
                        node.RemoveReferences(ReferenceTypeIds.Organizes, false);
                        break;
                    case "Remap":
                        node.UpdateReferenceTargets(context, new Dictionary<NodeId, NodeId>
                        {
                            [new NodeId(100)] = new NodeId(400)
                        });
                        break;
                    case "Binary":
                    case "Xml":
                        using (var stream = new MemoryStream())
                        {
                            var source = CreateNode();
                            source.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(400));
                            if (operation == "Binary")
                            {
                                source.SaveAsBinary(context, stream);
                                stream.Position = 0;
                                node.LoadAsBinary(context, stream);
                            }
                            else
                            {
                                source.SaveAsXml(context, stream);
                                using var input = new MemoryStream(stream.ToArray());
                                node.LoadFromXml(context, input);
                            }
                        }
                        break;
                    case "Initialize":
                        node.Create(context, CreateNode());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation));
                }
            }
        }

        [Test]
        public void AbortedReferenceReservationReleasesWritesAndRejectsStalePublication()
        {
            var node = CreateNode();
            NodeState.ReferenceSnapshot selected = null;
            NodeState.ReferenceSnapshot Select(NodeState _) => selected;
            using NodeState.ReferenceUpdate stale = node.PrepareReferences(
                Select, [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(100))], []);
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(200));
            Assert.Throws<InvalidOperationException>(stale.Reserve);
            Assert.Throws<InvalidOperationException>(() => stale.Notify(_ => Assert.Fail("Private callback.")));
            using NodeState.ReferenceUpdate aborted = node.PrepareReferences(Select, [], []);
            aborted.Reserve();
            aborted.Dispose();
            aborted.Dispose();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(300));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(100)), Is.False);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(200)), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(300)), Is.True);
            Assert.Throws<InvalidOperationException>(aborted.Reserve);
        }

        [Test]
        public void ReferenceNotificationsArePostpublicationAndFailuresDoNotSkipOtherChanges()
        {
            var node = CreateNode();
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(100));
            NodeState.ReferenceSnapshot selected = null;
            NodeState.ReferenceSnapshot Select(NodeState _) => selected;
            var failures = new List<Exception>();
            var observed = new List<ExpandedNodeId>();
            node.OnReferenceRemoved = (_, _, _, target) =>
            {
                Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(200)), Is.True);
                Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(100)), Is.False);
                observed.Add(target);
                throw new IOException("Removal notification failed after publication.");
            };
            node.OnReferenceAdded = (_, _, _, target) =>
            {
                observed.Add(target);
                throw new IOException("Addition notification failed after publication.");
            };
            using NodeState.ReferenceUpdate update = node.PrepareReferences(Select,
                [
                    new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(200)),
                    new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(300))
                ],
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(100))]);
            Assert.That(observed, Is.Empty);
            update.Reserve();
            selected = update.Next;
            update.Complete();
            update.Notify(failures.Add);
            update.Notify(failures.Add);
            Assert.That(observed, Is.EqualTo(new ExpandedNodeId[] { new(100), new(200), new(300) }));
            Assert.That(failures, Has.Count.EqualTo(3));
            Assert.That(failures.All(failure => failure is IOException), Is.True);
        }

        [Test]
        public void ReferenceImagesOwnTheirKeysPreserveInternalTargetsAndReleaseOwnership()
        {
            var node = CreateNode();
            var target = CreateNode();
            target.NodeId = new NodeId(200);
            node.AddReferences([new NodeStateReference(ReferenceTypeIds.Organizes, false, target)]);
            NodeState.ReferenceSnapshot selected = null;
            NodeState.ReferenceSnapshot Select(NodeState _) => selected;
            var mutable = new MutableReference
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TargetId = new NodeId(300)
            };
            using NodeState.ReferenceUpdate update = node.PrepareReferences(Select, [mutable], []);
            mutable.TargetId = new NodeId(400);
            update.Reserve();
            selected = update.Next;
            update.Complete();
            var references = new List<IReference>();
            node.GetReferences(CreateContext(), references);
            Assert.That(references, Has.Count.EqualTo(2));
            Assert.That(((NodeStateReference)references[0]).Target, Is.SameAs(target));
            Assert.That(references[1].TargetId, Is.EqualTo(new ExpandedNodeId(300)));
            Assert.Throws<InvalidOperationException>(() => node.PrepareReferences(_ => null, [], []));
            Assert.Throws<InvalidOperationException>(() => node.ReleaseReferenceView(_ => null, selected));
            node.ReleaseReferenceView(Select, selected);
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(500));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.HasComponent, false, new NodeId(300)), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(500)), Is.True);
            using NodeState.ReferenceUpdate anotherOwner = node.PrepareReferences(_ => null, [], []);
            Assert.DoesNotThrow(anotherOwner.Reserve);
        }

        [Test]
        public void InvalidPreparedReferenceDoesNotAcquireOwnershipOrChangeTheNode()
        {
            var node = CreateNode();
            Assert.Throws<ArgumentException>(() => node.PrepareReferences(
                _ => null, [new NodeStateReference(NodeId.Null, false, new NodeId(100))], []));
            var references = new List<IReference>();
            node.GetReferences(CreateContext(), references);
            Assert.That(references, Is.Empty);
            using NodeState.ReferenceUpdate update = node.PrepareReferences(_ => null, [], []);
            Assert.DoesNotThrow(update.Reserve);
        }

        [Test]
        public void MissingReferenceMutationsPreserveEmptyNodeIdentity()
        {
            var node = CreateNode();
            var unchanged = CreateNode();
            Assert.That(node.RemoveReference(ReferenceTypeIds.Organizes, false, new NodeId(100)), Is.False);
            Assert.That(node.RemoveReferences(ReferenceTypeIds.Organizes, false), Is.False);
            node.UpdateReferenceTargets(CreateContext(), []);
            Assert.That(node.DeepEquals(unchanged), Is.True);
        }

        private static BaseObjectState CreateNode()
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId(10),
                BrowseName = QualifiedName.From("ReferenceOwner"),
                DisplayName = LocalizedText.From("ReferenceOwner"),
                SymbolicName = "ReferenceOwner"
            };
        }

        private static SystemContext CreateContext()
        {
            var namespaces = new NamespaceTable();
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaces,
                ServerUris = new StringTable(),
                TypeTable = new TypeTable(namespaces)
            };
        }

        private static void AssertReaders(BaseObjectState node, uint target)
        {
            SystemContext context = CreateContext();
            ExpandedNodeId expected = new(target);
            var references = new List<IReference>();
            node.GetReferences(context, references);
            Assert.That(references.Select(reference => reference.TargetId),
                Is.EquivalentTo(new ExpandedNodeId[] { expected, new(300) }));
            references.Clear();
            node.GetReferences(context, references, ReferenceTypeIds.Organizes, false);
            Assert.That(references.Select(reference => reference.TargetId), Is.EqualTo(new[] { expected }));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, expected), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false,
                new NodeId(target == 100 ? 200u : 100u)), Is.False);
            using INodeBrowser browser = node.CreateBrowser(
                context, default, ReferenceTypeIds.Organizes, false, BrowseDirection.Forward, default, null, true);
            Assert.That(browser.Next().TargetId, Is.EqualTo(expected));
            Assert.That(browser.Next(), Is.Null);
            var hierarchy = new List<NodeStateHierarchyReference>();
            node.GetHierarchyReferences(context, "Owner", [], hierarchy);
            Assert.That(hierarchy.Select(reference => reference.TargetId),
                Is.EquivalentTo(new ExpandedNodeId[] { expected, new(300) }));
            var copy = (BaseObjectState)node.Clone();
            Assert.That(copy.ReferenceExists(ReferenceTypeIds.Organizes, false, expected), Is.True);
            Assert.That(node.DeepEquals(copy), Is.True);
            var initialized = CreateNode();
            initialized.Create(context, node);
            Assert.That(initialized.ReferenceExists(ReferenceTypeIds.Organizes, false, expected), Is.True);
            using var binary = new MemoryStream();
            node.SaveAsBinary(context, binary);
            binary.Position = 0;
            var fromBinary = CreateNode();
            fromBinary.LoadAsBinary(context, binary);
            Assert.That(fromBinary.ReferenceExists(ReferenceTypeIds.Organizes, false, expected), Is.True);
            using var xml = new MemoryStream();
            node.SaveAsXml(context, xml);
            using var input = new MemoryStream(xml.ToArray());
            var fromXml = CreateNode();
            fromXml.LoadFromXml(context, input);
            Assert.That(fromXml.ReferenceExists(ReferenceTypeIds.Organizes, false, expected), Is.True);
        }

        private sealed class MutableReference : IReference
        {
            public NodeId ReferenceTypeId { get; set; }

            public bool IsInverse { get; set; }

            public ExpandedNodeId TargetId { get; set; }
        }
    }
}
