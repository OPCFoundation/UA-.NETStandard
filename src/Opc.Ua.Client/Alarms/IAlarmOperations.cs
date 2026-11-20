/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Client-side operations for an OPC UA <c>AlarmConditionType</c> instance.
    /// Includes shelving, suppression, out-of-service, latching, and
    /// silencing operations defined in Part 9.
    /// </summary>
    public interface IAlarmOperations : IAcknowledgeableConditionOperations
    {
        /// <summary>
        /// Silences the alarm (clears the audible annunciation).
        /// </summary>
        ValueTask SilenceAsync(
            NodeId conditionId,
            CancellationToken ct = default);

        /// <summary>
        /// Suppresses the alarm. If <paramref name="comment"/> is provided,
        /// the <c>Suppress2</c> method is used; otherwise the basic
        /// <c>Suppress</c> method is called.
        /// </summary>
        ValueTask SuppressAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default);

        /// <summary>
        /// Unsuppresses the alarm.
        /// </summary>
        ValueTask UnsuppressAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default);

        /// <summary>
        /// Removes the alarm from service.
        /// </summary>
        ValueTask RemoveFromServiceAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default);

        /// <summary>
        /// Places the alarm back in service.
        /// </summary>
        ValueTask PlaceInServiceAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default);

        /// <summary>
        /// Resets a latched alarm.
        /// </summary>
        ValueTask ResetAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default);

        /// <summary>
        /// Shelves an alarm for the specified duration (in milliseconds).
        /// </summary>
        ValueTask TimedShelveAsync(
            NodeId conditionId,
            double shelvingTime,
            CancellationToken ct = default);

        /// <summary>
        /// Shelves an alarm until it goes inactive.
        /// </summary>
        ValueTask OneShotShelveAsync(
            NodeId conditionId,
            CancellationToken ct = default);

        /// <summary>
        /// Removes the alarm from the shelved state.
        /// </summary>
        ValueTask UnshelveAsync(
            NodeId conditionId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the alarm groups the alarm belongs to.
        /// </summary>
        ValueTask<ArrayOf<NodeId>> GetGroupMembershipsAsync(
            NodeId conditionId,
            CancellationToken ct = default);
    }
}
