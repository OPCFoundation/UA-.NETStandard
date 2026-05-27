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
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Tests;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

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
