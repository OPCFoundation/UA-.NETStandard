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
