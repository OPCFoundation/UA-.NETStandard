/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Extension methods that set the <c>EngineeringUnits</c> and
    /// <c>EURange</c> properties on a <see cref="BaseAnalogState"/>
    /// variable resolved through the fluent builder. Fail-fast with
    /// <see cref="StatusCodes.BadTypeMismatch"/> when the resolved node
    /// is not a <see cref="BaseAnalogState"/> (the only OPC UA node
    /// class that declares <c>EngineeringUnits</c> / <c>EURange</c>
    /// child properties).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extensions create the property child if it does not exist
    /// yet (using the <c>BaseAnalogState.AddEngineeringUnits</c> /
    /// <c>AddEURange</c> helpers emitted by the source generator), and
    /// then set its <see cref="BaseVariableState.Value"/> attribute to
    /// the supplied value. They are AOT-safe — no reflection.
    /// </para>
    /// <para>
    /// Typical usage:
    /// <code>
    /// builder.Variable&lt;double&gt;("Pump #1/Operational/Measurements/FluidTemperature")
    ///        .WithEngineeringUnits(EUInformations.Kelvin)
    ///        .WithEURange(min: 233.15, max: 473.15)
    ///        .OnRead(SimulateTemperature);
    /// </code>
    /// </para>
    /// </remarks>
    public static class EngineeringUnitsBuilderExtensions
    {
        /// <summary>
        /// Sets the <c>EngineeringUnits</c> property of the resolved
        /// variable to <paramref name="units"/>.
        /// </summary>
        /// <typeparam name="TValue">CLR value type of the variable.</typeparam>
        /// <param name="builder">The variable builder.</param>
        /// <param name="units">
        /// Engineering units descriptor (unit name, namespace, optional
        /// IEC 80000 code). Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="builder"/> or
        /// <paramref name="units"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadTypeMismatch"/> when the resolved
        /// node is not a <see cref="BaseAnalogState"/>.
        /// </exception>
        public static IVariableBuilder<TValue> WithEngineeringUnits<TValue>(
            this IVariableBuilder<TValue> builder,
            EUInformation units)
        {
            if (builder == null) { throw new ArgumentNullException(nameof(builder)); }
            if (units == null) { throw new ArgumentNullException(nameof(units)); }

            BaseAnalogState analog = RequireAnalog(builder);
            EnsureEngineeringUnitsProperty(analog, builder.Builder.Context);
            analog.EngineeringUnits!.Value = units;
            return builder;
        }

        /// <summary>
        /// Sets the <c>EURange</c> property of the resolved variable to
        /// <c>new Range(high: <paramref name="max"/>, low: <paramref name="min"/>)</c>.
        /// </summary>
        /// <typeparam name="TValue">CLR value type of the variable.</typeparam>
        /// <param name="builder">The variable builder.</param>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Inclusive upper bound.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="builder"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="min"/> &gt; <paramref name="max"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadTypeMismatch"/> when the resolved
        /// node is not a <see cref="BaseAnalogState"/>.
        /// </exception>
        public static IVariableBuilder<TValue> WithEURange<TValue>(
            this IVariableBuilder<TValue> builder,
            double min,
            double max)
        {
            if (builder == null) { throw new ArgumentNullException(nameof(builder)); }
            if (min > max)
            {
                throw new ArgumentException(
                    "min must be less than or equal to max.", nameof(min));
            }

            BaseAnalogState analog = RequireAnalog(builder);
            EnsureEURangeProperty(analog, builder.Builder.Context);
            analog.EURange!.Value = new Range(high: max, low: min);
            return builder;
        }

        /// <summary>
        /// Convenience overload that sets <c>EngineeringUnits</c> and
        /// <c>EURange</c> in one call.
        /// </summary>
        public static IVariableBuilder<TValue> WithUnits<TValue>(
            this IVariableBuilder<TValue> builder,
            EUInformation units,
            double min,
            double max)
        {
            return builder
                .WithEngineeringUnits(units)
                .WithEURange(min, max);
        }

        private static BaseAnalogState RequireAnalog<TValue>(
            IVariableBuilder<TValue> builder)
        {
            if (builder.Node is not BaseAnalogState analog)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Cannot set engineering units on '{0}': resolved node is '{1}', " +
                    "not a BaseAnalogState (or subtype). Only AnalogItemType / " +
                    "BaseAnalogType variables carry EngineeringUnits / EURange properties.",
                    builder.Node.BrowseName,
                    builder.Node.GetType().Name);
            }
            return analog;
        }

        private static void EnsureEngineeringUnitsProperty(
            BaseAnalogState analog,
            ISystemContext context)
        {
            if (analog.EngineeringUnits == null)
            {
                analog.AddEngineeringUnits(context);
            }
        }

        private static void EnsureEURangeProperty(
            BaseAnalogState analog,
            ISystemContext context)
        {
            if (analog.EURange == null)
            {
                analog.AddEURange(context);
            }
        }
    }
}
