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
        public async Task GetAvailableNodesAsyncReturnsEmptyWhenOptionalPropertiesAreAbsentOrInvalid()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);
            NodeId availableTransitionsNode = new(106u, 2);

            sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        string name =
                            requests[0].RelativePath.Elements[0].TargetName.Name ?? string.Empty;
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results =
                                [
                                    name == BrowseNames.AvailableStates
                                        ? new BrowsePathResult
                                        {
                                            StatusCode = StatusCodes.BadNoMatch,
                                            Targets = []
                                        }
                                        : new BrowsePathResult
                                        {
                                            StatusCode = StatusCodes.Good,
                                            Targets =
                                            [
                                                new BrowsePathTarget
                                                {
                                                    TargetId = availableTransitionsNode,
                                                    RemainingPathIndex = uint.MaxValue
                                                }
                                            ]
                                        }
                                ],
                                DiagnosticInfos = []
                            });
                    });
            sessionMock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, reads, _) => new ValueTask<ReadResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new DataValue(Variant.From("not a NodeId array"))
                        ],
                        DiagnosticInfos = []
                    }));

            IReadOnlyList<FiniteStateInfo> states = await client
                .GetAvailableStatesAsync().ConfigureAwait(false);
            IReadOnlyList<FiniteTransitionInfo> transitions = await client
                .GetAvailableTransitionsAsync().ConfigureAwait(false);

            Assert.That(states, Is.Empty);
            Assert.That(transitions, Is.Empty);
            sessionMock.Verify(s => s.BrowseAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ViewDescription>(),
                It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task GetAvailableStatesAsyncReturnsEmptyWhenAvailablePropertyResolvesToNullNodeIdAsync()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);

            sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, _, _) => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new BrowsePathResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    Targets =
                                    [
                                        new BrowsePathTarget
                                        {
                                            TargetId = ExpandedNodeId.Null,
                                            RemainingPathIndex = uint.MaxValue
                                        }
                                    ]
                                }
                            ],
                            DiagnosticInfos = []
                        }));

            IReadOnlyList<FiniteStateInfo> states = await client
                .GetAvailableStatesAsync().ConfigureAwait(false);

            Assert.That(states, Is.Empty);
            sessionMock.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task GetAvailableTransitionsAsyncReturnsEmptyWhenAvailableNodeIdsAreAllNullAsync()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);
            NodeId availableTransitionsNode = new(106u, 2);

            sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, _, _) => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new BrowsePathResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    Targets =
                                    [
                                        new BrowsePathTarget
                                        {
                                            TargetId = availableTransitionsNode,
                                            RemainingPathIndex = uint.MaxValue
                                        }
                                    ]
                                }
                            ],
                            DiagnosticInfos = []
                        }));
            sessionMock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, _, _) => new ValueTask<ReadResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new DataValue(Variant.From(new[] { NodeId.Null, NodeId.Null }.ToArrayOf()))
                        ],
                        DiagnosticInfos = []
                    }));

            IReadOnlyList<FiniteTransitionInfo> transitions = await client
                .GetAvailableTransitionsAsync().ConfigureAwait(false);

            Assert.That(transitions, Is.Empty);
            sessionMock.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            sessionMock.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(),
                It.IsAny<CancellationToken>()), Times.Once);
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

        [Test]
        public async Task HappyPathReadsSnapshotStatesTransitionsAndSubStateMachineAsync()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            FiniteStateMachineTypeClient client = CreateClient(sessionMock);
            NodeId currentStateNode = new(101u, 2);
            NodeId currentStateIdNode = new(102u, 2);
            NodeId lastTransitionNode = new(103u, 2);
            NodeId lastTransitionIdNode = new(104u, 2);
            NodeId availableStatesNode = new(105u, 2);
            NodeId availableTransitionsNode = new(106u, 2);
            NodeId idleState = new(201u, 2);
            NodeId runningState = new(202u, 2);
            NodeId startTransition = new(301u, 2);
            NodeId stateNumber = new(401u, 2);
            NodeId transitionNumber = new(402u, 2);
            NodeId subMachine = new(501u, 2);
            var translateRequests = new List<List<BrowsePath>>();
            var readRequests = new List<List<ReadValueId>>();
            var browseDescriptions = new List<BrowseDescription>();

            sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        var requestBatch = new List<BrowsePath>(requests.Count);
                        requestBatch.AddRange(requests);
                        translateRequests.Add(requestBatch);
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = ResolvePaths(
                                    requests,
                                    currentStateNode,
                                    currentStateIdNode,
                                    lastTransitionNode,
                                    lastTransitionIdNode,
                                    availableStatesNode,
                                    availableTransitionsNode,
                                    stateNumber,
                                    transitionNumber),
                                DiagnosticInfos = []
                            });
                    });
            sessionMock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    0,
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, reads, _) =>
                    {
                        var readBatch = new List<ReadValueId>(reads.Count);
                        readBatch.AddRange(reads);
                        readRequests.Add(readBatch);
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = ReadValues(
                                reads,
                                currentStateNode,
                                currentStateIdNode,
                                lastTransitionNode,
                                lastTransitionIdNode,
                                availableStatesNode,
                                availableTransitionsNode,
                                stateNumber,
                                transitionNumber,
                                idleState,
                                runningState,
                                startTransition),
                            DiagnosticInfos = []
                        });
                    });
            sessionMock.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    0,
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, browse, _) =>
                    {
                        browseDescriptions.AddRange(browse);
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new BrowseResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    References = ReferencesFor(
                                        browse[0],
                                        runningState,
                                        subMachine)
                                }
                            ],
                            DiagnosticInfos = []
                        });
                    });

            FiniteStateSnapshot snapshot = await client.GetCurrentFiniteStateAsync().ConfigureAwait(false);
            IReadOnlyList<FiniteStateInfo> states = await client.GetAvailableStatesAsync().ConfigureAwait(false);
            IReadOnlyList<FiniteTransitionInfo> transitions = await client
                .GetAvailableTransitionsAsync().ConfigureAwait(false);
            FiniteStateMachineTypeClient? child = await client
                .GetSubStateMachineAsync(runningState, NUnitTelemetryContext.Create())
                .ConfigureAwait(false);

            Assert.That(snapshot.CurrentState.Text, Is.EqualTo("Idle"));
            Assert.That(snapshot.CurrentStateId, Is.EqualTo(idleState));
            Assert.That(snapshot.LastTransition.Text, Is.EqualTo("Start"));
            Assert.That(snapshot.LastTransitionId, Is.EqualTo(startTransition));
            Assert.That(states, Has.Count.EqualTo(1));
            Assert.That(states[0].NodeId, Is.EqualTo(runningState));
            Assert.That(states[0].BrowseName.Name, Is.EqualTo("Running"));
            Assert.That(states[0].StateNumber, Is.EqualTo(10u));
            Assert.That(transitions, Has.Count.EqualTo(1));
            Assert.That(transitions[0].NodeId, Is.EqualTo(startTransition));
            Assert.That(transitions[0].BrowseName.Name, Is.EqualTo("Start"));
            Assert.That(transitions[0].TransitionNumber, Is.EqualTo(20u));
            Assert.That(child, Is.Not.Null);
            Assert.That(child!.ObjectId, Is.EqualTo(subMachine));
            Assert.That(translateRequests, Has.Count.EqualTo(5));
            Assert.That(readRequests, Has.Count.EqualTo(5));
            Assert.That(browseDescriptions, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(translateRequests[0], Has.Count.EqualTo(4));
                Assert.That(translateRequests[1], Has.Count.EqualTo(1));
                Assert.That(translateRequests[2], Has.Count.EqualTo(1));
                Assert.That(translateRequests[3], Has.Count.EqualTo(1));
                Assert.That(translateRequests[4], Has.Count.EqualTo(1));
                Assert.That(readRequests[0], Has.Count.EqualTo(4));
                Assert.That(readRequests[1], Has.Count.EqualTo(1));
                Assert.That(readRequests[2], Has.Count.EqualTo(2));
                Assert.That(readRequests[3], Has.Count.EqualTo(1));
                Assert.That(readRequests[4], Has.Count.EqualTo(2));
                AssertPath(
                    translateRequests[1][0],
                    client.ObjectId,
                    ReferenceTypeIds.HasComponent,
                    BrowseNames.AvailableStates);
                AssertPath(
                    translateRequests[2][0],
                    runningState,
                    ReferenceTypeIds.HasProperty,
                    BrowseNames.StateNumber);
                AssertPath(
                    translateRequests[3][0],
                    client.ObjectId,
                    ReferenceTypeIds.HasComponent,
                    BrowseNames.AvailableTransitions);
                AssertPath(
                    translateRequests[4][0],
                    startTransition,
                    ReferenceTypeIds.HasProperty,
                    BrowseNames.TransitionNumber);
                AssertRead(readRequests[1][0], availableStatesNode, Attributes.Value);
                AssertRead(readRequests[2][0], runningState, Attributes.BrowseName);
                AssertRead(readRequests[2][1], stateNumber, Attributes.Value);
                AssertRead(readRequests[3][0], availableTransitionsNode, Attributes.Value);
                AssertRead(readRequests[4][0], startTransition, Attributes.BrowseName);
                AssertRead(readRequests[4][1], transitionNumber, Attributes.Value);
                Assert.That(browseDescriptions[0].NodeId, Is.EqualTo(runningState));
                Assert.That(
                    browseDescriptions[0].ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasSubStateMachine));
            });
            sessionMock.Verify(s => s.BrowseAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ViewDescription>(),
                It.IsAny<uint>(),
                It.Is<ArrayOf<BrowseDescription>>(descriptions =>
                    descriptions.Count > 0 && descriptions[0].NodeId == client.ObjectId),
                It.IsAny<CancellationToken>()), Times.Never);
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
                                DiagnosticInfos = []
                            });
                    });
        }

        private static ArrayOf<BrowsePathResult> ResolvePaths(
            ArrayOf<BrowsePath> requests,
            NodeId currentStateNode,
            NodeId currentStateIdNode,
            NodeId lastTransitionNode,
            NodeId lastTransitionIdNode,
            NodeId availableStatesNode,
            NodeId availableTransitionsNode,
            NodeId stateNumber,
            NodeId transitionNumber)
        {
            var results = new BrowsePathResult[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                RelativePathElement last = requests[i].RelativePath.Elements[^1];
                NodeId resolved = last.TargetName.Name switch
                {
                    BrowseNames.CurrentState => currentStateNode,
                    BrowseNames.Id when requests[i].RelativePath.Elements[0].TargetName.Name == BrowseNames.CurrentState
                        => currentStateIdNode,
                    BrowseNames.LastTransition => lastTransitionNode,
                    BrowseNames.Id => lastTransitionIdNode,
                    BrowseNames.AvailableStates => availableStatesNode,
                    BrowseNames.AvailableTransitions => availableTransitionsNode,
                    BrowseNames.StateNumber => stateNumber,
                    BrowseNames.TransitionNumber => transitionNumber,
                    _ => NodeId.Null
                };
                results[i] = resolved.IsNull
                    ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch, Targets = [] }
                    : new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets =
                        [
                            new BrowsePathTarget
                            {
                                TargetId = resolved,
                                RemainingPathIndex = uint.MaxValue
                            }
                        ]
                    };
            }
            return results.ToArrayOf();
        }

        private static ArrayOf<DataValue> ReadValues(
            ArrayOf<ReadValueId> reads,
            NodeId currentStateNode,
            NodeId currentStateIdNode,
            NodeId lastTransitionNode,
            NodeId lastTransitionIdNode,
            NodeId availableStatesNode,
            NodeId availableTransitionsNode,
            NodeId stateNumber,
            NodeId transitionNumber,
            NodeId idleState,
            NodeId runningState,
            NodeId startTransition)
        {
            var values = new DataValue[reads.Count];
            for (int i = 0; i < reads.Count; i++)
            {
                NodeId nodeId = reads[i].NodeId;
                if (nodeId == currentStateNode)
                {
                    values[i] = new DataValue(
                        Variant.From(new LocalizedText("Idle")),
                        StatusCodes.Good,
                        DateTime.UtcNow);
                }
                else if (nodeId == currentStateIdNode)
                {
                    values[i] = new DataValue(Variant.From(idleState));
                }
                else if (nodeId == lastTransitionNode)
                {
                    values[i] = new DataValue(Variant.From(new LocalizedText("Start")));
                }
                else if (nodeId == lastTransitionIdNode)
                {
                    values[i] = new DataValue(Variant.From(startTransition));
                }
                else if (nodeId == availableStatesNode)
                {
                    values[i] = new DataValue(Variant.From(new[] { runningState }.ToArrayOf()));
                }
                else if (nodeId == availableTransitionsNode)
                {
                    values[i] = new DataValue(Variant.From(new[] { startTransition }.ToArrayOf()));
                }
                else if (nodeId == runningState && reads[i].AttributeId == Attributes.BrowseName)
                {
                    values[i] = new DataValue(Variant.From(new QualifiedName("Running")));
                }
                else if (nodeId == startTransition && reads[i].AttributeId == Attributes.BrowseName)
                {
                    values[i] = new DataValue(Variant.From(new QualifiedName("Start")));
                }
                else if (nodeId == stateNumber)
                {
                    values[i] = new DataValue(Variant.From(10u));
                }
                else if (nodeId == transitionNumber)
                {
                    values[i] = new DataValue(Variant.From(20u));
                }
                else
                {
                    values[i] = DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
                }
            }
            return values.ToArrayOf();
        }

        private static ArrayOf<ReferenceDescription> ReferencesFor(
            BrowseDescription browse,
            NodeId state,
            NodeId subMachine)
        {
            if (browse.NodeId == state &&
                browse.ReferenceTypeId == ReferenceTypeIds.HasSubStateMachine)
            {
                return new[]
                {
                    new ReferenceDescription
                    {
                        NodeId = subMachine,
                        BrowseName = new QualifiedName("SubMachine"),
                        TypeDefinition = ObjectTypeIds.FiniteStateMachineType
                    }
                }.ToArrayOf();
            }

            return [];
        }

        private static void AssertPath(
            BrowsePath path,
            NodeId startingNode,
            NodeId referenceTypeId,
            string targetName)
        {
            Assert.That(path.StartingNode, Is.EqualTo(startingNode));
            Assert.That(path.RelativePath.Elements, Has.Count.EqualTo(1));
            Assert.That(path.RelativePath.Elements[0].ReferenceTypeId, Is.EqualTo(referenceTypeId));
            Assert.That(path.RelativePath.Elements[0].TargetName.Name, Is.EqualTo(targetName));
        }

        private static void AssertRead(ReadValueId read, NodeId nodeId, uint attributeId)
        {
            Assert.That(read.NodeId, Is.EqualTo(nodeId));
            Assert.That(read.AttributeId, Is.EqualTo(attributeId));
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
