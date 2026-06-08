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
    /// Tests for <see cref="FiniteStateMachineTypeClientExtensions"/>.
    /// Covers null guards plus the empty / <c>BadNotFound</c> snapshot
    /// returned when none of the four browse paths
    /// (<c>CurrentState</c>, <c>CurrentState.Id</c>,
    /// <c>LastTransition</c>, <c>LastTransition.Id</c>) resolves.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class FiniteStateMachineTypeClientExtensionsTests
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
        public void GetCurrentFiniteStateAsyncWithNullClientThrowsArgumentNullException()
        {
            Assert.That(
                async () => await FiniteStateMachineTypeClientExtensions
                    .GetCurrentFiniteStateAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ObserveFiniteTransitionsAsyncWithNullClientThrowsArgumentNullException()
        {
            var streaming = new EmptyStreamingSubscription();
            Assert.That(
                () => FiniteStateMachineTypeClientExtensions
                    .ObserveFiniteTransitionsAsync(null!, streaming),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ObserveFiniteTransitionsAsyncWithNullStreamingThrowsArgumentNullException()
        {
            FiniteStateMachineTypeClient client = CreateClient(
                new Mock<ISessionClient>(MockBehavior.Loose));
            Assert.That(
                () => client.ObserveFiniteTransitionsAsync(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WaitForStateAsyncWithNullClientThrowsArgumentNullException()
        {
            var streaming = new EmptyStreamingSubscription();
            Assert.That(
                async () => await FiniteStateMachineTypeClientExtensions
                    .WaitForStateAsync(null!, streaming, new NodeId(1u, 0))
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WaitForStateAsyncWithNullStreamingThrowsArgumentNullException()
        {
            FiniteStateMachineTypeClient client = CreateClient(
                new Mock<ISessionClient>(MockBehavior.Loose));
            Assert.That(
                async () => await client
                    .WaitForStateAsync(null!, new NodeId(1u, 0))
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WaitForStateAsyncWithNullTargetStateThrowsArgumentException()
        {
            FiniteStateMachineTypeClient client = CreateClient(
                new Mock<ISessionClient>(MockBehavior.Loose));
            var streaming = new EmptyStreamingSubscription();
            Assert.That(
                async () => await client
                    .WaitForStateAsync(streaming, NodeId.Null)
                    .ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public void GetAvailableStatesAsyncWithNullClientThrowsArgumentNullException()
        {
            Assert.That(
                async () => await FiniteStateMachineTypeClientExtensions
                    .GetAvailableStatesAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void GetAvailableTransitionsAsyncWithNullClientThrowsArgumentNullException()
        {
            Assert.That(
                async () => await FiniteStateMachineTypeClientExtensions
                    .GetAvailableTransitionsAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task GetCurrentFiniteStateAsyncReturnsBadNotFoundEmptySnapshotWhenAllBrowsePathsResolveNull()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);
            SetupTranslateAllEmpty(sessionMock);

            FiniteStateSnapshot snapshot = await client
                .GetCurrentFiniteStateAsync().ConfigureAwait(false);

            Assert.That(snapshot.StateMachineId, Is.EqualTo(client.ObjectId));
            Assert.That(snapshot.CurrentState.IsNull, Is.True);
            Assert.That(snapshot.CurrentStateId.IsNull, Is.True);
            Assert.That(snapshot.LastTransition.IsNull, Is.True);
            Assert.That(snapshot.LastTransitionId.IsNull, Is.True);
            Assert.That(snapshot.Status, Is.EqualTo(StatusCodes.BadNotFound));
            // ReadAsync must NOT be called: there is nothing to read.
            sessionMock.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ObserveFiniteTransitionsAsyncYieldsNothingWhenCurrentStateIdUnresolved()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);
            SetupTranslateAllEmpty(sessionMock);

            int count = 0;
            await foreach (FiniteStateSnapshot _ in client
                .ObserveFiniteTransitionsAsync(new EmptyStreamingSubscription())
                .ConfigureAwait(false))
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        // Note: full happy-path coverage of GetCurrentFiniteStateAsync,
        // ObserveFiniteTransitionsAsync, WaitForStateAsync,
        // GetAvailableStatesAsync, and GetAvailableTransitionsAsync
        // (multi-call browse + read pipelines) is exercised by the
        // integration / conformance suites — see AlarmClientIntegrationTests.

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
                                DiagnosticInfos = []
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
