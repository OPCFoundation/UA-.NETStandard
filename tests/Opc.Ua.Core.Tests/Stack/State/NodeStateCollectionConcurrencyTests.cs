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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for concurrency issues in BaseVariableState class
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Category("NodeStateConcurrency")]
    [Parallelizable(ParallelScope.All)]
    public class NodeStateCollectionConcurrencyTests
    {
        [Test]
        public async Task AtomicReferenceMutationPublishesExactlyOneWinnerPerKeyAsync()
        {
            var node = new BaseObjectState(null);
            const int k_count = 128;
            const int k_workers = 8;
            int addedCallbacks = 0;
            int removedCallbacks = 0;
            node.OnReferenceAdded = (_, _, _, _) => Interlocked.Increment(ref addedCallbacks);
            node.OnReferenceRemoved = (_, _, _, _) => Interlocked.Increment(ref removedCallbacks);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workers = new Task[k_workers];
            for (int worker = 0; worker < workers.Length; worker++)
            {
                workers[worker] = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    for (uint index = 1; index <= k_count; index++)
                    {
                        node.AddReferenceIfMissing(ReferenceTypeIds.HasComponent, false, new NodeId(index, 1));
                        Assert.That(node.ReferenceExists(
                            ReferenceTypeIds.HasComponent, false, new NodeId(index, 1)), Is.True);
                    }
                });
            }
            start.SetResult(true);
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var snapshot = new List<IReference>();
            node.GetReferences(null, snapshot);
            Assert.That(snapshot, Has.Count.EqualTo(k_count));
            Assert.That(addedCallbacks, Is.EqualTo(k_count));
            for (int worker = 0; worker < workers.Length; worker++)
            {
                workers[worker] = Task.Run(() =>
                {
                    for (uint index = 1; index <= k_count; index++)
                    {
                        node.RemoveReference(ReferenceTypeIds.HasComponent, false, new NodeId(index, 1));
                    }
                });
            }
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var current = new List<IReference>();
            node.GetReferences(null, current);
            Assert.That(current, Is.Empty);
            Assert.That(removedCallbacks, Is.EqualTo(k_count));
            Assert.That(snapshot, Has.Count.EqualTo(k_count));
            var targets = new HashSet<ExpandedNodeId>();
            foreach (IReference reference in snapshot)
            {
                Assert.That(reference.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(reference.IsInverse, Is.False);
                Assert.That(targets.Add(reference.TargetId), Is.True);
            }
            for (uint index = 1; index <= k_count; index++)
            {
                Assert.That(targets, Does.Contain(new ExpandedNodeId(new NodeId(index, 1))));
            }
        }

        [Test]
        [CancelAfter(10000)]
        public void NodeStateReferencesCollectionConcurrencyTest(
            CancellationToken cancellationToken)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var testNodeState = new AnalogUnitRangeState(null);
            var serviceMessageContext = ServiceMessageContext.Create(telemetry);

            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            testNodeState.Create(
                new SystemContext(telemetry) { NamespaceUris = serviceMessageContext.NamespaceUris },
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
                new LocalizedText("TestNode"),
                true);

            var references = new List<IReference>();

            testNodeState.GetReferences(systemContext, references);

            int originalReferenceCount = references.Count;

            uint index = 0;
            var referenceTargets = new BlockingCollection<ExpandedNodeId>();

            var task = Task.Run(
                () =>
                {
                    DateTime utcNow = DateTime.UtcNow;

                    while (DateTime.UtcNow - utcNow < TimeSpan.FromSeconds(3))
                    {
                        var target = new ExpandedNodeId(index++, "test.namespace");
                        testNodeState.AddReference(ReferenceTypeIds.HasComponent, false, target);
                        referenceTargets.Add(target);
                    }

                    referenceTargets.CompleteAdding();
                },
                cancellationToken);

            foreach (ExpandedNodeId target in referenceTargets.GetConsumingEnumerable(
                cancellationToken))
            {
                bool removeReferenceSuccess = testNodeState.RemoveReference(
                    ReferenceTypeIds.HasComponent,
                    false,
                    target);
                Assert.That(removeReferenceSuccess, Is.True);
            }

            task.Wait(cancellationToken);

            references.Clear();
            testNodeState.GetReferences(systemContext, references);

            Assert.That(references, Has.Count.EqualTo(originalReferenceCount));
        }

        [Test]
        [CancelAfter(10000)]
        public void NodeStateNotifiersCollectionConcurrencyTest(CancellationToken cancellationToken)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serviceMessageContext = ServiceMessageContext.Create(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            var testNodeState = new BaseObjectState(null);

            testNodeState.Create(
                new SystemContext(telemetry) { NamespaceUris = serviceMessageContext.NamespaceUris },
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
                new LocalizedText("TestNode"),
                true);

            var notifiers = new List<NodeState.Notifier>();

            testNodeState.GetNotifiers(systemContext, notifiers);

            int originalNotifierCount = notifiers.Count;

            uint index = 0;
            var notifierTargets = new BlockingCollection<NodeState>();

            var task = Task.Run(
                () =>
                {
                    DateTime utcNow = DateTime.UtcNow;

                    while (DateTime.UtcNow - utcNow < TimeSpan.FromSeconds(3))
                    {
                        var targetState = new AnalogUnitRangeState(null);

                        testNodeState.Create(
                            new SystemContext(telemetry)
                            {
                                NamespaceUris = serviceMessageContext.NamespaceUris
                            },
                            new NodeId("TestNode", 7),
                            new QualifiedName("TestNode", 7),
                            new LocalizedText("TestNode"),
                            true);

                        var target = new ExpandedNodeId(index++, "test.namespace");
                        testNodeState.AddNotifier(
                            systemContext,
                            ReferenceTypeIds.HasEventSource,
                            false,
                            targetState);
                        notifierTargets.Add(targetState);
                    }

                    notifierTargets.CompleteAdding();
                },
                cancellationToken);

            foreach (NodeState target in notifierTargets.GetConsumingEnumerable(cancellationToken))
            {
                testNodeState.RemoveNotifier(systemContext, target, false);
            }

            task.Wait(cancellationToken);

            notifiers.Clear();
            testNodeState.GetNotifiers(systemContext, notifiers);

            Assert.That(notifiers, Has.Count.EqualTo(originalNotifierCount));
        }

        [Test]
        [CancelAfter(10000)]
        public void NodeStateChildrenCollectionConcurrencyTest(CancellationToken cancellationToken)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serviceMessageContext = ServiceMessageContext.Create(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            var testNodeState = new BaseObjectState(null);

            testNodeState.Create(
                new SystemContext(telemetry) { NamespaceUris = serviceMessageContext.NamespaceUris },
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
                new LocalizedText("TestNode"),
                true);

            var children = new List<BaseInstanceState>();

            testNodeState.GetChildren(systemContext, children);

            int originalNotifierCount = children.Count;

            uint index = 0;
            var childrenCollection = new BlockingCollection<BaseInstanceState>();

            var task = Task.Run(
                () =>
                {
                    DateTime utcNow = DateTime.UtcNow;

                    while (DateTime.UtcNow - utcNow < TimeSpan.FromSeconds(3))
                    {
                        var targetState = new AnalogUnitRangeState(null);

                        testNodeState.Create(
                            new SystemContext(telemetry)
                            {
                                NamespaceUris = serviceMessageContext.NamespaceUris
                            },
                            new NodeId("TestNode", 7),
                            new QualifiedName("TestNode", 7),
                            new LocalizedText("TestNode"),
                            true);

                        var target = new ExpandedNodeId(index++, "test.namespace");
                        testNodeState.AddChild(targetState);
                        childrenCollection.Add(targetState);
                    }

                    childrenCollection.CompleteAdding();
                },
                cancellationToken);

            foreach (BaseInstanceState child in childrenCollection.GetConsumingEnumerable(
                cancellationToken))
            {
                testNodeState.RemoveChild(child);
            }

            task.Wait(cancellationToken);

            children.Clear();
            testNodeState.GetChildren(systemContext, children);

            Assert.That(children, Has.Count.EqualTo(originalNotifierCount));
        }
    }
}
