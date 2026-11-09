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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions.Streaming;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Opens the typed proxy for a machinery item's
        /// <c>MachineryItemState</c> state machine, or <see langword="null"/>
        /// when the item publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryItemState_StateMachineTypeClient?> ItemStateAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            NodeId stateMachine = await ResolveStatusChildAsync(
                machineryItem,
                MachineryBrowseNames.MachineryItemState,
                cancellationToken).ConfigureAwait(false);
            return stateMachine.IsNull
                ? null
                : new MachineryItemState_StateMachineTypeClient(
                    Session,
                    stateMachine,
                    Telemetry);
        }

        /// <summary>
        /// Opens the typed proxy for a machinery item's
        /// <c>MachineryOperationMode</c> state machine, or
        /// <see langword="null"/> when the item publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryOperationModeStateMachineTypeClient?>
            OperationModeAsync(
                NodeId machineryItem,
                CancellationToken cancellationToken = default)
        {
            NodeId stateMachine = await ResolveStatusChildAsync(
                machineryItem,
                MachineryBrowseNames.MachineryOperationMode,
                cancellationToken).ConfigureAwait(false);
            return stateMachine.IsNull
                ? null
                : new MachineryOperationModeStateMachineTypeClient(
                    Session,
                    stateMachine,
                    Telemetry);
        }

        /// <summary>
        /// Reads the current <c>MachineryItemState</c>, or
        /// <see langword="null"/> when the item publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<FiniteStateSnapshot?> GetItemStateAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            if (m_options is not null)
            {
                MachineryStateSnapshot? snapshot = await ReadItemStateAsync(machineryItem, cancellationToken)
                    .ConfigureAwait(false);
                return snapshot?.State;
            }
            MachineryItemState_StateMachineTypeClient? proxy = await ItemStateAsync(
                machineryItem,
                cancellationToken).ConfigureAwait(false);
            if (proxy == null)
            {
                return null;
            }
            return await proxy.GetCurrentFiniteStateAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current <c>MachineryOperationMode</c>, or
        /// <see langword="null"/> when the item publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<FiniteStateSnapshot?> GetOperationModeAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            if (m_options is not null)
            {
                MachineryStateSnapshot? snapshot = await ReadOperationModeAsync(machineryItem, cancellationToken)
                    .ConfigureAwait(false);
                return snapshot?.State;
            }
            MachineryOperationModeStateMachineTypeClient? proxy = await OperationModeAsync(
                machineryItem,
                cancellationToken).ConfigureAwait(false);
            if (proxy == null)
            {
                return null;
            }
            return await proxy.GetCurrentFiniteStateAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Strictly reads item state and optional transition details with source
        /// evidence. Returns null only when the namespace or state machine is absent.
        /// </summary>
        /// <param name="machineryItem">The selected machine or component.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        public ValueTask<MachineryStateSnapshot?> ReadItemStateAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            return ReadStateSnapshotAsync(machineryItem, MachineryBrowseNames.MachineryItemState, cancellationToken);
        }

        /// <summary>
        /// Strictly reads operation mode and optional transition details with source
        /// evidence. Returns null only when the namespace or state machine is absent.
        /// </summary>
        /// <param name="machineryItem">The selected machine or component.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        public ValueTask<MachineryStateSnapshot?> ReadOperationModeAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            return ReadStateSnapshotAsync(
                machineryItem, MachineryBrowseNames.MachineryOperationMode, cancellationToken);
        }

        /// <summary>
        /// Streams <c>MachineryItemState</c> transitions. OPC 40001-1 declares
        /// no cause methods, so every transition observed here was driven by
        /// the server.
        /// </summary>
        /// <param name="machineryItem">The machine or component to observe.</param>
        /// <param name="streaming">
        /// The streaming subscription. Pass <see langword="null"/> to use the
        /// session's default, which requires a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="options">Optional monitoring options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        public async IAsyncEnumerable<FiniteStateSnapshot> ObserveItemStateAsync(
            NodeId machineryItem,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            MachineryItemState_StateMachineTypeClient? proxy = await ItemStateAsync(
                machineryItem,
                cancellationToken).ConfigureAwait(false);
            if (proxy == null)
            {
                yield break;
            }
            await foreach (FiniteStateSnapshot snapshot in proxy
                .ObserveFiniteTransitionsAsync(
                    streaming ?? GetDefaultStreaming(Session),
                    options,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                yield return snapshot;
            }
        }

        /// <summary>
        /// Streams <c>MachineryOperationMode</c> transitions.
        /// </summary>
        /// <param name="machineryItem">The machine or component to observe.</param>
        /// <param name="streaming">
        /// The streaming subscription. Pass <see langword="null"/> to use the
        /// session's default, which requires a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="options">Optional monitoring options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        public async IAsyncEnumerable<FiniteStateSnapshot> ObserveOperationModeAsync(
            NodeId machineryItem,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            MachineryOperationModeStateMachineTypeClient? proxy = await OperationModeAsync(
                machineryItem,
                cancellationToken).ConfigureAwait(false);
            if (proxy == null)
            {
                yield break;
            }
            await foreach (FiniteStateSnapshot snapshot in proxy
                .ObserveFiniteTransitionsAsync(
                    streaming ?? GetDefaultStreaming(Session),
                    options,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                yield return snapshot;
            }
        }

        private ValueTask<NodeId> ResolveStatusChildAsync(
            NodeId machineryItem,
            string browseName,
            CancellationToken cancellationToken)
        {
            if (m_options is not null &&
                !TryGetNamespaceIndex(Namespaces.Machinery, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<NodeId>(NodeId.Null);
            }
            ushort machineryNamespaceIndex = NamespaceIndexOf(
                Namespaces.Machinery);
            return ResolvePathAsync(
                machineryItem,
                cancellationToken,
                new QualifiedName(MachineryBrowseNames.Monitoring, machineryNamespaceIndex),
                new QualifiedName(MachineryBrowseNames.Status, machineryNamespaceIndex),
                new QualifiedName(browseName, machineryNamespaceIndex));
        }

        private async ValueTask<MachineryStateSnapshot?> ReadStateSnapshotAsync(
            NodeId machineryItem,
            string browseName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetNamespaceIndex(Namespaces.Machinery, out ushort namespaceIndex))
            {
                return null;
            }
            NodeId stateMachine = await ResolveStrictPathAsync(
                machineryItem,
                [
                    new QualifiedName(MachineryBrowseNames.Monitoring, namespaceIndex),
                    new QualifiedName(MachineryBrowseNames.Status, namespaceIndex),
                    new QualifiedName(browseName, namespaceIndex)
                ],
                cancellationToken).ConfigureAwait(false);
            if (stateMachine.IsNull)
            {
                return null;
            }
            var current = new QualifiedName(Ua.BrowseNames.CurrentState);
            var last = new QualifiedName(Ua.BrowseNames.LastTransition);
            var id = new QualifiedName(Ua.BrowseNames.Id);
            var number = new QualifiedName(Ua.BrowseNames.Number);
            ArrayOf<MachineryReadValue> fields = await ReadStrictFieldsAsync(
                stateMachine,
                [
                    [current],
                    [current, id],
                    [current, number],
                    [last],
                    [last, id],
                    [last, number],
                    [last, new QualifiedName(Ua.BrowseNames.TransitionTime)]
                ],
                ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);
            RequirePresent(fields[0]);
            RequirePresent(fields[1]);
            for (int ii = 0; ii < fields.Count; ii++)
            {
                BuiltInType type = ii switch
                {
                    0 or 3 => BuiltInType.LocalizedText,
                    1 or 4 => BuiltInType.NodeId,
                    2 or 5 => BuiltInType.UInt32,
                    _ => BuiltInType.DateTime
                };
                RequireFieldType(fields[ii], HasType(fields[ii].Value.WrappedValue, type));
            }
            if (!fields[3].IsAbsent)
            {
                RequirePresent(fields[4]);
            }
            fields[0].Value.WrappedValue.TryGetValue(out LocalizedText currentState);
            fields[1].Value.WrappedValue.TryGetValue(out NodeId currentId);
            fields[3].Value.WrappedValue.TryGetValue(out LocalizedText transition);
            fields[4].Value.WrappedValue.TryGetValue(out NodeId transitionId);
            return new MachineryStateSnapshot(
                new FiniteStateSnapshot(
                    stateMachine, currentState, currentId, transition, transitionId,
                    fields[0].Value.SourceTimestamp.ToDateTime(), fields[0].Value.StatusCode),
                fields)
            {
                CurrentStateNumber = fields[2].Value.WrappedValue.TryGetValue(out uint currentNumber)
                    ? currentNumber
                    : null,
                LastTransitionNumber = fields[5].Value.WrappedValue.TryGetValue(out uint transitionNumber)
                    ? transitionNumber
                    : null,
                TransitionTime = fields[6].Value.WrappedValue.TryGetValue(out DateTimeUtc time)
                    ? time.ToDateTime()
                    : null
            };
        }

        internal static IStreamingSubscription GetDefaultStreaming(ISession session)
        {
            if (session is ManagedSession managedSession)
            {
                return managedSession.DefaultStreaming;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "Observation without an explicit IStreamingSubscription requires a " +
                "ManagedSession.");
        }
    }
}
