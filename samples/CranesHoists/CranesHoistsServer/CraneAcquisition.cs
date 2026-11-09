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

using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.CranesHoists;
using Opc.Ua.Robotics;

namespace CranesHoistsSample.Server
{
    /// <summary>
    /// Supplies explicit simulated inputs, never values inferred from node defaults or accepted requests.
    /// </summary>
    internal static class CraneAcquisition
    {
        /// <summary>
        /// Creates a complete acquisition with its own quality and source time for one selected topology.
        /// </summary>
        internal static ArrayOf<CraneSample> Create(
            string systemId,
            bool advanced,
            int tick,
            DateTimeUtc acquiredAt)
        {
            var samples = new List<CraneSample>
            {
                Sample(systemId, CraneSignal.OperationalMode,
                    Variant.From((int)CraneOperationalModeEnumeration.MANUAL), acquiredAt),
                Sample(systemId, CraneSignal.SystemOn, Variant.From(true), acquiredAt),
                Sample("Hoist", CraneSignal.SpeedOverride, Variant.From(83.25), acquiredAt),
                Sample("LiftMotor", CraneSignal.MotorTemperature, Variant.From(35.0 + (tick % 10 * 0.1)),
                    acquiredAt, StatusCodes.UncertainDataSubNormal),
                Sample("Safety", CraneSignal.EmergencyStop, Variant.From(false), acquiredAt),
                Sample("Safety", CraneSignal.ProtectiveStop, Variant.From(false), acquiredAt),
                Sample("Safety", CraneSignal.SafetyOperationalMode,
                    Variant.From((int)OperationalModeEnumeration.MANUAL_REDUCED_SPEED), acquiredAt)
            };
            if (advanced)
            {
                samples.AddRange(
                [
                    Sample(systemId, CraneSignal.ExternalControlRequest,
                        Variant.From((int)ExternalControlRequestEnumeration.NOT_REQUESTED), acquiredAt),
                    Sample("Hoist", CraneSignal.DesignedWorkingPeriod, Variant.From(92.5), acquiredAt),
                    Sample("Lift", CraneSignal.ActualPosition, Variant.From(12.75), acquiredAt),
                    Sample("Lift", CraneSignal.AxisFunction, Variant.From(ArrayOf.Wrapped<ushort>([0])), acquiredAt),
                    Sample("Lift", CraneSignal.IsMoving, Variant.From(false), acquiredAt),
                    Sample("Lift", CraneSignal.SpeedLimitDirMinus, Variant.From(27.5), acquiredAt),
                    Sample("Lift", CraneSignal.SpeedLimitDirPlus, Variant.From(31.5), acquiredAt),
                    Sample("Lift", CraneSignal.LoadMass, Variant.From(153.0), acquiredAt),
                    Sample("LiftMotor", CraneSignal.Overheated, Variant.From(false), acquiredAt),
                    Sample("LiftMotor", CraneSignal.BrakeReleased, Variant.From(false), acquiredAt),
                    Sample("BackupBrake", CraneSignal.BrakeReleased, Variant.From(false), acquiredAt),
                    Sample("Overload", CraneSignal.Enabled, Variant.From(true), acquiredAt),
                    Sample("Overload", CraneSignal.Active, Variant.From(false), acquiredAt)
                ]);
            }
            return samples.ToArrayOf();
        }

        /// <summary>
        /// Retains the explicit acquisition status and timestamp.
        /// </summary>
        private static CraneSample Sample(
            string component,
            CraneSignal signal,
            Variant value,
            DateTimeUtc acquiredAt,
            StatusCode status = default)
        {
            return new CraneSample(new CraneSignalAddress(component, signal), new DataValue(value, status, acquiredAt));
        }
    }
}
