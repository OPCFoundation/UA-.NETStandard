/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Extrusion.Client;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Extrusion.Tests
{
    public sealed partial class ExtrusionClientWorkflowTests
    {
        [TestCase("Known")]
        [TestCase("Derived")]
        [TestCase("Unknown")]
        [TestCase("MissingType")]
        [TestCase("WrongType")]
        [TestCase("NullId")]
        [TestCase("EmptyFields")]
        public async Task ExtrusionEventsResolveSupertypesOrRetainUnknownPayloadsAsync(string shape)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId known = peer.Type(Cutter.ObjectTypeIds.CutEventType);
            NodeId actual = shape == "Known" ? known : new NodeId("VendorCut", 2);
            Variant typeField = shape switch
            {
                "MissingType" => Variant.Null,
                "WrongType" => Variant.From("not a NodeId"),
                "NullId" => Variant.From(NodeId.Null),
                _ => Variant.From(actual)
            };
            if (shape == "Derived")
            {
                var parent = new NodeId("VendorCutBase", 2);
                peer.ExpectSuperType(actual, parent);
                peer.ExpectSuperType(parent, known);
            }
            else if (shape == "Unknown")
            {
                peer.ExpectSuperType(actual, NodeId.Null);
            }
            var stream = new EventStreamPeer(peer, typeField, emptyFields: shape == "EmptyFields");
            List<ExtrusionEvent> values = await ExtrusionClientPeer.CollectAsync(
                peer.Client.ObserveEventsAsync(ExtrusionClientPeer.DeviceId, stream.Mock.Object,
                    cancellationToken: cancellation.Token)).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(1));
            AssertOriginalFields(values[0], stream);
            if (shape is "Known" or "Derived")
            {
                AssertCut(values[0], actual);
            }
            else
            {
                Assert.That(values[0].Record, Is.Null);
            }
            Assert.That(stream.Produced, Is.EqualTo(1));
            Assert.That(stream.Disposed, Is.EqualTo(1));
            stream.Verify();
            peer.Verify();
        }

        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(63, false)]
        [TestCase(64, false)]
        [TestCase(65, false)]
        public async Task ExtrusionEventTypeWalkStopsAtCyclesAndTheSixtyFourTypeBoundAsync(int length, bool cycle)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId known = peer.Type(Cutter.ObjectTypeIds.CutEventType);
            var first = new NodeId("VendorCut/0", 2);
            var queried = new List<NodeId>();
            for (int index = 0; index < Math.Min(length, 64); index++)
            {
                var current = new NodeId("VendorCut/" + index, 2);
                NodeId next = index == length - 1 ? cycle ? first : known : new NodeId("VendorCut/" + (index + 1), 2);
                peer.ExpectSuperType(current, next);
                queried.Add(current);
            }
            var stream = new EventStreamPeer(peer, Variant.From(first));
            List<ExtrusionEvent> values = await ExtrusionClientPeer.CollectAsync(
                peer.Client.ObserveEventsAsync(ExtrusionClientPeer.DeviceId, stream.Mock.Object,
                    cancellationToken: cancellation.Token)).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(1));
            if (!cycle && length <= 64)
            {
                AssertCut(values[0], first);
            }
            else
            {
                Assert.That(values[0].Record, Is.Null);
            }
            AssertOriginalFields(values[0], stream);
            Assert.That(peer.Cache.Invocations.Select(call => (NodeId)call.Arguments[0]!), Is.EqualTo(queried));
            Assert.That(stream.Produced, Is.EqualTo(1));
            Assert.That(stream.Disposed, Is.EqualTo(1));
            stream.Verify();
            peer.Verify();
        }

        [TestCase("Complete")]
        [TestCase("Break")]
        [TestCase("Empty")]
        [TestCase("Failure")]
        [TestCase("Cancel")]
        [TestCase("PreCancelled")]
        [TestCase("SupertypeFailure")]
        public async Task ExtrusionEventWorkflowsPreserveOptionsFailuresAndCancellationAsync(string ending)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId actual = ending == "SupertypeFailure" ? new NodeId("VendorCut",
                2) : peer.Type(Cutter.ObjectTypeIds.CutEventType);
            var options = new MonitoredItemOptions { QueueSize = 11, DiscardOldest = false };
            var streamFailure = new InvalidOperationException("The caller's event stream failed.");
            var cacheFailure = new ServiceResultException(StatusCodes.BadCommunicationError);
            if (ending == "SupertypeFailure")
            {
                peer.ExpectSuperType(actual, NodeId.Null, cacheFailure);
            }
            var stream = new EventStreamPeer(peer, Variant.From(actual), ending == "Empty" ? 0 : 2, options,
                ending == "Failure" ? streamFailure : null);
            if (ending == "PreCancelled")
            {
                cancellation.Cancel();
            }
            await using (IAsyncEnumerator<ExtrusionEvent> iterator = peer.Client.ObserveEventsAsync(
                ExtrusionClientPeer.DeviceId, stream.Mock.Object, options, cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token))
            {
                if (ending == "PreCancelled")
                {
                    OperationCanceledException? failure = await Assert.ThrowsAsync<OperationCanceledException>(
                        async () => await iterator.MoveNextAsync().ConfigureAwait(false));
                    Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
                }
                else if (ending == "SupertypeFailure")
                {
                    Assert.That(await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await iterator.MoveNextAsync().ConfigureAwait(false)), Is.SameAs(cacheFailure));
                    Assert.That(cacheFailure.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
                }
                else if (ending == "Empty")
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
                }
                else
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                    AssertCut(iterator.Current, actual);
                    AssertOriginalFields(iterator.Current, stream);
                    switch (ending)
                    {
                        case "Complete":
                            Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                            AssertCut(iterator.Current, actual);
                            Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
                            break;
                        case "Failure":
                            Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(
                                async () => await iterator.MoveNextAsync().ConfigureAwait(false)),
                                    Is.SameAs(streamFailure));
                            break;
                        case "Cancel":
                            cancellation.Cancel();
                            OperationCanceledException? failure = await Assert.ThrowsAsync<OperationCanceledException>(
                                async () => await iterator.MoveNextAsync().ConfigureAwait(false));
                            Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
                            break;
                        case "Break":
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(ending));
                    }
                }
            }
            Assert.That(stream.Produced, Is.EqualTo(ending == "Complete" ? 2 : ending is "Empty" or "PreCancelled"
                ? 0 : 1));
            Assert.That(stream.Disposed, Is.EqualTo(1));
            stream.Verify();
            peer.Verify();
        }

        [TestCase("NullNotifier")]
        [TestCase("NoStreaming")]
        public async Task ExtrusionEventWorkflowsRejectInvalidNotifierAndAbsentStreamingAsync(string invalid)
        {
            var peer = new ExtrusionClientPeer();
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            await using (IAsyncEnumerator<ExtrusionEvent> iterator = peer.Client.ObserveEventsAsync(
                invalid == "NullNotifier" ? NodeId.Null : ExtrusionClientPeer.DeviceId,
                invalid == "NoStreaming" ? null : stream.Object).GetAsyncEnumerator())
            {
                if (invalid == "NullNotifier")
                {
                    ArgumentNullException? failure = await Assert.ThrowsAsync<ArgumentNullException>(
                        async () => await iterator.MoveNextAsync().ConfigureAwait(false));
                    Assert.That(failure!.ParamName, Is.EqualTo("node"));
                }
                else
                {
                    InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await iterator.MoveNextAsync().ConfigureAwait(false));
                    Assert.That(failure!.Message, Does.Contain("Supply a streaming subscription"));
                }
            }
            Assert.That(stream.Invocations, Is.Empty);
            peer.VerifyNoServices();
            peer.Verify();
        }

        private static void AssertCut(ExtrusionEvent value, NodeId expectedType)
        {
            Assert.That(value.Record, Is.TypeOf<Cutter.CutEventTypeRecord>());
            var cut = (Cutter.CutEventTypeRecord)value.Record!;
            Assert.Multiple(() =>
            {
                Assert.That(cut.EventType, Is.EqualTo(expectedType));
                Assert.That(cut.EventId.Span.ToArray(), Is.EqualTo(new byte[] { 0x81, 0x23, 0xa7 }));
                Assert.That(cut.SourceNode, Is.EqualTo(new NodeId("cutter/source-18", 1)));
                Assert.That(cut.SourceName, Is.EqualTo("Cell 4 / cutter"));
                Assert.That(cut.Time, Is.EqualTo(ExtrusionClientPeer.SourceTime));
                Assert.That(cut.ReceiveTime, Is.EqualTo(ExtrusionClientPeer.ServerTime));
                Assert.That(cut.Message, Is.EqualTo(new LocalizedText("fr", "Coupe validée")));
                Assert.That(cut.Severity, Is.EqualTo((ushort)625));
                Assert.That(cut.CuttingProductId, Is.EqualTo("Product-9B"));
                Assert.That(cut.ActualOutput, Is.EqualTo(17ul));
                Assert.That(cut.TotalOutput, Is.EqualTo(1007ul));
            });
        }

        private static void AssertOriginalFields(ExtrusionEvent value, EventStreamPeer stream)
        {
            Assert.That(value.Fields, Is.EqualTo(stream.Fields));
            Assert.That(value.FieldPaths, Is.EqualTo(stream.Paths));
            Assert.That(value.FieldPaths.Count, Is.GreaterThan(10),
                "The public filter contains the registered event union.");
        }

        private static Variant[] EventFields(ExtrusionClientPeer peer, EventFilter filter, Variant eventType)
        {
            SimpleAttributeOperand[] clauses = filter.SelectClauses.ToArray()!;
            QualifiedName product = peer.Name(Cutter.Namespaces.ExtrusionCutter, "CuttingProductId");
            QualifiedName output = peer.Name(Cutter.Namespaces.ExtrusionCutter, "ActualOutput");
            QualifiedName total = peer.Name(Cutter.Namespaces.ExtrusionCutter, "TotalOutput");
            Assert.That(clauses.Any(clause => clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == product),
                Is.True);
            Assert.That(clauses.Any(clause => clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == output), Is.True);
            Assert.That(clauses.Any(clause => clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == total), Is.True);
            return clauses.Select(clause =>
            {
                if (clause.BrowsePath.Count != 1)
                {
                    return Variant.Null;
                }
                QualifiedName name = clause.BrowsePath[0];
                if (name == product)
                {
                    return Variant.From("Product-9B");
                }
                if (name == output)
                {
                    return Variant.From(17ul);
                }
                if (name == total)
                {
                    return Variant.From(1007ul);
                }
                if (name.NamespaceIndex != 0)
                {
                    return Variant.Null;
                }
                return name.Name switch
                {
                    Opc.Ua.BrowseNames.EventType => eventType,
                    Opc.Ua.BrowseNames.EventId => Variant.From(ByteString.From(new byte[] { 0x81, 0x23, 0xa7 })),
                    Opc.Ua.BrowseNames.SourceNode => Variant.From(new NodeId("cutter/source-18", 1)),
                    Opc.Ua.BrowseNames.SourceName => Variant.From("Cell 4 / cutter"),
                    Opc.Ua.BrowseNames.Time => Variant.From(ExtrusionClientPeer.SourceTime),
                    Opc.Ua.BrowseNames.ReceiveTime => Variant.From(ExtrusionClientPeer.ServerTime),
                    Opc.Ua.BrowseNames.Message => Variant.From(new LocalizedText("fr", "Coupe validée")),
                    Opc.Ua.BrowseNames.Severity => Variant.From((ushort)625),
                    _ => Variant.Null
                };
            }).ToArray();
        }

        private sealed class EventStreamPeer
        {
            internal EventStreamPeer(
                ExtrusionClientPeer peer, Variant type, int count = 1, MonitoredItemOptions? options = null,
                Exception? failure = null, bool emptyFields = false)
            {
                m_peer = peer;
                m_options = options;
                Mock.Setup(value => value.SubscribeEventsAsync(
                    ExtrusionClientPeer.DeviceId, It.IsAny<EventFilter>(), options, peer.Token))
                    .Returns((NodeId _, EventFilter filter, MonitoredItemOptions? actualOptions,
                        CancellationToken token) =>
                    {
                        Assert.That(actualOptions, Is.SameAs(options));
                        Paths = filter.SelectClauses.ToArray()!.Select(clause => clause.BrowsePath).ToArrayOf();
                        Fields = emptyFields ? [] : EventFields(peer, filter, type);
                        return ExtrusionClientPeer.Notifications(
                            Enumerable.Repeat(new EventNotification(null, Fields), count).ToArray(),
                            () => Produced++, () => Disposed++, failure, token);
                    });
            }

            internal Mock<IStreamingSubscription> Mock { get; } = new(MockBehavior.Strict);
            internal ArrayOf<Variant> Fields { get; private set; }
            internal ArrayOf<ArrayOf<QualifiedName>> Paths { get; private set; }
            internal int Produced { get; private set; }
            internal int Disposed { get; private set; }

            internal void Verify()
            {
                Mock.Verify(value => value.SubscribeEventsAsync(
                    ExtrusionClientPeer.DeviceId, It.IsAny<EventFilter>(), m_options, m_peer.Token), Times.Once);
                Mock.Verify(value => value.DisposeAsync(), Times.Never);
                Mock.VerifyNoOtherCalls();
            }

            private readonly ExtrusionClientPeer m_peer;
            private readonly MonitoredItemOptions? m_options;
        }
    }
}
