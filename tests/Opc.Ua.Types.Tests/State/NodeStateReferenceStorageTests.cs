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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Pins explicit-reference identity, ordering, mutation, and serialization behavior.
    /// </summary>
    [TestFixture]
    [Category("NodeStateReferenceStorage")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class NodeStateReferenceStorageTests
    {
        [Test]
        public void FreshNodeReadsDoNotAllocateReferenceStorage()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference reference = CreateReferences(1)[0];
            Assert.That(GetReferences(node, context), Is.Empty);
            Assert.That(node.ReferenceExists(reference.ReferenceTypeId, false, reference.TargetId), Is.False);
            Assert.That(node.RemoveReference(reference.ReferenceTypeId, false, reference.TargetId), Is.False);
            using INodeBrowser browser = CreateBrowser(node, context, ReferenceTypeIds.Aggregates, true);
            Assert.That(ReadBrowser(browser), Is.Empty);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(s_referencesField.GetValue(node), Is.Null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(128)]
        [TestCase(1024)]
        public void GetReferencesAndBrowsePreserveRequiredOrderAndIdentity(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(count);
            node.AddReferences(input);
            var oracle = new ReferenceDictionary<byte>();
            foreach (IReference reference in input)
            {
                oracle.Add(reference, 0);
                Assert.That(
                    node.ReferenceExists(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId),
                    Is.True);
            }
            AssertSameReferences(GetReferences(node, context), input);
            AssertNodeQueries(node, context, oracle);
        }

        [Test]
        public void ReferenceKeysDistinguishTypeDirectionAndTarget()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            ExpandedNodeId target = new NodeId(123u, 1);
            IReference[] input =
            [
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, target),
                new NodeStateReference(ReferenceTypeIds.HasComponent, true, target),
                new NodeStateReference(ReferenceTypeIds.Organizes, false, target),
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(124u, 1))
            ];
            node.AddReferences(input);
            AssertSameReferences(GetReferences(node, context), input);
            Assert.That(node.RemoveReference(ReferenceTypeIds.HasComponent, false, target), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.HasComponent, false, target), Is.False);
            AssertSameReferences(GetReferences(node, context), input.Skip(1));
            foreach (IReference reference in input.Skip(1))
            {
                Assert.That(node.ReferenceExists(
                    reference.ReferenceTypeId, reference.IsInverse, reference.TargetId), Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MutableExternalReferencesKeepInsertionTimeIndexesAndObjectIdentity(bool existing)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            var oracle = new ReferenceDictionary<byte>();
            if (existing)
            {
                IReference first = CreateReferences(1)[0];
                node.AddReferences([first]);
                oracle.Add(first, 0);
            }
            var external = new ReferenceNode(ReferenceTypeIds.Organizes, false, new NodeId(4000u, 1));
            node.AddReferences([external]);
            oracle.Add(external, 0);
            external.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            external.IsInverse = true;
            external.TargetId = new NodeId(5000u, 1);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(4000u, 1)), Is.True);
            Assert.That(node.ReferenceExists(external.ReferenceTypeId, external.IsInverse, external.TargetId),
                Is.False);
            Assert.That(GetReferences(node, context).Last(), Is.SameAs(external));
            AssertNodeQueries(node, context, oracle);
            Assert.That(node.RemoveReference(external.ReferenceTypeId, external.IsInverse, external.TargetId),
                Is.False);
            Assert.That(node.RemoveReference(ReferenceTypeIds.Organizes, false, new NodeId(4000u, 1)), Is.True);
            oracle.Remove(new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(4000u, 1)));
            AssertSameReferences(GetReferences(node, context), oracle.Keys);
        }

        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        public void ReferenceQueriesMatchDictionaryAcrossShrinkAndRegrowth(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            var oracle = new ReferenceDictionary<byte>();
            IReference[] input = CreateReferences(count);
            foreach (IReference reference in input)
            {
                node.AddReferences([reference]);
                oracle.Add(reference, 0);
                AssertNodeQueries(node, context, oracle);
            }
            foreach (IReference reference in input.Skip(1))
            {
                Assert.That(node.RemoveReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId),
                    Is.EqualTo(oracle.Remove(reference)));
                AssertNodeQueries(node, context, oracle);
            }
            foreach (IReference reference in input.Skip(1).Reverse())
            {
                node.AddReferences([reference]);
                oracle.Add(reference, 0);
                AssertNodeQueries(node, context, oracle);
            }
            AssertSameReferences(GetReferences(node, context), input.Take(1).Concat(input.Skip(1).Reverse()));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void FilteredSubtypeBrowseRequiresTypeTableOnlyWhenStorageExists(int count)
        {
            SystemContext context = CreateContext();
            context.TypeTable = null!;
            var node = new BaseObjectState(null);
            using (INodeBrowser absent = CreateBrowser(node, context, ReferenceTypeIds.Aggregates, true))
            {
                Assert.That(ReadBrowser(absent), Is.Empty);
            }
            IReference[] input = CreateReferences(count);
            node.AddReferences(input);
            Assert.That(
                () => CreateBrowser(node, context, ReferenceTypeIds.Aggregates, true).Dispose(),
                Throws.ArgumentNullException);
            using INodeBrowser exact = CreateBrowser(node, context, ReferenceTypeIds.HasComponent, false);
            AssertSameReferences(ReadBrowser(exact),
                input.Where(reference => reference.ReferenceTypeId == ReferenceTypeIds.HasComponent));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public async Task DuplicatePoliciesPreserveFirstObjectAndCallbackSequenceAsync(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(count);
            var events =
                new List<(bool Added, NodeId Type, bool Inverse, ExpandedNodeId Target, bool Exists, int Count)>();
            node.OnReferenceAdded = (sender, type, inverse, target) => events.Add(
                (true, type, inverse, target, sender.ReferenceExists(type, inverse, target),
                    GetReferences(sender, context).Count));
            node.OnReferenceRemoved = (sender, type, inverse, target) => events.Add(
                (false, type, inverse, target, sender.ReferenceExists(type, inverse, target),
                    GetReferences(sender, context).Count));
            node.AddReferences([.. input, .. input.Select(Duplicate)]);
            Assert.That(events, Is.EqualTo(input.Select(reference =>
                (true, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId, true, count))));
            AssertSameReferences(GetReferences(node, context), input);
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            IReference first = input[0];

            Assert.That(() => node.AddReference(first.ReferenceTypeId, first.IsInverse, first.TargetId),
                Throws.ArgumentException);
            Assert.That(node.AddReferenceIfMissing(first.ReferenceTypeId, first.IsInverse, first.TargetId), Is.False);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            node.AddReferences([.. input.Select(Duplicate)]);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.References));
            Assert.That(events, Has.Count.EqualTo(count));
            AssertSameReferences(GetReferences(node, context), input);

            Assert.That(node.RemoveReference(first.ReferenceTypeId, first.IsInverse, first.TargetId), Is.True);
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            Assert.That(node.RemoveReference(first.ReferenceTypeId, first.IsInverse, first.TargetId), Is.False);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            IReference replacement = Duplicate(first);
            node.AddReferences([replacement]);
            Assert.That(events.Skip(count), Is.EqualTo(
            [
                (false, first.ReferenceTypeId, first.IsInverse, first.TargetId, false, count - 1),
                (true, first.ReferenceTypeId, first.IsInverse, first.TargetId, true, count)
            ]));
            AssertSameReferences(GetReferences(node, context), input.Skip(1).Append(replacement));
            var oracle = new ReferenceDictionary<byte>();
            foreach (IReference reference in input)
            {
                oracle.Add(reference, 0);
            }
            oracle.Remove(first);
            oracle.Add(replacement, 0);
            AssertNodeQueries(node, context, oracle);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task CallbacksAllowAnotherTaskToObserveCompletedMutationAsync(int operation)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference reference = CreateReferences(1)[0];
            bool removing = operation == 3;
            if (removing)
            {
                node.AddReferences([reference]);
            }
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            int callbacks = 0;
            void Observe(NodeState sender, NodeId type, bool inverse, ExpandedNodeId target)
            {
                Interlocked.Increment(ref callbacks);
                entered.TrySetResult(true);
                // Hold the synchronous callback open, not a Task: the test orchestrates its release asynchronously.
                if (!release.Wait(s_timeout))
                {
                    throw new TimeoutException("The reference callback was not released.");
                }
                Assert.That(sender, Is.SameAs(node));
                Assert.That(type, Is.EqualTo(reference.ReferenceTypeId));
                Assert.That(inverse, Is.EqualTo(reference.IsInverse));
                Assert.That(target, Is.EqualTo(reference.TargetId));
            }
            node.OnReferenceAdded = Observe;
            node.OnReferenceRemoved = Observe;
            var mutation = Task.Run(() =>
            {
                switch (operation)
                {
                    case 0:
                        node.AddReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                        break;
                    case 1:
                        Assert.That(node.AddReferenceIfMissing(
                            reference.ReferenceTypeId, reference.IsInverse, reference.TargetId), Is.True);
                        break;
                    case 2:
                        node.AddReferences([reference]);
                        break;
                    default:
                        Assert.That(node.RemoveReference(
                            reference.ReferenceTypeId, reference.IsInverse, reference.TargetId), Is.True);
                        break;
                }
            });
            Task? observer = null;
            try
            {
                await AssertCompletesAsync(entered.Task).ConfigureAwait(false);
                observer = Task.Run(() =>
                {
                    Assert.That(node.ReferenceExists(
                        reference.ReferenceTypeId, reference.IsInverse, reference.TargetId), Is.EqualTo(!removing));
                    List<IReference> references = GetReferences(node, context);
                    Assert.That(references, Has.Count.EqualTo(removing ? 0 : 1));
                    if (!removing)
                    {
                        AssertReferenceValues(references, [reference]);
                    }
                });
                await AssertCompletesAsync(observer).ConfigureAwait(false);
            }
            finally
            {
                release.Set();
                await Task.WhenAll(mutation, observer ?? Task.CompletedTask)
                    .WaitAsync(s_timeout).ConfigureAwait(false);
            }
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.References));
        }

        [Test]
        public void BrowserSnapshotRetainsOriginalReferencesAfterNodeMutation()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(4);
            node.AddReferences(input);
            using INodeBrowser original = CreateBrowser(node, context, default, false);
            List<IReference> snapshot = GetReferences(node, context);
            Assert.That(node.RemoveReference(
                input[0].ReferenceTypeId, input[0].IsInverse, input[0].TargetId), Is.True);
            IReference added = CreateReferences(5)[4];
            node.AddReferences([added]);
            AssertSameReferences(ReadBrowser(original), input);
            AssertSameReferences(snapshot, input);
            using INodeBrowser current = CreateBrowser(node, context, default, false);
            AssertSameReferences(ReadBrowser(current), input.Skip(1).Append(added));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        public void RemoveReferencesNotifiesInInsertionOrderAndAllowsReadding(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(count);
            node.AddReferences(input);
            var removed = new List<ExpandedNodeId>();
            node.OnReferenceRemoved = (_, _, _, target) => removed.Add(target);
            IReference[] matches = [.. input.Where(reference =>
                reference.ReferenceTypeId == ReferenceTypeIds.HasComponent && !reference.IsInverse)];
            Assert.That(node.RemoveReferences(ReferenceTypeIds.HasComponent, false), Is.EqualTo(matches.Length != 0));
            Assert.That(removed, Is.EqualTo(matches.Select(reference => reference.TargetId)));
            IReference[] remaining = [.. input.Except(matches)];
            AssertSameReferences(GetReferences(node, context), remaining);
            Assert.That(node.RemoveReferences(ReferenceTypeIds.HasComponent, false), Is.False);
            Assert.That(removed, Has.Count.EqualTo(matches.Length));
            node.AddReferences(matches);
            AssertSameReferences(GetReferences(node, context), remaining.Concat(matches));
        }

        [Test]
        public void BrowserDeduplicatesIntrinsicAndAdditionalReferencesByValue()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null) { TypeDefinitionId = ObjectTypeIds.BaseObjectType };
            var child = new BaseObjectState(node) { NodeId = new NodeId(500u, 1) };
            node.AddChild(child);
            var childReference = new NodeStateReference(child.ReferenceTypeId, false, child.NodeId);
            var typeReference = new NodeStateReference(
                ReferenceTypeIds.HasTypeDefinition, false, node.TypeDefinitionId);
            IReference explicitReference = CreateReferences(1)[0];
            node.AddReferences([childReference, typeReference, explicitReference]);
            IReference additional = Duplicate(explicitReference);
            using INodeBrowser browser = CreateBrowser(node, context, default, false,
                additional: [additional, Duplicate(explicitReference)]);
            List<IReference> actual = ReadBrowser(browser);
            Assert.That(actual, Has.Count.EqualTo(3));
            Assert.That(actual[0], Is.SameAs(additional));
            AssertReferenceValues(actual.Skip(1), [childReference, typeReference]);
            Assert.That(((NodeStateReference)actual[1]).Target, Is.SameAs(child));
            AssertSameReferences(GetReferences(node, context), [childReference, typeReference, explicitReference]);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(16)]
        public void CloneSharesReferencesButOwnsIndependentStorage(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(count);
            node.AddReferences(input);
            var copy = (BaseObjectState)node.Clone();
            AssertSameReferences(GetReferences(copy, context), input);
            Assert.That(s_referencesField.GetValue(node), Is.Not.Null);
            Assert.That(s_referencesField.GetValue(copy), Is.Not.Null);
            Assert.That(s_referencesField.GetValue(copy), Is.Not.SameAs(s_referencesField.GetValue(node)));
            if (count != 0)
            {
                IReference first = input[0];
                Assert.That(copy.RemoveReference(first.ReferenceTypeId, first.IsInverse, first.TargetId), Is.True);
            }
            IReference added = CreateReferences(count + 1)[count];
            copy.AddReferences([added]);
            AssertSameReferences(GetReferences(node, context), input);
            AssertSameReferences(GetReferences(copy, context), input.Skip(1).Append(added));
            if (count != 0)
            {
                IReference last = input[count - 1];
                Assert.That(node.RemoveReference(last.ReferenceTypeId, last.IsInverse, last.TargetId), Is.True);
                AssertSameReferences(GetReferences(node, context), input.Take(count - 1));
                AssertSameReferences(GetReferences(copy, context), input.Skip(1).Append(added));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RetargetCoalescesMappedReferencesRecursivelyWithoutChangingRemoteIdentityAsync(
            bool existingDestination)
        {
            SystemContext context = CreateContext();
            var root = new BaseObjectState(null);
            var child = new BaseObjectState(root) { NodeId = new NodeId(800u, 1) };
            var grandchild = new BaseObjectState(child) { NodeId = new NodeId(801u, 1) };
            root.AddChild(child);
            child.AddChild(grandchild);
            NodeId type = ReferenceTypeIds.HasComponent;
            var oldFirst = new NodeId(101u, 1);
            var oldSecond = new NodeId(102u, 1);
            var destination = new NodeId(103u, 1);
            var remote = new NodeStateReference(type, false,
                new ExpandedNodeId(new NodeId("Unmapped", 0), "urn:unknown:namespace", 1));
            var local = new NodeStateReference(type, false, new NodeId(104u, 1));
            var collision = new NodeStateReference(type, false, destination);
            IReference[] input =
            [
                new NodeStateReference(type, false, oldFirst), remote, collision,
                new NodeStateReference(type, false, oldSecond), local,
                new NodeStateReference(type, true, oldFirst)
            ];
            if (!existingDestination)
            {
                input = [.. input.Where(reference => !ReferenceEquals(reference, collision))];
            }
            BaseObjectState[] nodes = [root, child, grandchild];
            int callbacks = 0;
            foreach (BaseObjectState node in nodes)
            {
                node.AddReferences(input);
                await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
                node.OnReferenceAdded = (_, _, _, _) => callbacks++;
                node.OnReferenceRemoved = (_, _, _, _) => callbacks++;
            }
            root.UpdateReferenceTargets(context, new Dictionary<NodeId, NodeId>
            {
                [oldFirst] = destination,
                [oldSecond] = destination
            });
            foreach (BaseObjectState node in nodes)
            {
                List<IReference> actual = GetReferences(node, context);
                Assert.That(actual, Has.Count.EqualTo(4));
                Assert.That(actual[0], Is.SameAs(remote));
                Assert.That(actual[existingDestination ? 2 : 1], Is.SameAs(local));
                IReference replaced = actual[existingDestination ? 1 : 2];
                AssertReferenceValues([replaced, actual[3]],
                    [collision, new NodeStateReference(type, true, destination)]);
                Assert.That(replaced, Is.Not.SameAs(collision));
                Assert.That(node.ReferenceExists(type, false, oldFirst), Is.False);
                Assert.That(node.ReferenceExists(type, false, oldSecond), Is.False);
                Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.References));
                await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            }
            List<IReference> unchanged = GetReferences(root, context);
            root.UpdateReferenceTargets(context, []);
            AssertSameReferences(GetReferences(root, context), unchanged);
            Assert.That(nodes.Select(node => node.ChangeMasks), Is.All.EqualTo(NodeStateChangeMasks.None));
            Assert.That(callbacks, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public async Task BinaryUpdateMergesAndReplacesExistingKeyWithoutMovingOrNotifyingAsync(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(count);
            IReference additional = CreateReferences(count + 1)[count];
            node.AddReferences(input);
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            int callbacks = 0;
            node.OnReferenceAdded = (_, _, _, _) => callbacks++;
            node.OnReferenceRemoved = (_, _, _, _) => callbacks++;
            UpdateBinary(node, context, [Duplicate(input[0]), additional, Duplicate(input[0])]);
            List<IReference> actual = GetReferences(node, context);
            AssertReferenceValues(actual, input.Append(additional));
            Assert.That(actual[0], Is.Not.SameAs(input[0]));
            AssertSameReferences(actual.Skip(1).Take(count - 1), input.Skip(1));
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(callbacks, Is.Zero);
            UpdateBinary(node, context, []);
            AssertSameReferences(GetReferences(node, context), actual);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(callbacks, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task XmlUpdateClearsAndReplacesDuplicatesWithoutCallbacksAsync(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(4);
            node.AddReferences(input);
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            int callbacks = 0;
            node.OnReferenceAdded = (_, _, _, _) => callbacks++;
            node.OnReferenceRemoved = (_, _, _, _) => callbacks++;
            IReference[] incoming = [.. input.Skip(2).Take(count)];
            UpdateXml(node, context, [.. incoming, .. incoming.Select(Duplicate)]);
            List<IReference> actual = GetReferences(node, context);
            AssertReferenceValues(actual, incoming);
            for (int index = 0; index < actual.Count; index++)
            {
                Assert.That(actual[index], Is.Not.SameAs(incoming[index]));
            }
            Assert.That(node.ReferenceExists(
                input[0].ReferenceTypeId, input[0].IsInverse, input[0].TargetId), Is.False);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.References));
            Assert.That(callbacks, Is.Zero);
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            UpdateXml(node, context, []);
            Assert.That(GetReferences(node, context), Is.Empty);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.References));
            Assert.That(callbacks, Is.Zero);
        }

        [Test]
        public void ReferenceStreamsPreserveAbsoluteUrisWithoutNamespaceRemapping([Values] bool xml)
        {
            SystemContext context = CreateContext();
            IServiceMessageContext messageContext = CreateMessageContext(context);
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(16);
            node.AddReferences(input);
            var loaded = new BaseObjectState(null);
            if (xml)
            {
                using var encoder = new XmlEncoder(messageContext);
                encoder.Push("Root", Namespaces.OpcUaXsd);
                node.SaveReferences(context, encoder);
                encoder.Pop();
                using var text = new StringReader(encoder.CloseAndReturnText()!);
                using var reader = XmlReader.Create(text, CoreUtils.DefaultXmlReaderSettings());
                using var decoder = new XmlDecoder(null, reader, messageContext);
                loaded.UpdateReferences(context, decoder);
            }
            else
            {
                using var output = new MemoryStream();
                using (var encoder = new BinaryEncoder(output, messageContext, true))
                {
                    node.SaveReferences(context, encoder);
                }
                using var stream = new MemoryStream(output.ToArray());
                using var decoder = new BinaryDecoder(stream, messageContext, true);
                loaded.UpdateReferences(context, decoder);
            }
            AssertReferenceValues(GetReferences(loaded, context), input);
            AssertSameReferences(GetReferences(node, context), input);
        }

        [Test]
        public void WholeNodeFormatsPreserveMixedReferencesAcrossReferenceCounts(
            [Values(0, 1, 2, 4, 8, 16, 128, 1024)] int count,
            [Values] bool xml)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(9000u, 1),
                SymbolicName = "ReferenceNode",
                BrowseName = new QualifiedName("ReferenceNode", 1),
                DisplayName = new LocalizedText("Reference node")
            };
            IReference[] input = CreateReferences(count);
            // Full-node decoders currently discard absolute namespace URIs when applying mappings.
            // Exercise indexed namespaces here; the reference-stream test pins absolute URI preservation.
            input = [.. input.Select(reference => string.IsNullOrEmpty(reference.TargetId.NamespaceUri)
                ? reference
                : new NodeStateReference(reference.ReferenceTypeId, reference.IsInverse,
                    reference.TargetId.WithNamespaceIndex(2)))];
            node.AddReferences(input);
            using var output = new MemoryStream();
            if (xml)
            {
                node.SaveAsXml(context, output);
            }
            else
            {
                node.SaveAsBinary(context, output);
            }
            using var stream = new MemoryStream(output.ToArray());
            var loaded = new BaseObjectState(null);
            if (xml)
            {
                loaded.LoadFromXml(context, stream);
            }
            else
            {
                loaded.LoadAsBinary(context, stream);
            }
            Assert.That(loaded.NodeId, Is.EqualTo(new NodeId(9000u, 1)));
            Assert.That(loaded.BrowseName, Is.EqualTo(new QualifiedName("ReferenceNode", 1)));
            Assert.That(loaded.SymbolicName, Is.EqualTo("ReferenceNode"));
            Assert.That(loaded.DisplayName, Is.EqualTo(new LocalizedText("Reference node")));
            AssertReferenceValues(GetReferences(loaded, context), input);
            AssertSameReferences(GetReferences(node, context), input);
            var oracle = new ReferenceDictionary<byte>();
            foreach (IReference reference in GetReferences(loaded, context))
            {
                oracle.Add(reference, 0);
            }
            AssertNodeQueries(loaded, context, oracle);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SerializationUsesSnapshotAndEncodesOutsideCollectionLockAsync(bool xml)
        {
            SystemContext context = CreateContext();
            IServiceMessageContext messageContext = CreateMessageContext(context);
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(2);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            int armed = 0;
            var reference = new Mock<IReference>();
            reference.SetupGet(value => value.ReferenceTypeId).Returns(input[0].ReferenceTypeId);
            reference.SetupGet(value => value.IsInverse).Returns(input[0].IsInverse);
            reference.SetupGet(value => value.TargetId).Returns(() =>
            {
                if (Interlocked.Exchange(ref armed, 0) != 0)
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(s_timeout))
                    {
                        throw new TimeoutException("The reference encoder was not released.");
                    }
                }
                return input[0].TargetId;
            });
            node.AddReferences([reference.Object]);
            Volatile.Write(ref armed, 1);
            using var output = new MemoryStream();
            var serialization = Task.Run(() =>
            {
                if (xml)
                {
                    using var encoder = new XmlEncoder(messageContext);
                    encoder.Push("Root", Namespaces.OpcUaXsd);
                    node.SaveReferences(context, encoder);
                    encoder.Pop();
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(encoder.CloseAndReturnText()!);
                    output.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    using var encoder = new BinaryEncoder(output, messageContext, true);
                    node.SaveReferences(context, encoder);
                }
            });
            Task? mutation = null;
            try
            {
                await AssertCompletesAsync(entered.Task).ConfigureAwait(false);
                mutation = Task.Run(() => node.AddReferences([input[1]]));
                await AssertCompletesAsync(mutation).ConfigureAwait(false);
            }
            finally
            {
                release.Set();
                await Task.WhenAll(serialization, mutation ?? Task.CompletedTask)
                    .WaitAsync(s_timeout).ConfigureAwait(false);
            }
            using var stream = new MemoryStream(output.ToArray());
            var loaded = new BaseObjectState(null);
            if (xml)
            {
                using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
                using var decoder = new XmlDecoder(null, reader, messageContext);
                loaded.UpdateReferences(context, decoder);
            }
            else
            {
                using var decoder = new BinaryDecoder(stream, messageContext, true);
                loaded.UpdateReferences(context, decoder);
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            }
            AssertReferenceValues(GetReferences(loaded, context), [input[0]]);
            AssertSameReferences(GetReferences(node, context), [reference.Object, input[1]]);
        }

        [TestCase(16)]
        [TestCase(128)]
        [TestCase(1024)]
        public void FilteredIndexOrderSurvivesGrowthRemovalAndReplacement(int count)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            var oracle = new ReferenceDictionary<byte>();
            IReference[] input = [.. Enumerable.Range(1, count).Select(index =>
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId((uint)index, 1)))];
            node.AddReferences(input);
            foreach (IReference reference in input)
            {
                oracle.Add(reference, 0);
            }
            AssertSameReferences(GetReferences(node, context), input);
            AssertNodeQueries(node, context, oracle);

            IReference[] removed = [.. input.Where((_, index) => index % 3 == 0)];
            foreach (IReference reference in removed)
            {
                Assert.That(node.RemoveReference(reference.ReferenceTypeId, false, reference.TargetId), Is.True);
                Assert.That(oracle.Remove(reference), Is.True);
            }
            IReference[] reinserted = [.. removed];
            Array.Reverse(reinserted);
            foreach (IReference reference in reinserted)
            {
                node.AddReferences([reference]);
                oracle.Add(reference, 0);
            }
            AssertSameReferences(GetReferences(node, context),
                input.Except(removed).Concat(reinserted));
            AssertNodeQueries(node, context, oracle);

            UpdateBinary(node, context, [Duplicate(input[1])]);
            List<IReference> current = GetReferences(node, context);
            IReference replacement = current[0];
            Assert.That(replacement, Is.Not.SameAs(input[1]));
            AssertReferenceValues([replacement], [input[1]]);
            oracle[replacement] = 1;
            AssertNodeQueries(node, context, oracle);
        }

        [Test]
        public void FilteredIndexOrderIsNotTheUnfilteredInsertionOrder()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input =
            [
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(3u, 1)),
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(1u, 1)),
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(2u, 1))
            ];
            node.AddReferences(input);
            AssertSameReferences(GetReferences(node, context), input);
            using INodeBrowser browser = CreateBrowser(node, context, ReferenceTypeIds.HasComponent, false);
            AssertSameReferences(ReadBrowser(browser), [input[1], input[2], input[0]]);
        }

        [Test]
        public void DictionaryVersionIncludesFailedValidMutationsButNotInvalidKeysOrRemoveAll()
        {
            var dictionary = new ReferenceDictionary<byte>();
            IReference first = CreateReferences(1)[0];
            IReference invalid = new NodeStateReference(default, false, first.TargetId);
            Assert.That(() => dictionary.Add(invalid, 0), Throws.ArgumentNullException);
            Assert.That(dictionary.Remove(invalid), Is.False);
            Assert.That(dictionary.Version, Is.Zero);
            dictionary.Add(first, 0);
            Assert.That(dictionary.Version, Is.EqualTo(1));
            Assert.That(() => dictionary.Add(Duplicate(first), 1), Throws.ArgumentException);
            Assert.That(dictionary.Version, Is.EqualTo(2));
            Assert.That(dictionary.Remove(CreateReferences(2)[1]), Is.False);
            Assert.That(dictionary.Version, Is.EqualTo(3));
            dictionary[Duplicate(first)] = 1;
            Assert.That(dictionary.Version, Is.EqualTo(4));
            Assert.That(dictionary.RemoveAll(first.ReferenceTypeId, first.IsInverse), Is.True);
            Assert.That(dictionary.Version, Is.EqualTo(4));
            Assert.That(dictionary, Is.Empty);
            dictionary.Clear();
            dictionary.Clear();
            Assert.That(dictionary.Version, Is.EqualTo(6));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidSingleMutationDoesNotCreateStorage(int operation)
        {
            var node = new BaseObjectState(null);
            IReference reference = CreateReferences(1)[0];
            foreach (bool invalidType in new[] { false, true })
            {
                NodeId type = invalidType ? default : reference.ReferenceTypeId;
                ExpandedNodeId target = invalidType ? reference.TargetId : default;
                Action mutate = operation switch
                {
                    0 => () => node.AddReference(type, false, target),
                    1 => () => node.AddReferenceIfMissing(type, false, target),
                    _ => () => node.RemoveReference(type, false, target)
                };
                ArgumentNullException? exception = Assert.Throws<ArgumentNullException>(mutate);
                Assert.That(exception!.ParamName, Is.EqualTo(invalidType ? "referenceTypeId" : "targetId"));
                Assert.That(node.ReferenceExists(type, false, target), Is.False);
            }
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(s_referencesField.GetValue(node), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BulkFailureRetainsValidPrefixWithoutCallbacksOrMask(bool nullReference)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(2);
            IReference invalid = nullReference ? null! : new NodeStateReference(default, false, input[0].TargetId);
            int callbacks = 0;
            node.OnReferenceAdded = (_, _, _, _) => callbacks++;
            Assert.That(() => node.AddReferences([input[0], invalid, input[1]]), Throws.ArgumentNullException);
            AssertSameReferences(GetReferences(node, context), [input[0]]);
            Assert.That(callbacks, Is.Zero);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(node.ReferenceExists(input[1].ReferenceTypeId, input[1].IsInverse, input[1].TargetId),
                Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task ThrowingCallbackPreservesOperationSpecificMaskAndCommittedMutationAsync(int operation)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(2);
            if (operation == 3)
            {
                node.AddReferences([input[0]]);
            }
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            var failure = new InvalidOperationException("Reference callback failure");
            int callbacks = 0;
            NodeStateChangeMasks observedMask = NodeStateChangeMasks.Deleted;
            void Fail(NodeState sender, NodeId type, bool inverse, ExpandedNodeId target)
            {
                callbacks++;
                observedMask = sender.ChangeMasks;
                Assert.That(sender.ReferenceExists(type, inverse, target), Is.EqualTo(operation != 3));
                throw failure;
            }
            node.OnReferenceAdded = Fail;
            node.OnReferenceRemoved = Fail;
            Action mutate = operation switch
            {
                0 => () => node.AddReference(input[0].ReferenceTypeId, false, input[0].TargetId),
                1 => () => node.AddReferenceIfMissing(input[0].ReferenceTypeId, false, input[0].TargetId),
                2 => () => node.AddReferences(input),
                _ => () => node.RemoveReference(input[0].ReferenceTypeId, false, input[0].TargetId)
            };
            Assert.That(Assert.Throws<InvalidOperationException>(mutate), Is.SameAs(failure));
            Assert.That(callbacks, Is.EqualTo(1));
            NodeStateChangeMasks expectedMask = operation == 2
                ? NodeStateChangeMasks.None : NodeStateChangeMasks.References;
            Assert.That(observedMask, Is.EqualTo(expectedMask));
            Assert.That(node.ChangeMasks, Is.EqualTo(expectedMask));
            AssertReferenceValues(GetReferences(node, context),
                operation == 3 ? [] : operation == 2 ? input : [input[0]]);
        }

        [Test]
        public void BulkCallbacksReadCurrentPropertiesAfterReentrantRemoval()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference first = CreateReferences(1)[0];
            var second = new ReferenceNode(ReferenceTypeIds.HasProperty, false, new NodeId(200u, 1));
            var observed = new List<ExpandedNodeId>();
            node.OnReferenceAdded = (sender, _, _, target) =>
            {
                observed.Add(target);
                if (observed.Count == 1)
                {
                    Assert.That(sender.RemoveReference(second.ReferenceTypeId, false, second.TargetId), Is.True);
                    second.TargetId = new NodeId(201u, 1);
                }
            };
            node.AddReferences([first, second]);
            ExpandedNodeId[] expected = [first.TargetId, new NodeId(201u, 1)];
            Assert.That(observed, Is.EqualTo(expected));
            AssertSameReferences(GetReferences(node, context), [first]);
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.References));
        }

        [Test]
        public void TargetIdentityAndInsertionIdSurviveTargetNodeMutationAndClone()
        {
            SystemContext context = CreateContext();
            var target = new BaseObjectState(null) { NodeId = new NodeId(21u, 1) };
            var reference = new NodeStateReference(ReferenceTypeIds.HasComponent, false, target);
            var node = new BaseObjectState(null);
            node.AddReferences([reference]);
            target.NodeId = new NodeId(22u, 1);
            var clone = (BaseObjectState)node.Clone();
            foreach (NodeState current in new[] { node, clone })
            {
                AssertSameReferences(GetReferences(current, context), [reference]);
                Assert.That(reference.Target, Is.SameAs(target));
                Assert.That(current.ReferenceExists(ReferenceTypeIds.HasComponent, false, new NodeId(21u, 1)),
                    Is.True);
                Assert.That(current.ReferenceExists(ReferenceTypeIds.HasComponent, false, target.NodeId), Is.False);
            }
        }

        [Test]
        public void CloneReindexesMutableReferencesButInitializationRecreatesThem()
        {
            SystemContext context = CreateContext();
            var source = new BaseObjectState(null);
            var external = new ReferenceNode(ReferenceTypeIds.Organizes, false, new NodeId(30u, 1));
            source.AddReferences([external]);
            external.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            external.IsInverse = true;
            external.TargetId = new NodeId(31u, 1);
            var clone = (BaseObjectState)source.Clone();
            AssertSameReferences(GetReferences(clone, context), [external]);
            Assert.That(source.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(30u, 1)), Is.True);
            Assert.That(clone.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(30u, 1)), Is.False);
            Assert.That(clone.ReferenceExists(external.ReferenceTypeId, true, external.TargetId), Is.True);
            var initialized = new InitializableObject();
            IReference discarded = CreateReferences(1)[0];
            initialized.AddReferences([discarded]);
            initialized.InitializeFrom(context, source);
            List<IReference> current = GetReferences(initialized, context);
            AssertReferenceValues(current, [external]);
            Assert.That(current[0], Is.Not.SameAs(external));
            Assert.That(initialized.ReferenceExists(external.ReferenceTypeId, true, external.TargetId), Is.True);
            Assert.That(initialized.ReferenceExists(
                discarded.ReferenceTypeId, discarded.IsInverse, discarded.TargetId), Is.False);
            AssertSameReferences(GetReferences(source, context), [external]);
        }

        [Test]
        public async Task DeepEqualsDistinguishesAbsentEmptyOrderAndReferenceIdentityAsync()
        {
            SystemContext context = CreateContext();
            var first = new BaseObjectState(null);
            var second = new BaseObjectState(null);
            Assert.That(first.DeepEquals(second), Is.True);
            second.AddReferences([]);
            await second.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            Assert.That(first.DeepEquals(second), Is.False);
            first.AddReferences([]);
            await first.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            Assert.That(first.DeepEquals(second), Is.True);
            IReference[] input = CreateReferences(2);
            first.AddReferences(input);
            second.AddReferences([input[1], input[0]]);
            Assert.That(first.DeepEquals(second), Is.False);
            second.RemoveReference(input[1].ReferenceTypeId, input[1].IsInverse, input[1].TargetId);
            second.AddReferences([input[1]]);
            Assert.That(first.DeepEquals(second), Is.True);
            UpdateBinary(second, context, [Duplicate(input[0])]);
            Assert.That(first.DeepEquals(second), Is.False);
        }

        [Test]
        public void MutableRemovalAndRetargetUseCurrentValuesNotHiddenInsertionKeys()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            var external = new ReferenceNode(ReferenceTypeIds.Organizes, false, new NodeId(60u, 1));
            node.AddReferences([external]);
            external.TargetId = new NodeId(61u, 1);
            Assert.That(node.RemoveReferences(ReferenceTypeIds.Organizes, false), Is.True);
            AssertSameReferences(GetReferences(node, context), [external]);
            node.UpdateReferenceTargets(context, new Dictionary<NodeId, NodeId>
            {
                [new NodeId(61u, 1)] = new NodeId(62u, 1)
            });
            List<IReference> current = GetReferences(node, context);
            Assert.That(current, Has.Count.EqualTo(2));
            Assert.That(current[0], Is.SameAs(external));
            Assert.That(current[1].TargetId, Is.EqualTo(new ExpandedNodeId(new NodeId(62u, 1))));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(60u, 1)), Is.True);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(61u, 1)), Is.False);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(62u, 1)), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidDecodedReferenceCommitsOnlyValidPrefixWithFormatSpecificMaskAsync(bool xml)
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(3);
            node.AddReferences([input[0]]);
            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            IReference[] incoming =
            [
                input[1], new NodeStateReference(default, false, input[2].TargetId), input[2]
            ];
            int callbacks = 0;
            node.OnReferenceAdded = (_, _, _, _) => callbacks++;
            node.OnReferenceRemoved = (_, _, _, _) => callbacks++;
            if (xml)
            {
                Assert.That(() => UpdateXml(node, context, incoming), Throws.ArgumentNullException);
            }
            else
            {
                Assert.That(() => UpdateBinary(node, context, incoming), Throws.ArgumentNullException);
            }
            List<IReference> current = GetReferences(node, context);
            AssertReferenceValues(current, xml ? [input[1]] : [input[0], input[1]]);
            if (!xml)
            {
                Assert.That(current[0], Is.SameAs(input[0]));
            }
            Assert.That(node.ChangeMasks,
                Is.EqualTo(xml ? NodeStateChangeMasks.References : NodeStateChangeMasks.None));
            Assert.That(callbacks, Is.Zero);
        }

        [Test]
        public void TruncatedBinaryInputDoesNotApplyDecodedPrefix()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null);
            IReference[] input = CreateReferences(2);
            node.AddReferences([input[0]]);
            IServiceMessageContext messageContext = CreateMessageContext(context);
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, messageContext, true))
            {
                encoder.WriteInt32(null, 2);
                encoder.WriteNodeId(null, input[1].ReferenceTypeId);
                encoder.WriteBoolean(null, input[1].IsInverse);
                encoder.WriteExpandedNodeId(null, input[1].TargetId);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, messageContext, true);
            ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                () => node.UpdateReferences(context, decoder));
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            AssertSameReferences(GetReferences(node, context), [input[0]]);
        }

        [Test]
        public void HierarchyExportUsesCurrentReferenceValuesAndPreservesRecursiveOrder()
        {
            SystemContext context = CreateContext();
            var root = new BaseObjectState(null);
            var child = new BaseObjectState(root) { NodeId = new NodeId(70u, 1), SymbolicName = "Child" };
            root.AddChild(child);
            var local = new ReferenceNode(ReferenceTypeIds.Organizes, false, new NodeId(71u, 1));
            var remote = new NodeStateReference(ReferenceTypeIds.HasProperty, true,
                new ExpandedNodeId(new NodeId(72u), "urn:unmapped", 1));
            root.AddReferences([remote, local]);
            child.AddReferences([local]);
            local.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            local.IsInverse = true;
            local.TargetId = child.NodeId;
            var hierarchy = new Dictionary<NodeId, string> { [child.NodeId] = "/Child" };
            var references = new List<NodeStateHierarchyReference>();
            root.GetHierarchyReferences(context, "Root", hierarchy, references);
            Assert.That(references, Has.Count.EqualTo(3));
            string[] sourcePaths = ["Root", "Root", "Root/Child"];
            Assert.That(references.Select(reference => reference.SourcePath), Is.EqualTo(sourcePaths));
            Assert.That(references[0].TargetId, Is.EqualTo(remote.TargetId));
            Assert.That(references[0].TargetPath, Is.Null);
            Assert.That(references[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
            foreach (NodeStateHierarchyReference reference in references.Skip(1))
            {
                Assert.That(reference.TargetId.IsNull, Is.True);
                Assert.That(reference.TargetPath, Is.EqualTo("/Child"));
                Assert.That(reference.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(reference.IsInverse, Is.True);
            }
            AssertSameReferences(GetReferences(root, context), [remote, local]);
            AssertSameReferences(GetReferences(child, context), [local]);
        }

        private static void UpdateBinary(NodeState node, SystemContext context, IReference[] references)
        {
            IServiceMessageContext messageContext = CreateMessageContext(context);
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, messageContext, true))
            {
                encoder.WriteInt32(null, references.Length);
                foreach (IReference reference in references)
                {
                    encoder.WriteNodeId(null, reference.ReferenceTypeId);
                    encoder.WriteBoolean(null, reference.IsInverse);
                    encoder.WriteExpandedNodeId(null, reference.TargetId);
                }
            }
            using var input = new MemoryStream(output.ToArray());
            using var decoder = new BinaryDecoder(input, messageContext, true);
            node.UpdateReferences(context, decoder);
        }

        private static void UpdateXml(NodeState node, SystemContext context, IReference[] references)
        {
            IServiceMessageContext messageContext = CreateMessageContext(context);
            using var encoder = new XmlEncoder(messageContext);
            encoder.Push("Root", Namespaces.OpcUaXsd);
            if (references.Length != 0)
            {
                encoder.Push("References", Namespaces.OpcUaXsd);
                foreach (IReference reference in references)
                {
                    encoder.Push("Reference", Namespaces.OpcUaXsd);
                    encoder.WriteNodeId("ReferenceTypeId", reference.ReferenceTypeId);
                    encoder.WriteBoolean("IsInverse", reference.IsInverse);
                    encoder.WriteExpandedNodeId("TargetId", reference.TargetId);
                    encoder.Pop();
                }
                encoder.Pop();
            }
            encoder.Pop();
            using var text = new StringReader(encoder.CloseAndReturnText()!);
            using var reader = XmlReader.Create(text, CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(null, reader, messageContext);
            node.UpdateReferences(context, decoder);
        }

        private static ServiceMessageContext CreateMessageContext(SystemContext context)
        {
            var messageContext = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            return messageContext;
        }

        private static async Task AssertCompletesAsync(Task task)
        {
            // Timeout is only a deadlock watchdog; no assertion depends on execution speed or ordering by delay.
            using var timeout = new CancellationTokenSource();
            var watchdog = Task.Delay(s_timeout, timeout.Token);
            try
            {
                Assert.That(await Task.WhenAny(task, watchdog).ConfigureAwait(false), Is.SameAs(task),
                    "The reference operation did not complete before the deadlock watchdog expired.");
                await task.ConfigureAwait(false);
            }
            finally
            {
                timeout.Cancel();
            }
        }

        private static void AssertReferenceValues(
            IEnumerable<IReference> actual,
            IEnumerable<IReference> expected)
        {
            IReference[] actualReferences = [.. actual];
            IReference[] expectedReferences = [.. expected];
            Assert.That(actualReferences, Has.Length.EqualTo(expectedReferences.Length));
            for (int index = 0; index < actualReferences.Length; index++)
            {
                Assert.That(actualReferences[index].ReferenceTypeId,
                    Is.EqualTo(expectedReferences[index].ReferenceTypeId));
                Assert.That(actualReferences[index].IsInverse, Is.EqualTo(expectedReferences[index].IsInverse));
                Assert.That(actualReferences[index].TargetId, Is.EqualTo(expectedReferences[index].TargetId));
            }
        }

        private static void AssertNodeQueries(
            NodeState node,
            SystemContext context,
            ReferenceDictionary<byte> oracle)
        {
            foreach (BrowseDirection direction in s_directions)
            {
                using INodeBrowser browser = CreateBrowser(node, context, default, false, direction);
                AssertSameReferences(ReadBrowser(browser), oracle.Keys.Where(reference =>
                    direction == BrowseDirection.Both ||
                    reference.IsInverse == (direction == BrowseDirection.Inverse)));
            }
            foreach (NodeId type in s_queryTypes)
            {
                foreach (bool inverse in new[] { false, true })
                {
                    var actual = new List<IReference>();
                    node.GetReferences(context, actual, type, inverse);
                    AssertSameReferences(actual, oracle.Keys.Where(reference =>
                        reference.ReferenceTypeId == type && reference.IsInverse == inverse));
                }
                foreach (bool subtypes in new[] { false, true })
                {
                    foreach (BrowseDirection direction in s_directions)
                    {
                        var expected = new List<IReference>();
                        if (direction != BrowseDirection.Inverse)
                        {
                            expected.AddRange(subtypes
                                ? oracle.Find(type, false, context.TypeTable) : oracle.Find(type, false));
                        }
                        if (direction != BrowseDirection.Forward)
                        {
                            expected.AddRange(subtypes
                                ? oracle.Find(type, true, context.TypeTable) : oracle.Find(type, true));
                        }
                        using INodeBrowser browser = CreateBrowser(node, context, type, subtypes, direction);
                        AssertSameReferences(ReadBrowser(browser), expected);
                    }
                }
            }
        }

        private static SystemContext CreateContext()
        {
            var types = new Mock<ITypeTable>();
            types.Setup(table => table.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>()))
                .Returns((NodeId type, NodeId parent) => type == parent ||
                    (parent == ReferenceTypeIds.Aggregates &&
                        (type == ReferenceTypeIds.HasComponent || type == ReferenceTypeIds.HasProperty)));
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable(),
                TypeTable = types.Object
            };
            context.NamespaceUris.Append("urn:reference-storage:local");
            context.NamespaceUris.Append("urn:reference-storage:remote");
            return context;
        }

        private static IReference[] CreateReferences(int count)
        {
            var references = new IReference[count];
            for (int index = 0; index < count; index++)
            {
                uint id = (uint)(1000 + index);
                ExpandedNodeId target = (index % 8) switch
                {
                    0 => new ExpandedNodeId(new NodeId(id, 1)),
                    1 => new ExpandedNodeId(new NodeId("Target" + index, 1)),
                    2 => new ExpandedNodeId(new NodeId(new Guid(index + 1, 2, 3, new byte[8]), 1)),
                    3 => new ExpandedNodeId(new NodeId(ByteString.From(BitConverter.GetBytes(index + 1)), 1)),
                    4 => new ExpandedNodeId(new NodeId(id), "urn:reference-storage:remote"),
                    5 => new ExpandedNodeId(new NodeId("Remote" + index, 0), "urn:reference-storage:remote", 1),
                    6 => new ExpandedNodeId(new NodeId(id, 1), null, 1),
                    _ => new ExpandedNodeId(new NodeId("Other" + index, 0), "urn:reference-storage:remote", 1)
                };
                references[index] = new NodeStateReference(
                    s_referenceTypes[index % s_referenceTypes.Length],
                    (index + (index / 8)) % 2 != 0,
                    target);
            }
            return references;
        }

        private static NodeStateReference Duplicate(IReference reference)
        {
            return new NodeStateReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
        }

        private static List<IReference> GetReferences(NodeState node, ISystemContext context)
        {
            var references = new List<IReference>();
            node.GetReferences(context, references);
            return references;
        }

        private static INodeBrowser CreateBrowser(
            NodeState node,
            ISystemContext context,
            NodeId type,
            bool subtypes,
            BrowseDirection direction = BrowseDirection.Both,
            IEnumerable<IReference>? additional = null)
        {
            return node.CreateBrowser(context, null, type, subtypes, direction, default, additional, false);
        }

        private static List<IReference> ReadBrowser(INodeBrowser browser)
        {
            var references = new List<IReference>();
            IReference? reference;
            while ((reference = browser.Next()) != null)
            {
                references.Add(reference);
            }
            Assert.That(browser.Next(), Is.Null);
            return references;
        }

        private static void AssertSameReferences(IEnumerable<IReference> actual, IEnumerable<IReference> expected)
        {
            IReference[] actualReferences = [.. actual];
            IReference[] expectedReferences = [.. expected];
            Assert.That(actualReferences, Has.Length.EqualTo(expectedReferences.Length));
            for (int index = 0; index < actualReferences.Length; index++)
            {
                Assert.That(actualReferences[index], Is.SameAs(expectedReferences[index]), $"Reference {index}");
            }
        }

        private sealed class InitializableObject : BaseObjectState
        {
            public InitializableObject()
                : base(null)
            {
            }

            public void InitializeFrom(ISystemContext context, NodeState source)
            {
                Initialize(context, source);
            }
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        private static readonly FieldInfo s_referencesField = typeof(NodeState).GetField(
            "m_references", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(NodeState).FullName, "m_references");

        private static readonly NodeId[] s_referenceTypes =
        [
            ReferenceTypeIds.HasComponent, ReferenceTypeIds.Organizes, ReferenceTypeIds.HasProperty
        ];

        private static readonly NodeId[] s_queryTypes =
        [
            ReferenceTypeIds.HasComponent, ReferenceTypeIds.Organizes, ReferenceTypeIds.HasProperty,
            ReferenceTypeIds.Aggregates, ReferenceTypeIds.HasSubtype
        ];

        private static readonly BrowseDirection[] s_directions =
        [
            BrowseDirection.Forward, BrowseDirection.Inverse, BrowseDirection.Both
        ];
    }
}
