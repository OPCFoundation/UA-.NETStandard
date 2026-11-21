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

namespace Opc.Ua.PackML.Client
{
    /// <summary>
    /// Status values read in one service request. Each field retains its own quality and timestamps.
    /// </summary>
    public sealed class PackMLStatusSnapshot
    {
        /// <summary>
        /// Creates a status snapshot from positionally aligned read results.
        /// </summary>
        internal PackMLStatusSnapshot(
            ArrayOf<NodeId> nodes,
            ArrayOf<DataValue> values,
            IServiceMessageContext context)
        {
            CurMachSpeed = PackMLFields.Status.CurMachSpeed.Decode(nodes[0], values[0], context);
            MachSpeed = PackMLFields.Status.MachSpeed.Decode(nodes[1], values[1], context);
            EquipmentBlocked = PackMLFields.Status.EquipmentBlocked.Decode(nodes[2], values[2], context);
            EquipmentStarved = PackMLFields.Status.EquipmentStarved.Decode(nodes[3], values[3], context);
            MaterialInterlock = PackMLFields.Status.MaterialInterlock.Decode(nodes[4], values[4], context);
            MaterialInterlocked = PackMLFields.Status.MaterialInterlocked.Decode(nodes[5], values[5], context);
            Parameter = PackMLFields.Status.Parameter.Decode(nodes[6], values[6], context);
            Product = PackMLFields.Status.Product.Decode(nodes[7], values[7], context);
            RemoteParameter = PackMLFields.Status.RemoteParameter.Decode(nodes[8], values[8], context);
            StateChangeInProcess = PackMLFields.Status.StateChangeInProcess.Decode(nodes[9], values[9], context);
            StateRequested = PackMLFields.Status.StateRequested.Decode(nodes[10], values[10], context);
            UnitModeChangeInProcess =
                PackMLFields.Status.UnitModeChangeInProcess.Decode(nodes[11], values[11], context);
            UnitModeCurrent = PackMLFields.Status.UnitModeCurrent.Decode(nodes[12], values[12], context);
            UnitModeRequested = PackMLFields.Status.UnitModeRequested.Decode(nodes[13], values[13], context);
            UnitSupportedModes = PackMLFields.Status.UnitSupportedModes.Decode(nodes[14], values[14], context);
            BaseState = PackMLFields.State.BaseState.Decode(nodes[15], values[15], context);
            MachineState = PackMLFields.State.MachineState.Decode(nodes[16], values[16], context);
            ExecuteState = PackMLFields.State.ExecuteState.Decode(nodes[17], values[17], context);
            BaseAvailableStates = PackMLFields.State.BaseAvailableStates.Decode(nodes[18], values[18], context);
            MachineAvailableStates = PackMLFields.State.MachineAvailableStates.Decode(nodes[19], values[19], context);
            ExecuteAvailableStates = PackMLFields.State.ExecuteAvailableStates.Decode(nodes[20], values[20], context);
            BaseAvailableTransitions =
                PackMLFields.State.BaseAvailableTransitions.Decode(nodes[21], values[21], context);
            MachineAvailableTransitions =
                PackMLFields.State.MachineAvailableTransitions.Decode(nodes[22], values[22], context);
            ExecuteAvailableTransitions =
                PackMLFields.State.ExecuteAvailableTransitions.Decode(nodes[23], values[23], context);
        }

        /// <summary>
        /// Gets the measured machine speed.
        /// </summary>
        public PackMLValue<float> CurMachSpeed { get; }

        /// <summary>
        /// Gets the commanded machine speed.
        /// </summary>
        public PackMLValue<float> MachSpeed { get; }

        /// <summary>
        /// Gets the blocked flag.
        /// </summary>
        public PackMLValue<bool> EquipmentBlocked { get; }

        /// <summary>
        /// Gets the starved flag.
        /// </summary>
        public PackMLValue<bool> EquipmentStarved { get; }

        /// <summary>
        /// Gets the material interlocks.
        /// </summary>
        public PackMLValue<ArrayOf<bool>> MaterialInterlock { get; }

