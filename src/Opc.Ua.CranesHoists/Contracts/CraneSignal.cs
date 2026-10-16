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

namespace Opc.Ua.CranesHoists
{
    /// <summary>
    /// Identifies standard monitoring and request variables in the SDK, not a new wire enumeration.
    /// </summary>
    public enum CraneSignal
    {
        /// <summary>The crane's reported operating mode.</summary>
        OperationalMode,

        /// <summary>Whether the crane can become operational as specified in OPC 40020-1.</summary>
        SystemOn,

        /// <summary>The provider-reported external control request state.</summary>
        ExternalControlRequest,

        /// <summary>The client's accepted global speed-limitation request.</summary>
        SpeedLimitGlobalEnabled,

        /// <summary>The crane motion-device category.</summary>
        MotionDeviceCategory,

        /// <summary>The inherited Robotics speed override.</summary>
        SpeedOverride,

        /// <summary>The remaining designed working period, in percent.</summary>
        DesignedWorkingPeriod,

        /// <summary>Whether actual position can be reported.</summary>
        ActualPositionCapability,

        /// <summary>The inherited Robotics actual position.</summary>
        ActualPosition,

        /// <summary>The selected axis functions, using the standard discrete-value metadata.</summary>
        AxisFunction,

        /// <summary>Whether the axis is moving.</summary>
        IsMoving,

        /// <summary>The nominal axis speed.</summary>
        RatedSpeed,

        /// <summary>The provider-reported active minus-direction speed limit.</summary>
        SpeedLimitDirMinus,

        /// <summary>The accepted minus-direction limit request.</summary>
        SpeedLimitDirMinusSetpoint,

        /// <summary>The provider-reported active plus-direction speed limit.</summary>
        SpeedLimitDirPlus,

        /// <summary>The accepted plus-direction limit request.</summary>
        SpeedLimitDirPlusSetpoint,

        /// <summary>The accepted minus-direction enable request.</summary>
        SpeedLimitEnabledDirMinus,

        /// <summary>The accepted plus-direction enable request.</summary>
        SpeedLimitEnabledDirPlus,

        /// <summary>The actual lifted mass.</summary>
        LoadMass,

        /// <summary>The optional inherited load center-of-mass frame.</summary>
        LoadCenterOfMass,

        /// <summary>The optional inherited load inertia vector.</summary>
        LoadInertia,

        /// <summary>The inherited motor temperature.</summary>
        MotorTemperature,

        /// <summary>The reported motor overtemperature state.</summary>
        Overheated,

        /// <summary>The service or backup brake's reported released state.</summary>
        BrakeReleased,

        /// <summary>The protective function's human-readable name.</summary>
        ProtectionName,

        /// <summary>The protective function category.</summary>
        ProtectiveFunctionMode,

        /// <summary>Whether the function uses a graceful ramp down.</summary>
        RampDown,

        /// <summary>Whether a protective function is supervising the system.</summary>
        Enabled,

        /// <summary>Whether a protective function has initiated a stop or slowdown.</summary>
        Active,

        /// <summary>Minus-direction slowdown capability, not an actuation request.</summary>
        DirMinusSlowdown,

        /// <summary>Minus-direction stop capability, not an actuation request.</summary>
        DirMinusStop,

        /// <summary>Plus-direction slowdown capability, not an actuation request.</summary>
        DirPlusSlowdown,

        /// <summary>Plus-direction stop capability, not an actuation request.</summary>
        DirPlusStop,

        /// <summary>The inherited Robotics emergency-stop state.</summary>
        EmergencyStop,

        /// <summary>The inherited Robotics protective-stop state.</summary>
        ProtectiveStop,

        /// <summary>The inherited Robotics operational mode.</summary>
        SafetyOperationalMode
    }

    /// <summary>
    /// Identifies one standard signal on a uniquely named component within a crane system.
    /// </summary>
    /// <param name="ComponentId">The application-chosen, system-local component identifier.</param>
    /// <param name="Signal">The standard signal.</param>
    public readonly record struct CraneSignalAddress(string ComponentId, CraneSignal Signal);

    /// <summary>
    /// Classifies actual standard crane instances independently of a vendor's subtype name.
    /// </summary>
    public enum CraneElementKind
    {
        /// <summary>A CraneMotionDeviceSystemType instance.</summary>
        System,

        /// <summary>A CraneMotionDeviceType instance.</summary>
        MotionDevice,

        /// <summary>A CraneAxisType instance.</summary>
        Axis,

        /// <summary>A CraneMotorType instance.</summary>
        Motor,

        /// <summary>A BrakeType instance.</summary>
        Brake,

        /// <summary>A ProtectiveFunctionType instance.</summary>
        ProtectiveFunction
    }
}
