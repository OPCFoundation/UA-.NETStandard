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
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server.StateMachines
{
    /// <summary>
    /// Maps the values of a state enumeration to the state identifiers of a <see cref="LadsStateTable"/>.
    /// The enumeration members are named exactly like the states of the type.
    /// </summary>
    /// <typeparam name="TEnum">The state enumeration.</typeparam>
    internal sealed class LadsStateMap<TEnum>
        where TEnum : struct, Enum
    {
        /// <summary>
        /// Creates the map.
        /// </summary>
        /// <param name="table">The state table.</param>
        /// <param name="values">Every member of the enumeration.</param>
        public LadsStateMap(LadsStateTable table, params TEnum[] values)
        {
            Table = table;
            foreach (TEnum value in values)
            {
                uint id = table.FindState(value.ToString()).Id;
                m_toId[value] = id;
                m_toValue[id] = value;
            }
        }

        /// <summary>
        /// Gets the state table.
        /// </summary>
        public LadsStateTable Table { get; }

        /// <summary>
        /// Gets the state identifier of a value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The state identifier.</returns>
        public uint ToId(TEnum value)
        {
            return m_toId[value];
        }

        /// <summary>
        /// Gets the value of a state identifier.
        /// </summary>
        /// <param name="stateId">The state identifier.</param>
        /// <returns>The value.</returns>
        public TEnum ToValue(uint stateId)
        {
            return m_toValue[stateId];
        }

        private readonly Dictionary<TEnum, uint> m_toId = [];
        private readonly Dictionary<uint, TEnum> m_toValue = [];
    }

    /// <summary>
    /// The enumeration maps of every state machine the runtime drives.
    /// </summary>
    internal static class LadsStateMaps
    {
        /// <summary>
        /// Gets the device state map.
        /// </summary>
        public static LadsStateMap<LadsDeviceStateValue> Device { get; } = new(
            LadsStateMachineTables.Device,
            LadsDeviceStateValue.Initialization,
            LadsDeviceStateValue.Operate,
            LadsDeviceStateValue.Sleep,
            LadsDeviceStateValue.Shutdown);

        /// <summary>
        /// Gets the functional state map.
        /// </summary>
        public static LadsStateMap<LadsFunctionalStateValue> Functional { get; } = new(
            LadsStateMachineTables.Functional,
            LadsFunctionalStateValue.Stopped,
            LadsFunctionalStateValue.Running,
            LadsFunctionalStateValue.Stopping,
            LadsFunctionalStateValue.Aborting,
            LadsFunctionalStateValue.Aborted,
            LadsFunctionalStateValue.Clearing);

        /// <summary>
        /// Gets the running state map.
        /// </summary>
        public static LadsStateMap<LadsRunningStateValue> Running { get; } = new(
            LadsStateMachineTables.Running,
            LadsRunningStateValue.Idle,
            LadsRunningStateValue.Starting,
            LadsRunningStateValue.Execute,
            LadsRunningStateValue.Completing,
            LadsRunningStateValue.Complete,
            LadsRunningStateValue.Resetting,
            LadsRunningStateValue.Holding,
            LadsRunningStateValue.Held,
            LadsRunningStateValue.Unholding,
            LadsRunningStateValue.Suspending,
            LadsRunningStateValue.Suspended,
            LadsRunningStateValue.Unsuspending);

        /// <summary>
        /// Gets the cover state map.
        /// </summary>
        public static LadsStateMap<LadsCoverStateValue> Cover { get; } = new(
            LadsStateMachineTables.Cover,
            LadsCoverStateValue.Opened,
            LadsCoverStateValue.Closing,
            LadsCoverStateValue.Closed,
            LadsCoverStateValue.Locking,
            LadsCoverStateValue.Locked,
            LadsCoverStateValue.Unlocking,
            LadsCoverStateValue.Opening,
            LadsCoverStateValue.Error);

        /// <summary>
        /// Gets the maintenance state map.
        /// </summary>
        public static LadsStateMap<LadsMaintenanceStateValue> Maintenance { get; } = new(
            LadsStateMachineTables.Maintenance,
            LadsMaintenanceStateValue.Planned,
            LadsMaintenanceStateValue.Executing,
            LadsMaintenanceStateValue.Finished);

        /// <summary>
        /// Gets the Machinery item state map.
        /// </summary>
        public static LadsStateMap<MachineryItemStateValue> ItemState { get; } = new(
            LadsStateMachineTables.ItemState,
            MachineryItemStateValue.NotAvailable,
            MachineryItemStateValue.OutOfService,
            MachineryItemStateValue.NotExecuting,
            MachineryItemStateValue.Executing);

        /// <summary>
        /// Gets the Machinery operation mode map.
        /// </summary>
        public static LadsStateMap<MachineryOperationModeValue> OperationMode { get; } = new(
            LadsStateMachineTables.OperationMode,
            MachineryOperationModeValue.None,
            MachineryOperationModeValue.Maintenance,
            MachineryOperationModeValue.Setup,
            MachineryOperationModeValue.Processing);
    }
}
