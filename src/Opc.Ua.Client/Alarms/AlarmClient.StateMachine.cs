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
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Part 16 state-machine helpers on <see cref="AlarmClient"/>.
    /// Surface the new generic state-machine API for the
    /// <c>ShelvingState</c> child of every Part 9 alarm condition,
    /// using the same proxy-delegation pattern as the rest of the
    /// client.
    /// </summary>
    public partial class AlarmClient
    {
        /// <summary>
        /// Reads the current shelving-state-machine snapshot for the
        /// supplied alarm condition.
        /// </summary>
        /// <param name="conditionId">The alarm condition NodeId.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<FiniteStateSnapshot> GetShelvingStateAsync(
            NodeId conditionId,
            CancellationToken ct = default)
        {
            ShelvedStateMachineTypeClient? shelvingState =
                await new AlarmConditionTypeClient(m_session, conditionId, m_telemetry)
                    .GetShelvingStateAsync(m_telemetry, ct)
                    .ConfigureAwait(false);
            if (shelvingState == null)
            {
                return new FiniteStateSnapshot(
                    conditionId,
                    LocalizedText.Null,
                    NodeId.Null,
                    LocalizedText.Null,
                    NodeId.Null,
                    DateTime.MinValue,
                    StatusCodes.BadNotFound);
            }
            return await shelvingState.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Subscribes to the supplied alarm condition's
        /// <c>ShelvingState</c> machine and yields a fresh
        /// <see cref="FiniteStateSnapshot"/> every time the alarm
        /// shelves / unshelves.
        /// </summary>
        public async IAsyncEnumerable<FiniteStateSnapshot> ObserveShelvingTransitionsAsync(
            NodeId conditionId,
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ShelvedStateMachineTypeClient? shelvingState =
                await new AlarmConditionTypeClient(m_session, conditionId, m_telemetry)
                    .GetShelvingStateAsync(m_telemetry, ct)
                    .ConfigureAwait(false);
            if (shelvingState == null)
            {
                yield break;
            }
            await foreach (FiniteStateSnapshot snapshot in shelvingState
                .ObserveFiniteTransitionsAsync(streaming, options, ct)
                .ConfigureAwait(false))
            {
                yield return snapshot;
            }
        }
    }
}
