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
using System.Collections.Generic;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Fluent extensions that bridge a configured
    /// <see cref="IVariableBuilder{TValue}"/> to the <b>runtime</b> so a
    /// manager can push value changes to subscribed MonitoredItems after the
    /// builder has been sealed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two complementary mechanisms are provided:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Bind{TValue}(IVariableBuilder{TValue}, out IValueUpdater{TValue})"/>
    /// captures an <see cref="IValueUpdater{TValue}"/> handle that the
    /// manager stores and calls whenever it decides to push a new value
    /// (for example from an <c>OnCall</c> method handler). This is the
    /// supported replacement for the old <c>Node.ClearChangeMasks(...)</c>
    /// recipe, which is unavailable once the builder is sealed.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="PollEvery{TValue}(IVariableBuilder{TValue}, TimeSpan, Func{TValue})"/>
    /// registers a periodic sampling loop that reads a getter and, on
    /// change, pushes the new value so subscriptions update automatically —
    /// without the manager writing any change-notification code.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public static class RuntimeValueBuilderExtensions
    {
        /// <summary>
        /// Captures an <see cref="IValueUpdater{TValue}"/> handle for the
        /// resolved variable. The handle stays valid after the builder is
        /// sealed; calling <see cref="IValueUpdater{TValue}.SetValue(TValue)"/>
        /// updates the value and notifies subscribed MonitoredItems.
        /// </summary>
        /// <example>
        /// <code>
        /// IValueUpdater&lt;float&gt; ao01;
        /// builder.MyEquipment03.AO01.Builder.AsVariable&lt;float&gt;()
        ///        .Bind(out ao01);
        /// // later, from a method handler:
        /// ao01.SetValue(newValue);
        /// </code>
        /// </example>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        /// <param name="builder">The typed variable builder.</param>
        /// <param name="updater">
        /// Receives the runtime handle for the resolved variable.
        /// </param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <see langword="null"/>.
        /// </exception>
        public static IVariableBuilder<TValue> Bind<TValue>(
            this IVariableBuilder<TValue> builder,
            out IValueUpdater<TValue> updater)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            updater = new ValueUpdater<TValue>(builder.Node, builder.Builder.Context);
            return builder;
        }

        /// <summary>
        /// Registers a periodic sampling loop that invokes
        /// <paramref name="sample"/> and, when the value changes, updates the
        /// variable and notifies subscribed MonitoredItems. An initial sample
        /// is applied immediately so the node starts with a meaningful value.
        /// </summary>
        /// <remarks>
        /// Requires the owning manager to derive from
        /// <see cref="FluentNodeManagerBase"/> (the same requirement as the
        /// <c>Simulation</c> surface, whose loop infrastructure this reuses).
        /// </remarks>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        /// <param name="builder">The typed variable builder.</param>
        /// <param name="interval">
        /// Sampling interval. Must be positive.
        /// </param>
        /// <param name="sample">Getter invoked on every tick.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IVariableBuilder<TValue> PollEvery<TValue>(
            this IVariableBuilder<TValue> builder,
            TimeSpan interval,
            Func<TValue> sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            return builder.PollEvery(interval, _ => sample());
        }

        /// <summary>
        /// Registers a periodic sampling loop that invokes
        /// <paramref name="sample"/> with the calling
        /// <see cref="ISystemContext"/> and, when the value changes, updates
        /// the variable and notifies subscribed MonitoredItems. An initial
        /// sample is applied immediately so the node starts with a meaningful
        /// value.
        /// </summary>
        /// <remarks>
        /// Requires the owning manager to derive from
        /// <see cref="FluentNodeManagerBase"/> (the same requirement as the
        /// <c>Simulation</c> surface, whose loop infrastructure this reuses).
        /// </remarks>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        /// <param name="builder">The typed variable builder.</param>
        /// <param name="interval">
        /// Sampling interval. Must be positive.
        /// </param>
        /// <param name="sample">Getter invoked on every tick.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="sample"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="interval"/> is not positive.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the owning
        /// manager does not derive from <see cref="FluentNodeManagerBase"/>.
        /// </exception>
        public static IVariableBuilder<TValue> PollEvery<TValue>(
            this IVariableBuilder<TValue> builder,
            TimeSpan interval,
            Func<ISystemContext, TValue> sample)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    interval,
                    "Sampling interval must be positive.");
            }

            if (builder.Builder is not NodeManagerBuilder concrete ||
                concrete.Simulations == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "PollEvery requires the node manager to derive from " +
                    "FluentNodeManagerBase. Manager type '{0}' does not opt in.",
                    builder.Builder.NodeManager?.GetType().FullName ?? "(unknown)");
            }

            ISystemContext context = concrete.Context;
            var updater = new ValueUpdater<TValue>(builder.Node, context);
            EqualityComparer<TValue> comparer = EqualityComparer<TValue>.Default;

            // Prime the node with the current value so reads and the first
            // notification start from a meaningful state.
            TValue last = sample(context);
            updater.SetValue(last);

            concrete.Simulations
                .NewSimulation(interval)
                .OnTick((ctx, _) =>
                {
                    TValue current = sample(ctx);
                    if (!comparer.Equals(current, last))
                    {
                        last = current;
                        updater.SetValue(current);
                    }
                });

            return builder;
        }
    }
}
