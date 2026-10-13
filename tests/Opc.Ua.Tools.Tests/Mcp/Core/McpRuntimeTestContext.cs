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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpRuntime
{
    /// <summary>
    /// Supplies real caller authority and deterministic, entirely in-memory runtime collaborators.
    /// </summary>
    internal sealed class McpRuntimeTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates an isolated registry without entering a caller scope or starting a real timer.
        /// </summary>
        internal McpRuntimeTestContext(
            Action<McpExecutionOptions>? configure = null,
            IMcpOperationPolicy? policy = null)
        {
            Options = new McpExecutionOptions
            {
                EnabledCapabilities =
                [
                    McpCapability.WotRuntime, McpCapability.WotWrite,
                    McpCapability.WotInvoke, McpCapability.PubSubRuntime
                ],
                CallerGrants = Owners.ConvertAll(caller => new McpCallerGrant
                {
                    Caller = caller,
                    Capabilities =
                    [
                        McpCapability.WotRuntime, McpCapability.WotWrite,
                        McpCapability.WotInvoke, McpCapability.PubSubRuntime
                    ]
                })
            };
            configure?.Invoke(Options);
            Policy = policy ?? new McpExecutionPolicy(Options, Callers, CreateTelemetry());
            Registry = new McpRuntimeRegistry(Policy, Options, Clock, CreateTelemetry());
        }

        /// <summary>
        /// Gets the baseline trusted identity.
        /// </summary>
        internal static McpCallerIdentity Owner { get; } = new("runtime-owner", "urn:test:authority");

        /// <summary>
        /// Gets independently granted identities differing in every ownership-key dimension.
        /// </summary>
        internal static ArrayOf<McpCallerIdentity> Owners { get; } =
        [
            Owner,
            new("runtime-other", "urn:test:authority"),
            new("runtime-owner", "urn:test:other-authority"),
            new("runtime-owner", "urn:test:authority", IsLocal: true),
            new("RUNTIME-OWNER", "urn:test:authority"),
            new("runtime-owner", "urn:test:Authority")
        ];

        /// <summary>
        /// Gets the fixed clock origin used in exact timestamp assertions.
        /// </summary>
        internal static DateTimeOffset Epoch { get; } = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        /// <summary>
        /// Gets the mutable input options, allowing snapshot-isolation tests.
        /// </summary>
        internal McpExecutionOptions Options { get; }

        /// <summary>
        /// Gets the actual trusted request context used by the real policy.
        /// </summary>
        internal McpCallerContext Callers { get; } = new();

        /// <summary>
        /// Gets the policy at the production authorization seam.
        /// </summary>
        internal IMcpOperationPolicy Policy { get; }

        /// <summary>
        /// Gets the manually advanced clock and timer implementation.
        /// </summary>
        internal McpRuntimeTestClock Clock { get; } = new(Epoch);

        /// <summary>
        /// Gets the production registry under test.
        /// </summary>
        internal McpRuntimeRegistry Registry { get; }

        /// <summary>
        /// Gets how often an admitted factory was actually invoked.
        /// </summary>
        internal int FactoryCalls => Volatile.Read(ref m_factoryCalls);

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (!m_shutdownObserved)
            {
                await AwaitAsync(Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Enters the baseline owner's real trusted request scope.
        /// </summary>
        internal IDisposable EnterOwner()
        {
            return Callers.EnterTrustedCaller(Owner);
        }

        /// <summary>
        /// Records admission while transferring exactly the supplied resource to the registry.
        /// </summary>
        internal ValueTask<McpRuntimeInfo> OpenAsync(
            string name,
            McpTestRuntime runtime,
            McpCapability capability = McpCapability.WotRuntime,
            CancellationToken ct = default)
        {
            return OpenAsync(name, _ => ValueTask.FromResult(runtime), capability, ct);
        }

        /// <summary>
        /// Wraps the real OpenAsync entry point with a factory-invocation counter.
        /// </summary>
        internal ValueTask<McpRuntimeInfo> OpenAsync(
            string name,
            Func<CancellationToken, ValueTask<McpTestRuntime>> factory,
            McpCapability capability = McpCapability.WotRuntime,
            CancellationToken ct = default)
        {
            return Registry.OpenAsync(name, capability, token =>
            {
                Interlocked.Increment(ref m_factoryCalls);
                return factory(token);
            }, ct);
        }

        /// <summary>
        /// Calls the credential-dependency overload while counting only factories admitted by the registry.
        /// </summary>
        internal ValueTask<McpRuntimeInfo> OpenAsync(
            string name,
            ArrayOf<McpSecretDependency> dependencies,
            Func<CancellationToken, ValueTask<McpTestRuntime>> factory,
            CancellationToken ct = default)
        {
            return Registry.OpenAsync(name, McpCapability.WotRuntime, dependencies, token =>
            {
                Interlocked.Increment(ref m_factoryCalls);
                return factory(token);
            }, ct);
        }

        /// <summary>
        /// Calls the combined dependency overload without disguising session signals as credential references.
        /// </summary>
        internal ValueTask<McpRuntimeInfo> OpenAsync(
            string name,
            ArrayOf<McpSecretDependency> dependencies,
            ArrayOf<Task> invalidationSignals,
            Func<CancellationToken, ValueTask<McpTestRuntime>> factory,
            CancellationToken ct = default)
        {
            return Registry.OpenAsync(name, McpCapability.WotRuntime, dependencies, invalidationSignals, token =>
            {
                Interlocked.Increment(ref m_factoryCalls);
                return factory(token);
            }, ct);
        }

        /// <summary>
        /// Observes a shutdown outcome explicitly, without making expected faults escape again in teardown.
        /// </summary>
        internal async Task<Exception?> ObserveShutdownAsync()
        {
            Task shutdown = Registry.DisposeAsync().AsTask();
            Exception? failure = await CaptureAsync(shutdown).ConfigureAwait(false);
            m_shutdownObserved = shutdown.IsCompleted;
            return failure;
        }

        /// <summary>
        /// Creates asynchronous gates so completing one never executes test continuations under a lock.
        /// </summary>
        /// <typeparam name="T">The result supplied when the gate completes.</typeparam>
        internal static TaskCompletionSource<T> Completion<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Bounds a potential deadlock; elapsed wall time is never the ordering mechanism in a test.
        /// </summary>
        internal static async Task AwaitAsync(Task task)
        {
            await task.WaitAsync(s_watchdog).ConfigureAwait(false);
        }

        /// <summary>
        /// Bounds a potential deadlock while retaining the operation's actual result.
        /// </summary>
        /// <typeparam name="T">The result returned by the operation.</typeparam>
        internal static Task<T> AwaitAsync<T>(Task<T> task)
        {
            return task.WaitAsync(s_watchdog);
        }

        /// <summary>
        /// Captures an exception for exact status, identity, or primary-versus-cleanup assertions.
        /// </summary>
        internal static async Task<Exception?> CaptureAsync(Task task)
        {
            try
            {
                await AwaitAsync(task).ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        /// <summary>
        /// Supplies strict telemetry without network sinks or ambient logging configuration.
        /// </summary>
        internal static ITelemetryContext CreateTelemetry()
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(context => context.LoggerFactory).Returns(NullLoggerFactory.Instance);
            return telemetry.Object;
        }

        /// <summary>
        /// Models host revocation between calls without replacing the registry's policy collaborator.
        /// </summary>
        internal static Mock<IMcpOperationPolicy> MutablePolicy(HashSet<McpCapability> capabilities)
        {
            var policy = new Mock<IMcpOperationPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.RequireCaller()).Returns(Owner);
            policy.Setup(value => value.Require(It.IsAny<McpCapability>())).Returns((McpCapability capability) =>
            {
                if (!capabilities.Contains(capability))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Synthetic revoked grant.");
                }
                return Owner;
            });
            return policy;
        }

        private static readonly TimeSpan s_watchdog = TimeSpan.FromSeconds(10);
        private int m_factoryCalls;
        private bool m_shutdownObserved;
    }

    /// <summary>
    /// Records owned disposal attempts and a concrete committed business effect without protocol I/O.
    /// </summary>
    internal sealed class McpTestRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Optionally holds cleanup at an explicit gate or injects an exact cleanup failure.
        /// </summary>
        internal McpTestRuntime(Task? releaseDisposal = null, Exception? disposalFailure = null)
        {
            m_releaseDisposal = releaseDisposal ?? Task.CompletedTask;
            m_disposalFailure = disposalFailure;
        }

        /// <summary>
        /// Gets a signal raised when the registry actually calls resource disposal.
        /// </summary>
        internal TaskCompletionSource<bool> DisposalStarted { get; } = McpRuntimeTestContext.Completion<bool>();

        /// <summary>
        /// Gets the number of disposal calls, deliberately without fake-side deduplication.
        /// </summary>
        internal int DisposeCalls => Volatile.Read(ref m_disposeCalls);

        /// <summary>
        /// Gets how many business commands ran; registry inspection must leave this zero.
        /// </summary>
        internal int BusinessCalls => Volatile.Read(ref m_businessCalls);

        /// <summary>
        /// Gets the concrete state changed by the fake business operation.
        /// </summary>
        internal int CommittedValue => Volatile.Read(ref m_committedValue);

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref m_disposeCalls);
            DisposalStarted.TrySetResult(true);
            await m_releaseDisposal.ConfigureAwait(false);
            if (m_disposalFailure is not null)
            {
                throw m_disposalFailure;
            }
        }

        /// <summary>
        /// Performs one deterministic effect that must not be replayed or masked by lease cleanup.
        /// </summary>
        internal int Commit(int amount)
        {
            Interlocked.Increment(ref m_businessCalls);
            return Interlocked.Add(ref m_committedValue, amount);
        }

        private readonly Task m_releaseDisposal;
        private readonly Exception? m_disposalFailure;
        private int m_disposeCalls;
        private int m_businessCalls;
        private int m_committedValue = 10;
    }

    /// <summary>
    /// Supplies a distinct runtime kind for authorization-before-type-disclosure assertions.
    /// </summary>
    internal sealed class McpOtherTestRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Gets the number of owned disposal attempts for the alternate concrete runtime kind.
        /// </summary>
        internal int DisposeCalls => Volatile.Read(ref m_disposeCalls);

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref m_disposeCalls);
            return ValueTask.CompletedTask;
        }

        private int m_disposeCalls;
    }

    /// <summary>
    /// A dependency-free fake TimeProvider; Advance fires due timers, coalescing periodic ticks.
    /// </summary>
    internal sealed class McpRuntimeTestClock : TimeProvider
    {
        /// <summary>
        /// Starts at a fixed UTC instant rather than the machine clock.
        /// </summary>
        internal McpRuntimeTestClock(DateTimeOffset start)
        {
            m_now = start;
        }

        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>
        /// Gets how many timers have been created, including timers that were subsequently disposed.
        /// </summary>
        internal int CreatedTimers
        {
            get
            {
                lock (m_state)
                {
                    return m_createdTimers;
                }
            }
        }

        /// <summary>
        /// Gets how many timer registrations are still armed.
        /// </summary>
        internal int ActiveTimers
        {
            get
            {
                lock (m_state)
                {
                    return m_timers.Count(timer => timer.DueAt != long.MaxValue);
                }
            }
        }

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            lock (m_state)
            {
                return m_now;
            }
        }

        /// <inheritdoc/>
        public override long GetTimestamp()
        {
            return GetUtcNow().UtcTicks;
        }

        /// <inheritdoc/>
        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (m_state)
            {
                var timer = new ManualTimer(this, callback, state);
                m_timers.Add(timer);
                m_createdTimers++;
                timer.Change(dueTime, period);
                return timer;
            }
        }

        /// <summary>
        /// Advances fake time only; callbacks execute outside the clock guard and never sleep.
        /// </summary>
        internal void Advance(TimeSpan elapsed)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
            ManualTimer[] due;
            lock (m_state)
            {
                m_now += elapsed;
                due = [.. m_timers.Where(timer => timer.DueAt <= m_now.UtcTicks).OrderBy(timer => timer.DueAt)];
                foreach (ManualTimer timer in due)
                {
                    timer.DueAt = timer.Period > TimeSpan.Zero
                        ? m_now.UtcTicks + timer.Period.Ticks
                        : long.MaxValue;
                }
            }
            foreach (ManualTimer timer in due)
            {
                timer.Fire();
            }
        }

        private readonly Lock m_state = new();
        private readonly List<ManualTimer> m_timers = [];
        private DateTimeOffset m_now;
        private int m_createdTimers;

        /// <summary>
        /// Implements the timer seam consumed by PeriodicTimer and timed CancellationTokenSource.
        /// </summary>
        private sealed class ManualTimer(
            McpRuntimeTestClock owner, TimerCallback callback, object? state) : ITimer
        {
            /// <summary>
            /// Gets or sets the next absolute fake timestamp, or MaxValue when unarmed.
            /// </summary>
            internal long DueAt { get; set; } = long.MaxValue;

            /// <summary>
            /// Gets the interval used to coalesce periodic callbacks during one Advance.
            /// </summary>
            internal TimeSpan Period { get; private set; }

            /// <inheritdoc/>
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.m_state)
                {
                    if (m_disposed)
                    {
                        return false;
                    }
                    DueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? long.MaxValue
                        : owner.m_now.UtcTicks + dueTime.Ticks;
                    Period = period;
                    return true;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (owner.m_state)
                {
                    m_disposed = true;
                    DueAt = long.MaxValue;
                    owner.m_timers.Remove(this);
                }
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            /// <summary>
            /// Fires only registrations that were not disposed by an earlier callback.
            /// </summary>
            internal void Fire()
            {
                lock (owner.m_state)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                }
                callback(state);
            }

            private bool m_disposed;
        }
    }
}
#endif
