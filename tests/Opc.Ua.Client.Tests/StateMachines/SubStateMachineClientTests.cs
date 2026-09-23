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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Tests;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

// Local fake IStreamingSubscription test types are no-op IAsyncDisposable
// instances with nothing to dispose; CA2000's leak warning does not apply.
#pragma warning disable CA2000

namespace Opc.Ua.Client.Tests.StateMachines
{
    /// <summary>
    /// Null-guard + argument-validation tests for the sub-state-
    /// machine client extensions on
    /// <see cref="FiniteStateMachineTypeClient"/>:
    /// <c>GetSubStateMachineAsync</c> and
    /// <c>ObserveEffectiveStateAsync</c>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class SubStateMachineClientTests
    {
        private static FiniteStateMachineTypeClient CreateClient(Mock<ISessionClient> sessionMock)
        {
            sessionMock.SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            return new FiniteStateMachineTypeClient(
                sessionMock.Object,
                new NodeId(11u, 2),
                NUnitTelemetryContext.Create());
        }

        [Test]
        public void GetSubStateMachineAsyncWithNullClientThrowsArgumentNullException()
        {
            ITelemetryContext tel = NUnitTelemetryContext.Create();
            Assert.That(
                async () => await FiniteStateMachineTypeClientExtensions
                    .GetSubStateMachineAsync(null!, new NodeId(5u, 0), tel)
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void GetSubStateMachineAsyncWithNullTelemetryThrowsArgumentNullException()
        {
            FiniteStateMachineTypeClient client = CreateClient(
                new Mock<ISessionClient>(MockBehavior.Loose));
            Assert.That(
                async () => await client
                    .GetSubStateMachineAsync(new NodeId(5u, 0), null!)
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void GetSubStateMachineAsyncWithNullParentStateIdThrowsArgumentException()
        {
            FiniteStateMachineTypeClient client = CreateClient(
                new Mock<ISessionClient>(MockBehavior.Loose));
            ITelemetryContext tel = NUnitTelemetryContext.Create();
            Assert.That(
                async () => await client
                    .GetSubStateMachineAsync(NodeId.Null, tel)
                    .ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public void ObserveEffectiveStateAsyncWithNullClientThrowsArgumentNullException()
        {
            ITelemetryContext tel = NUnitTelemetryContext.Create();
            var streaming = new EmptyStreamingSubscription();
            Assert.That(
                () => FiniteStateMachineTypeClientExtensions
                    .ObserveEffectiveStateAsync(null!, streaming, tel),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ObserveEffectiveStateAsyncWithNullStreamingThrowsArgumentNullException()
        {
            FiniteStateMachineTypeClient client = CreateClient(
                new Mock<ISessionClient>(MockBehavior.Loose));
            ITelemetryContext tel = NUnitTelemetryContext.Create();
            Assert.That(
                () => client.ObserveEffectiveStateAsync(null!, tel),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ObserveEffectiveStateAsyncWithNullTelemetryThrowsArgumentNullException()
        {
            FiniteStateMachineTypeClient client = CreateClient(
                new Mock<ISessionClient>(MockBehavior.Loose));
            var streaming = new EmptyStreamingSubscription();
            Assert.That(
                () => client.ObserveEffectiveStateAsync(streaming, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void FiniteStateSnapshotSubMachinePropertyDefaultsToNull()
        {
            var snap = new FiniteStateSnapshot(
                new NodeId(1u, 0),
                LocalizedText.Null,
                NodeId.Null,
                LocalizedText.Null,
                NodeId.Null,
                DateTime.UtcNow,
                StatusCodes.Good);

            Assert.That(snap.SubMachine, Is.Null);
        }

        [Test]
        public void FiniteStateSnapshotSubMachineSetterPropagatesViaWith()
        {
            var inner = new FiniteStateSnapshot(
                new NodeId(2u, 0),
                LocalizedText.Null,
                NodeId.Null,
                LocalizedText.Null,
                NodeId.Null,
                DateTime.UtcNow,
                StatusCodes.Good);
            var outer = new FiniteStateSnapshot(
                new NodeId(1u, 0),
                LocalizedText.Null,
                NodeId.Null,
                LocalizedText.Null,
                NodeId.Null,
                DateTime.UtcNow,
                StatusCodes.Good)
            { SubMachine = inner };

            Assert.That(outer.SubMachine, Is.SameAs(inner));
        }

        [Test]
        public async Task ObserveEffectiveStateAsyncYieldsNothingWhenNoStatesAndNoTransitionResolution()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);
            ITelemetryContext tel = NUnitTelemetryContext.Create();

            // AvailableStates and the parent CurrentState/Id paths do not
            // resolve, so no sub-state machines or transitions are observed.
            SetupTranslateAllEmpty(sessionMock);

            int yielded = 0;
            await foreach (FiniteStateSnapshot _ in client
                .ObserveEffectiveStateAsync(new EmptyStreamingSubscription(), tel)
                .ConfigureAwait(false))
            {
                yielded++;
            }
            Assert.That(yielded, Is.Zero);
        }

        [Test]
        public void ObserveEffectiveStateAsyncRespectsCancellationDuringDiscovery()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);
            ITelemetryContext tel = NUnitTelemetryContext.Create();

            // Make TranslateBrowsePaths honour cancellation before
            // AvailableStates discovery can complete.
            sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = default,
                                DiagnosticInfos = default
                            });
                    });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () =>
                {
                    await foreach (FiniteStateSnapshot _ in client
                        .ObserveEffectiveStateAsync(
                            new EmptyStreamingSubscription(), tel, options: null, ct: cts.Token)
                        .ConfigureAwait(false))
                    {
                    }
                },
                Throws.InstanceOf<OperationCanceledException>());
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ObserveEffectiveStatePropagatesPumpFaultAndStopsSiblingAsync(bool parentFails)
        {
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            FiniteStateMachineTypeClient client = CreateClient(session);
            var childMachine = new NodeId(12u, 2);
            var parentState = new NodeId(20u, 2);
            var childState = new NodeId(21u, 2);
            var availableStates = new NodeId(30u, 2);
            var parentValue = new NodeId(31u, 2);
            var parentId = new NodeId(32u, 2);
            var childValue = new NodeId(41u, 2);
            var childId = new NodeId(42u, 2);
            var failure = new ServiceResultException(StatusCodes.BadRequestTimeout);
            bool failRead = false;
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    null, It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                {
                    var results = new List<BrowsePathResult>();
                    foreach (BrowsePath path in paths)
                    {
                        string name = path.RelativePath.Elements[0].TargetName.Name!;
                        NodeId target = NodeId.Null;
                        if (name == BrowseNames.AvailableStates)
                        {
                            target = availableStates;
                        }
                        else if (name == BrowseNames.CurrentState)
                        {
                            bool parent = path.StartingNode == client.ObjectId;
                            target = path.RelativePath.Elements.Count == 2
                                ? (parent ? parentId : childId)
                                : (parent ? parentValue : childValue);
                        }
                        results.Add(target.IsNull
                            ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                            : new BrowsePathResult
                            {
                                Targets = [new BrowsePathTarget { TargetId = target }]
                            });
                    }
                    return new TranslateBrowsePathsToNodeIdsResponse { Results = results };
                });
            session.Setup(value => value.ReadAsync(
                    null, 0, It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, double _, TimestampsToReturn _,
                    ArrayOf<ReadValueId> reads, CancellationToken _) =>
                {
                    if (failRead && reads[0].NodeId == (parentFails ? parentValue : childValue))
                    {
                        throw failure;
                    }
                    var values = new List<DataValue>();
                    foreach (ReadValueId read in reads)
                    {
                        Variant value;
                        if (read.NodeId == availableStates)
                        {
                            ArrayOf<NodeId> states = [parentState];
                            value = Variant.From(states);
                        }
                        else if (read.NodeId == parentState)
                        {
                            value = Variant.From(new QualifiedName("Active", 2));
                        }
                        else if (read.NodeId == parentId || read.NodeId == childId)
                        {
                            value = Variant.From(read.NodeId == parentId ? parentState : childState);
                        }
                        else
                        {
                            value = Variant.From(new LocalizedText(read.NodeId == parentValue ? "Active" : "Idle"));
                        }
                        values.Add(new DataValue(value));
                    }
                    return new ReadResponse { Results = values };
                });
            session.Setup(value => value.BrowseAsync(
                    null, null, 0,
                    It.Is<ArrayOf<BrowseDescription>>(nodes => nodes.Count == 1 && nodes[0].NodeId == parentState),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [new BrowseResult
                    {
                        References = [new ReferenceDescription { NodeId = childMachine }]
                    }]
                });
            var parentChanges = Channel.CreateUnbounded<DataValueChange>();
            var childChanges = Channel.CreateUnbounded<DataValueChange>();
            var parentReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var childReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var parentStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var childStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var streaming = new Mock<IStreamingSubscription>();
            streaming.Setup(value => value.SubscribeDataChangesAsync(
                    parentId, It.IsAny<MonitoringOptions>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId _, MonitoringOptions _, CancellationToken ct) =>
                    ObserveChangesAsync(parentChanges.Reader, parentReady, parentStopped, ct));
            streaming.Setup(value => value.SubscribeDataChangesAsync(
                    childId, It.IsAny<MonitoringOptions>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId _, MonitoringOptions _, CancellationToken ct) =>
                    ObserveChangesAsync(childChanges.Reader, childReady, childStopped, ct));
            using var cancellation = new CancellationTokenSource();
            await using IAsyncEnumerator<FiniteStateSnapshot> enumerator = client
                .ObserveEffectiveStateAsync(
                    streaming.Object, NUnitTelemetryContext.Create(), ct: cancellation.Token)
                .GetAsyncEnumerator();
            Task<bool> move = enumerator.MoveNextAsync().AsTask();
            try
            {
                await Task.WhenAll(parentReady.Task, childReady.Task)
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                parentChanges.Writer.TryWrite(default);
                Assert.That(await move.ConfigureAwait(false), Is.True);
                Assert.That(enumerator.Current.CurrentStateId, Is.EqualTo(parentState));
                Assert.That(enumerator.Current.SubMachine, Is.Null);
                move = enumerator.MoveNextAsync().AsTask();
                childChanges.Writer.TryWrite(default);
                Assert.That(await move.ConfigureAwait(false), Is.True);
                Assert.That(enumerator.Current.SubMachine!.CurrentStateId, Is.EqualTo(childState));

                failRead = true;
                move = enumerator.MoveNextAsync().AsTask();
                (parentFails ? parentChanges : childChanges).Writer.TryWrite(default);
                ServiceResultException actual = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await move.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))!;

                Assert.That(actual, Is.SameAs(failure));
                Assert.That(await (parentFails ? childStopped : parentStopped).Task.ConfigureAwait(false), Is.True);
                Assert.That(parentStopped.Task.IsCompleted, Is.True);
                Assert.That(childStopped.Task.IsCompleted, Is.True);
            }
            finally
            {
                cancellation.Cancel();
                parentChanges.Writer.TryComplete();
                childChanges.Writer.TryComplete();
                try
                {
                    await move.ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Observe the failed move before disposing the enumerator, including on the red path.
                }
                catch (OperationCanceledException)
                {
                    // The cleanup cancellation also releases a reader when an assertion fails.
                }
            }
        }

        private static async IAsyncEnumerable<DataValueChange> ObserveChangesAsync(
            ChannelReader<DataValueChange> reader,
            TaskCompletionSource<bool> ready,
            TaskCompletionSource<bool> stopped,
            [EnumeratorCancellation] CancellationToken ct)
        {
            ready.TrySetResult(true);
            try
            {
                await foreach (DataValueChange change in reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    yield return change;
                }
            }
            finally
            {
                stopped.TrySetResult(ct.IsCancellationRequested);
            }
        }

        private static void SetupTranslateAllEmpty(Mock<ISessionClient> sessionMock)
        {
            sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        var results = new BrowsePathResult[requests.Count];
                        for (int i = 0; i < results.Length; i++)
                        {
                            results[i] = new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = default
                            };
                        }
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = ArrayOf.Wrapped(results),
                                DiagnosticInfos = default
                            });
                    });
        }

        private sealed class EmptyStreamingSubscription : IStreamingSubscription
        {
            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                NodeId nodeId,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                IReadOnlyList<NodeId> nodeIds,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
                NodeId notifierId,
                EventFilter filter,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
