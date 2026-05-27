/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Tests;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Tests.Alarms
{
    /// <summary>
    /// Tests for the Part 16 state-machine partial on
    /// <see cref="AlarmClient"/>. Drives the
    /// <c>ShelvedStateMachineTypeClient</c> proxy via a mocked
    /// <see cref="ISessionClient"/> and asserts the wired-up
    /// <c>conditionId</c> propagates as the <c>StartingNode</c> of the
    /// proxy's browse-path requests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Alarms")]
    [Parallelizable]
    public sealed class AlarmClientStateMachineTests
    {
        private Mock<ISessionClient> m_sessionMock = null!;
        private AlarmClient m_client = null!;
        private NodeId m_conditionId;

        [SetUp]
        public void SetUp()
        {
            m_sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            m_sessionMock.SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            m_client = new AlarmClient(m_sessionMock.Object, NUnitTelemetryContext.Create());
            m_conditionId = new NodeId(77u, 4);
        }

        [Test]
        public async Task GetShelvingStateAsyncForwardsConditionIdToShelvedStateMachineTypeClient()
        {
            ArrayOf<BrowsePath> captured = default;
            SetupTranslate(allEmpty: true, capture: p => captured = p);

            FiniteStateSnapshot snapshot = await m_client
                .GetShelvingStateAsync(m_conditionId).ConfigureAwait(false);

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(captured.Count, Is.EqualTo(4));
            foreach (BrowsePath bp in captured)
            {
                Assert.That(bp.StartingNode, Is.EqualTo(m_conditionId));
            }
        }

        [Test]
        public async Task GetShelvingStateAsyncReturnsBadNotFoundWhenNoBrowsePathsResolve()
        {
            SetupTranslate(allEmpty: true, capture: null);

            FiniteStateSnapshot snapshot = await m_client
                .GetShelvingStateAsync(m_conditionId).ConfigureAwait(false);

            Assert.That(snapshot.StateMachineId, Is.EqualTo(m_conditionId));
            Assert.That(snapshot.CurrentState.IsNull, Is.True);
            Assert.That(snapshot.CurrentStateId.IsNull, Is.True);
            Assert.That(snapshot.Status, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetShelvingStateAsyncReturnsParsedLocalizedTextFromRead()
        {
            // Resolve CurrentState only (path 0). The remaining 3 paths
            // return empty targets — the snapshot is built from a single
            // Read of the CurrentState variable.
            var currentStateNodeId = new NodeId(101u, 0);
            SetupTranslate(
                results:
                [
                    MakeResult((ExpandedNodeId)currentStateNodeId),
                    MakeEmptyResult(),
                    MakeEmptyResult(),
                    MakeEmptyResult()
                ],
                capture: null);

            var expected = new LocalizedText("en", "Unshelved");
            m_sessionMock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = ArrayOf.Wrapped(
                    [
                        new DataValue(Variant.From(expected))
                    ]),
                    DiagnosticInfos = []
                }));

            FiniteStateSnapshot snapshot = await m_client
                .GetShelvingStateAsync(m_conditionId).ConfigureAwait(false);

            Assert.That(snapshot.CurrentState, Is.EqualTo(expected));
            Assert.That(snapshot.StateMachineId, Is.EqualTo(m_conditionId));
        }

        [Test]
        public async Task ObserveShelvingTransitionsAsyncReturnsNonNullEnumerableForFakeStream()
        {
            SetupTranslate(allEmpty: true, capture: null);
            var streaming = new EmptyStreamingSubscription();

            IAsyncEnumerable<FiniteStateSnapshot> enumerable =
                m_client.ObserveShelvingTransitionsAsync(m_conditionId, streaming);
            Assert.That(enumerable, Is.Not.Null);

            // When no CurrentState.Id child resolves, the underlying
            // implementation yields no items but iterates cleanly.
            int count = 0;
            await foreach (FiniteStateSnapshot _ in enumerable.ConfigureAwait(false))
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        private void SetupTranslate(
            bool allEmpty,
            Action<ArrayOf<BrowsePath>>? capture)
        {
            ArrayOf<BrowsePathResult> results = ArrayOf.Wrapped(
            [
                MakeEmptyResult(),
                MakeEmptyResult(),
                MakeEmptyResult(),
                MakeEmptyResult()
            ]);
            _ = allEmpty;
            SetupTranslate(results, capture);
        }

        private void SetupTranslate(
            ArrayOf<BrowsePathResult> results,
            Action<ArrayOf<BrowsePath>>? capture)
        {
            m_sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, r, _) => capture?.Invoke(r))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results,
                        DiagnosticInfos = []
                    }));
        }

        private static BrowsePathResult MakeResult(ExpandedNodeId target) => new()
        {
            StatusCode = StatusCodes.Good,
            Targets = ArrayOf.Wrapped(
            [
                new BrowsePathTarget { TargetId = target }
            ])
        };

        private static BrowsePathResult MakeEmptyResult() => new()
        {
            StatusCode = StatusCodes.Good,
            Targets = default
        };

        /// <summary>
        /// Minimal <see cref="IStreamingSubscription"/> that yields no
        /// notifications. Used for tests where the underlying state
        /// machine resolves no CurrentState.Id child node.
        /// </summary>
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

            public ValueTask DisposeAsync() => default;
        }
    }
}
