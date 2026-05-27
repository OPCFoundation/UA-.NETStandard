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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
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

            ArrayOf<ReadValueId> nodesToRead = ArrayOf.Wrapped(nodes.ToArray());
            ReadResponse response = await client.Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, nodesToRead, ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse<ReadValueId, DataValue>(
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
                DateTime ts = (DateTime)dv.SourceTimestamp;
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
            NodeId currentStateIdNodeId = await StateMachineTypeClientExtensions
                .ResolveChildNodeIdAsync(client, BrowseNames.CurrentState,
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
        public static async ValueTask<FiniteStateSnapshot> WaitForStateAsync(
            this FiniteStateMachineTypeClient client,
            IStreamingSubscription streaming,
            NodeId targetStateId,
            TimeSpan? timeout = null,
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

            using CancellationTokenSource? timeoutCts = timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            timeoutCts?.CancelAfter(timeout!.Value);
            CancellationToken effective = timeoutCts?.Token ?? ct;

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
        /// Returns every <c>StateType</c> child of the state machine
        /// instance. Useful for introspecting the machine's available
        /// states at runtime.
        /// </summary>
        public static ValueTask<IReadOnlyList<FiniteStateInfo>> GetAvailableStatesAsync(
            this FiniteStateMachineTypeClient client,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            return BrowseChildrenAsync<FiniteStateInfo>(
                client,
                ObjectTypeIds.StateType,
                BrowseNames.StateNumber,
                static (id, name, num) => new FiniteStateInfo(id, name, num),
                ct);
        }

        /// <summary>
        /// Returns every <c>TransitionType</c> child of the state
        /// machine instance.
        /// </summary>
        public static ValueTask<IReadOnlyList<FiniteTransitionInfo>> GetAvailableTransitionsAsync(
            this FiniteStateMachineTypeClient client,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            return BrowseChildrenAsync<FiniteTransitionInfo>(
                client,
                ObjectTypeIds.TransitionType,
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
                MakeNestedPath(client.ObjectId, BrowseNames.LastTransition, BrowseNames.Id),
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

        private static async ValueTask<IReadOnlyList<T>> BrowseChildrenAsync<T>(
            FiniteStateMachineTypeClient client,
            NodeId typeDefinitionId,
            string numberPropertyName,
            Func<NodeId, QualifiedName, uint, T> factory,
            CancellationToken ct)
        {
            ArrayOf<BrowseDescription> nodesToBrowse = ArrayOf.Wrapped(new[]
            {
                new BrowseDescription
                {
                    NodeId = client.ObjectId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            });

            BrowseResponse browse = await client.Session.BrowseAsync(
                null, null, 0, nodesToBrowse, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse<BrowseDescription, BrowseResult>(
                browse.Results, nodesToBrowse);

            ArrayOf<ReferenceDescription> refs = browse.Results[0].References;
            var results = new List<T>(refs.Count);
            var childIds = new List<NodeId>(refs.Count);
            var childNames = new List<QualifiedName>(refs.Count);

            foreach (ReferenceDescription r in refs)
            {
                if (r.TypeDefinition.IsNull)
                {
                    continue;
                }
                NodeId typeDefNodeId = ExpandedNodeId.ToNodeId(
                    r.TypeDefinition, client.Session.MessageContext.NamespaceUris);
                if (typeDefNodeId != typeDefinitionId)
                {
                    continue;
                }
                childIds.Add(ExpandedNodeId.ToNodeId(
                    r.NodeId, client.Session.MessageContext.NamespaceUris));
                childNames.Add(r.BrowseName);
            }

            if (childIds.Count == 0)
            {
                return results;
            }

            // Resolve and read the StateNumber/TransitionNumber property
            // for each child (optional — defaults to zero on failure).
            var pathRequestsArray = new BrowsePath[childIds.Count];
            for (int i = 0; i < childIds.Count; i++)
            {
                pathRequestsArray[i] = new BrowsePath
                {
                    StartingNode = childIds[i],
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
            ArrayOf<BrowsePath> pathRequests = ArrayOf.Wrapped(pathRequestsArray);
            TranslateBrowsePathsToNodeIdsResponse pathResp = await client.Session
                .TranslateBrowsePathsToNodeIdsAsync(null, pathRequests, ct)
                .ConfigureAwait(false);

            var numberReads = new List<ReadValueId>(childIds.Count);
            var numberIndexes = new List<int>(childIds.Count);
            for (int i = 0; i < pathResp.Results.Count; i++)
            {
                BrowsePathResult pr = pathResp.Results[i];
                if (StatusCode.IsGood(pr.StatusCode) && pr.Targets.Count > 0)
                {
                    numberReads.Add(new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            pr.Targets[0].TargetId,
                            client.Session.MessageContext.NamespaceUris),
                        AttributeId = Attributes.Value
                    });
                    numberIndexes.Add(i);
                }
            }

            var numbers = new uint[childIds.Count];
            if (numberReads.Count > 0)
            {
                ArrayOf<ReadValueId> wrapped = ArrayOf.Wrapped(numberReads.ToArray());
                ReadResponse readResp = await client.Session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, wrapped, ct)
                    .ConfigureAwait(false);
                for (int i = 0; i < readResp.Results.Count; i++)
                {
                    if (readResp.Results[i].WrappedValue.TryGetValue(out uint number))
                    {
                        numbers[numberIndexes[i]] = number;
                    }
                }
            }

            for (int i = 0; i < childIds.Count; i++)
            {
                results.Add(factory(childIds[i], childNames[i], numbers[i]));
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
            ArgumentNullException.ThrowIfNull(telemetry);
            if (parentStateNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Parent state node id must not be null.",
                    nameof(parentStateNodeId));
            }

            ArrayOf<BrowseDescription> nodesToBrowse = ArrayOf.Wrapped(new[]
            {
                new BrowseDescription
                {
                    NodeId = parentStateNodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasSubStateMachine,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            });

            BrowseResponse response = await parent.Session.BrowseAsync(
                null, null, 0, nodesToBrowse, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse<BrowseDescription, BrowseResult>(
                response.Results, nodesToBrowse);

            if (response.Results.Count == 0 ||
                response.Results[0].References.Count == 0)
            {
                return null;
            }

            ReferenceDescription r = response.Results[0].References[0];
            NodeId childId = ExpandedNodeId.ToNodeId(
                r.NodeId, parent.Session.MessageContext.NamespaceUris);
            return new FiniteStateMachineTypeClient(
                parent.Session, childId, telemetry);
        }

        /// <summary>
        /// Yields a combined snapshot whenever the parent transitions.
        /// The <see cref="FiniteStateSnapshot.SubMachine"/> field on
        /// the yielded snapshot carries the sub-state-machine's
        /// current snapshot when the parent's current state has an
        /// attached sub-SM, or <c>null</c> otherwise.
        /// </summary>
        /// <remarks>
        /// V1 limitation: sub-SM transitions that occur BETWEEN
        /// parent transitions are not surfaced as separate yielded
        /// snapshots. To observe sub-SM transitions independently,
        /// resolve the sub-SM via <see cref="GetSubStateMachineAsync"/>
        /// and call its <see cref="ObserveFiniteTransitionsAsync"/>.
        /// </remarks>
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
            ArgumentNullException.ThrowIfNull(telemetry);
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
            await foreach (FiniteStateSnapshot parentSnap in parent
                .ObserveFiniteTransitionsAsync(streaming, options, ct)
                .ConfigureAwait(false))
            {
                FiniteStateSnapshot? subSnap = null;
                if (!parentSnap.CurrentStateId.IsNull)
                {
                    FiniteStateMachineTypeClient? sub =
                        await parent.GetSubStateMachineAsync(
                            parentSnap.CurrentStateId, telemetry, ct)
                            .ConfigureAwait(false);
                    if (sub != null)
                    {
                        subSnap = await sub.GetCurrentFiniteStateAsync(ct)
                            .ConfigureAwait(false);
                    }
                }
                yield return parentSnap with { SubMachine = subSnap };
            }
        }
    }
}
