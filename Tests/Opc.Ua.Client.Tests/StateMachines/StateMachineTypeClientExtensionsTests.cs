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
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Tests;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

// Local fake IStreamingSubscription test types are no-op IAsyncDisposable
// instances with nothing to dispose; CA2000's leak warning does not apply.
#pragma warning disable CA2000

namespace Opc.Ua.Client.Tests.StateMachines
{
    /// <summary>
    /// Tests for <see cref="StateMachineTypeClientExtensions"/>: the
    /// generic Part 16 state-machine API. Covers null guards and the
    /// <c>BadNotFound</c> short-circuit returned when the server does
    /// not expose a <c>CurrentState</c> child node.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineTypeClientExtensionsTests
    {
        private static StateMachineTypeClient CreateClient(Mock<ISessionClient> sessionMock)
        {
            sessionMock.SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            return new StateMachineTypeClient(
                sessionMock.Object,
                new NodeId(7u, 2),
                NUnitTelemetryContext.Create());
        }

        [Test]
        public void GetCurrentStateAsyncWithNullClientThrowsArgumentNullException()
        {
            Assert.That(
                async () => await StateMachineTypeClientExtensions
                    .GetCurrentStateAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ObserveStateChangesAsyncWithNullClientThrowsArgumentNullException()
        {
            var streaming = new EmptyStreamingSubscription();
            Assert.That(
                () => StateMachineTypeClientExtensions
                    .ObserveStateChangesAsync(null!, streaming),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ObserveStateChangesAsyncWithNullStreamingThrowsArgumentNullException()
        {
            StateMachineTypeClient client = CreateClient(new Mock<ISessionClient>(MockBehavior.Loose));
            Assert.That(
                () => client.ObserveStateChangesAsync(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WaitForStateAsyncWithNullClientThrowsArgumentNullException()
        {
            var streaming = new EmptyStreamingSubscription();
            Assert.That(
                async () => await StateMachineTypeClientExtensions
                    .WaitForStateAsync(null!, streaming, new LocalizedText("a"))
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WaitForStateAsyncWithNullStreamingThrowsArgumentNullException()
        {
            StateMachineTypeClient client = CreateClient(new Mock<ISessionClient>(MockBehavior.Loose));
            Assert.That(
                async () => await client
                    .WaitForStateAsync(null!, new LocalizedText("a"))
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task GetCurrentStateAsyncReturnsBadNotFoundWhenTranslateBrowsePathsResolvesNull()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            StateMachineTypeClient client = CreateClient(sessionMock);
            SetupTranslateEmpty(sessionMock);

            StateMachineSnapshot snapshot = await client.GetCurrentStateAsync()
                .ConfigureAwait(false);

            Assert.That(snapshot.StateMachineId, Is.EqualTo(client.ObjectId));
            Assert.That(snapshot.CurrentState.IsNull, Is.True);
            Assert.That(snapshot.Status, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            // ReadAsync must NOT be called when no current-state node is resolved.
            sessionMock.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ObserveStateChangesAsyncYieldsNothingWhenCurrentStateUnresolved()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            StateMachineTypeClient client = CreateClient(sessionMock);
            SetupTranslateEmpty(sessionMock);

            int count = 0;
            await foreach (StateMachineSnapshot _ in client
                .ObserveStateChangesAsync(new EmptyStreamingSubscription())
                .ConfigureAwait(false))
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        // Note: full happy-path coverage of GetCurrentStateAsync and
        // WaitForStateAsync (the two-call read pipeline + transition
        // observation) requires extensive ISessionClient mocking and is
        // exercised end-to-end by the conformance / integration suites
        // (see AlarmClientIntegrationTests). The mock-driven coverage
        // here focuses on the early-exit and null-guard contracts.

        private static void SetupTranslateEmpty(Mock<ISessionClient> sessionMock)
        {
            sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = default
                            }
                        ]),
                        DiagnosticInfos = []
                    }));
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

            public ValueTask DisposeAsync() => default;
        }
    }
}
