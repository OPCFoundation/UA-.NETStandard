/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// A structure of the OPC 40001 series that a build actually materialised
    /// and wired. A conformance unit is advertised exactly when its facet was
    /// recorded, so the server never claims a unit whose structure is not
    /// there.
    /// </summary>
    internal enum MachineryFacet
    {
        Machines,
        MachineIdentification,
        ComponentIdentification,

        /// <summary>
        /// The machine nameplate publishes its writable members on every
        /// instance and leaves them writable.
        /// </summary>
        MachineIdentificationWritable,

        /// <summary>
        /// Every component nameplate publishes the mandatory members.
        /// </summary>
        ComponentIdentificationMandatory,

        /// <summary>
        /// Every component nameplate leaves its writable members writable.
        /// </summary>
        ComponentIdentificationWritable,
        Components,
        BuildingBlockOrganization,
        Monitoring,
        MachineryItemState,
        OperationMode,
        OperationCounters,
        LifetimeCounters,
        MachineryEquipment,
        EquipmentLife,
        Notifications,
        ProcessValues,
        ProcessValueSetpoint,
        ProcessValueStatus,
        ProcessValueLimits,
        ProcessValueLimitAlarm,
        ProcessValueDeviationBase,
        ProcessValueDeviationAlarm,
        ProcessValueDeviationAutoAdjustment,
        ProcessValueDeviationSensitivity,
        ProcessValuePercentage,

        /// <summary>
        /// At least one process value publishes the OPC 30081 simulation
        /// members on its analog signal.
        /// </summary>
        ProcessValuesSimulation,

        /// <summary>
        /// A device object carries a nameplate and points at the process
        /// values through OPC 30081's <c>ISignalSetType</c>.
        /// </summary>
        ProcessValuesDeviceObject,

        /// <summary>
        /// The device object's nameplate carries the simple device info.
        /// </summary>
        ProcessValuesSimpleDeviceInfo,
        ZeroPointAdjustment,
        JobManagement,
        JobResults,

        /// <summary>
        /// The predefined OPC 40001-3 job parameters are recognised and their
        /// declared types enforced.
        /// </summary>
        JobPredefinedParameters,
        EnergyBaseStructure,
        EnergyMainGrouping,
        EnergyNonElectrical,
        EnergyMassFlow,
        EnergyVolumeFlow,
        EnergyContains,
        ResultManagement,
        ResultVariables,
        ResultFiles,
        ResultEvents,

        /// <summary>
        /// Every exposed result carries the predefined OPC 40001-101 metadata.
        /// </summary>
        ResultPredefinedMetaData
    }

    /// <summary>
    /// Receives the facets a Machinery build materialised.
    /// </summary>
    internal interface IMachineryFacetSink
    {
        void RecordFacet(MachineryFacet facet);
    }
}
