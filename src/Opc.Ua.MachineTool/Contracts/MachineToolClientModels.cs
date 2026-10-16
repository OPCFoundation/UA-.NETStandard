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

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Identifies a discovered machine or an independently modeled wireless peripheral.
    /// </summary>
    public enum MachineToolDeviceKind
    {
        /// <summary>
        /// A MachineToolType instance or subtype, including GMS.
        /// </summary>
        MachineTool,

        /// <summary>
        /// A WirelessMachineToolPeripheralType instance or subtype.
        /// </summary>
        WirelessPeripheral
    }

    /// <summary>
    /// Identifies a discovered instance without equating loaded namespaces with runtime capabilities.
    /// </summary>
    /// <param name="NodeId">The instance identifier.</param>
    /// <param name="BrowseName">Its namespace-qualified browse name.</param>
    /// <param name="DisplayName">Its localized display name.</param>
    /// <param name="TypeDefinitionId">Its actual standard or vendor type.</param>
    /// <param name="Kind">Its standard model family.</param>
    public sealed record MachineToolDeviceEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinitionId,
        MachineToolDeviceKind Kind);

    /// <summary>
    /// Retains complete data quality and acquisition timestamps for mandatory and optional machine values.
    /// </summary>
    public sealed record MachineToolReadSnapshot
    {
        /// <summary>
        /// Gets the machine identifier.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the mandatory operation mode sample.
        /// </summary>
        public DataValue OperationMode { get; init; }

        /// <summary>
        /// Gets the optional feed override, or an uninitialized DataValue when absent.
        /// </summary>
        public DataValue FeedOverride { get; init; }

        /// <summary>
        /// Gets the optional warm-up indication, or an uninitialized DataValue when absent.
        /// </summary>
        public DataValue IsWarmUp { get; init; }

        /// <summary>
        /// Gets the retained mandatory active-program name sample.
        /// </summary>
        public DataValue ActiveProgramName { get; init; }

        /// <summary>
        /// Gets the mandatory active-program state sample.
        /// </summary>
        public DataValue ActiveProgramState { get; init; }
    }

    /// <summary>
    /// Bounds a one-shot observation, including subscription setup, without leaving persistent monitoring handles.
    /// </summary>
    public sealed record MachineToolObservationOptions
    {
        /// <summary>
        /// Gets the observation window, greater than zero and at most one minute.
        /// </summary>
        public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets the maximum number of retained notifications, from one through one hundred.
        /// </summary>
        public int MaximumNotifications { get; init; } = 50;

        /// <summary>
        /// Rejects invalid bounds before creating any server-side resources.
        /// </summary>
        public void Validate()
        {
            if (Window <= TimeSpan.Zero || Window > TimeSpan.FromMinutes(1))
            {
                throw new ArgumentOutOfRangeException(nameof(Window));
            }
            if (MaximumNotifications is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumNotifications));
            }
        }
    }

    /// <summary>
    /// Retains a notification's exact node, data quality, acquisition time and publish ordering.
    /// </summary>
    /// <param name="NodeId">The monitored variable identifier.</param>
    /// <param name="Value">The complete data sample, including bad or uncertain quality.</param>
    /// <param name="PublishTime">The server publication time, distinct from acquisition time.</param>
    /// <param name="SequenceNumber">The publication sequence number.</param>
    public sealed record MachineToolObservedValue(
        NodeId NodeId, DataValue Value, DateTimeUtc PublishTime, uint SequenceNumber);

    /// <summary>
    /// Returns bounded observations without claiming a complete history or silently hiding notification losses.
    /// </summary>
    /// <param name="Changes">The captured samples.</param>
    /// <param name="LimitReached">Whether the notification limit ended the observation.</param>
    /// <param name="DroppedNotifications">Known local discards and observed server overflow markers.</param>
    public sealed record MachineToolDataObservation(
        ArrayOf<MachineToolObservedValue> Changes, bool LimitReached, long DroppedNotifications);
}
