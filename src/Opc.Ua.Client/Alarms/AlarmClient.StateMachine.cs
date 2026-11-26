// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
