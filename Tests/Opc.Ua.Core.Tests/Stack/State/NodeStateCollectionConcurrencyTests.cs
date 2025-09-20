using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
        [CancelAfter(10000)]
        public void NodeStateReferencesCollectionConcurrencyTest(
            CancellationToken cancellationToken)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var testNodeState = new AnalogUnitRangeState(null);
            var serviceMessageContext = new ServiceMessageContext(telemetry);

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
                Assert.IsTrue(removeReferenceSuccess);
            }

            task.Wait(cancellationToken);

            references.Clear();
            testNodeState.GetReferences(systemContext, references);

            Assert.AreEqual(originalReferenceCount, references.Count);
        }

        [Test]
        [CancelAfter(10000)]
        public void NodeStateNotifiersCollectionConcurrencyTest(CancellationToken cancellationToken)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serviceMessageContext = new ServiceMessageContext(telemetry);
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

            Assert.AreEqual(originalNotifierCount, notifiers.Count);
        }

        [Test]
        [CancelAfter(10000)]
        public void NodeStateChildrenCollectionConcurrencyTest(CancellationToken cancellationToken)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serviceMessageContext = new ServiceMessageContext(telemetry);
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

            Assert.AreEqual(originalNotifierCount, children.Count);
        }
    }
}
