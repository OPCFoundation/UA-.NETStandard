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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.StateMachines
{
    /// <summary>
    /// Extension methods on the source-generated
    /// <see cref="FiniteStateMachineTypeClient"/> proxy adding the
    /// finite-state-machine generic API (typed state + transition
    /// observation, state enumeration, target-state wait).
    /// </summary>
    /// <remarks>
    /// Vendor concrete state machines that derive from
    /// <c>FiniteStateMachineType</c> (and produce a generated client
    /// via the proxy generator) inherit every helper here
    /// transparently.
    /// </remarks>
    public static class FiniteStateMachineTypeClientExtensions
    {
        /// <summary>
        /// Reads the current state of the wrapped finite state
        /// machine, including the typed state and last transition
        /// NodeIds.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        public static async ValueTask<FiniteStateSnapshot> GetCurrentFiniteStateAsync(
            this FiniteStateMachineTypeClient client,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            (NodeId currentStateNodeId, NodeId currentStateIdNodeId,
             NodeId lastTransitionNodeId, NodeId lastTransitionIdNodeId) =
                await ResolveStateAndTransitionNodesAsync(client, ct)
                    .ConfigureAwait(false);

            var nodes = new List<ReadValueId>(4);
            void Add(NodeId id)
            {
                if (!id.IsNull)
                {
                    nodes.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Value });
                }
            }
            Add(currentStateNodeId);
            Add(currentStateIdNodeId);
            Add(lastTransitionNodeId);
            Add(lastTransitionIdNodeId);

            if (nodes.Count == 0)
            {
                return new FiniteStateSnapshot(
                    client.ObjectId,
                    LocalizedText.Null,
                    NodeId.Null,
                    LocalizedText.Null,
                    NodeId.Null,
                    DateTime.MinValue,
                    StatusCodes.BadNotFound);
            }

            var nodesToRead = ArrayOf.Wrapped(nodes.ToArray());
            ReadResponse response = await client.Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, nodesToRead, ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(
                response.Results, nodesToRead);

            LocalizedText currentState = LocalizedText.Null;
            NodeId currentStateId = NodeId.Null;
            LocalizedText lastTransition = LocalizedText.Null;
            NodeId lastTransitionId = NodeId.Null;
            DateTime timestamp = DateTime.UtcNow;
            StatusCode worst = StatusCodes.Good;

            int idx = 0;
            if (!currentStateNodeId.IsNull)
            {
                DataValue dv = response.Results[idx++];
                if (dv.WrappedValue.TryGetValue(out LocalizedText lt))
                {
                    currentState = lt;
                }
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    worst = dv.StatusCode;
                }
                var ts = (DateTime)dv.SourceTimestamp;
                if (ts != DateTime.MinValue)
                {
                    timestamp = ts;
                }
            }
            if (!currentStateIdNodeId.IsNull && idx < response.Results.Count)
            {
                DataValue dv = response.Results[idx++];
                if (dv.WrappedValue.TryGetValue(out NodeId id))
                {
                    currentStateId = id;
                }
            }
            if (!lastTransitionNodeId.IsNull && idx < response.Results.Count)
            {
                DataValue dv = response.Results[idx++];
                if (dv.WrappedValue.TryGetValue(out LocalizedText lt))
                {
                    lastTransition = lt;
                }
            }
            if (!lastTransitionIdNodeId.IsNull && idx < response.Results.Count)
            {
                DataValue dv = response.Results[idx++];
                if (dv.WrappedValue.TryGetValue(out NodeId id))
                {
                    lastTransitionId = id;
                }
            }

            return new FiniteStateSnapshot(
                client.ObjectId,
                currentState,
                currentStateId,
                lastTransition,
                lastTransitionId,
                timestamp,
                worst);
        }

        /// <summary>
        /// Subscribes to the state machine's <c>CurrentState.Id</c>
        /// variable and yields a fresh <see cref="FiniteStateSnapshot"/>
        /// every time the state changes. Each yielded snapshot is
        /// refreshed by reading <c>CurrentState</c>, <c>LastTransition</c>
        /// and <c>LastTransition.Id</c> so the consumer sees consistent
        /// typed state + transition data per transition.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        public static IAsyncEnumerable<FiniteStateSnapshot> ObserveFiniteTransitionsAsync(
            this FiniteStateMachineTypeClient client,
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            return ObserveFiniteTransitionsImpl(client, streaming, options, ct);
        }

        private static async IAsyncEnumerable<FiniteStateSnapshot> ObserveFiniteTransitionsImpl(
            FiniteStateMachineTypeClient client,
            IStreamingSubscription streaming,
            MonitoringOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            NodeId currentStateIdNodeId = await client
                .ResolveChildNodeIdAsync(BrowseNames.CurrentState,
                    BrowseNames.Id, ct).ConfigureAwait(false);
            if (currentStateIdNodeId.IsNull)
            {
                yield break;
            }

            await foreach (DataValueChange _ in streaming
                .SubscribeDataChangesAsync(currentStateIdNodeId, options, ct)
                .ConfigureAwait(false))
            {
                yield return await client.GetCurrentFiniteStateAsync(ct)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waits until the state machine reaches a target state by
        /// NodeId. Composes
        /// <see cref="ObserveFiniteTransitionsAsync"/> with timeout
        /// and cancellation.
        /// </summary>
        /// <param name="client">The proxy client.</param>
        /// <param name="streaming">The streaming subscription used to observe transitions.</param>
        /// <param name="targetStateId">The state NodeId to wait for.</param>
        /// <param name="timeout">Optional timeout; <c>null</c> waits indefinitely.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used for the timeout
        /// scheduler; defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public static async ValueTask<FiniteStateSnapshot> WaitForStateAsync(
            this FiniteStateMachineTypeClient client,
            IStreamingSubscription streaming,
            NodeId targetStateId,
            TimeSpan? timeout = null,
            TimeProvider? timeProvider = null,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            if (targetStateId.IsNull)
            {
                throw new ArgumentException("Target state NodeId must not be null.",
                    nameof(targetStateId));
            }

            FiniteStateSnapshot current = await client.GetCurrentFiniteStateAsync(ct)
                .ConfigureAwait(false);
            if (current.CurrentStateId == targetStateId)
            {
                return current;
            }

            TimeProvider tp = timeProvider ?? TimeProvider.System;
            using CancellationTokenSource? timeoutCts = timeout.HasValue
                ? tp.CreateCancellationTokenSource(timeout.Value)
                : null;
            using CancellationTokenSource? linkedCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
                : null;
            CancellationToken effective = linkedCts?.Token ?? ct;

            await foreach (FiniteStateSnapshot snap in client
                .ObserveFiniteTransitionsAsync(streaming, options: null, effective)
                .ConfigureAwait(false))
            {
                if (snap.CurrentStateId == targetStateId)
                {
                    return snap;
                }
            }

            throw new OperationCanceledException(
                "Target state not reached before cancellation or timeout.", ct);
        }

        /// <summary>
        /// Returns the states referenced by the optional
        /// <c>AvailableStates</c> property of the state machine instance.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        public static ValueTask<IReadOnlyList<FiniteStateInfo>> GetAvailableStatesAsync(
            this FiniteStateMachineTypeClient client,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            return ReadAvailableNodesAsync(
                client,
                BrowseNames.AvailableStates,
                BrowseNames.StateNumber,
                static (id, name, num) => new FiniteStateInfo(id, name, num),
                ct);
        }

        /// <summary>
        /// Returns the transitions referenced by the optional
        /// <c>AvailableTransitions</c> property of the state machine instance.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        public static ValueTask<IReadOnlyList<FiniteTransitionInfo>> GetAvailableTransitionsAsync(
            this FiniteStateMachineTypeClient client,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            return ReadAvailableNodesAsync(
                client,
                BrowseNames.AvailableTransitions,
                BrowseNames.TransitionNumber,
                static (id, name, num) => new FiniteTransitionInfo(id, name, num),
                ct);
        }

        private static async ValueTask<(NodeId currentState, NodeId currentStateId,
            NodeId lastTransition, NodeId lastTransitionId)>
            ResolveStateAndTransitionNodesAsync(
                FiniteStateMachineTypeClient client,
                CancellationToken ct)
        {
            // Resolve all four NodeIds in one round-trip via
            // TranslateBrowsePathsToNodeIds.
            ArrayOf<BrowsePath> requests =
            [
                MakePath(client.ObjectId, BrowseNames.CurrentState),
                MakeNestedPath(client.ObjectId, BrowseNames.CurrentState, BrowseNames.Id),
                MakePath(client.ObjectId, BrowseNames.LastTransition),
                MakeNestedPath(client.ObjectId, BrowseNames.LastTransition, BrowseNames.Id)
            ];

            TranslateBrowsePathsToNodeIdsResponse response =
                await client.Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, requests, ct).ConfigureAwait(false);

            return (
                Resolve(response, 0, client),
                Resolve(response, 1, client),
                Resolve(response, 2, client),
                Resolve(response, 3, client));

            static NodeId Resolve(TranslateBrowsePathsToNodeIdsResponse r, int i,
                FiniteStateMachineTypeClient c)
            {
                if (i >= r.Results.Count ||
                    StatusCode.IsBad(r.Results[i].StatusCode) ||
                    r.Results[i].Targets.Count == 0)
                {
                    return NodeId.Null;
                }
                return ExpandedNodeId.ToNodeId(
                    r.Results[i].Targets[0].TargetId,
                    c.Session.MessageContext.NamespaceUris);
            }

            static BrowsePath MakePath(NodeId start, string name) => new()
            {
                StartingNode = start,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(name)
                        }
                    ]
                }
            };

            static BrowsePath MakeNestedPath(NodeId start, string parent, string child) => new()
            {
                StartingNode = start,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(parent)
                        },
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasProperty,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(child)
                        }
                    ]
                }
            };
        }

        private static async ValueTask<IReadOnlyList<T>> ReadAvailableNodesAsync<T>(
            FiniteStateMachineTypeClient client,
            string availablePropertyName,
            string numberPropertyName,
            Func<NodeId, QualifiedName, uint, T> factory,
            CancellationToken ct)
        {
            ArrayOf<BrowsePath> availablePropertyPath =
            [
                new BrowsePath
                {
                    StartingNode = client.ObjectId,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(availablePropertyName)
                            }
                        ]
                    }
                }
            ];

            TranslateBrowsePathsToNodeIdsResponse propertyPathResponse =
                await client.Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, availablePropertyPath, ct).ConfigureAwait(false);
            if (propertyPathResponse.Results.Count == 0 ||
                !StatusCode.IsGood(propertyPathResponse.Results[0].StatusCode) ||
                propertyPathResponse.Results[0].Targets.Count == 0)
            {
                return [];
            }

            NodeId availablePropertyId = ExpandedNodeId.ToNodeId(
                propertyPathResponse.Results[0].Targets[0].TargetId,
                client.Session.MessageContext.NamespaceUris);
            if (availablePropertyId.IsNull)
            {
                return [];
            }

            ArrayOf<ReadValueId> propertyRead =
            [
                new ReadValueId
                {
                    NodeId = availablePropertyId,
                    AttributeId = Attributes.Value
                }
            ];
            ReadResponse propertyReadResponse = await client.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, propertyRead, ct)
                .ConfigureAwait(false);
            if (propertyReadResponse.Results.Count == 0 ||
                !StatusCode.IsGood(propertyReadResponse.Results[0].StatusCode) ||
                !propertyReadResponse.Results[0].WrappedValue.TryGetValue(
                    out ArrayOf<NodeId> availableNodeIds))
            {
                return [];
            }

            var nodeIds = new List<NodeId>(availableNodeIds.Count);
            foreach (NodeId nodeId in availableNodeIds)
            {
                if (!nodeId.IsNull)
                {
                    nodeIds.Add(nodeId);
                }
            }
            if (nodeIds.Count == 0)
            {
                return [];
            }

            var numberPathArray = new BrowsePath[nodeIds.Count];
            for (int i = 0; i < nodeIds.Count; i++)
            {
                numberPathArray[i] = new BrowsePath
                {
                    StartingNode = nodeIds[i],
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(numberPropertyName)
                            }
                        ]
                    }
                };
            }
            ArrayOf<BrowsePath> numberPaths = ArrayOf.Wrapped(numberPathArray);
            TranslateBrowsePathsToNodeIdsResponse numberPathResponse = await client.Session
                .TranslateBrowsePathsToNodeIdsAsync(null, numberPaths, ct)
                .ConfigureAwait(false);

            var detailReads = new List<ReadValueId>(nodeIds.Count * 2);
            foreach (NodeId nodeId in nodeIds)
            {
                detailReads.Add(new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.BrowseName
                });
            }

            var numberIndexes = new List<int>(nodeIds.Count);
            for (int i = 0; i < nodeIds.Count && i < numberPathResponse.Results.Count; i++)
            {
                BrowsePathResult pr = numberPathResponse.Results[i];
                if (StatusCode.IsGood(pr.StatusCode) && pr.Targets.Count > 0)
                {
                    NodeId numberPropertyId = ExpandedNodeId.ToNodeId(
                        pr.Targets[0].TargetId,
                        client.Session.MessageContext.NamespaceUris);
                    if (numberPropertyId.IsNull)
                    {
                        continue;
                    }
                    detailReads.Add(new ReadValueId
                    {
                        NodeId = numberPropertyId,
                        AttributeId = Attributes.Value
                    });
                    numberIndexes.Add(i);
                }
            }

            ArrayOf<ReadValueId> wrappedDetailReads = ArrayOf.Wrapped(detailReads.ToArray());
            ReadResponse detailReadResponse = await client.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, wrappedDetailReads, ct)
                .ConfigureAwait(false);

            var names = new QualifiedName[nodeIds.Count];
            for (int i = 0; i < nodeIds.Count && i < detailReadResponse.Results.Count; i++)
            {
                DataValue value = detailReadResponse.Results[i];
                if (StatusCode.IsGood(value.StatusCode) &&
                    value.WrappedValue.TryGetValue(out QualifiedName name))
                {
                    names[i] = name;
                }
            }

            var numbers = new uint[nodeIds.Count];
            int readIndex = nodeIds.Count;
            for (int i = 0;
                i < numberIndexes.Count && readIndex < detailReadResponse.Results.Count;
                i++, readIndex++)
            {
                DataValue value = detailReadResponse.Results[readIndex];
                if (StatusCode.IsGood(value.StatusCode) &&
                    value.WrappedValue.TryGetValue(out uint number))
                {
                    numbers[numberIndexes[i]] = number;
                }
            }

            var results = new List<T>(nodeIds.Count);
            for (int i = 0; i < nodeIds.Count; i++)
            {
                results.Add(factory(nodeIds[i], names[i], numbers[i]));
            }
            return results;
        }

        /// <summary>
        /// Resolves the sub-state-machine instance attached to
        /// <paramref name="parentStateNodeId"/> via the
        /// <c>HasSubStateMachine</c> reference. Returns <c>null</c>
        /// when no sub-SM is attached.
        /// </summary>
        /// <param name="parent">The parent finite state-machine
        /// client.</param>
        /// <param name="parentStateNodeId">The NodeId of the parent
        /// state node (e.g. the <c>StateType</c> instance — for
        /// servers that wire the reference from the FSM root rather
        /// than the state node, pass the FSM's <c>ObjectId</c>).</param>
        /// <param name="telemetry">Telemetry context for the returned
        /// sub-SM client.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static async ValueTask<FiniteStateMachineTypeClient?>
            GetSubStateMachineAsync(
                this FiniteStateMachineTypeClient parent,
                NodeId parentStateNodeId,
                ITelemetryContext telemetry,
                CancellationToken ct = default)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (parentStateNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Parent state node id must not be null.",
                    nameof(parentStateNodeId));
            }

            var nodesToBrowse = ArrayOf.Wrapped(
            [
                new BrowseDescription
                {
                    NodeId = parentStateNodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasSubStateMachine,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ]);

            BrowseResponse response = await parent.Session.BrowseAsync(
                null, null, 0, nodesToBrowse, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(
                response.Results, nodesToBrowse);

            if (response.Results.Count == 0 ||
                response.Results[0].References.Count == 0)
            {
                return null;
            }

            ReferenceDescription r = response.Results[0].References[0];
            var childId = ExpandedNodeId.ToNodeId(
                r.NodeId, parent.Session.MessageContext.NamespaceUris);
            return new FiniteStateMachineTypeClient(
                parent.Session, childId, telemetry);
        }

        /// <summary>
        /// Yields a combined snapshot whenever the parent transitions
        /// OR when the parent's currently-active sub-state-machine
        /// transitions. The <see cref="FiniteStateSnapshot.SubMachine"/>
        /// field on the yielded snapshot carries the sub-SM's current
        /// snapshot when the parent's current state has an attached
        /// sub-SM, or <c>null</c> otherwise.
        /// </summary>
        /// <remarks>
        /// All discovered sub-SMs are subscribed up-front so the
        /// implementation does not need to dynamically subscribe /
        /// unsubscribe as the parent transitions — that pattern races
        /// with fast sub-SM events. Sub-SM events arriving while the
        /// parent is NOT in the state that owns that sub-SM are
        /// discarded.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <c>null</c>.</exception>
        public static IAsyncEnumerable<FiniteStateSnapshot>
            ObserveEffectiveStateAsync(
                this FiniteStateMachineTypeClient parent,
                IStreamingSubscription streaming,
                ITelemetryContext telemetry,
                MonitoringOptions? options = null,
                CancellationToken ct = default)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return ObserveEffectiveStateImplAsync(parent, streaming, telemetry, options, ct);
        }

        private static async IAsyncEnumerable<FiniteStateSnapshot>
            ObserveEffectiveStateImplAsync(
                FiniteStateMachineTypeClient parent,
                IStreamingSubscription streaming,
                ITelemetryContext telemetry,
                MonitoringOptions? options,
                [EnumeratorCancellation] CancellationToken ct)
        {
            // Discover all sub-SMs up front. Map parent-state NodeId
            // → sub-SM client; both are needed to filter incoming
            // sub-SM events to only those for the currently-active
            // sub-SM.
            IReadOnlyList<FiniteStateInfo> states =
                await parent.GetAvailableStatesAsync(ct).ConfigureAwait(false);

            var subSmByState = new Dictionary<NodeId, FiniteStateMachineTypeClient>();
            foreach (FiniteStateInfo info in states)
            {
                FiniteStateMachineTypeClient? sub =
                    await parent.GetSubStateMachineAsync(
                        info.NodeId, telemetry, ct).ConfigureAwait(false);
                if (sub != null)
                {
                    subSmByState[info.NodeId] = sub;
                }
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var channel = System.Threading.Channels.Channel.CreateUnbounded<TaggedSnapshot>(
                new System.Threading.Channels.UnboundedChannelOptions
                {
                    SingleReader = true
                });

            // Background pump tasks — one per source stream. The
            // parent pump is always present; the sub-SM pumps run
            // only for discovered sub-SMs.
            var pumpTasks = new List<Task>(1 + subSmByState.Count)
            {
                PumpAsync(
                    parent.ObserveFiniteTransitionsAsync(streaming, options, linkedCts.Token),
                    parentAttachedTo: NodeId.Null,
                    channel.Writer,
                    linkedCts.Token)
            };
            foreach (KeyValuePair<NodeId, FiniteStateMachineTypeClient> kv in subSmByState)
            {
                pumpTasks.Add(PumpAsync(
                    kv.Value.ObserveFiniteTransitionsAsync(streaming, options, linkedCts.Token),
                    parentAttachedTo: kv.Key,
                    channel.Writer,
                    linkedCts.Token));
            }

            // When all pumps complete, close the channel so the reader
            // exits. Propagate the first pump fault (other than
            // cancellation, which is the normal shutdown path) so the
            // reader observes it as a fault rather than as a silent
            // end-of-stream.
            _ = Task.WhenAll(pumpTasks).ContinueWith(
                t =>
                {
                    Exception? fault = null;
                    if (t.IsFaulted && t.Exception != null)
                    {
                        // Filter out cancellation noise; surface any
                        // other pump fault.
                        fault = t.Exception.Flatten().InnerExceptions
                            .FirstOrDefault(e => e is not OperationCanceledException);
                    }
                    channel.Writer.TryComplete(fault);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            // Reader loop: maintain current parent state + last sub-SM
            // snapshot per attached state. Yield combined snapshot on
            // every accepted event.
            FiniteStateSnapshot? latestParent = null;
            NodeId currentParentStateId = NodeId.Null;
            var latestSubByState = new Dictionary<NodeId, FiniteStateSnapshot>();

            try
            {
                while (await channel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out TaggedSnapshot? tagged))
                    {
                        if (tagged.ParentAttachedTo.IsNull)
                        {
                            // Parent transition. Update tracking state
                            // and yield with the active sub-SM's
                            // latest known snapshot (or null if no
                            // sub-SM attached to the new state).
                            latestParent = tagged.Snapshot;
                            currentParentStateId = tagged.Snapshot.CurrentStateId;
                            FiniteStateSnapshot? activeSub =
                                latestSubByState.TryGetValue(currentParentStateId, out FiniteStateSnapshot? known)
                                    ? known
                                    : null;
                            yield return latestParent with { SubMachine = activeSub };
                        }
                        else
                        {
                            // Sub-SM transition. Remember the snapshot
                            // for that sub-SM. Yield only if THIS
                            // sub-SM is the one currently active in
                            // the parent.
                            latestSubByState[tagged.ParentAttachedTo] = tagged.Snapshot;
                            if (Equals(currentParentStateId, tagged.ParentAttachedTo) &&
                                latestParent != null)
                            {
                                yield return latestParent with { SubMachine = tagged.Snapshot };
                            }
                        }
                    }
                }
            }
            finally
            {
                linkedCts.Cancel();
                try
                {
                    await Task.WhenAll(pumpTasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown — pumps observe the linked
                    // CTS cancellation. Non-cancellation pump faults
                    // are surfaced through the channel completion
                    // above; we deliberately do NOT swallow them
                    // here.
                }
            }
        }

        private static async Task PumpAsync(
            IAsyncEnumerable<FiniteStateSnapshot> source,
            NodeId parentAttachedTo,
            System.Threading.Channels.ChannelWriter<TaggedSnapshot> writer,
            CancellationToken ct)
        {
            try
            {
                await foreach (FiniteStateSnapshot snap in source
                    .WithCancellation(ct).ConfigureAwait(false))
                {
                    await writer.WriteAsync(
                        new TaggedSnapshot(parentAttachedTo, snap), ct)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        /// <summary>
        /// Wraps a yielded snapshot together with the parent-state
        /// NodeId it is attached to. <see cref="NodeId.Null"/> tags
        /// identify parent snapshots; non-null tags identify sub-SM
        /// snapshots that only yield when the parent's current state
        /// matches <see cref="ParentAttachedTo"/>.
        /// </summary>
        private sealed record TaggedSnapshot(NodeId ParentAttachedTo, FiniteStateSnapshot Snapshot);
    }
}
