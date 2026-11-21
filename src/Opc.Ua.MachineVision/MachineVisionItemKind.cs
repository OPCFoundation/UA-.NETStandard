// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Vision
{
    /// <summary>
    /// Component categories defined by OPC 40100-2 VisionSystemAssetType.
    /// </summary>
    public enum MachineVisionItemKind
    {
        /// <summary>
        /// Computing devices.
        /// </summary>
        ComputingDevice,

        /// <summary>
        /// Display units.
        /// </summary>
        DisplayUnit,

        /// <summary>
        /// Physical interfaces.
        /// </summary>
        PhysicalInterface,

        /// <summary>
        /// Image sensors.
        /// </summary>
        ImageSensor,

        /// <summary>
        /// Frame grabbers.
        /// </summary>
        FrameGrabber,

        /// <summary>
        /// Lenses.
        /// </summary>
        Lens,

        /// <summary>
        /// Lens controllers.
        /// </summary>
        LensController,

        /// <summary>
        /// Optical filters.
        /// </summary>
        OpticalFilter,

        /// <summary>
        /// Other optical equipment.
        /// </summary>
        OtherOpticalEquipment,

        /// <summary>
        /// Way encoders.
        /// </summary>
        WayEncoder,

        /// <summary>
        /// Trigger sensors.
        /// </summary>
        TriggerSensor,

        /// <summary>
        /// Lamps.
        /// </summary>
        Lamp,

        /// <summary>
        /// Lighting controllers.
        /// </summary>
        LightingController,

        /// <summary>
        /// Pattern generators.
        /// </summary>
        PatternGenerator,

        /// <summary>
        /// Calibration targets, optionally adopting an IA target.
        /// </summary>
        CalibrationTarget,

        /// <summary>
        /// Acquisition backgrounds.
        /// </summary>
        AcquisitionBackground,

        /// <summary>
        /// Surrounding environment.
        /// </summary>
        SurroundingEnvironment,

        /// <summary>
        /// Housings.
        /// </summary>
        Housing,

        /// <summary>
        /// Motion devices, optionally adopting a Robotics device.
        /// </summary>
        MotionDevice,

        /// <summary>
        /// Power supplies.
        /// </summary>
        PowerSupply,

        /// <summary>
        /// Climate controllers.
        /// </summary>
        ClimateController,

        /// <summary>
        /// Licenses.
        /// </summary>
        License,

        /// <summary>
        /// Software components.
        /// </summary>
        SoftwareComponent,

        /// <summary>
        /// Network devices.
        /// </summary>
        NetworkDevice,

        /// <summary>
        /// Cables.
        /// </summary>
        Cable
    }
}
