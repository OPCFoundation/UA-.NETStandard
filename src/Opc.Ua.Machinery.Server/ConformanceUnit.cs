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

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// The names of the OPC 40001 conformance units a Machinery node manager
    /// can advertise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every value below is the literal title of a row in the
    /// "Conformance Units" table of the matching part, as published at
    /// <c>reference.opcfoundation.org</c> for the exact model version this
    /// repository vendors. Most also appear verbatim as a
    /// <c>&lt;Category&gt;</c> element in the NodeSet:
    /// <code>
    /// grep -o '&lt;Category&gt;[^&lt;]*&lt;/Category&gt;' src/Opc.Ua.Machinery*/Model/*.NodeSet2.xml | sort -u
    /// </code>
    /// The NodeSet carries a category only where a <em>type</em> belongs to a
    /// unit, so the units that talk about instances or methods — the result
    /// methods, the building-block organizer, the energy structure — exist
    /// only in the specification text.
    /// </para>
    /// <para>
    /// They are declared here rather than inline so the advertised set can be
    /// reviewed in one place and a typo cannot be introduced per usage site.
    /// </para>
    /// </remarks>
    internal static class ConformanceUnits
    {
        /// <summary>The <c>Machines</c> folder is exposed.</summary>
        public const string FindMachines = "Machinery Find Machines";

        /// <summary>A machine publishes a machine identification add-in.</summary>
        public const string MachineIdentification = "Machinery Machine Identification";

        /// <summary>A machine component publishes a component identification add-in.</summary>
        public const string ComponentIdentification = "Machinery Component Identification";

        /// <summary>
        /// The machine nameplate's writable members are present and writable.
        /// </summary>
        public const string MachineIdentificationWritable =
            "Machinery Machine Identification Writable";

        /// <summary>
        /// Every component nameplate carries the mandatory members.
        /// </summary>
        public const string ComponentIdentificationMandatory =
            "Machinery Component Identification Mandatory";

        /// <summary>
        /// Every component nameplate's writable members are present and writable.
        /// </summary>
        public const string ComponentIdentificationWritable =
            "Machinery Component Identification Writable";

        /// <summary>A machine publishes its components.</summary>
        public const string FindComponentsOfMachines =
            "Machinery Find Components of Machines";

        /// <summary>
        /// A machinery item organizes its building blocks below the
        /// <c>MachineryBuildingBlocks</c> folder (OPC 40001-1 §7.1).
        /// </summary>
        public const string BuildingBlockOrganization =
            "Machinery Building Block Organization";

        /// <summary>A machine publishes the monitoring add-in.</summary>
        public const string Monitoring = "Machinery Monitoring";

        /// <summary>A machinery item publishes its item state machine.</summary>
        public const string MachineryItemState = "Machinery MachineryItem State";

        /// <summary>A machinery item publishes its operation-mode state machine.</summary>
        public const string OperationMode = "Machinery Operation Mode";

        /// <summary>A machinery item publishes operation counters.</summary>
        public const string OperationCounter = "Machinery Operation Counter";

        /// <summary>A machinery item publishes lifetime counters.</summary>
        public const string LifetimeCounter = "Machinery Lifetime Counter";

        /// <summary>A machine publishes the machinery-equipment folder.</summary>
        public const string MachineryEquipment = "Machinery MachineryEquipment";

        /// <summary>A machine publishes the notifications add-in.</summary>
        public const string Notifications = "Machinery Notifications";

        /// <summary>
        /// The <c>ProcessValueType</c> and <c>ProcessValueSetpointVariableType</c>
        /// are exposed. A type-exposure unit: loading the model satisfies it.
        /// </summary>
        public const string ProcessValuesBaseTypes = "Machinery Process Values Base Types";

        /// <summary>
        /// The <c>ProcessValueSetpointVariableType</c> is exposed. A
        /// type-exposure unit.
        /// </summary>
        public const string ProcessValuesBaseSetpointType =
            "Machinery Process Values Base SetpointType";

        /// <summary>
        /// The <c>ZeroPointAdjustmentEventType</c> is exposed. A type-exposure
        /// unit.
        /// </summary>
        public const string ProcessValuesBaseEventTypes =
            "Machinery Process Values Base EventTypes";

        /// <summary>At least one <c>ProcessValueType</c> instance exists.</summary>
        public const string ProcessValuesAnalogObjectInstances =
            "Machinery Process Values Analog Object Instances";

        /// <summary>At least one process value publishes a setpoint.</summary>
        public const string ProcessValuesBaseProcessValueSetpoint =
            "Machinery Process Values Base Process Value Setpoint";

        /// <summary>
        /// At least one process value generates zero-point-adjustment events.
        /// </summary>
        public const string ProcessValuesZeroPointAdjustmentEvents =
            "Machinery Process Values ZeroPointAdjustment Events";

        /// <summary>At least one signal publishes <c>PercentageValue</c>.</summary>
        public const string ProcessValuesPercentageValue =
            "Machinery Process Values Percentage Value";

        /// <summary>At least one signal publishes a limit variable.</summary>
        public const string ProcessValuesLimitsBase = "Machinery Process Values Limits Base";

        /// <summary>At least one process value publishes an exclusive limit alarm.</summary>
        public const string ProcessValuesLimitsAlarm =
            "Machinery Process Values Limits Alarm";

        /// <summary>At least one process value publishes the <c>LimitAlarm</c> object.</summary>
        public const string ProcessValuesLimitsAlarmObject =
            "Machinery Process Values Limits Alarm Object";

        /// <summary>At least one setpoint publishes a deviation variable.</summary>
        public const string ProcessValuesDeviationBase =
            "Machinery Process Values Deviation Base";

        /// <summary>
        /// At least one process value publishes an exclusive deviation alarm.
        /// </summary>
        public const string ProcessValuesDeviationAlarm =
            "Machinery Process Values Deviation Alarm";

        /// <summary>
        /// At least one process value publishes the <c>DeviationAlarm</c> object.
        /// </summary>
        public const string ProcessValuesDeviationAlarmObject =
            "Machinery Process Values Deviation Alarm Object";

        /// <summary>
        /// At least one setpoint publishes <c>AutoDeviationAdjustment</c>.
        /// </summary>
        public const string ProcessValuesDeviationAutoAdjustment =
            "Machinery Process Values Deviation AutoAdjustment";

        /// <summary>At least one setpoint publishes <c>DeviationSensitivity</c>.</summary>
        public const string ProcessValuesDeviationSensitivity =
            "Machinery Process Values Deviation Sensitivity";

        /// <summary>At least one process value publishes the <c>Status</c> variable.</summary>
        public const string ProcessValuesMonitoring = "Machinery Process Values Monitoring";

        /// <summary>
        /// At least one process value publishes the <c>AlarmSuppression</c>
        /// variable.
        /// </summary>
        public const string ProcessValuesAlarmSuppression =
            "Machinery Process Values AlarmSuppression";

        /// <summary>
        /// At least one analog signal publishes OPC 30081's
        /// <c>ActualValue</c>, <c>SimulationValue</c> and
        /// <c>SimulationState</c>.
        /// </summary>
        /// <remarks>
        /// The unit belongs to OPC 30081 PA-DIM, not to OPC 40001-2, and is
        /// reproduced with the prefix the published table carries.
        /// </remarks>
        public const string ProcessValuesSimulation =
            "PA-DIM AnalogSignalVariable Simulation";

        /// <summary>
        /// A device object implementing <c>ISignalSetType</c> points at the
        /// process values.
        /// </summary>
        public const string ProcessValuesDeviceObject =
            "Machinery Process Values Device Object";

        /// <summary>
        /// The device object publishes the simple device information.
        /// </summary>
        public const string ProcessValuesSimpleDeviceInfo =
            "Machinery Process Values Simple Device Info";

        /// <summary>
        /// A <c>JobManagementType</c> instance is published as an AddIn below
        /// a <c>MachineryBuildingBlocks</c> folder.
        /// </summary>
        public const string JobManagementBase = "Machinery Job Management Base";

        /// <summary>
        /// The conformance units of OPC 40001-3's predefined job parameters,
        /// spelled exactly as §9 publishes them.
        /// </summary>
        /// <remarks>
        /// Reported together, because the library recognises and type-checks
        /// the whole predefined set rather than a subset of it. The names are
        /// reproduced literally: a conformance tool compares them as strings.
        /// </remarks>
        public static readonly string[] JobPredefinedParameters =
        [
            "Machinery Job Management Planned ComponentName",
            "Machinery Job Management Planned CustomerOrderNumbers",
            "Machinery Job Management Planned Customers",
            "Machinery Job Management Planned DrawingNumber",
            "Machinery Job Management Planned DrawingVersionNumber",
            "Machinery Job Management Planned ExecutionMode",
            "Machinery Job Management Planned JobAnnotation",
            "Machinery Job Management Planned JobName",
            "Machinery Job Management Planned Location",
            "Machinery Job Management Planned OrderNumbers",
            "Machinery Job Management Planned PlannedDuration",
            "Machinery Job Management Planned PlannedOrderQuantity",
            "Machinery Job Management Planned PlannedProductionTime",
            "Machinery Job Management Planned PlannedQuantityPerRun",
            "Machinery Job Management Planned PlannedSetupTime",
            "Machinery Job Management Planned PlannedTimePerRun",
            "Machinery Job Management Planned RelatedContainer",
            "Machinery Job Management Result ActualProductionTime",
            "Machinery Job Management Result ActualQuantityCurrentRun",
            "Machinery Job Management Result ActualUnitDelayTime",
            "Machinery Job Management Result ActualUnitSetupTime",
            "Machinery Job Management Result BOM",
            "Machinery Job Management Result ComponentName",
            "Machinery Job Management Result CustomerOrderNumbers",
            "Machinery Job Management Result Customers",
            "Machinery Job Management Result DrawingNumber",
            "Machinery Job Management Result DrawingVersionNumber",
            "Machinery Job Management Result EndTime",
            "Machinery Job Management Result EstimatedRemainingTime",
            "Machinery Job Management Result ExecutionMode",
            "Machinery Job Management Result GoodQuantity",
            "Machinery Job Management Result JobName",
            "Machinery Job Management Result JobResult",
            "Machinery Job Management Result Location",
            "Machinery Job Management Result OrderNumbers",
            "Machinery Job Management Result PerformanceInfo",
            "Machinery Job Management Result ProducedQuantity",
            "Machinery Job Management Result RelatedContainer",
            "Machinery Job Management Result RunsCompleted",
            "Machinery Job Management Result RunsStarted",
            "Machinery Job Management Result StartTime",
            "Machinery Job Management Planned Base",
        ];

        /// <summary>
        /// The server accepts job-order strings of at least the length
        /// OPC 40001-3 requires.
        /// </summary>
        public const string JobManagementMinimumStringLength =
            "Machinery Job Management Minimum String Length";

        /// <summary>The job-order result surface is published.</summary>
        public const string JobManagementResultBase = "Machinery Job Management Result Base";

        /// <summary>
        /// A <c>Consumption</c> object carries at least one resource folder
        /// named after one of the well-known browse names.
        /// </summary>
        public const string EnergyBaseStructure = "Machinery Energy Base Structure";

        /// <summary>Every resource folder carries a <c>Main</c> metering point.</summary>
        public const string EnergyMainGrouping = "Machinery Energy Main grouping";

        /// <summary>
        /// A metering point implements <c>INonElectricalEnergyType</c>.
        /// </summary>
        public const string EnergyNonElectricalBase = "Machinery Energy Non Electrical Base";

        /// <summary>A metering point implements <c>IMassFlowType</c>.</summary>
        public const string EnergyNonElectricalMassFlow =
            "Machinery Energy Non Electrical Mass Flow";

        /// <summary>A metering point implements <c>IVolumeFlowType</c>.</summary>
        public const string EnergyNonElectricalVolumeFlow =
            "Machinery Energy Non Electrical Volume Flow";

        /// <summary>The <c>Contains</c> reference type is used.</summary>
        public const string EnergyContains = "Machinery Energy Contains";

        /// <summary>
        /// The result types are exposed. A type-exposure unit: loading the
        /// model satisfies it.
        /// </summary>
        public const string ResultTypes = "Machinery-Result Types";

        /// <summary>A result management object supports <c>GetLatestResult</c>.</summary>
        public const string ResultGetLatestResult = "Machinery-Result GetLatestResult";

        /// <summary>
        /// A result management object supports <c>GetResultById</c> and
        /// <c>ReleaseResultHandle</c>.
        /// </summary>
        public const string ResultGetResultById = "Machinery-Result GetResultById";

        /// <summary>
        /// A result management object supports <c>GetResultIdListFiltered</c>.
        /// </summary>
        public const string ResultGetResultsFiltered = "Machinery-Result GetResultsFiltered";

        /// <summary>
        /// A result management object supports <c>AcknowledgeResults</c>.
        /// </summary>
        /// <remarks>
        /// The published table spells this one without the hyphen the other
        /// result units carry; it is reproduced as published.
        /// </remarks>
        public const string ResultAcknowledgeResults = "Machinery Result AcknowledgeResults";

        /// <summary>The <c>Results</c> folder carries at least one result variable.</summary>
        public const string ResultVariables = "Machinery-Result ResultVariables";

        /// <summary>Result-ready events are generated.</summary>
        public const string ResultEvents = "Machinery-Result ResultEvents";

        /// <summary>Results are downloadable through the <c>ResultTransfer</c> object.</summary>
        public const string ResultFiles = "Machinery-Result ResultFiles";

        /// <summary>
        /// Every exposed result carries <c>ExternalRecipeId</c>,
        /// <c>InternalRecipeId</c>, <c>JobId</c>, <c>ProductId</c>,
        /// <c>StepId</c> and <c>CreationTime</c>.
        /// </summary>
        public const string ResultPredefinedResultMetaData =
            "Machinery-Result PredefinedResultMetaData";
    }

    /// <summary>
    /// The URIs of the OPC 40001 server facets a Machinery node manager can
    /// advertise on <c>Server/ServerCapabilities/ServerProfileArray</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each URI below is the published value from the "Profile URIs" table of
    /// the matching part. The three URI shapes are the specifications' own:
    /// parts 1, 3 and 101 use <c>/UA-Profile/Machinery/…</c> without a
    /// trailing slash, while part 2 uses <c>/UA/Machinery/ProcessValues/…</c>
    /// <em>with</em> one. They are reproduced exactly as published — a facet
    /// URI is matched literally by conformance tooling, so normalising them
    /// would break the match.
    /// </para>
    /// <para>
    /// A facet is advertised only when every conformance unit the
    /// specification marks mandatory for it, and that this library is
    /// responsible for, was materialised and wired at runtime. The base
    /// server's own units — address-space, view, attribute, method and
    /// event-subscription facets that the OPC 40001 facets also require —
    /// are reported by the stack and are not re-checked here.
    /// </para>
    /// </remarks>
    internal static class ServerProfiles
    {
        /// <summary>Machinery Machine Identification Server Facet.</summary>
        public const string MachineIdentification =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/MachineIdentification";

        /// <summary>Machinery Component Identification Server Facet.</summary>
        public const string ComponentIdentification =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/ComponentIdentification";

        /// <summary>Machinery State Server Facet.</summary>
        public const string State =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/State";

        /// <summary>Machinery Operation Counter Server Facet.</summary>
        public const string OperationCounter =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/OperationCounter";

        /// <summary>Machinery Lifetime Counter Server Facet.</summary>
        public const string LifetimeCounter =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/LifetimeCounter";

        /// <summary>Machinery Monitoring Server Facet.</summary>
        public const string Monitoring =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/Monitoring";

        /// <summary>Machinery MachineryEquipment Server Facet.</summary>
        public const string MachineryEquipment =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/MachineryEquipment";

        /// <summary>Machinery Notifications Server Facet.</summary>
        public const string Notifications =
            "http://opcfoundation.org/UA-Profile/Machinery/Server/Notifications";

        /// <summary>Machinery-Process Values Base Server Facet.</summary>
        public const string ProcessValuesBase =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/Base/";

        /// <summary>Machinery-Process Values Base Process Value Setpoint Server Facet.</summary>
        public const string ProcessValuesSetpoint =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/Setpoint/";

        /// <summary>Machinery-Process Values Percentage Value Server Facet.</summary>
        public const string ProcessValuesPercentageValue =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/PercentageValue/";

        /// <summary>Machinery-Process Values Limits Base Server Facet.</summary>
        public const string ProcessValuesLimitsBase =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/LimitsBase/";

        /// <summary>Machinery-Process Values Limits Alarm Server Facet.</summary>
        public const string ProcessValuesLimitsAlarm =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/LimitsAlarm/";

        /// <summary>Machinery-Process Values Limits Monitoring Server Facet.</summary>
        public const string ProcessValuesLimitsMonitoring =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/LimitsMonitoring/";

        /// <summary>Machinery-Process Values Limits Alarm Suppression Server Facet.</summary>
        public const string ProcessValuesLimitsAlarmSuppression =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/LimitsAlarmSuppression/";

        /// <summary>Machinery-Process Values Deviation Base Server Facet.</summary>
        public const string ProcessValuesDeviationBase =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/DeviationBase/";

        /// <summary>Machinery-Process Values Deviation Alarm Server Facet.</summary>
        public const string ProcessValuesDeviationAlarm =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/DeviationAlarm/";

        /// <summary>Machinery-Process Values Deviation Monitoring Server Facet.</summary>
        public const string ProcessValuesDeviationMonitoring =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/DeviationMonitoring/";

        /// <summary>
        /// Machinery-Process Values Deviation Alarm Suppression Server Facet.
        /// </summary>
        public const string ProcessValuesDeviationAlarmSuppression =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/" +
            "DeviationAlarmSuppression/";

        /// <summary>Machinery-Process Values Deviation AutoAdjustment Server Facet.</summary>
        public const string ProcessValuesDeviationAutoAdjustment =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/" +
            "DeviationAutoAdjustment/";

        /// <summary>
        /// Machinery-Process Values Zero Point Adjustment Base Server Facet.
        /// </summary>
        public const string ProcessValuesZeroPointAdjustmentBase =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/" +
            "ZeroPointAdjustmentBase/";

        /// <summary>
        /// Machinery-Process Values Zero Point Adjustment Events Server Facet.
        /// </summary>
        public const string ProcessValuesZeroPointAdjustmentEvents =
            "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/" +
            "ZeroPointAdjustmentEvents/";

        /// <summary>Machinery Job Management Base Server Facet.</summary>
        public const string JobManagementBase =
            "http://opcfoundation.org/UA-Profile/Machinery/Jobs/Server/Base";

        /// <summary>Machinery Energy Base Server Facet.</summary>
        public const string EnergyBase =
            "http://opcfoundation.org/UA-Profile/Machinery/Energy/Server/Base";

        /// <summary>Machinery-Result Simple Result Transfer.</summary>
        public const string ResultSimpleTransfer =
            "http://opcfoundation.org/UA-Profile/Machinery/Result/Server/SimpleResultTransfer";

        /// <summary>Machinery-Result Result Transfer.</summary>
        public const string ResultTransfer =
            "http://opcfoundation.org/UA-Profile/Machinery/Result/Server/ResultTransfer";

        /// <summary>Machinery-Result Result Transfer Variables.</summary>
        public const string ResultTransferVariables =
            "http://opcfoundation.org/UA-Profile/Machinery/Result/Server/" +
            "ResultTransferVariables";
    }
}
