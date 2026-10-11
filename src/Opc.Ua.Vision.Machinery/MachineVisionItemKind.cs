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
