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

using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Contracts
{
    public sealed partial record GmsSystemSnapshot
    {
        /// <summary>
        /// Gets optional native identification observations, or null when that surface is not configured.
        /// </summary>
        public GmsIdentificationObservation? Identification { get; init; }

        /// <summary>
        /// Gets the complete native sensor inventory, including explicitly empty inventories.
        /// </summary>
        public ArrayOf<GmsSensor> Sensors { get; init; }

        /// <summary>
        /// Gets optional native part-loading observations.
        /// </summary>
        public GmsLoadingObservation? Loading { get; init; }

        /// <summary>
        /// Gets optional native tool-monitoring observations.
        /// </summary>
        public GmsToolMonitoringObservation? ToolMonitoring { get; init; }

        /// <summary>
        /// Gets the configured static accessory slots. Their observed values can change on every acquisition.
        /// </summary>
        public ArrayOf<GmsAccessory> Accessories { get; init; }

        /// <summary>
        /// Gets the configured static analog additional-sensor slots.
        /// </summary>
        public ArrayOf<GmsAdditionalSensor> AdditionalSensors { get; init; }
    }

    /// <summary>
    /// Native GMS identification observations. Null DataValues omit optional fields; bad values preserve presence.
    /// Workspace contains an actual CartesianWorkspaceType or CylindricalWorkspaceType encoded structure.
    /// </summary>
    public sealed record GmsIdentificationObservation
    {
        /// <summary>
        /// Gets the more specific equipment classification, retaining its acquisition metadata.
        /// </summary>
        public DataValue SubDeviceClass { get; init; }

        /// <summary>
        /// Gets the native workspace geometry; no unit is invented because the released structure has no unit field.
        /// </summary>
        public DataValue Workspace { get; init; }
    }

    /// <summary>
    /// A native SensorType or MultiSensorType with shared controller identity and tool-life semantics.
    /// </summary>
    public sealed record GmsSensor
    {
        /// <summary>
        /// Gets inherited scalar tool observations and the stable slot key.
        /// Kind selects Tool or MultiTool. Nested tools are supplied through Sensors instead of Tool.Tools.
        /// </summary>
        public MachineToolTool Tool { get; init; } = new();

        /// <summary>
        /// Gets the constituents of a MultiSensor. A single sensor cannot contain further tools.
        /// </summary>
        public ArrayOf<GmsSensor> Sensors { get; init; }

        /// <summary>
        /// Gets the mandatory single-sensor UInteger classification, retaining its original numeric representation.
        /// </summary>
        public DataValue Class { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets optional sensor capabilities as a homogeneous UInteger array with the standard EnumStrings.
        /// </summary>
        public DataValue Capabilities { get; init; }

        /// <summary>
        /// Gets the optional ToolAlignmentState observation.
        /// </summary>
        public DataValue Alignment { get; init; }

        /// <summary>
        /// Gets optional names of the sensor's axes.
        /// </summary>
        public DataValue Axes { get; init; }

        /// <summary>
        /// Gets the optional ToolIsQualifiedStatus observation, without deriving it from a command.
        /// </summary>
        public DataValue IsQualifiedStatus { get; init; }

        /// <summary>
        /// Gets whether the single sensor is an absolute probe.
        /// </summary>
        public DataValue AbsoluteProbe { get; init; }

        /// <summary>
        /// Gets the single sensor's native engineering-unit structure.
        /// </summary>
        public DataValue EngineeringUnit { get; init; }

        /// <summary>
        /// Gets the single sensor's measuring-range magnitude.
        /// </summary>
        public DataValue MeasuringRange { get; init; }

        /// <summary>
        /// Gets the single sensor's working-range magnitude.
        /// </summary>
        public DataValue WorkingRange { get; init; }

        /// <summary>
        /// Gets the single sensor's positive resolution.
        /// </summary>
        public DataValue Resolution { get; init; }

        /// <summary>
        /// Gets the required single-sensor qualification countdown. Multisensors have no own ToolLife.
        /// </summary>
        public GmsQualifiedLife? Qualified { get; init; }
    }

    /// <summary>
    /// An equipment-reported qualification lifetime in hours. Publication never restarts or invents the countdown.
    /// </summary>
    public sealed record GmsQualifiedLife
    {
        /// <summary>
        /// Gets remaining qualified hours as a Double DataValue, including negative values after expiry.
        /// </summary>
        public DataValue RemainingHours { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets optional initial qualified hours.
        /// </summary>
        public DataValue StartHours { get; init; }

        /// <summary>
        /// Gets an optional warning threshold in hours.
        /// </summary>
        public DataValue WarningHours { get; init; }

        /// <summary>
        /// Gets an optional replacement threshold in hours.
        /// </summary>
        public DataValue LimitHours { get; init; }
    }

    /// <summary>
    /// Native loading observations; safe-loading position is monitoring information, never a motion permission.
    /// </summary>
    public sealed record GmsLoadingObservation
    {
        /// <summary>
        /// Gets the inherited element name.
        /// </summary>
        public string Name { get; init; } = "Loading";

        /// <summary>
        /// Gets the mandatory UInteger state: Unknown, Empty, Filled or InProgress (0 through 3).
        /// </summary>
        public DataValue LoadStatus { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets the optional observed loading-position flag.
        /// </summary>
        public DataValue IsInLoadingPosition { get; init; }
    }

    /// <summary>
    /// Observes active native tools by stable local slot keys or already assigned local NodeIds.
    /// </summary>
    public sealed record GmsToolMonitoringObservation
    {
        /// <summary>
        /// Gets the inherited working-unit name.
        /// </summary>
        public string Name { get; init; } = "Sensors";

        /// <summary>
        /// Gets active slot keys, translated to actual prospective Sensor/MultiSensor NodeIds.
        /// Null omits ActiveTool; empty explicitly reports no active tools.
        /// </summary>
        public ArrayOf<string> ActiveSensorKeys { get; init; }

        /// <summary>
        /// Gets an alternative native NodeId-array observation. It is mutually exclusive with ActiveSensorKeys.
        /// Every NodeId must name an actual sensor of this instance, including during dynamic updates.
        /// </summary>
        public DataValue ActiveToolIds { get; init; }

        /// <summary>
        /// Gets acquisition metadata for the name and key-based ActiveTool observation.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// Selects a released accessory type, not a mechanical-control operation.
    /// </summary>
    public enum GmsAccessoryKind
    {
        /// <summary>
        /// A native rotary table.
        /// </summary>
        RotaryTable,

        /// <summary>
        /// A native sensor exchange rack.
        /// </summary>
        SensorExchangeRack,

        /// <summary>
        /// A native tip exchange rack.
        /// </summary>
        TipExchangeRack
    }

    /// <summary>
    /// Shared MachineryItemIdentification fields of an accessory's native Identification AddIn.
    /// </summary>
    /// <param name="Manufacturer">The actual manufacturer.</param>
    /// <param name="SerialNumber">The actual serial number.</param>
    public sealed record GmsItemIdentification(LocalizedText Manufacturer, string SerialNumber)
    {
        /// <summary>
        /// Gets the optional manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>
        /// Gets the optional model.
        /// </summary>
        public LocalizedText Model { get; init; }

        /// <summary>
        /// Gets the optional product code.
        /// </summary>
        public string? ProductCode { get; init; }
    }

    /// <summary>
    /// A static accessory slot with dynamically observed native state.
    /// </summary>
    public sealed record GmsAccessory
    {
        /// <summary>
        /// Gets the unique application browse name.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the released concrete accessory type.
        /// </summary>
        public GmsAccessoryKind Kind { get; init; }

        /// <summary>
        /// Gets the actual accessory's required nameplate.
        /// </summary>
        public GmsItemIdentification? Identification { get; init; }

        /// <summary>
        /// Gets the required rotary-table Boolean observation.
        /// </summary>
        public DataValue IsIntegrated { get; init; }

        /// <summary>
        /// Gets the required rotary-table Byte observation, not a narrowed Int32.
        /// </summary>
        public DataValue NumberOfAxes { get; init; }

        /// <summary>
        /// Gets the required sensor- or tip-rack Boolean observation.
        /// </summary>
        public DataValue IsAvailable { get; init; }
    }

    /// <summary>
    /// A native AdditionalSensorType observation with explicit engineering units.
    /// </summary>
    public sealed record GmsAdditionalSensor
    {
        /// <summary>
        /// Gets the unique application browse name.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the provider's actual global sensor identifier.
        /// </summary>
        public string Identifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets an optional operator-facing sensor name.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// Gets the original finite scalar Number sample, including status and acquisition time.
        /// </summary>
        public DataValue Value { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets the required engineering unit; it is not inferred from the classification.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// Gets the optional UInteger class: Other, Temperature, Vibration or Humidity (0 through 3).
        /// </summary>
        public DataValue Class { get; init; }
    }
}
