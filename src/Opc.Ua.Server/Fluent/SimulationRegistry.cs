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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Manager-owned registry of periodic simulation loops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loops are added at <c>Configure</c> time through the
    /// <see cref="ISimulationBuilder"/> fluent surface. The registry itself owns no
    /// lifecycle: the first <c>Simulation(...)</c> call registers a manager-scoped
    /// behavior, so the loops start when behaviors activate and are released — awaited,
    /// not blocked on — when the address space is deleted.
    /// </para>
    /// <para>
    /// Loops are driven from the server <see cref="TimeProvider"/> rather than from
    /// <c>PeriodicTimer</c> and <c>Stopwatch</c>, so tests can run them on a fake
    /// clock instead of wall-clock sleeps.
    /// </para>
    /// <para>
    /// Exceptions inside tick handlers are caught and logged; they do not kill the
    /// loop. A handler that observes cancellation stops its own loop only.
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
        /// Returns a builder for a new simulation loop with the given tick interval.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
        /// Gets whether any loop has been registered.
        /// </summary>
        public bool HasLoops
        {
            get
            {
                lock (m_gate)
                {
                    return m_loops.Count > 0;
                }
            }
        }

        /// <summary>
        /// Starts every registered loop, bound to the supplied shutdown token.
        /// </summary>
        /// <remarks>
        /// Subsequent <see cref="NewSimulation(TimeSpan)"/> calls are rejected: the
        /// loops have been snapshotted and a late addition would never run.
        /// </remarks>
        public void Start()
        {
            List<SimulationLoop> snapshot;
            CancellationToken shutdownToken;
            lock (m_gate)
            {
                if (m_started || m_cts == null)
                {
                    return;
                }
                m_started = true;
                shutdownToken = m_cts.Token;
                snapshot = [.. m_loops];
            }

            TimeProvider timeProvider = m_owner.NodeManagerTimeProvider;
            foreach (SimulationLoop loop in snapshot)
            {
                loop.Start(timeProvider, shutdownToken);
            }
        }

        /// <summary>
        /// Stops every loop and awaits its drain.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Dispose"/> this never abandons a running handler: the
        /// caller is the async teardown path and can afford to wait.
        /// </remarks>
        public async ValueTask StopAsync()
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
#if NET8_0_OR_GREATER
                await cts.CancelAsync().ConfigureAwait(false);
#else
                cts.Cancel();
#endif
                foreach (SimulationLoop loop in snapshot)
                {
                    try
                    {
                        await loop.RunningTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // expected on shutdown
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_logger?.SimulationDrainFailedIgnoringOnDisposal(ex);
            }
            finally
            {
                cts.Dispose();
            }
        }

        /// <summary>
        /// Signals every loop to stop without waiting.
        /// </summary>
        /// <remarks>
        /// Synchronous disposal must not block, so this only trips the token. The
        /// awaited drain belongs to <see cref="StopAsync"/>, which the behavior release
        /// path runs.
        /// </remarks>
        public void Dispose()
        {
            CancellationTokenSource? cts;
            lock (m_gate)
            {
                cts = m_cts;
                m_cts = null;
                m_loops.Clear();
            }

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already released by the async path.
            }
            finally
            {
                cts.Dispose();
            }
        }

        internal ISystemContext Context => m_owner.SystemContext;

        private readonly FluentNodeManagerBase m_owner;
        private readonly ILogger? m_logger;
        private readonly Lock m_gate = new();
        private readonly List<SimulationLoop> m_loops = [];
        private CancellationTokenSource? m_cts;
        private bool m_started;
    }

    /// <summary>
    /// Releases the simulation loops when the owning node manager tears down.
    /// </summary>
    /// <remarks>
    /// Registered as a manager-scoped behavior by the first <c>Simulation(...)</c>
    /// call, so the awaited drain runs on the async teardown path instead of blocking
    /// inside <see cref="SimulationRegistry.Dispose"/>.
    /// </remarks>
    internal sealed class SimulationLifetime : IAsyncDisposable
    {
        public SimulationLifetime(SimulationRegistry registry)
        {
            m_registry = registry;
        }

        public ValueTask DisposeAsync()
        {
            return m_registry.StopAsync();
        }

        private readonly SimulationRegistry m_registry;
    }

    /// <summary>
    /// A single periodic simulation loop. Implements <see cref="ISimulationBuilder"/>
    /// for chaining additional handlers before the registry starts.
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
            AddHandler((ctx, dt, _) =>
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
            AddHandler(handler);
            return this;
        }

        public Task RunningTask { get; private set; }

        public void Start(TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            // Snapshot the handlers: the running loop must not enumerate a list a late
            // OnTick could still mutate.
            List<Func<ISystemContext, TimeSpan, CancellationToken, ValueTask>> handlers;
            lock (m_handlerGate)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;
                if (m_handlers.Count == 0)
                {
                    // No handlers — skip starting a useless timer.
                    return;
                }
                handlers = [.. m_handlers];
            }

            // Capture into locals so the closure is allocation-stable and AOT-safe.
            ISystemContext context = m_registry.Context;
            TimeSpan interval = m_interval;
            ILogger? logger = m_logger;

            if (cancellationToken.IsCancellationRequested)
            {
                // Task.Run would never run the body, which would strand the timer.
                return;
            }

            // The timer is created here, not inside the loop task: it must be armed
            // before Start returns, or a clock advanced immediately afterwards would
            // fire into nothing.
            //
            // A bounded signal coalesces missed ticks the way PeriodicTimer does, so a
            // slow handler drops ticks rather than queueing them up.
            // Ownership of both handles transfers to the loop task below, which disposes
            // them in its finally; the analyzer cannot see across that lambda boundary.
            // Baseline the clock before arming: the timer can fire before the loop task
            // is scheduled — deterministically so under a fake clock advanced right
            // after Start — and a baseline taken inside the task would report the first
            // tick as zero elapsed even though a full interval passed.
            long startTimestamp = timeProvider.GetTimestamp();

