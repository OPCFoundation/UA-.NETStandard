// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// Maps <see cref="LadsFunctionKind"/> values to the LADS function types.
    /// </summary>
    public static class LadsFunctionKinds
    {
        /// <summary>
        /// Gets the kinds ordered so that a subtype precedes its supertypes; the
        /// first kind a type definition derives from is its most specific kind.
        /// </summary>
        public static ArrayOf<LadsFunctionKind> MostSpecificFirst { get; } =
        [
            LadsFunctionKind.AnalogControlWithTotalizer,
            LadsFunctionKind.AnalogControlWithComposedTargetValue,
            LadsFunctionKind.AnalogControlWithRelativeTargetValue,
            LadsFunctionKind.AnalogControl,
            LadsFunctionKind.MultiModeAnalogControl,
            LadsFunctionKind.MultiStateDiscreteControl,
            LadsFunctionKind.TwoStateDiscreteControl,
            LadsFunctionKind.DiscreteControl,
            LadsFunctionKind.TimerControl,
            LadsFunctionKind.BaseControl,
            LadsFunctionKind.Cover,
            LadsFunctionKind.AnalogScalarSensorWithCompensation,
            LadsFunctionKind.AnalogScalarSensor,
            LadsFunctionKind.AnalogArraySensor,
            LadsFunctionKind.AnalogSensor,
            LadsFunctionKind.TwoStateDiscreteSensor,
            LadsFunctionKind.MultiStateDiscreteSensor,
            LadsFunctionKind.DiscreteSensor,
            LadsFunctionKind.MultiSensor,
            LadsFunctionKind.BaseSensor
        ];

        /// <summary>
        /// Gets the ObjectType of a function kind.
        /// </summary>
        /// <param name="kind">The function kind.</param>
        /// <returns>The type, or <c>FunctionType</c> for <see cref="LadsFunctionKind.Unknown"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The kind is not defined.</exception>
        public static ExpandedNodeId TypeIdOf(LadsFunctionKind kind)
        {
            return kind switch
            {
                LadsFunctionKind.Unknown => ObjectTypeIds.FunctionType,
                LadsFunctionKind.BaseSensor => ObjectTypeIds.BaseSensorFunctionType,
                LadsFunctionKind.AnalogSensor => ObjectTypeIds.AnalogSensorFunctionType,
                LadsFunctionKind.AnalogScalarSensor => ObjectTypeIds.AnalogScalarSensorFunctionType,
                LadsFunctionKind.AnalogScalarSensorWithCompensation =>
                    ObjectTypeIds.AnalogScalarSensorFunctionWithCompensationType,
                LadsFunctionKind.AnalogArraySensor => ObjectTypeIds.AnalogArraySensorFunctionType,
                LadsFunctionKind.DiscreteSensor => ObjectTypeIds.DiscreteSensorFunctionType,
                LadsFunctionKind.TwoStateDiscreteSensor => ObjectTypeIds.TwoStateDiscreteSensorFunctionType,
                LadsFunctionKind.MultiStateDiscreteSensor => ObjectTypeIds.MultiStateDiscreteSensorFunctionType,
                LadsFunctionKind.MultiSensor => ObjectTypeIds.MultiSensorFunctionType,
                LadsFunctionKind.BaseControl => ObjectTypeIds.BaseControlFunctionType,
                LadsFunctionKind.AnalogControl => ObjectTypeIds.AnalogControlFunctionType,
                LadsFunctionKind.AnalogControlWithTotalizer => ObjectTypeIds.AnalogControlFunctionWithTotalizerType,
                LadsFunctionKind.AnalogControlWithComposedTargetValue =>
                    ObjectTypeIds.AnalogControlFunctionWithComposedTargetValueType,
                LadsFunctionKind.AnalogControlWithRelativeTargetValue =>
                    ObjectTypeIds.AnalogControlFunctionWithRelativeTargetValueType,
                LadsFunctionKind.MultiModeAnalogControl => ObjectTypeIds.MultiModeAnalogControlFunctionType,
                LadsFunctionKind.DiscreteControl => ObjectTypeIds.DiscreteControlFunctionType,
                LadsFunctionKind.TwoStateDiscreteControl => ObjectTypeIds.TwoStateDiscreteControlFunctionType,
                LadsFunctionKind.MultiStateDiscreteControl => ObjectTypeIds.MultiStateDiscreteControlFunctionType,
                LadsFunctionKind.TimerControl => ObjectTypeIds.TimerControlFunctionType,
                LadsFunctionKind.Cover => ObjectTypeIds.CoverFunctionType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Gets whether a kind is a sensor function.
        /// </summary>
        /// <param name="kind">The function kind.</param>
        public static bool IsSensor(LadsFunctionKind kind)
        {
            return kind is >= LadsFunctionKind.BaseSensor and <= LadsFunctionKind.MultiSensor;
        }

        /// <summary>
        /// Gets whether a kind is a control function.
        /// </summary>
        /// <param name="kind">The function kind.</param>
        public static bool IsControl(LadsFunctionKind kind)
        {
            return kind is >= LadsFunctionKind.BaseControl and <= LadsFunctionKind.TimerControl;
        }
    }
}