        /// <summary>
        /// Gets the material interlocked flag.
        /// </summary>
        public PackMLValue<bool> MaterialInterlocked { get; }

        /// <summary>
        /// Gets the current parameter definitions.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLDescriptorDataType>> Parameter { get; }

        /// <summary>
        /// Gets the current product definitions.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLProductDataType>> Product { get; }

        /// <summary>
        /// Gets the remote interface parameters.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLRemoteInterfaceDataType>> RemoteParameter { get; }

        /// <summary>
        /// Gets the state transition flag.
        /// </summary>
        public PackMLValue<bool> StateChangeInProcess { get; }

        /// <summary>
        /// Gets the requested state number.
        /// </summary>
        public PackMLValue<int> StateRequested { get; }

        /// <summary>
        /// Gets the mode transition flag.
        /// </summary>
        public PackMLValue<bool> UnitModeChangeInProcess { get; }

        /// <summary>
        /// Gets the current vendor-defined mode enumeration as an Int32.
        /// </summary>
        public PackMLValue<int> UnitModeCurrent { get; }

        /// <summary>
        /// Gets the Boolean mode-requested flag.
        /// </summary>
        public PackMLValue<bool> UnitModeRequested { get; }

        /// <summary>
        /// Gets the supported-mode enumeration NodeId.
        /// </summary>
        public PackMLValue<NodeId> UnitSupportedModes { get; }

        /// <summary>
        /// Gets the base state identifier.
        /// </summary>
        public PackMLValue<NodeId> BaseState { get; }

        /// <summary>
        /// Gets the machine state identifier.
        /// </summary>
        public PackMLValue<NodeId> MachineState { get; }

        /// <summary>
        /// Gets the execute state identifier; it may be unavailable in the active mode.
        /// </summary>
        public PackMLValue<NodeId> ExecuteState { get; }

        /// <summary>
        /// Gets the base states available in the active mode.
        /// </summary>
        public PackMLValue<ArrayOf<NodeId>> BaseAvailableStates { get; }

        /// <summary>
        /// Gets the machine states available in the active mode.
        /// </summary>
        public PackMLValue<ArrayOf<NodeId>> MachineAvailableStates { get; }

        /// <summary>
        /// Gets the execute states available in the active mode.
        /// </summary>
        public PackMLValue<ArrayOf<NodeId>> ExecuteAvailableStates { get; }

        /// <summary>
        /// Gets the base transitions available in the active mode.
        /// </summary>
        public PackMLValue<ArrayOf<NodeId>> BaseAvailableTransitions { get; }

        /// <summary>
        /// Gets the machine transitions available in the active mode.
        /// </summary>
        public PackMLValue<ArrayOf<NodeId>> MachineAvailableTransitions { get; }

        /// <summary>
        /// Gets the execute transitions available in the active mode.
        /// </summary>
        public PackMLValue<ArrayOf<NodeId>> ExecuteAvailableTransitions { get; }
    }

