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

using System;

namespace Opc.Ua.CranesHoists
{
    /// <summary>
    /// Shares the canonical signal paths between providers, runtime and typed client.
    /// </summary>
    internal static class CraneSignals
    {
        /// <summary>
        /// Returns the standard signals applicable to a crane element.
        /// </summary>
        internal static ArrayOf<CraneSignal> For(CraneElementKind kind)
        {
            return kind switch
            {
                CraneElementKind.System =>
                [
                    CraneSignal.OperationalMode, CraneSignal.SystemOn,
                    CraneSignal.ExternalControlRequest, CraneSignal.SpeedLimitGlobalEnabled
                ],
                CraneElementKind.MotionDevice =>
                [
                    CraneSignal.MotionDeviceCategory, CraneSignal.SpeedOverride, CraneSignal.DesignedWorkingPeriod
                ],
                CraneElementKind.Axis =>
                [
                    CraneSignal.ActualPositionCapability, CraneSignal.ActualPosition, CraneSignal.AxisFunction,
                    CraneSignal.IsMoving, CraneSignal.RatedSpeed, CraneSignal.LoadMass,
                    CraneSignal.LoadCenterOfMass, CraneSignal.LoadInertia,
                    CraneSignal.SpeedLimitDirMinus, CraneSignal.SpeedLimitDirPlus,
                    CraneSignal.SpeedLimitDirMinusSetpoint, CraneSignal.SpeedLimitDirPlusSetpoint,
                    CraneSignal.SpeedLimitEnabledDirMinus, CraneSignal.SpeedLimitEnabledDirPlus
                ],
                CraneElementKind.Motor =>
                [
                    CraneSignal.MotorTemperature, CraneSignal.Overheated, CraneSignal.BrakeReleased
                ],
                CraneElementKind.Brake => [CraneSignal.BrakeReleased],
                CraneElementKind.ProtectiveFunction =>
                [
                    CraneSignal.ProtectionName, CraneSignal.ProtectiveFunctionMode, CraneSignal.RampDown,
                    CraneSignal.Enabled, CraneSignal.Active, CraneSignal.DirMinusSlowdown, CraneSignal.DirMinusStop,
                    CraneSignal.DirPlusSlowdown, CraneSignal.DirPlusStop
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Gets whether a signal is a client request rather than provider-reported state.
        /// </summary>
        internal static bool IsRequest(CraneSignal signal)
        {
            return signal is CraneSignal.SpeedLimitGlobalEnabled or
                CraneSignal.SpeedLimitDirMinusSetpoint or CraneSignal.SpeedLimitDirPlusSetpoint or
                CraneSignal.SpeedLimitEnabledDirMinus or CraneSignal.SpeedLimitEnabledDirPlus;
        }

        /// <summary>
        /// Gets the published type identifier corresponding to a typed element.
        /// </summary>
        internal static uint TypeId(CraneElementKind kind)
        {
            return kind switch
            {
                CraneElementKind.System => ObjectTypes.CraneMotionDeviceSystemType,
                CraneElementKind.MotionDevice => ObjectTypes.CraneMotionDeviceType,
                CraneElementKind.Axis => ObjectTypes.CraneAxisType,
                CraneElementKind.Motor => ObjectTypes.CraneMotorType,
                CraneElementKind.Brake => ObjectTypes.BrakeType,
                CraneElementKind.ProtectiveFunction => ObjectTypes.ProtectiveFunctionType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Resolves a variable's actual standard namespace, not its enclosing crane namespace.
        /// </summary>
        internal static string Namespace(CraneElementKind kind, CraneSignal signal)
        {
            return signal is CraneSignal.SpeedOverride or CraneSignal.ActualPosition or
                CraneSignal.MotorTemperature or CraneSignal.LoadMass or CraneSignal.LoadCenterOfMass or
                CraneSignal.LoadInertia or CraneSignal.EmergencyStop or
                CraneSignal.ProtectiveStop or CraneSignal.SafetyOperationalMode ||
                (kind == CraneElementKind.Motor && signal == CraneSignal.BrakeReleased)
                ? Robotics.Namespaces.Robotics
                : CranesHoistsModel.NamespaceUri;
        }

        /// <summary>
        /// Gets the literal standard browse name.
        /// </summary>
        internal static string Name(CraneSignal signal)
        {
            return signal switch
            {
                CraneSignal.OperationalMode => BrowseNames.CraneOperationalMode,
                CraneSignal.MotionDeviceCategory => BrowseNames.CraneMotionDeviceCategory,
                CraneSignal.AxisFunction => BrowseNames.CraneAxisFunction,
                CraneSignal.LoadMass => Robotics.BrowseNames.Mass,
                CraneSignal.LoadCenterOfMass => Robotics.BrowseNames.CenterOfMass,
                CraneSignal.LoadInertia => Robotics.BrowseNames.Inertia,
                CraneSignal.ProtectionName => BrowseNames.Name,
                CraneSignal.SafetyOperationalMode => Robotics.BrowseNames.OperationalMode,
                _ => signal.ToString()
            };
        }

        /// <summary>
        /// Gets whether the signal is declared directly on its component.
        /// </summary>
        internal static bool IsProperty(CraneSignal signal)
        {
            return signal is CraneSignal.MotionDeviceCategory or CraneSignal.ProtectionName or
                CraneSignal.ProtectiveFunctionMode or CraneSignal.RampDown;
        }

        /// <summary>
        /// Constructs a namespace-correct path to an actual standard signal.
        /// </summary>
        internal static ArrayOf<QualifiedName> Path(
            CraneElementKind kind,
            CraneSignal signal,
            NamespaceTable namespaces)
        {
            var leaf = new QualifiedName(Name(signal), Index(namespaces, Namespace(kind, signal)));
            if (IsProperty(signal))
            {
                return [leaf];
            }
            if (signal is CraneSignal.LoadMass or CraneSignal.LoadCenterOfMass or CraneSignal.LoadInertia)
            {
                return
                [
                    new QualifiedName(BrowseNames.ActualLoad, Index(namespaces, CranesHoistsModel.NamespaceUri)),
                    leaf
                ];
            }
            return
            [
                new QualifiedName(Di.BrowseNames.ParameterSet, Index(namespaces, Di.Namespaces.OpcUaDi)),
                leaf
            ];
        }

        /// <summary>
        /// Resolves a required namespace without changing a remote session's namespace table.
        /// </summary>
        internal static ushort Index(NamespaceTable namespaces, string uri)
        {
            int index = namespaces.GetIndex(uri);
            return index >= 0
                ? (ushort)index
                : throw ServiceResultException.Create(
                    StatusCodes.BadNotFound, "Required namespace '{0}' is absent.", uri);
        }
    }
}
