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

namespace Opc.Ua.CranesHoists
{
    /// <summary>
    /// Bounds one observation, including subscription setup, without creating persistent handles.
    /// </summary>
    public sealed record CraneObservationOptions
    {
        /// <summary>
        /// Gets the total observation window, at most sixty seconds.
        /// </summary>
        public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets the retained notification limit, at most one hundred.
        /// </summary>
        public int MaximumNotifications { get; init; } = 50;

        /// <summary>
        /// Gets the decoded payload budget, at most one mebibyte.
        /// </summary>
        public int MaximumPayloadBytes { get; init; } = 1024 * 1024;

        /// <summary>
        /// Validates bounds before allocating any server-side resources.
        /// </summary>
        public void Validate()
        {
            if (Window <= TimeSpan.Zero || Window > TimeSpan.FromSeconds(60))
            {
                throw new ArgumentOutOfRangeException(nameof(Window));
            }
            if (MaximumNotifications is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumNotifications));
            }
            if (MaximumPayloadBytes is < 1 or > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumPayloadBytes));
            }
        }
    }

    /// <summary>
    /// A complete acquired notification from one actual monitored node.
    /// </summary>
    public sealed class CraneObservedValue
    {
        /// <summary>
        /// Retains the source value without dropping its quality or timestamps.
        /// </summary>
        public CraneObservedValue(NodeId nodeId, in DataValue value)
        {
            NodeId = nodeId;
            Value = value;
        }

        /// <summary>
        /// Gets the monitored node.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Gets the original data value.
        /// </summary>
        public DataValue Value { get; }
    }

    /// <summary>
    /// A standard event's identity and all selected fields, including bad field-status values.
    /// </summary>
    /// <param name="EventType">The original event type.</param>
    /// <param name="EventId">The original event identifier.</param>
    /// <param name="Fields">Selected values in the observation's FieldPaths order.</param>
    public sealed record CraneObservedEvent(NodeId EventType, ByteString EventId, ArrayOf<Variant> Fields);

    /// <summary>
    /// The bounded result of data or event observation; it is never an unqualified claim of completeness.
    /// </summary>
    /// <param name="Values">Original data-change samples.</param>
    /// <param name="Events">Original selected event fields.</param>
    /// <param name="FieldPaths">The namespace-qualified event selection paths.</param>
    /// <param name="LimitReached">Whether the notification bound ended observation.</param>
    /// <param name="DroppedNotifications">
    /// Locally discarded or explicitly signaled server overflow notifications.
    /// </param>
    public sealed record CraneObservation(
        ArrayOf<CraneObservedValue> Values,
        ArrayOf<CraneObservedEvent> Events,
        ArrayOf<ArrayOf<QualifiedName>> FieldPaths,
        bool LimitReached,
        long DroppedNotifications);
}
