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

            // BrowseAsync (called by GetAvailableStatesAsync) returns
            // empty: no states, hence no sub-SMs to discover.
            SetupBrowseEmpty(sessionMock);

            // TranslateBrowsePathsToNodeIds (called by the parent
            // ObserveFiniteTransitionsAsync's resolve step) returns
            // empty results: parent CurrentState/Id won't resolve, so
            // ObserveFiniteTransitionsAsync yields nothing.
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

            // Make Browse honour cancellation: callers cancel the CT
            // before discovery can complete.
            sessionMock.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = ArrayOf.Wrapped(Array.Empty<BrowseResult>()),
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

        private static void SetupBrowseEmpty(Mock<ISessionClient> sessionMock)
        {
            sessionMock.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, descriptions, _) =>
                    {
                        var results = new BrowseResult[descriptions.Count];
                        for (int i = 0; i < results.Length; i++)
                        {
                            results[i] = new BrowseResult
                            {
                                StatusCode = StatusCodes.Good,
                                References = default
                            };
                        }
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = ArrayOf.Wrapped(results),
                            DiagnosticInfos = default
                        });
                    });
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
