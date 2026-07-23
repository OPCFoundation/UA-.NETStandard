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

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Common identification data exposed by Robotics components and the
    /// underlying OPC UA DI model.
    /// </summary>
    public sealed record RoboticsComponentIdentification
    {
        /// <summary>
        /// The component instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The component BrowseName.
        /// </summary>
        public QualifiedName BrowseName { get; init; } = QualifiedName.Null;

        /// <summary>
        /// The user-facing component name.
        /// </summary>
        public LocalizedText ComponentName { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The DI asset identifier.
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// The manufacturer name.
        /// </summary>
        public LocalizedText Manufacturer { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The manufacturer model name.
        /// </summary>
        public LocalizedText Model { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The manufacturer product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// The manufacturer serial number.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// A URI or location for the component manual.
        /// </summary>
        public string? DeviceManual { get; init; }
    }

    /// <summary>
    /// Engineering metadata for a measured or commanded scalar value.
    /// </summary>
    public sealed record RoboticsEngineeringValue
    {
        /// <summary>
        /// The engineering unit advertised by the variable.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The supported or advertised engineering range.
        /// </summary>
        public Range? Range { get; init; }
    }

    /// <summary>
    /// Engineering limits for the standard AxisType telemetry values.
    /// </summary>
    public sealed record AxisLimits
    {
        /// <summary>
        /// The supported position range.
        /// </summary>
        public Range? Position { get; init; }

        /// <summary>
        /// The supported speed range.
        /// </summary>
        public Range? Speed { get; init; }

        /// <summary>
        /// The supported acceleration range.
        /// </summary>
        public Range? Acceleration { get; init; }
    }

    /// <summary>
    /// Engineering units and limits for the standard AxisType telemetry values.
    /// </summary>
    public sealed record AxisEngineeringOptions
    {
        /// <summary>
        /// The engineering unit for ActualPosition.
        /// </summary>
        public EUInformation? PositionUnit { get; init; }

        /// <summary>
        /// The engineering unit for ActualSpeed.
        /// </summary>
        public EUInformation? SpeedUnit { get; init; }

        /// <summary>
        /// The engineering unit for ActualAcceleration.
        /// </summary>
        public EUInformation? AccelerationUnit { get; init; }

        /// <summary>
        /// The advertised axis ranges.
        /// </summary>
        public AxisLimits Limits { get; init; } = new();
    }

    /// <summary>
    /// Timestamped and status-bearing values for the standard AxisType telemetry.
    /// </summary>
    public sealed record AxisStateSnapshot
    {
        /// <summary>
        /// The ActualPosition value, including status and timestamps.
        /// </summary>
        public DataValue ActualPosition { get; init; } = DataValue.Null;

        /// <summary>
        /// The ActualSpeed value, including status and timestamps.
        /// </summary>
        public DataValue ActualSpeed { get; init; } = DataValue.Null;

        /// <summary>
        /// The ActualAcceleration value, including status and timestamps.
        /// </summary>
        public DataValue ActualAcceleration { get; init; } = DataValue.Null;
    }

    /// <summary>
    /// An OPC 40010 relationship between two resolved instance NodeIds.
    /// </summary>
    public sealed record RoboticsRelationshipEntry
    {
        /// <summary>
        /// The source instance NodeId.
        /// </summary>
        public NodeId SourceId { get; init; } = NodeId.Null;

        /// <summary>
        /// The resolved ReferenceType NodeId.
        /// </summary>
        public NodeId ReferenceTypeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The target instance NodeId.
        /// </summary>
        public NodeId TargetId { get; init; } = NodeId.Null;

        /// <summary>
        /// Whether the entry was observed in the inverse direction.
        /// </summary>
        public bool IsInverse { get; init; }
    }

    /// <summary>
    /// Categorized semantic relationships defined by the Robotics NodeSet.
    /// </summary>
    public sealed record RoboticsRelationshipSnapshot
    {
        /// <summary>
        /// Controller-to-motion-device and task-control-to-motion-device Controls relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> Controls { get; init; } = [];

        /// <summary>
        /// Axis-to-power-train Requires relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> Requires { get; init; } = [];

        /// <summary>
        /// Power-train-to-axis Moves relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> Moves { get; init; } = [];

        /// <summary>
        /// Motor-to-drive IsDrivenBy relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> IsDrivenBy { get; init; } = [];

        /// <summary>
        /// Power-train HasSlave relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> HasSlave { get; init; } = [];

        /// <summary>
        /// Symmetric IsConnectedTo relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> IsConnectedTo { get; init; } = [];

        /// <summary>
        /// Controller-to-safety-state HasSafetyStates relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> HasSafetyStates { get; init; } = [];
    }

    /// <summary>
    /// Read-model snapshot of a MotionDeviceSystemType instance.
    /// </summary>
    public sealed record MotionDeviceSystemSnapshot
    {
        /// <summary>
        /// The system identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// Controller instance NodeIds contained by the system.
        /// </summary>
        public ArrayOf<NodeId> ControllerIds { get; init; } = [];

        /// <summary>
        /// Motion-device instance NodeIds contained by the system.
        /// </summary>
        public ArrayOf<NodeId> MotionDeviceIds { get; init; } = [];

        /// <summary>
        /// Safety-state instance NodeIds contained by the system.
        /// </summary>
        public ArrayOf<NodeId> SafetyStateIds { get; init; } = [];
    }

    /// <summary>
    /// Read-model snapshot of a ControllerType instance.
    /// </summary>
    public sealed record ControllerSnapshot
    {
        /// <summary>
        /// The controller identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// Task controls hosted by this controller.
        /// </summary>
        public ArrayOf<NodeId> TaskControlIds { get; init; } = [];

        /// <summary>
        /// Additional DI component instance NodeIds installed in the controller.
        /// </summary>
        public ArrayOf<NodeId> ComponentIds { get; init; } = [];
    }

    /// <summary>
    /// Read-model snapshot of a MotionDeviceType instance.
    /// </summary>
    public sealed record MotionDeviceSnapshot
    {
        /// <summary>
        /// The motion-device identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The generated OPC 40010 motion-device category.
        /// </summary>
        public MotionDeviceCategoryEnumeration Category { get; init; }

        /// <summary>
        /// The SpeedOverride value, including status and timestamps.
        /// </summary>
        public DataValue SpeedOverride { get; init; } = DataValue.Null;

        /// <summary>
        /// Axis instance NodeIds contained by the motion device.
        /// </summary>
        public ArrayOf<NodeId> AxisIds { get; init; } = [];

        /// <summary>
        /// Power-train instance NodeIds contained by the motion device.
        /// </summary>
        public ArrayOf<NodeId> PowerTrainIds { get; init; } = [];

        /// <summary>
        /// Additional DI component instance NodeIds contained by the motion device.
        /// </summary>
        public ArrayOf<NodeId> AdditionalComponentIds { get; init; } = [];

        /// <summary>
        /// The contained flange-load instance, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId FlangeLoadId { get; init; } = NodeId.Null;
    }

    /// <summary>
    /// Read-model snapshot of an AxisType instance.
    /// </summary>
    public sealed record AxisSnapshot
    {
        /// <summary>
        /// The axis identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The generated OPC 40010 axis motion profile.
        /// </summary>
        public AxisMotionProfileEnumeration MotionProfile { get; init; }

        /// <summary>
        /// Engineering units and limits for axis telemetry.
        /// </summary>
        public AxisEngineeringOptions Engineering { get; init; } = new();

        /// <summary>
        /// Current axis telemetry values.
        /// </summary>
        public AxisStateSnapshot State { get; init; } = new();

        /// <summary>
        /// The contained additional-load instance, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId AdditionalLoadId { get; init; } = NodeId.Null;
    }

    /// <summary>
    /// Read-model snapshot of a LoadType instance.
    /// </summary>
    public sealed record LoadSnapshot
    {
        /// <summary>
        /// The load instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The load mass, including status and timestamps.
        /// </summary>
        public DataValue Mass { get; init; } = DataValue.Null;

        /// <summary>
        /// Engineering metadata for the load mass.
        /// </summary>
        public RoboticsEngineeringValue MassEngineering { get; init; } = new();

        /// <summary>
        /// The load center of mass, including status and timestamps.
        /// </summary>
        public DataValue CenterOfMass { get; init; } = DataValue.Null;

        /// <summary>
        /// The load inertia, including status and timestamps.
        /// </summary>
        public DataValue Inertia { get; init; } = DataValue.Null;
    }

    /// <summary>
    /// Read-model snapshot of a PowerTrainType instance.
    /// </summary>
    public sealed record PowerTrainSnapshot
    {
        /// <summary>
        /// The power-train identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// Motor instance NodeIds in this power train.
        /// </summary>
        public ArrayOf<NodeId> MotorIds { get; init; } = [];

        /// <summary>
        /// Gear instance NodeIds in this power train.
        /// </summary>
        public ArrayOf<NodeId> GearIds { get; init; } = [];
    }

    /// <summary>
    /// Read-model snapshot of a MotorType instance.
    /// </summary>
    public sealed record MotorSnapshot
    {
        /// <summary>
        /// The motor identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();
    }

    /// <summary>
    /// Read-model snapshot of a GearType instance.
    /// </summary>
    public sealed record GearSnapshot
    {
        /// <summary>
        /// The gear identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The gear ratio expressed with the standard RationalNumber type.
        /// </summary>
        public RationalNumber? GearRatio { get; init; }

        /// <summary>
        /// The optional pitch value, including status and timestamps.
        /// </summary>
        public DataValue Pitch { get; init; } = DataValue.Null;
    }

    /// <summary>
    /// Read-model snapshot of a DriveType instance.
    /// </summary>
    public sealed record DriveSnapshot
    {
        /// <summary>
        /// The drive identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();
    }

    /// <summary>
    /// Read-model snapshot of a SafetyStateType instance.
    /// </summary>
    public sealed record SafetyStateSnapshot
    {
        /// <summary>
        /// The safety-state identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The EmergencyStop value, including status and timestamps.
        /// </summary>
        public DataValue EmergencyStop { get; init; } = DataValue.Null;

        /// <summary>
        /// The OperationalMode value, including status and timestamps.
        /// </summary>
        public DataValue OperationalMode { get; init; } = DataValue.Null;

        /// <summary>
        /// The ProtectiveStop value, including status and timestamps.
        /// </summary>
        public DataValue ProtectiveStop { get; init; } = DataValue.Null;

        /// <summary>
        /// Emergency-stop function entries.
        /// </summary>
        public ArrayOf<SafetyFunctionSnapshot> EmergencyStopFunctions { get; init; } = [];

        /// <summary>
        /// Protective-stop function entries.
        /// </summary>
        public ArrayOf<SafetyFunctionSnapshot> ProtectiveStopFunctions { get; init; } = [];
    }

    /// <summary>
    /// Snapshot of an emergency-stop or protective-stop function.
    /// </summary>
    public sealed record SafetyFunctionSnapshot
    {
        /// <summary>
        /// The function instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The manufacturer-specific function name.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// The Active value, including status and timestamps.
        /// </summary>
        public DataValue Active { get; init; } = DataValue.Null;

        /// <summary>
        /// The optional Enabled value, including status and timestamps.
        /// </summary>
        public DataValue Enabled { get; init; } = DataValue.Null;
    }

    /// <summary>
    /// Read-model snapshot of a TaskControlType instance.
    /// </summary>
    public sealed record TaskControlSnapshot
    {
        /// <summary>
        /// The task-control identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The optional execution mode, including status and timestamps.
        /// </summary>
        public DataValue ExecutionMode { get; init; } = DataValue.Null;

        /// <summary>
        /// Whether a task program is loaded, including status and timestamps.
        /// </summary>
        public DataValue TaskProgramLoaded { get; init; } = DataValue.Null;

        /// <summary>
        /// The loaded task-program name, including status and timestamps.
        /// </summary>
        public DataValue TaskProgramName { get; init; } = DataValue.Null;

        /// <summary>
        /// The TaskControlOperation instance, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId TaskControlOperationId { get; init; } = NodeId.Null;

        /// <summary>
        /// Task modules exposed by this task control.
        /// </summary>
        public ArrayOf<NodeId> TaskModuleIds { get; init; } = [];
    }

    /// <summary>
    /// Read-model snapshot of a TaskModuleType instance.
    /// </summary>
    public sealed record TaskModuleSnapshot
    {
        /// <summary>
        /// The task-module instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The task-module name.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// The task-module version.
        /// </summary>
        public string? Version { get; init; }

        /// <summary>
        /// Whether another task module references this module.
        /// </summary>
        public DataValue IsReferenced { get; init; } = DataValue.Null;
    }

    /// <summary>
    /// Focused server-independent read projection of a discovered Robotics topology.
    /// </summary>
    public sealed record RoboticsTopologySnapshot
    {
        /// <summary>
        /// Motion-device systems in the topology.
        /// </summary>
        public ArrayOf<MotionDeviceSystemSnapshot> Systems { get; init; } = [];

        /// <summary>
        /// Controllers in the topology.
        /// </summary>
        public ArrayOf<ControllerSnapshot> Controllers { get; init; } = [];

        /// <summary>
        /// Motion devices in the topology.
        /// </summary>
        public ArrayOf<MotionDeviceSnapshot> MotionDevices { get; init; } = [];

        /// <summary>
        /// Axes in the topology.
        /// </summary>
        public ArrayOf<AxisSnapshot> Axes { get; init; } = [];

        /// <summary>
        /// Loads in the topology.
        /// </summary>
        public ArrayOf<LoadSnapshot> Loads { get; init; } = [];

        /// <summary>
        /// Power trains in the topology.
        /// </summary>
        public ArrayOf<PowerTrainSnapshot> PowerTrains { get; init; } = [];

        /// <summary>
        /// Motors in the topology.
        /// </summary>
        public ArrayOf<MotorSnapshot> Motors { get; init; } = [];

        /// <summary>
        /// Gears in the topology.
        /// </summary>
        public ArrayOf<GearSnapshot> Gears { get; init; } = [];

        /// <summary>
        /// Drives in the topology.
        /// </summary>
        public ArrayOf<DriveSnapshot> Drives { get; init; } = [];

        /// <summary>
        /// Safety states in the topology.
        /// </summary>
        public ArrayOf<SafetyStateSnapshot> SafetyStates { get; init; } = [];

        /// <summary>
        /// Task controls in the topology.
        /// </summary>
        public ArrayOf<TaskControlSnapshot> TaskControls { get; init; } = [];

        /// <summary>
        /// Task modules in the topology.
        /// </summary>
        public ArrayOf<TaskModuleSnapshot> TaskModules { get; init; } = [];

        /// <summary>
        /// Semantic Robotics relationships observed in the topology.
        /// </summary>
        public RoboticsRelationshipSnapshot Relationships { get; init; } = new();
    }
}
