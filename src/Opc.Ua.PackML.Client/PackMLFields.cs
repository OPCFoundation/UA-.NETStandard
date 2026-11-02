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
    /// Typed descriptors for the OPC 30050 fields, including optional and mode-specific members.
    /// </summary>
    public static class PackMLFields
    {
        /// <summary>
        /// Gets the optional equipment tag.
        /// </summary>
        public static PackMLField<string> TagID { get; } = Text("TagID");

        /// <summary>
        /// Gets the optional PackML version.
        /// </summary>
        public static PackMLField<string> PackMLVersion { get; } = Text("PackMLVersion");

        /// <summary>
        /// Status field descriptors.
        /// </summary>
        public static class Status
        {
            /// <summary>
            /// Gets the measured machine speed.
            /// </summary>
            public static PackMLField<float> CurMachSpeed { get; } = Float("Status/CurMachSpeed");

            /// <summary>
            /// Gets the target machine speed.
            /// </summary>
            public static PackMLField<float> MachSpeed { get; } = Float("Status/MachSpeed");

            /// <summary>
            /// Gets whether the equipment is blocked.
            /// </summary>
            public static PackMLField<bool> EquipmentBlocked { get; } = Boolean("Status/EquipmentBlocked");

            /// <summary>
            /// Gets whether the equipment is starved.
            /// </summary>
            public static PackMLField<bool> EquipmentStarved { get; } = Boolean("Status/EquipmentStarved");

            /// <summary>
            /// Gets the material interlocks.
            /// </summary>
            public static PackMLField<ArrayOf<bool>> MaterialInterlock { get; } = new(
                "Status/MaterialInterlock",
                static (v, _) => (v.TryGetValue(out ArrayOf<bool> value), value));

            /// <summary>
            /// Gets whether the material is interlocked.
            /// </summary>
            public static PackMLField<bool> MaterialInterlocked { get; } = Boolean("Status/MaterialInterlocked");

            /// <summary>
            /// Gets the current parameter definitions.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLDescriptorDataType>> Parameter { get; } =
                Structures<PackMLDescriptorDataType>("Status/Parameter");

            /// <summary>
            /// Gets the current product definitions.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLProductDataType>> Product { get; } =
                Structures<PackMLProductDataType>("Status/Product");

            /// <summary>
            /// Gets the remote interface parameters.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLRemoteInterfaceDataType>> RemoteParameter { get; } =
                Structures<PackMLRemoteInterfaceDataType>("Status/RemoteParameter");

            /// <summary>
            /// Gets whether a state transition is in progress.
            /// </summary>
            public static PackMLField<bool> StateChangeInProcess { get; } = Boolean("Status/StateChangeInProcess");

            /// <summary>
            /// Gets the requested state number.
            /// </summary>
            public static PackMLField<int> StateRequested { get; } = Integer("Status/StateRequested");

            /// <summary>
            /// Gets whether a unit mode change is in progress.
            /// </summary>
            public static PackMLField<bool> UnitModeChangeInProcess { get; } =
                Boolean("Status/UnitModeChangeInProcess");

            /// <summary>
            /// Gets the vendor-defined mode enumeration, represented on the wire as Int32.
            /// </summary>
            public static PackMLField<int> UnitModeCurrent { get; } = Integer("Status/UnitModeCurrent");

            /// <summary>
            /// Gets the Boolean UnitModeRequested flag, not a numeric mode identifier.
            /// </summary>
            public static PackMLField<bool> UnitModeRequested { get; } = Boolean("Status/UnitModeRequested");

            /// <summary>
            /// Gets the NodeId of the supported-mode enumeration.
            /// </summary>
            public static PackMLField<NodeId> UnitSupportedModes { get; } = new(
                "Status/UnitSupportedModes",
                static (v, _) => (v.TryGetValue(out NodeId value), value));
        }

        /// <summary>
        /// Administration field descriptors.
        /// </summary>
        public static class Admin
        {
            /// <summary>
            /// Gets the accumulated seconds since reset.
            /// </summary>
            public static PackMLField<int> AccTimeSinceReset { get; } = Integer("Admin/AccTimeSinceReset");

            /// <summary>
            /// Gets the active alarm array.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLAlarmDataType>> Alarm { get; } =
                Structures<PackMLAlarmDataType>("Admin/Alarm");

            /// <summary>
            /// Gets the alarm extent.
            /// </summary>
            public static PackMLField<int> AlarmExtent { get; } = Integer("Admin/AlarmExtent");

            /// <summary>
            /// Gets the alarm history.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLAlarmDataType>> AlarmHistory { get; } =
                Structures<PackMLAlarmDataType>("Admin/AlarmHistory");

            /// <summary>
            /// Gets the alarm history extent.
            /// </summary>
            public static PackMLField<int> AlarmHistoryExtent { get; } = Integer("Admin/AlarmHistoryExtent");

            /// <summary>
            /// Gets the design machine speed.
            /// </summary>
            public static PackMLField<float> MachDesignSpeed { get; } = Float("Admin/MachDesignSpeed");

            /// <summary>
            /// Gets the cumulative times per mode.
            /// </summary>
            public static PackMLField<ArrayOf<int>> ModeCumulativeTime { get; } = Integers("Admin/ModeCumulativeTime");

            /// <summary>
            /// Gets the current times per mode.
            /// </summary>
            public static PackMLField<ArrayOf<int>> ModeCurrentTime { get; } = Integers("Admin/ModeCurrentTime");

            /// <summary>
            /// Gets the administration parameters.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLDescriptorDataType>> Parameter { get; } =
                Structures<PackMLDescriptorDataType>("Admin/Parameter");

            /// <summary>
            /// Gets the consumed product counters.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLCountDataType>> ProdConsumedCount { get; } =
                Structures<PackMLCountDataType>("Admin/ProdConsumedCount");

            /// <summary>
            /// Gets the defective product counters.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLCountDataType>> ProdDefectiveCount { get; } =
                Structures<PackMLCountDataType>("Admin/ProdDefectiveCount");

            /// <summary>
            /// Gets the processed product counters.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLCountDataType>> ProdProcessedCount { get; } =
                Structures<PackMLCountDataType>("Admin/ProdProcessedCount");

            /// <summary>
            /// Gets the cumulative times indexed by mode and state.
            /// </summary>
            public static PackMLField<MatrixOf<int>> StateCumulativeTime { get; } = new(
                "Admin/StateCumulativeTime",
                static (v, _) => (v.TryGetValue(out MatrixOf<int> value), value));

            /// <summary>
            /// Gets the current times indexed by mode and state.
            /// </summary>
            public static PackMLField<MatrixOf<int>> StateCurrentTime { get; } = new(
                "Admin/StateCurrentTime",
                static (v, _) => (v.TryGetValue(out MatrixOf<int> value), value));

            /// <summary>
            /// Gets the current stop reason.
            /// </summary>
            public static PackMLField<PackMLAlarmDataType> StopReason { get; } = new(
                "Admin/StopReason",
                static (v, context) =>
                    (v.TryGetStructure(context, out PackMLAlarmDataType value), value));

            /// <summary>
            /// Gets the stop reason extent.
            /// </summary>
            public static PackMLField<int> StopReasonExtent { get; } = Integer("Admin/StopReasonExtent");

            /// <summary>
            /// Gets the active warnings.
            /// </summary>
            public static PackMLField<ArrayOf<PackMLAlarmDataType>> Warning { get; } =
                Structures<PackMLAlarmDataType>("Admin/Warning");

            /// <summary>
            /// Gets the warning extent.
            /// </summary>
            public static PackMLField<int> WarningExtent { get; } = Integer("Admin/WarningExtent");
        }

        /// <summary>
        /// Nested state machine fields. Availability can vary with the active unit mode.
        /// </summary>
        public static class State
        {
            /// <summary>
            /// Gets the base state identifier.
            /// </summary>
            public static PackMLField<NodeId> BaseState { get; } = StateId("BaseStateMachine");

            /// <summary>
            /// Gets the machine state identifier.
            /// </summary>
            public static PackMLField<NodeId> MachineState { get; } = StateId("BaseStateMachine/MachineState");

            /// <summary>
            /// Gets the execute state identifier.
            /// </summary>
            public static PackMLField<NodeId> ExecuteState { get; } =
                StateId("BaseStateMachine/MachineState/ExecuteState");

            /// <summary>
            /// Gets the base states available in the active unit mode.
            /// </summary>
            public static PackMLField<ArrayOf<NodeId>> BaseAvailableStates { get; } =
                NodeIds("BaseStateMachine/:AvailableStates");

            /// <summary>
            /// Gets the machine states available in the active unit mode.
            /// </summary>
            public static PackMLField<ArrayOf<NodeId>> MachineAvailableStates { get; } =
                NodeIds("BaseStateMachine/MachineState/:AvailableStates");

            /// <summary>
            /// Gets the execute states available in the active unit mode.
            /// </summary>
            public static PackMLField<ArrayOf<NodeId>> ExecuteAvailableStates { get; } =
                NodeIds("BaseStateMachine/MachineState/ExecuteState/:AvailableStates");

            /// <summary>
            /// Gets the base transitions available in the active unit mode.
            /// </summary>
            public static PackMLField<ArrayOf<NodeId>> BaseAvailableTransitions { get; } =
                NodeIds("BaseStateMachine/:AvailableTransitions");

            /// <summary>
            /// Gets the machine transitions available in the active unit mode.
            /// </summary>
            public static PackMLField<ArrayOf<NodeId>> MachineAvailableTransitions { get; } =
                NodeIds("BaseStateMachine/MachineState/:AvailableTransitions");

            /// <summary>
            /// Gets the execute transitions available in the active unit mode.
            /// </summary>
            public static PackMLField<ArrayOf<NodeId>> ExecuteAvailableTransitions { get; } =
                NodeIds("BaseStateMachine/MachineState/ExecuteState/:AvailableTransitions");
        }

        /// <summary>
        /// Creates a Boolean descriptor.
        /// </summary>
        private static PackMLField<bool> Boolean(string path)
        {
            return new(path, static (v, _) => (v.TryGetValue(out bool value), value));
        }

        /// <summary>
        /// Creates an Int32 descriptor, including Enumeration values.
        /// </summary>
        private static PackMLField<int> Integer(string path)
        {
            return new(path, static (v, _) => (v.TryGetValue(out int value), value));
        }

        /// <summary>
        /// Creates an Int32 array descriptor.
        /// </summary>
        private static PackMLField<ArrayOf<int>> Integers(string path)
        {
            return new(path, static (v, _) => (v.TryGetValue(out ArrayOf<int> value), value));
        }

        /// <summary>
        /// Creates a Float descriptor.
        /// </summary>
        private static PackMLField<float> Float(string path)
        {
            return new(path, static (v, _) => (v.TryGetValue(out float value), value));
        }

        /// <summary>
        /// Creates a String descriptor.
        /// </summary>
        private static PackMLField<string> Text(string path)
        {
            return new(path, static (v, _) => (v.TryGetValue(out string value), value));
        }

        /// <summary>
        /// Creates a generated structure array descriptor.
        /// </summary>
        /// <typeparam name="T">The generated structure element type.</typeparam>
        private static PackMLField<ArrayOf<T>> Structures<T>(string path) where T : class, IEncodeable, new()
        {
            return new(path, static (v, context) => (v.TryGetStructure(context, out ArrayOf<T> value), value));
        }

        /// <summary>
        /// Creates a descriptor with namespace-zero CurrentState and Id components.
        /// </summary>
        private static PackMLField<NodeId> StateId(string path)
        {
            return new(path + "/:CurrentState/:Id", static (v, _) => (v.TryGetValue(out NodeId value), value));
        }

        /// <summary>
        /// Creates a NodeId array descriptor.
        /// </summary>
        private static PackMLField<ArrayOf<NodeId>> NodeIds(string path)
        {
            return new(path, static (v, _) => (v.TryGetValue(out ArrayOf<NodeId> value), value));
        }
    }
}
