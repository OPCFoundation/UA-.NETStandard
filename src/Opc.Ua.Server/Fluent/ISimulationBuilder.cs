// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(builder, "Simulation");

            // The first simulation hands loop release to the behavior mechanism, so
            // teardown awaits the drain instead of blocking inside Dispose.
            concrete.EnsureSimulationLifecycleRegistered();
            return concrete.Simulations!.NewSimulation(interval);
        }
    }
}
