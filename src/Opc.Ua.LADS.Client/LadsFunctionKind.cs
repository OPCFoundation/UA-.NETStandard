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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The function types of OPC 30500-1, Section 7.
    /// </summary>
    public enum LadsFunctionKind
    {
        /// <summary>
        /// A <c>FunctionType</c> subtype that is not one of the standard types.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// <c>BaseSensorFunctionType</c>.
        /// </summary>
        BaseSensor,

        /// <summary>
        /// <c>AnalogSensorFunctionType</c>.
        /// </summary>
        AnalogSensor,

        /// <summary>
        /// <c>AnalogScalarSensorFunctionType</c>.
        /// </summary>
        AnalogScalarSensor,

        /// <summary>
        /// <c>AnalogScalarSensorFunctionWithCompensationType</c>.
        /// </summary>
        AnalogScalarSensorWithCompensation,

        /// <summary>
        /// <c>AnalogArraySensorFunctionType</c>.
        /// </summary>
        AnalogArraySensor,

        /// <summary>
        /// <c>DiscreteSensorFunctionType</c>.
        /// </summary>
        DiscreteSensor,

        /// <summary>
        /// <c>TwoStateDiscreteSensorFunctionType</c>.
        /// </summary>
        TwoStateDiscreteSensor,

        /// <summary>
        /// <c>MultiStateDiscreteSensorFunctionType</c>.
        /// </summary>
        MultiStateDiscreteSensor,

        /// <summary>
        /// <c>MultiSensorFunctionType</c>.
        /// </summary>
        MultiSensor,

        /// <summary>
        /// <c>BaseControlFunctionType</c>.
        /// </summary>
        BaseControl,

        /// <summary>
        /// <c>AnalogControlFunctionType</c>.
        /// </summary>
        AnalogControl,

        /// <summary>
        /// <c>AnalogControlFunctionWithTotalizerType</c>.
        /// </summary>
        AnalogControlWithTotalizer,

        /// <summary>
        /// <c>AnalogControlFunctionWithComposedTargetValueType</c>.
        /// </summary>
        AnalogControlWithComposedTargetValue,

        /// <summary>
        /// <c>AnalogControlFunctionWithRelativeTargetValueType</c>.
        /// </summary>
        AnalogControlWithRelativeTargetValue,

        /// <summary>
        /// <c>MultiModeAnalogControlFunctionType</c>.
        /// </summary>
        MultiModeAnalogControl,

        /// <summary>
        /// <c>DiscreteControlFunctionType</c>.
        /// </summary>
        DiscreteControl,

        /// <summary>
        /// <c>TwoStateDiscreteControlFunctionType</c>.
        /// </summary>
        TwoStateDiscreteControl,

        /// <summary>
        /// <c>MultiStateDiscreteControlFunctionType</c>.
        /// </summary>
        MultiStateDiscreteControl,

        /// <summary>
        /// <c>TimerControlFunctionType</c>.
        /// </summary>
        TimerControl,

        /// <summary>
        /// <c>CoverFunctionType</c>.
        /// </summary>
        Cover
    }
}
