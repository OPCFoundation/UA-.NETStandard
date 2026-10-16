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
    /// <summary>
    /// Couples a resolved variable with its unconverted data sample. Absent is distinct from present but bad.
    /// </summary>
    /// <param name="NodeId">The actual variable identifier, or NodeId.Null when absent.</param>
    /// <param name="Value">The raw sample, preserving numeric representation, status and both timestamps.</param>
    public readonly record struct GmsVariableReading(NodeId NodeId, DataValue Value)
    {
        /// <summary>
        /// Gets whether the optional variable is modeled, independently of its current data quality.
        /// </summary>
        public bool IsPresent => !NodeId.IsNull;
    }

    /// <summary>
    /// Identifies a discovered native or vendor-derived GMS without interpreting a namespace as a profile claim.
    /// </summary>
    /// <param name="NodeId">The actual instance.</param>
    /// <param name="TypeDefinitionId">Its actual type definition.</param>
    /// <param name="BrowseName">Its namespace-qualified browse name.</param>
    /// <param name="DisplayName">Its operator-facing name.</param>
    public sealed record GmsDeviceEntry(
        NodeId NodeId, NodeId TypeDefinitionId, QualifiedName BrowseName, LocalizedText DisplayName);

    /// <summary>
    /// Reads native identification without coercing workspace ExtensionObjects or discarding raw quality.
    /// </summary>
    /// <param name="NodeId">The actual derived nameplate.</param>
    /// <param name="Manufacturer">The mandatory DI manufacturer.</param>
    /// <param name="SerialNumber">The mandatory DI serial number.</param>
    /// <param name="ProductInstanceUri">The mandatory product-instance URI.</param>
    /// <param name="SubDeviceClass">The optional native subclass.</param>
    /// <param name="Workspace">The optional actual Cartesian or cylindrical workspace sample.</param>
    public sealed record GmsIdentificationReadSnapshot(
        NodeId NodeId, GmsVariableReading Manufacturer, GmsVariableReading SerialNumber,
        GmsVariableReading ProductInstanceUri, GmsVariableReading SubDeviceClass, GmsVariableReading Workspace);

    /// <summary>
    /// Reads native loading information without treating a position observation as a motion command.
    /// </summary>
    /// <param name="NodeId">The actual loading-monitoring object.</param>
    /// <param name="LoadStatus">The required UInteger classification.</param>
    /// <param name="IsInLoadingPosition">The optional Boolean observation.</param>
    public sealed record GmsLoadingReadSnapshot(
        NodeId NodeId, GmsVariableReading LoadStatus, GmsVariableReading IsInLoadingPosition);

    /// <summary>
    /// Reads native active-tool references with original acquisition metadata.
    /// </summary>
    /// <param name="NodeId">The actual tool-monitoring object.</param>
    /// <param name="ActiveTool">The optional actual NodeId-array sample.</param>
    public sealed record GmsToolMonitoringReadSnapshot(NodeId NodeId, GmsVariableReading ActiveTool);

    /// <summary>
    /// Reads mandatory inherited monitoring and selected native scalar observations.
    /// Each field retains its own acquisition timestamp; this is not an atomic server-wide historical snapshot.
    /// </summary>
    /// <param name="NodeId">The actual GMS instance.</param>
    /// <param name="Inherited">The shared MachineTool monitoring and active program.</param>
    /// <param name="Identification">The actual native nameplate and workspace.</param>
    /// <param name="Loading">The optional loading monitor, null when absent.</param>
    /// <param name="ToolMonitoring">The optional active-tool monitor, null when absent.</param>
    public sealed record GmsSystemReadSnapshot(
        NodeId NodeId, MachineToolReadSnapshot Inherited, GmsIdentificationReadSnapshot Identification,
        GmsLoadingReadSnapshot? Loading, GmsToolMonitoringReadSnapshot? ToolMonitoring);

    /// <summary>
    /// Reads a native Sensor or MultiSensor, composing shared identity/life values with native raw observations.
    /// </summary>
    /// <param name="Inherited">The actual inherited tool identity and tool-life samples, including Qualified.</param>
    /// <param name="Class">The required single-sensor classification, absent for a multisensor.</param>
    /// <param name="Capabilities">The optional native capabilities.</param>
    /// <param name="Alignment">The optional native alignment.</param>
    /// <param name="Axes">The optional native axes.</param>
    /// <param name="IsQualifiedStatus">The optional native qualification state.</param>
    /// <param name="AbsoluteProbe">The optional absolute-probe observation.</param>
    /// <param name="EngineeringUnit">The optional single-sensor engineering unit.</param>
    /// <param name="MeasuringRange">The optional measuring range.</param>
    /// <param name="WorkingRange">The optional working range.</param>
    /// <param name="Resolution">The optional resolution.</param>
    /// <param name="Sensors">The actual constituent native sensors of a multisensor.</param>
    public sealed record GmsSensorReadSnapshot(
        MachineToolToolReadSnapshot Inherited, GmsVariableReading Class, GmsVariableReading Capabilities,
        GmsVariableReading Alignment, GmsVariableReading Axes, GmsVariableReading IsQualifiedStatus,
        GmsVariableReading AbsoluteProbe, GmsVariableReading EngineeringUnit, GmsVariableReading MeasuringRange,
        GmsVariableReading WorkingRange, GmsVariableReading Resolution, ArrayOf<GmsSensorReadSnapshot> Sensors);

    /// <summary>
    /// Reads the mandatory native item nameplate of an accessory, without assuming a component-type substitute.
    /// </summary>
    /// <param name="NodeId">The actual MachineryItemIdentification AddIn.</param>
    /// <param name="Manufacturer">The mandatory manufacturer sample.</param>
    /// <param name="SerialNumber">The mandatory serial-number sample.</param>
    public sealed record GmsItemIdentificationReadSnapshot(
        NodeId NodeId, GmsVariableReading Manufacturer, GmsVariableReading SerialNumber);

    /// <summary>
    /// Reads one concrete native accessory and its actual type and nameplate.
    /// </summary>
    /// <param name="NodeId">The actual accessory.</param>
    /// <param name="TypeDefinitionId">Its actual standard or vendor type.</param>
    /// <param name="BrowseName">Its qualified browse name.</param>
    /// <param name="Kind">The recognized native accessory kind.</param>
    /// <param name="Identification">Its required actual nameplate.</param>
    /// <param name="IsIntegrated">The rotary-table observation, absent on racks.</param>
    /// <param name="NumberOfAxes">The Byte rotary-table observation, absent on racks.</param>
    /// <param name="IsAvailable">The rack observation, absent on rotary tables.</param>
    public sealed record GmsAccessoryReadSnapshot(
        NodeId NodeId, NodeId TypeDefinitionId, QualifiedName BrowseName, GmsAccessoryKind Kind,
        GmsItemIdentificationReadSnapshot Identification, GmsVariableReading IsIntegrated,
        GmsVariableReading NumberOfAxes, GmsVariableReading IsAvailable);

    /// <summary>
    /// Reads one actual AdditionalSensorType variable without narrowing its native Number representation.
    /// </summary>
    /// <param name="NodeId">The actual analog variable.</param>
    /// <param name="TypeDefinitionId">Its actual standard or vendor variable type.</param>
    /// <param name="BrowseName">Its qualified browse name.</param>
    /// <param name="Value">The unconverted Number sample.</param>
    /// <param name="Identifier">Its mandatory identity.</param>
    /// <param name="Name">Its optional name.</param>
    /// <param name="Class">Its optional native class.</param>
    /// <param name="EngineeringUnits">Its mandatory engineering unit.</param>
    public sealed record GmsAdditionalSensorReadSnapshot(
        NodeId NodeId, NodeId TypeDefinitionId, QualifiedName BrowseName, DataValue Value,
        GmsVariableReading Identifier, GmsVariableReading Name, GmsVariableReading Class,
        GmsVariableReading EngineeringUnits);

    /// <summary>
    /// A bounded native inventory, retaining the shared tool list's topology version and native descendants.
    /// </summary>
    /// <param name="NodeId">The actual GMS root.</param>
    /// <param name="Inherited">The shared actual tool inventory and version.</param>
    /// <param name="Sensors">The native top-level sensor inventory.</param>
    /// <param name="Accessories">The native static accessories.</param>
    /// <param name="AdditionalSensors">The native static analog additional sensors.</param>
    public sealed record GmsEquipmentReadSnapshot(
        NodeId NodeId, MachineToolEquipmentReadSnapshot Inherited, ArrayOf<GmsSensorReadSnapshot> Sensors,
        ArrayOf<GmsAccessoryReadSnapshot> Accessories, ArrayOf<GmsAdditionalSensorReadSnapshot> AdditionalSensors);

    /// <summary>
    /// Reads the actual native calibration forecast without executing a calibration or dereferencing certificate URIs.
    /// </summary>
    /// <param name="NodeId">The actual calibration forecast.</param>
    /// <param name="TypeDefinitionId">Its actual standard or vendor type.</param>
    /// <param name="PredictedTime">The inherited provider prediction.</param>
    /// <param name="Calibrated">The required state.</param>
    /// <param name="DateOfCalibration">The required actual last calibration date.</param>
    /// <param name="CalibrationInterval">The optional Duration interval.</param>
    /// <param name="CalibrationPreptime">The optional Duration preparation lead time.</param>
    /// <param name="CalibrationCertificate">The optional references to all available calibration certificates.</param>
    public sealed record GmsCalibrationReadSnapshot(
        NodeId NodeId, NodeId TypeDefinitionId, GmsVariableReading PredictedTime,
        GmsVariableReading Calibrated, GmsVariableReading DateOfCalibration, GmsVariableReading CalibrationInterval,
        GmsVariableReading CalibrationPreptime, GmsVariableReading CalibrationCertificate);

    /// <summary>
    /// Reads a native Number-valued catalog without changing its declared scalar or array representation.
    /// </summary>
    /// <param name="NodeId">The actual catalog variable.</param>
    /// <param name="Value">Its original Number or Number-array sample.</param>
    /// <param name="CatalogName">Its required external catalog name.</param>
    /// <param name="CatalogEntry">Its required external catalog entry.</param>
    /// <param name="EnumValues">Its inherited enumeration metadata.</param>
    /// <param name="ValueAsText">Its inherited textual observation.</param>
    public sealed record GmsCatalogReadSnapshot(
        NodeId NodeId, DataValue Value, GmsVariableReading CatalogName, GmsVariableReading CatalogEntry,
        GmsVariableReading EnumValues, GmsVariableReading ValueAsText);

    /// <summary>
    /// Reads defined native metadata on an actual retained GMS part.
    /// </summary>
    /// <param name="Inherited">The actual inherited part and state.</param>
    /// <param name="PartDescription">The optional description.</param>
    /// <param name="PartAmendmentStatus">The optional amendment status.</param>
    /// <param name="ProductionNumber">The optional production number.</param>
    /// <param name="NestIdentifier">The optional native nest catalog.</param>
    /// <param name="Operator">The optional native operator catalog.</param>
    /// <param name="PartCarrierIdentifier">The optional native carrier catalog.</param>
    /// <param name="ProcessingMachineIdentifier">The optional native machine catalog.</param>
    /// <param name="ProcessParameter">The optional native Number-array parameter catalog.</param>
    public sealed record GmsPartReadSnapshot(
        MachineToolPartReadSnapshot Inherited, GmsVariableReading PartDescription,
        GmsVariableReading PartAmendmentStatus,
        GmsVariableReading ProductionNumber, GmsCatalogReadSnapshot? NestIdentifier, GmsCatalogReadSnapshot? Operator,
        GmsCatalogReadSnapshot? PartCarrierIdentifier, GmsCatalogReadSnapshot? ProcessingMachineIdentifier,
        GmsCatalogReadSnapshot? ProcessParameter);

    /// <summary>
    /// Reads native metadata and actual current-run parts on a retained GMS job.
    /// </summary>
    /// <param name="Inherited">The actual inherited job, programs, part sets and state history.</param>
    /// <param name="BatchIdentifier">The optional native batch identity.</param>
    /// <param name="Duration">The optional elapsed duration.</param>
    /// <param name="RemainingTime">The optional provider-reported remaining duration.</param>
    /// <param name="MeasurementReason">The optional native measurement reason.</param>
    /// <param name="Parts">The bounded native metadata of its actual individual parts.</param>
    public sealed record GmsJobReadSnapshot(
        MachineToolJobReadSnapshot Inherited, GmsVariableReading BatchIdentifier, GmsVariableReading Duration,
        GmsVariableReading RemainingTime, GmsVariableReading MeasurementReason, ArrayOf<GmsPartReadSnapshot> Parts);

    /// <summary>
    /// Composes retained MachineTool production with the defined GMS job/part extensions.
    /// It does not invent GMS_ProductionStatisticsType or claim the affected complete production facet.
    /// </summary>
    /// <param name="Inherited">The actual retained production, including defined inherited statistics.</param>
    /// <param name="Jobs">Native metadata of the actual retained jobs.</param>
    public sealed record GmsProductionReadSnapshot(
        MachineToolProductionReadSnapshot Inherited, ArrayOf<GmsJobReadSnapshot> Jobs);
}
