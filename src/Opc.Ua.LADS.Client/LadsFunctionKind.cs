// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
