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
using Opc.Ua.Client.StateMachines;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

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
            ushort machineryNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Namespaces.Machinery);
            return ResolvePathAsync(
                machineryItem,
                cancellationToken,
                new QualifiedName(MachineryBrowseNames.Monitoring, machineryNamespaceIndex),
                new QualifiedName(MachineryBrowseNames.Status, machineryNamespaceIndex),
                new QualifiedName(browseName, machineryNamespaceIndex));
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
