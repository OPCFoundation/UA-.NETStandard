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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Manager-owned registry of periodic simulation loops. Each loop is
    /// driven by a periodic timer and survives until the owning
    /// <see cref="FluentNodeManagerBase"/> is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loops are added at <c>Configure</c> time via the
    /// <see cref="ISimulationBuilder"/> fluent surface. They start running
    /// when <see cref="Start"/> is invoked by the owning manager (after
    /// <c>Configure</c> completes); subsequent loop additions are
    /// rejected.
    /// </para>
    /// <para>
    /// Exceptions inside tick handlers are caught and logged; they do
    /// not kill the loop. <see cref="Dispose"/> cancels every loop and
    /// awaits them (bounded by a small grace period) before returning.
    /// </para>
    /// <para>
    /// On <c>net6.0</c> and later, the loop uses
    /// <c>System.Threading.PeriodicTimer</c>. On older targets
    /// (<c>net472</c>, <c>net48</c>, <c>netstandard2.1</c>) the loop
    /// falls back to a
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> loop with
    /// drift compensation. Both paths use
    /// <see cref="Stopwatch.GetTimestamp"/> as the monotonic time
    /// source.
    /// </para>
    /// </remarks>
    internal sealed class SimulationRegistry : IDisposable
    {
        public SimulationRegistry(
            FluentNodeManagerBase owner,
            ILogger? logger)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            m_logger = logger;
            m_cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Returns a builder for a new simulation loop with the given
        /// tick interval.
        /// </summary>
        public ISimulationBuilder NewSimulation(TimeSpan interval)
        {
            lock (m_gate)
            {
                if (m_started)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "Cannot add a simulation loop after the registry has started.");
                }
                var loop = new SimulationLoop(this, interval, m_logger);
                m_loops.Add(loop);
                return loop;
            }
        }

        /// <summary>
        /// Starts every registered loop. Subsequent
        /// <see cref="NewSimulation(TimeSpan)"/> calls are rejected. Invoked
        /// once by the owning manager when the builder is sealed.
        /// </summary>
        public void Start()
        {
            lock (m_gate)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;
                if (m_cts == null)
                {
                    return;
                }
                foreach (SimulationLoop loop in m_loops)
                {
                    loop.Start(m_cts.Token);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CancellationTokenSource? cts;
            List<SimulationLoop> snapshot;
            lock (m_gate)
            {
                cts = m_cts;
                m_cts = null;
                snapshot = [.. m_loops];
                m_loops.Clear();
            }

            if (cts == null)

            {

                return;

            }
            try
            {
                cts.Cancel();
                // Bound the dispose wait so a misbehaving handler can't
                // stall manager teardown indefinitely.
                var drain = Task.WhenAll(snapshot.ConvertAll(l => l.RunningTask));
                drain.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex) when (
                ex.InnerException is OperationCanceledException)
            {
                // expected on shutdown
            }
            catch (Exception ex)
            {
                m_logger?.LogWarning(ex,
                    "Simulation drain failed; ignoring on disposal.");
            }
            finally
            {
                cts.Dispose();
            }
        }

        internal ISystemContext Context => m_owner.SystemContext;

        private readonly FluentNodeManagerBase m_owner;
        private readonly ILogger? m_logger;
        private readonly object m_gate = new();
        private readonly List<SimulationLoop> m_loops = [];
        private CancellationTokenSource? m_cts;
        private bool m_started;
    }

    /// <summary>
    /// A single periodic simulation loop. Implements
    /// <see cref="ISimulationBuilder"/> for chaining additional handlers
    /// before <see cref="SimulationRegistry.Start"/>.
    /// </summary>
    internal sealed class SimulationLoop : ISimulationBuilder
    {
        public SimulationLoop(
            SimulationRegistry registry,
            TimeSpan interval,
            ILogger? logger)
        {
            m_registry = registry;
            m_interval = interval;
            m_logger = logger;
            RunningTask = Task.CompletedTask;
        }

        public ISimulationBuilder OnTick(Action<ISystemContext, TimeSpan> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_handlers.Add((ctx, dt, _) =>
            {
                handler(ctx, dt);
                return default;
            });
            return this;
        }

        public ISimulationBuilder OnTick(
            Func<ISystemContext, TimeSpan, CancellationToken, ValueTask> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_handlers.Add(handler);
            return this;
        }

        public Task RunningTask { get; private set; }

        public void Start(CancellationToken cancellationToken)
        {
            if (m_handlers.Count == 0)
            {
                // No handlers — skip starting a useless timer.
                return;
            }

            // Capture into local variables so the closure is allocation-stable
            // and AOT-safe (no captured-this generic instantiation).
            ISystemContext context = m_registry.Context;
            TimeSpan interval = m_interval;
            List<Func<ISystemContext, TimeSpan, CancellationToken, ValueTask>> handlers = m_handlers;
            ILogger? logger = m_logger;

            RunningTask = Task.Run(async () =>
            {
                long lastTimestamp = Stopwatch.GetTimestamp();
                try
                {
#if NET6_0_OR_GREATER
                    using var timer = new PeriodicTimer(interval);
                    while (await timer.WaitForNextTickAsync(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        long now = Stopwatch.GetTimestamp();
                        TimeSpan elapsed = TimestampToTimeSpan(now - lastTimestamp);
                        lastTimestamp = now;

                        await InvokeHandlersAsync(
                                handlers, context, elapsed, logger, cancellationToken)
                            .ConfigureAwait(false);
                    }
#else
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

                        long now = Stopwatch.GetTimestamp();
                        TimeSpan elapsed = TimestampToTimeSpan(now - lastTimestamp);
                        lastTimestamp = now;

                        await InvokeHandlersAsync(
                                handlers, context, elapsed, logger, cancellationToken)
                            .ConfigureAwait(false);
                    }
#endif
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }, cancellationToken);
        }

        private static async ValueTask InvokeHandlersAsync(
            List<Func<ISystemContext, TimeSpan, CancellationToken, ValueTask>> handlers,
            ISystemContext context,
            TimeSpan elapsed,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            foreach (Func<ISystemContext, TimeSpan, CancellationToken, ValueTask> h in handlers)
            {
                try
                {
                    await h(context, elapsed, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex,
                        "Simulation tick handler threw; loop continues.");
                }
            }
        }

        private static TimeSpan TimestampToTimeSpan(long delta)
        {
            // Convert Stopwatch ticks to TimeSpan ticks. Stopwatch.Frequency
            // is per-second; TimeSpan.TicksPerSecond = 10_000_000.
            double seconds = (double)delta / Stopwatch.Frequency;
            return TimeSpan.FromSeconds(seconds);
        }

        private readonly SimulationRegistry m_registry;
        private readonly TimeSpan m_interval;
        private readonly ILogger? m_logger;
        private readonly List<Func<ISystemContext, TimeSpan, CancellationToken, ValueTask>> m_handlers = [];
    }
}
