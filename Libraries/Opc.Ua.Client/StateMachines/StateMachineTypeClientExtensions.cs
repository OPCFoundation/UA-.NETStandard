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
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.StateMachines
{
    /// <summary>
    /// Extension methods that add the generic, extensible OPC UA
    /// Part 16 state-machine API on top of the source-generated
    /// <see cref="StateMachineTypeClient"/> proxy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every subtype client emitted by the
    /// <c>ObjectTypeProxyGenerator</c> — including standard subtypes
    /// like <c>ShelvedStateMachineTypeClient</c> and vendor
    /// extensions — inherits these helpers transparently via the
    /// <see cref="StateMachineTypeClient"/> inheritance chain.
    /// </para>
    /// <para>
    /// Methods on this class cover the <c>StateMachineType</c> base
    /// shape (CurrentState only). The finite-state variant extends
    /// this surface via the
    /// <c>FiniteStateMachineTypeClientExtensions</c> class.
    /// </para>
    /// </remarks>
    public static class StateMachineTypeClientExtensions
    {
        /// <summary>
        /// Reads the current state of the wrapped state machine.
        /// </summary>
        /// <param name="client">The proxy client.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The current <see cref="StateMachineSnapshot"/>.</returns>
        public static async ValueTask<StateMachineSnapshot> GetCurrentStateAsync(
            this StateMachineTypeClient client,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            NodeId currentStateNodeId = await ResolveChildNodeIdAsync(
                client, BrowseNames.CurrentState, ct).ConfigureAwait(false);
            if (currentStateNodeId.IsNull)
            {
                return new StateMachineSnapshot(
                    client.ObjectId,
                    LocalizedText.Null,
                    DateTime.MinValue,
                    StatusCodes.BadNotFound);
            }

            DataValue dv = await ReadValueAsync(client, currentStateNodeId, ct)
                .ConfigureAwait(false);
            LocalizedText currentState = dv.WrappedValue.TryGetValue(
                out LocalizedText name) ? name : LocalizedText.Null;
            return new StateMachineSnapshot(
                client.ObjectId,
                currentState,
                ToTimestamp(dv.SourceTimestamp),
                dv.StatusCode);
        }

        /// <summary>
        /// Subscribes to changes of the state machine's CurrentState
        /// variable and yields a fresh snapshot for each state
        /// transition.
        /// </summary>
        public static IAsyncEnumerable<StateMachineSnapshot> ObserveStateChangesAsync(
            this StateMachineTypeClient client,
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
            return ObserveStateChangesImpl(client, streaming, options, ct);
        }

        private static async IAsyncEnumerable<StateMachineSnapshot> ObserveStateChangesImpl(
            StateMachineTypeClient client,
            IStreamingSubscription streaming,
            MonitoringOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            NodeId currentStateNodeId = await ResolveChildNodeIdAsync(
                client, BrowseNames.CurrentState, ct).ConfigureAwait(false);
            if (currentStateNodeId.IsNull)
            {
                yield break;
            }

            await foreach (DataValueChange change in streaming
                .SubscribeDataChangesAsync(currentStateNodeId, options, ct)
                .ConfigureAwait(false))
            {
                LocalizedText currentState =
                    change.Value.WrappedValue.TryGetValue(out LocalizedText name)
                        ? name
                        : LocalizedText.Null;
                yield return new StateMachineSnapshot(
                    client.ObjectId,
                    currentState,
                    ToTimestamp(change.Value.SourceTimestamp),
                    change.Value.StatusCode);
            }
        }

        /// <summary>
        /// Waits until the state machine reaches a target state
        /// (matched by localized name) or the timeout/cancellation
        /// fires.
        /// </summary>
        /// <param name="client">The proxy client.</param>
        /// <param name="streaming">The streaming subscription used to observe transitions.</param>
        /// <param name="targetState">The state to wait for.</param>
        /// <param name="timeout">Optional timeout; <c>null</c> waits indefinitely.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used for the timeout
        /// scheduler; defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public static async ValueTask WaitForStateAsync(
            this StateMachineTypeClient client,
            IStreamingSubscription streaming,
            LocalizedText targetState,
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

            StateMachineSnapshot current = await client.GetCurrentStateAsync(ct)
                .ConfigureAwait(false);
            if (current.CurrentState == targetState)
            {
                return;
            }

            TimeProvider tp = timeProvider ?? TimeProvider.System;
            using CancellationTokenSource? timeoutCts = timeout.HasValue
                ? tp.CreateCancellationTokenSource(timeout.Value)
                : null;
            using CancellationTokenSource? linkedCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
                : null;
            CancellationToken effective = linkedCts?.Token ?? ct;

            await foreach (StateMachineSnapshot snap in client
                .ObserveStateChangesAsync(streaming, options: null, effective)
                .ConfigureAwait(false))
            {
                if (snap.CurrentState == targetState)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Resolves a direct child NodeId by browse name from the
        /// state machine instance. Returns <c>NodeId.Null</c> if the
        /// child is not present. Internal helper shared with the
        /// finite-state extension class.
        /// </summary>
        internal static async ValueTask<NodeId> ResolveChildNodeIdAsync(
            StateMachineTypeClient client,
            string browseName,
            CancellationToken ct)
        {
            var relativePath = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(browseName)
                    }
                ]
            };
            var browsePath = new BrowsePath
            {
                StartingNode = client.ObjectId,
                RelativePath = relativePath
            };
            ArrayOf<BrowsePath> requests = [browsePath];

            TranslateBrowsePathsToNodeIdsResponse response =
                await client.Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, requests, ct).ConfigureAwait(false);

            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                client.Session.MessageContext.NamespaceUris);
        }

        /// <summary>
        /// Resolves a two-step child by browse name path
        /// (e.g. "CurrentState/Id").
        /// </summary>
        internal static async ValueTask<NodeId> ResolveChildNodeIdAsync(
            StateMachineTypeClient client,
            string parentBrowseName,
            string childBrowseName,
            CancellationToken ct)
        {
            var relativePath = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(parentBrowseName)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(childBrowseName)
                    }
                ]
            };
            var browsePath = new BrowsePath
            {
                StartingNode = client.ObjectId,
                RelativePath = relativePath
            };
            ArrayOf<BrowsePath> requests = [browsePath];

            TranslateBrowsePathsToNodeIdsResponse response =
                await client.Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, requests, ct).ConfigureAwait(false);

            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                client.Session.MessageContext.NamespaceUris);
        }

        /// <summary>
        /// Reads the Value attribute of a single node.
        /// </summary>
        internal static async ValueTask<DataValue> ReadValueAsync(
            StateMachineTypeClient client,
            NodeId nodeId,
            CancellationToken ct)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                }
            ];
            ReadResponse response = await client.Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(
                response.Results, nodesToRead);
            return response.Results[0];
        }

        internal static DateTime ToTimestamp(DateTimeUtc sourceTimestamp)
        {
            var dt = (DateTime)sourceTimestamp;
            return dt == DateTime.MinValue ? DateTime.UtcNow : dt;
        }
    }
}