    /// <summary>
    /// Administration values read in one service request, retaining optional fields and per-field metadata.
    /// </summary>
    public sealed class PackMLAdminSnapshot
    {
        /// <summary>
        /// Creates an administration snapshot.
        /// </summary>
        internal PackMLAdminSnapshot(
            ArrayOf<NodeId> nodes,
            ArrayOf<DataValue> values,
            IServiceMessageContext context)
        {
            AccTimeSinceReset = PackMLFields.Admin.AccTimeSinceReset.Decode(nodes[0], values[0], context);
            Alarm = PackMLFields.Admin.Alarm.Decode(nodes[1], values[1], context);
            AlarmExtent = PackMLFields.Admin.AlarmExtent.Decode(nodes[2], values[2], context);
            AlarmHistory = PackMLFields.Admin.AlarmHistory.Decode(nodes[3], values[3], context);
            AlarmHistoryExtent = PackMLFields.Admin.AlarmHistoryExtent.Decode(nodes[4], values[4], context);
            MachDesignSpeed = PackMLFields.Admin.MachDesignSpeed.Decode(nodes[5], values[5], context);
            ModeCumulativeTime = PackMLFields.Admin.ModeCumulativeTime.Decode(nodes[6], values[6], context);
            ModeCurrentTime = PackMLFields.Admin.ModeCurrentTime.Decode(nodes[7], values[7], context);
            Parameter = PackMLFields.Admin.Parameter.Decode(nodes[8], values[8], context);
            ProdConsumedCount = PackMLFields.Admin.ProdConsumedCount.Decode(nodes[9], values[9], context);
            ProdDefectiveCount = PackMLFields.Admin.ProdDefectiveCount.Decode(nodes[10], values[10], context);
            ProdProcessedCount = PackMLFields.Admin.ProdProcessedCount.Decode(nodes[11], values[11], context);
            StateCumulativeTime = PackMLFields.Admin.StateCumulativeTime.Decode(nodes[12], values[12], context);
            StateCurrentTime = PackMLFields.Admin.StateCurrentTime.Decode(nodes[13], values[13], context);
            StopReason = PackMLFields.Admin.StopReason.Decode(nodes[14], values[14], context);
            StopReasonExtent = PackMLFields.Admin.StopReasonExtent.Decode(nodes[15], values[15], context);
            Warning = PackMLFields.Admin.Warning.Decode(nodes[16], values[16], context);
            WarningExtent = PackMLFields.Admin.WarningExtent.Decode(nodes[17], values[17], context);
        }

        /// <summary>
        /// Gets accumulated seconds since reset.
        /// </summary>
        public PackMLValue<int> AccTimeSinceReset { get; }

        /// <summary>
        /// Gets active alarms.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLAlarmDataType>> Alarm { get; }

        /// <summary>
        /// Gets the alarm extent.
        /// </summary>
        public PackMLValue<int> AlarmExtent { get; }

        /// <summary>
        /// Gets alarm history.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLAlarmDataType>> AlarmHistory { get; }

        /// <summary>
        /// Gets the history extent.
        /// </summary>
        public PackMLValue<int> AlarmHistoryExtent { get; }

        /// <summary>
        /// Gets design machine speed.
        /// </summary>
        public PackMLValue<float> MachDesignSpeed { get; }

        /// <summary>
        /// Gets cumulative mode times.
        /// </summary>
        public PackMLValue<ArrayOf<int>> ModeCumulativeTime { get; }

        /// <summary>
        /// Gets current mode times.
        /// </summary>
        public PackMLValue<ArrayOf<int>> ModeCurrentTime { get; }

        /// <summary>
        /// Gets administration parameters.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLDescriptorDataType>> Parameter { get; }

        /// <summary>
        /// Gets consumed product counters.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLCountDataType>> ProdConsumedCount { get; }

        /// <summary>
        /// Gets defective product counters.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLCountDataType>> ProdDefectiveCount { get; }

        /// <summary>
        /// Gets processed product counters.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLCountDataType>> ProdProcessedCount { get; }

        /// <summary>
        /// Gets cumulative state times, preserving the mode/state matrix dimensions.
        /// </summary>
        public PackMLValue<MatrixOf<int>> StateCumulativeTime { get; }

        /// <summary>
        /// Gets current state times, preserving the mode/state matrix dimensions.
        /// </summary>
        public PackMLValue<MatrixOf<int>> StateCurrentTime { get; }

        /// <summary>
        /// Gets the current stop reason.
        /// </summary>
        public PackMLValue<PackMLAlarmDataType> StopReason { get; }

        /// <summary>
        /// Gets the stop reason extent.
        /// </summary>
        public PackMLValue<int> StopReasonExtent { get; }

        /// <summary>
        /// Gets active warnings.
        /// </summary>
        public PackMLValue<ArrayOf<PackMLAlarmDataType>> Warning { get; }

        /// <summary>
        /// Gets the warning extent.
        /// </summary>
        public PackMLValue<int> WarningExtent { get; }
    }
}
