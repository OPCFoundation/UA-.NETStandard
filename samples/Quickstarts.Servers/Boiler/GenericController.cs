// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;
using Range = Opc.Ua.Range;

namespace Boiler
{
    /// <summary>
    /// An object representing a generic controller.
    /// </summary>
    public partial class GenericControllerState
    {
        /// <summary>
        /// Updates the measurement and calculates the new control output.
        /// </summary>
        public double UpdateMeasurement(AnalogItemState<double> source)
        {
            PropertyState<double> measurement = m_measurement!;
            PropertyState<double> setPoint = m_setPoint!;
            PropertyState<double> controlOut = m_controlOut!;

            Range? range = source.EURange?.Value;
            measurement.Value = source.Value;

            // clamp the setpoint.
            if (range != null)
            {
                if (setPoint.Value > range.High)
                {
                    setPoint.Value = range.High;
                }

                if (setPoint.Value < range.Low)
                {
                    setPoint.Value = range.Low;
                }
            }

            // calculate error.
            controlOut.Value = setPoint.Value - measurement.Value;

            if (range != null)
            {
                controlOut.Value /= range.Magnitude;

                if (Math.Abs(controlOut.Value) > 1.0)
                {
                    controlOut.Value = controlOut.Value < 0 ? -1.0 : +1.0;
                }
            }

            // return the new output.
            return controlOut.Value;
        }
    }
}
