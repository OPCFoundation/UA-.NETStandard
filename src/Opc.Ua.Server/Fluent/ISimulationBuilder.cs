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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Fluent builder for registering a manager-owned simulation loop —
    /// a periodic tick callback that fires at a fixed interval until the
    /// owning <see cref="FluentNodeManagerBase"/> is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The simulation registry is part of <see cref="FluentNodeManagerBase"/>;
    /// callers obtain a builder via the
    /// <see cref="SimulationBuilderExtensions.Simulation(INodeManagerBuilder, TimeSpan)"/>
    /// extension on <see cref="INodeManagerBuilder"/>. The handler is
    /// invoked off a periodic timer; exceptions inside the
    /// handler are logged and do not kill the loop.
    /// </para>
    /// </remarks>
    public interface ISimulationBuilder
    {
        /// <summary>
        /// Registers a synchronous tick handler. The handler is invoked
        /// every <see cref="TimeSpan"/> tick configured via
        /// <see cref="SimulationBuilderExtensions.Simulation(INodeManagerBuilder, TimeSpan)"/>.
        /// </summary>
        /// <param name="handler">
        /// Callback receiving the manager's
        /// <see cref="ISystemContext"/> and the elapsed
        /// <see cref="TimeSpan"/> since the previous tick.
        /// </param>
        /// <returns>The same builder for chaining additional ticks.</returns>
        ISimulationBuilder OnTick(Action<ISystemContext, TimeSpan> handler);

        /// <summary>
        /// Registers an asynchronous tick handler. The handler must
        /// honour the supplied <see cref="CancellationToken"/>, which
        /// is cancelled when the manager disposes.
        /// </summary>
        ISimulationBuilder OnTick(
            Func<ISystemContext, TimeSpan, CancellationToken, ValueTask> handler);
    }

    /// <summary>
    /// Entry-point extension that returns an
    /// <see cref="ISimulationBuilder"/> for the manager owning a
    /// supplied <see cref="INodeManagerBuilder"/>. The manager must
    /// derive from <see cref="FluentNodeManagerBase"/> — calling this
    /// on a manager that does not opt in throws
    /// <see cref="StatusCodes.BadConfigurationError"/>.
    /// </summary>
    public static class SimulationBuilderExtensions
    {
        /// <summary>
        /// Registers a periodic simulation tick on the owning manager.
        /// </summary>
        /// <param name="builder">The node manager builder.</param>
        /// <param name="interval">
        /// Tick interval. Must be positive. The first tick fires after
        /// the interval has elapsed (no immediate fire).
        /// </param>
        /// <returns>A simulation builder for chaining handlers.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="builder"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="interval"/> is not positive.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the
        /// owning manager does not derive from
        /// <see cref="FluentNodeManagerBase"/>.
        /// </exception>
        public static ISimulationBuilder Simulation(
            this INodeManagerBuilder builder,
            TimeSpan interval)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    interval,
                    "Simulation interval must be positive.");
            }

            if (builder is not NodeManagerBuilder concrete ||
                concrete.Simulations == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Simulation requires the node manager to derive from " +
                    "FluentNodeManagerBase. Manager type '{0}' does not opt in.",
                    builder.NodeManager?.GetType().FullName ?? "(unknown)");
            }

            return concrete.Simulations.NewSimulation(interval);
        }
    }
}
