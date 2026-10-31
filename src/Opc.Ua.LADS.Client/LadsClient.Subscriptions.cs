/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.LADS.Client
{
    public sealed partial class LadsClient
    {
        /// <summary>
        /// Observes the sensor, control and state variables of a function. Each
        /// change names the member (see <see cref="LadsFunctionMembers"/>) it is for.
        /// </summary>
        /// <param name="functionId">The NodeId of the function.</param>
        /// <param name="streaming">
        /// The streaming subscription to monitor with; <c>null</c> to use the
        /// default streaming subscription of a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token that ends the observation.</param>
        /// <returns>The changes, starting with the current values.</returns>
        public async IAsyncEnumerable<LadsValueChange> ObserveFunctionAsync(
            NodeId functionId,
            IStreamingSubscription? streaming = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionId, nameof(functionId));
            IStreamingSubscription subscription = StreamingOf(streaming);
            List<(string Name, NodeId NodeId)> members = await ResolveFunctionMembersAsync(
                functionId, cancellationToken).ConfigureAwait(false);
            if (members.Count == 0)
            {
                yield break;
            }
            await foreach (LadsValueChange change in ObserveAsync(
                functionId, members, subscription, cancellationToken).ConfigureAwait(false))
            {
                yield return change;
            }
        }

        /// <summary>
        /// Observes the current state of a state machine.
        /// </summary>
        /// <param name="stateMachineId">The NodeId of the state machine.</param>
        /// <param name="streaming">
        /// The streaming subscription to monitor with; <c>null</c> to use the
        /// default streaming subscription of a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token that ends the observation.</param>
        /// <returns>The states, starting with the current state.</returns>
        public async IAsyncEnumerable<LadsStateSnapshot> ObserveStateAsync(
            NodeId stateMachineId,
            IStreamingSubscription? streaming = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfNull(stateMachineId, nameof(stateMachineId));
            IStreamingSubscription subscription = StreamingOf(streaming);
            var currentState = new QualifiedName(Ua.BrowseNames.CurrentState, 0);
            NodeId[] nodes = await ResolveAsync(
                stateMachineId,
                [[currentState], [currentState, new QualifiedName(Ua.BrowseNames.Id, 0)]],
                cancellationToken).ConfigureAwait(false);
            if (nodes[0].IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, $"The node {stateMachineId} has no CurrentState.");
            }
            var members = new List<(string, NodeId)> { (Ua.BrowseNames.CurrentState, nodes[0]) };
            if (!nodes[1].IsNull)
            {
                members.Add((Ua.BrowseNames.Id, nodes[1]));
            }
            string? name = null;
            NodeId id = NodeId.Null;
            await foreach (LadsValueChange change in ObserveAsync(
                stateMachineId, members, subscription, cancellationToken).ConfigureAwait(false))
            {
                if (change.Name == Ua.BrowseNames.Id)
                {
                    id = AsNodeId(change.Value);
                }
                else
                {
                    name = AsString(change.Value) ?? string.Empty;
                }
                if (name is not null)
                {
                    yield return new LadsStateSnapshot(stateMachineId, name, id);
                }
            }
        }

        /// <summary>
        /// Waits until a state machine reaches a state.
        /// </summary>
        /// <param name="stateMachineId">The NodeId of the state machine.</param>
        /// <param name="stateName">The name of the state to wait for.</param>
        /// <param name="timeout">The longest time to wait.</param>
        /// <param name="streaming">
        /// The streaming subscription to monitor with; <c>null</c> to use the
        /// default streaming subscription of a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The state that was reached.</returns>
        /// <exception cref="TimeoutException">The state was not reached in time.</exception>
        public async ValueTask<LadsStateSnapshot> WaitForStateAsync(
            NodeId stateMachineId,
            string stateName,
            TimeSpan timeout,
            IStreamingSubscription? streaming = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                throw new ArgumentException("A state name is required.", nameof(stateName));
            }
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await foreach (LadsStateSnapshot state in ObserveStateAsync(
                    stateMachineId, streaming, timeoutSource.Token).ConfigureAwait(false))
                {
                    if (state.IsIn(stateName))
                    {
                        return state;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The state machine {stateMachineId} did not reach {stateName} in time.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"The state machine {stateMachineId} did not reach {stateName}.");
        }

        /// <summary>
        /// Maps a data change back to the member it was monitored for, by the
        /// monitored item name the streaming subscription derives from the NodeId.
        /// </summary>
        internal static (string Name, NodeId NodeId)? MapChange(
            DataValueChange change,
            IReadOnlyList<(string Name, NodeId NodeId)> members)
        {
            if (members.Count == 1)
            {
                return members[0];
            }
            string? itemName = change.MonitoredItem?.Name;
            if (itemName is null)
            {
                return null;
            }
            (string, NodeId)? best = null;
            int bestLength = -1;
            foreach ((string Name, NodeId NodeId) member in members)
            {
                string suffix = "_" + member.NodeId;
                if (suffix.Length > bestLength && itemName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    best = member;
                    bestLength = suffix.Length;
                }
            }
            return best;
        }

        private async IAsyncEnumerable<LadsValueChange> ObserveAsync(
            NodeId sourceId,
            List<(string Name, NodeId NodeId)> members,
            IStreamingSubscription subscription,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var nodes = new NodeId[members.Count];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                nodes[ii] = members[ii].NodeId;
            }
            var options = new MonitoringOptions
            {
                SamplingInterval = Options.SamplingInterval,
                QueueSize = Options.QueueSize,
                DiscardOldest = true
            };
            await foreach (DataValueChange change in subscription.SubscribeDataChangesAsync(
                nodes, options, cancellationToken).ConfigureAwait(false))
            {
                if (MapChange(change, members) is { } member)
                {
                    yield return new LadsValueChange(sourceId, member.Name, member.NodeId, change.Value);
                }
            }
        }
    }
}