#pragma warning disable CA2000
            var tick = new SemaphoreSlim(0, 1);
            ITimer timer;
            try
            {
                timer = timeProvider.CreateTimer(
                    static state =>
                    {
                        try
                        {
                            ((SemaphoreSlim)state!).Release();
                        }
                        catch (SemaphoreFullException)
                        {
                            // Previous tick still running; skip this one.
                        }
                        catch (ObjectDisposedException)
                        {
                            // Loop already torn down.
                        }
                    },
                    tick,
                    interval,
                    interval);
            }
            catch
            {
                // The loop task never starts, so nothing else will release the signal.
                tick.Dispose();
                throw;
            }
#pragma warning restore CA2000

            RunningTask = Task.Run(
                async () =>
                {
                    long lastTimestamp = startTimestamp;
                    try
                    {
                        while (true)
                        {
                            await tick.WaitAsync(cancellationToken).ConfigureAwait(false);

                            long now = timeProvider.GetTimestamp();
                            TimeSpan elapsed =
                                timeProvider.GetElapsedTime(lastTimestamp, now);
                            lastTimestamp = now;

                            await InvokeHandlersAsync(
                                    handlers, context, elapsed, logger, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // expected on shutdown
                    }
                    finally
                    {
                        timer.Dispose();
                        tick.Dispose();
                    }
                },
                // Deliberately not the shutdown token: cancelling between the check
                // above and scheduling would stop the delegate from ever running, and
                // its finally is what disposes the timer and the signal. The loop
                // observes cancellation in WaitAsync instead.
                CancellationToken.None);
        }

        private void AddHandler(
            Func<ISystemContext, TimeSpan, CancellationToken, ValueTask> handler)
        {
            lock (m_handlerGate)
            {
                if (m_started)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "Cannot add a tick handler after the simulation has started.");
                }
                m_handlers.Add(handler);
            }
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
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    // Shutdown: let the loop unwind.
                    throw;
                }
                catch (Exception ex)
                {
                    // A handler that cancels something of its own must not silently
                    // kill the whole loop, so it is logged like any other failure.
                    logger?.SimulationTickHandlerThrewLoopContinues(ex);
                }
            }
        }

        private readonly SimulationRegistry m_registry;
        private readonly TimeSpan m_interval;
        private readonly ILogger? m_logger;
        private readonly Lock m_handlerGate = new();
        private readonly List<Func<ISystemContext, TimeSpan, CancellationToken, ValueTask>> m_handlers = [];
        private bool m_started;
    }

    /// <summary>
    /// Source-generated log messages for SimulationRegistry.
    /// </summary>
    internal static partial class SimulationRegistryLog
    {
        [LoggerMessage(EventId = ServerEventIds.SimulationRegistry + 0, Level = LogLevel.Warning,
            Message = "Simulation drain failed; ignoring on disposal.")]
        public static partial void SimulationDrainFailedIgnoringOnDisposal(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SimulationRegistry + 1, Level = LogLevel.Error,
            Message = "Simulation tick handler threw; loop continues.")]
        public static partial void SimulationTickHandlerThrewLoopContinues(this ILogger logger, Exception ex);
    }

}
