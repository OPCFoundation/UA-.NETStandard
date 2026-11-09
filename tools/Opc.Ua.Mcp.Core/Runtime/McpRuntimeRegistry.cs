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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Owns explicitly created protocol runtimes under caller-bound opaque leases, quotas and expiry.
    /// Registrations never replace an existing runtime or take ownership of a borrowed host application.
    /// </summary>
    public sealed class McpRuntimeRegistry : IAsyncDisposable
    {
        /// <summary>
        /// Creates an initially idle registry with host-supplied authority, clock and telemetry.
        /// </summary>
        public McpRuntimeRegistry(
            IMcpOperationPolicy policy, McpExecutionOptions options,
            TimeProvider timeProvider, ITelemetryContext telemetry)
        {
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_options = options?.Snapshot() ?? throw new ArgumentNullException(nameof(options));
            m_time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            ArgumentNullException.ThrowIfNull(telemetry);
            m_logger = telemetry.CreateLogger<McpRuntimeRegistry>();
        }

        /// <summary>
        /// Reserves capacity before activation and publishes a handle only when the runtime is ready.
        /// The factory transfers ownership of its returned runtime to this registry.
        /// </summary>
        /// <typeparam name="T">The asynchronously disposable runtime implementation.</typeparam>
        public ValueTask<McpRuntimeInfo> OpenAsync<T>(
            string name,
            McpCapability capability,
            Func<CancellationToken, ValueTask<T>> factory,
            CancellationToken ct = default)
            where T : class, IAsyncDisposable
        {
            return OpenAsync(name, capability, [], factory, ct);
        }

        /// <summary>
        /// Reserves a runtime whose activation and lifetime also depend on authorized credential references.
        /// Expiry or invalidation cancels the runtime without relying on a later caller request.
        /// </summary>
        /// <typeparam name="T">The asynchronously disposable runtime implementation.</typeparam>
        public ValueTask<McpRuntimeInfo> OpenAsync<T>(
            string name,
            McpCapability capability,
            ArrayOf<McpSecretDependency> dependencies,
            Func<CancellationToken, ValueTask<T>> factory,
            CancellationToken ct = default)
            where T : class, IAsyncDisposable
        {
            return OpenAsync(name, capability, dependencies, [], factory, ct);
        }

        /// <summary>
        /// Adds non-secret generation invalidation signals to the owned runtime lifetime before activation.
        /// </summary>
        /// <typeparam name="T">The asynchronously disposable runtime implementation.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<McpRuntimeInfo> OpenAsync<T>(
            string name,
            McpCapability capability,
            ArrayOf<McpSecretDependency> dependencies,
            ArrayOf<Task> invalidationSignals,
            Func<CancellationToken, ValueTask<T>> factory,
            CancellationToken ct = default)
            where T : class, IAsyncDisposable
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, 128);
            ArgumentNullException.ThrowIfNull(factory);
            ct.ThrowIfCancellationRequested();
            McpCallerIdentity caller = m_policy.Require(capability);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dependencies.Count, 16);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(invalidationSignals.Count, 16);
            foreach (Task signal in invalidationSignals)
            {
                ArgumentNullException.ThrowIfNull(signal);
                if (signal.IsCompleted)
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid,
                        "A runtime session or model generation is no longer current.");
                }
            }
            foreach (McpSecretDependency dependency in dependencies)
            {
                ArgumentNullException.ThrowIfNull(dependency);
                ArgumentNullException.ThrowIfNull(dependency.Reference);
                ArgumentNullException.ThrowIfNull(dependency.Invalidated);
                if (dependency.Invalidated.IsCompleted || dependency.Reference.ExpiresAt <= m_time.GetUtcNow())
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                        "A runtime credential is revoked or expired.");
                }
            }
            Entry entry;
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_entries.Count >= m_options.MaxRuntimes ||
                    m_entries.Values.Count(value => value.Owner == caller) >= m_options.MaxRuntimesPerCaller)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The host or caller runtime quota is full.");
                }
                if (m_entries.Values.Any(value =>
                    value.Owner == caller && string.Equals(value.Name, name, StringComparison.Ordinal)))
                {
                    throw new ServiceResultException(StatusCodes.BadEntryExists,
                        "The caller already owns a runtime with that name.");
                }
                entry = new Entry(
                    Guid.NewGuid().ToString("N"), name, caller, capability, m_time.GetUtcNow(), m_options)
                {
                    Dependencies = [.. dependencies],
                    InvalidationSignals = [.. invalidationSignals]
                };
                m_entries.Add(entry.Handle, entry);
                m_sweeper ??= SweepAsync();
            }
            for (int index = 0; index < entry.Dependencies.Count; index++)
            {
                entry.DependencyTasks.Add(WatchDependencyAsync(entry, entry.Dependencies[index]));
            }
            for (int index = 0; index < entry.InvalidationSignals.Count; index++)
            {
                entry.DependencyTasks.Add(WatchInvalidationAsync(entry, entry.InvalidationSignals[index]));
            }
            using var timeout = new CancellationTokenSource(m_options.OperationTimeout, m_time);
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(
                ct, timeout.Token, entry.Lifetime.Token);
            try
            {
                T runtime = await factory(opening.Token).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The runtime factory returned no runtime.");
                entry.Creation.TrySetResult(runtime);
                ct.ThrowIfCancellationRequested();
                lock (m_state)
                {
                    if (entry.Closing || m_disposed || Expired(entry, m_time.GetUtcNow()))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The runtime was closed while activation was in progress.");
                    }
                    opening.Token.ThrowIfCancellationRequested();
                    entry.Runtime = runtime;
                    entry.LastUsed = m_time.GetUtcNow();
                    return Snapshot(entry);
                }
            }
            catch
            {
                entry.Creation.TrySetResult(null);
                try
                {
                    await BeginClose(entry).ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    m_logger.RuntimeCleanupFailed(entry.Handle, cleanup);
                }
                throw;
            }
        }

        /// <summary>
        /// Acquires an owned runtime operation without exposing synchronization or extending its absolute lease.
        /// </summary>
        /// <typeparam name="T">The runtime implementation required by the operation.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<McpRuntimeUse<T>> AcquireAsync<T>(
            string handle, McpCapability capability, CancellationToken ct = default)
            where T : class, IAsyncDisposable
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(handle);
            ct.ThrowIfCancellationRequested();
            McpCallerIdentity caller = m_policy.Require(capability);
            Entry entry;
            T runtime;
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                entry = GetOwned(handle, caller);
                if (entry.Runtime is not T typed)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "The handle does not identify a ready runtime of the requested kind.");
                }
                if (Expired(entry, m_time.GetUtcNow()))
                {
                    _ = BeginClose(entry);
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The runtime lease has expired.");
                }
                runtime = typed;
                entry.Users++;
            }
            CancellationTokenSource? timeout = null;
            CancellationTokenSource? linked = null;
            bool acquired = false;
            try
            {
                _ = m_policy.Require(entry.Capability);
                lock (m_state)
                {
                    if (entry.Closing || Expired(entry, m_time.GetUtcNow()))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The runtime closed before the operation could be admitted.");
                    }
                    entry.LastUsed = m_time.GetUtcNow();
                }
                timeout = new CancellationTokenSource(m_options.OperationTimeout, m_time);
                linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token, entry.Lifetime.Token);
                await entry.Operations.WaitAsync(linked.Token).ConfigureAwait(false);
                acquired = true;
                var use = new McpRuntimeUse<T>(runtime, () =>
                {
                    entry.Operations.Release();
                    Release(entry);
                }, timeout, linked);
                timeout = null;
                linked = null;
                return use;
            }
            catch
            {
                if (acquired)
                {
                    entry.Operations.Release();
                }
                Release(entry);
                throw;
            }
            finally
            {
                linked?.Dispose();
                timeout?.Dispose();
            }
        }

        /// <summary>
        /// Lists only the current caller's live handles and public lifecycle metadata.
        /// </summary>
        public ArrayOf<McpRuntimeInfo> List()
        {
            McpCallerIdentity caller = m_policy.RequireCaller();
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                DateTimeOffset now = m_time.GetUtcNow();
                return m_entries.Values
                    .Where(entry => entry.Owner == caller && (entry.Closing || !Expired(entry, now)))
                    .Select(Snapshot).ToArray().ToArrayOf();
            }
        }

        /// <summary>
        /// Closes one owned handle and waits for its in-flight work and activation to drain.
        /// Closing does not require continued permission to start new runtimes.
        /// </summary>
        public async ValueTask CloseAsync(string handle, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(handle);
            McpCallerIdentity caller = m_policy.RequireCaller();
            Task closing;
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                closing = BeginClose(GetOwned(handle, caller));
            }
            await closing.WaitAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_state)
            {
                m_disposal ??= DisposeCoreAsync();
                return new ValueTask(m_disposal);
            }
        }

        /// <summary>
        /// Drains every owned resource once, including closing entries that still consume capacity.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            Task[] closing;
            Task? sweeper;
            lock (m_state)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                foreach (Entry entry in m_entries.Values.ToArray())
                {
                    _ = BeginClose(entry);
                }
                closing = [.. m_closing];
                sweeper = m_sweeper;
            }
            await m_shutdown.CancelAsync().ConfigureAwait(false);
            try
            {
                if (sweeper is not null)
                {
                    await sweeper.ConfigureAwait(false);
                }
                await Task.WhenAll(closing).ConfigureAwait(false);
            }
            finally
            {
                m_shutdown.Dispose();
            }
        }

        /// <summary>
        /// Finds an owned entry without revealing whether a foreign caller's handle exists.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Entry GetOwned(string handle, McpCallerIdentity caller)
        {
            if (!m_entries.TryGetValue(handle, out Entry? entry) || entry.Owner != caller || entry.Closing)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    "The runtime handle is unknown, closed or not owned by this caller.");
            }
            return entry;
        }

        /// <summary>
        /// Starts one tracked close operation; resource disposal occurs outside the state guard.
        /// </summary>
        private Task BeginClose(Entry entry)
        {
            lock (m_state)
            {
                if (entry.CloseTask is not null)
                {
                    return entry.CloseTask;
                }
                entry.Closing = true;
                if (entry.Users == 0)
                {
                    entry.Drained.TrySetResult(true);
                }
                entry.CloseTask = Task.Run(async () =>
                {
                    Exception? cancellationFailure = null;
                    try
                    {
                        await entry.Lifetime.CancelAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cancellationFailure = exception;
                        m_logger.RuntimeCleanupFailed(entry.Handle, exception);
                    }
                    await entry.Drained.Task.ConfigureAwait(false);
                    IAsyncDisposable? runtime = await entry.Creation.Task.ConfigureAwait(false);
                    try
                    {
                        if (runtime is not null)
                        {
                            await runtime.DisposeAsync().ConfigureAwait(false);
                        }
                        if (cancellationFailure is not null)
                        {
                            throw new InvalidOperationException(
                                "Runtime disposal completed but cancellation callbacks failed.", cancellationFailure);
                        }
                        lock (m_state)
                        {
                            m_entries.Remove(entry.Handle);
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (m_state)
                        {
                            entry.Faulted = true;
                        }
                        m_logger.RuntimeCleanupFailed(entry.Handle, exception);
                        throw;
                    }
                    finally
                    {
                        await Task.WhenAll(entry.DependencyTasks).ConfigureAwait(false);
                        entry.Operations.Dispose();
                        entry.Lifetime.Dispose();
                    }
                });
                m_closing.Add(entry.CloseTask);
                return entry.CloseTask;
            }
        }

        /// <summary>
        /// Releases a pending or active operation and completes closing only after every user has left.
        /// </summary>
        private void Release(Entry entry)
        {
            lock (m_state)
            {
                entry.Users--;
                if (entry.Closing && entry.Users == 0)
                {
                    entry.Drained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Expires abandoned handles through the injected clock without running any protocol operation.
        /// </summary>
        private async Task SweepAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), m_time);
            try
            {
                while (await timer.WaitForNextTickAsync(m_shutdown.Token).ConfigureAwait(false))
                {
                    lock (m_state)
                    {
                        DateTimeOffset now = m_time.GetUtcNow();
                        foreach (Entry entry in m_entries.Values
                            .Where(entry => !entry.Closing && Expired(entry, now)).ToArray())
                        {
                            _ = BeginClose(entry);
                        }
                        m_closing.RemoveAll(task => task.IsCompletedSuccessfully);
                    }
                }
            }
            catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
            {
            }
        }

        /// <summary>
        /// Applies both independent lease ceilings, including activation time.
        /// </summary>
        private bool Expired(Entry entry, DateTimeOffset now)
        {
            if (now - entry.Created >= m_options.RuntimeLeaseTimeout ||
                now - entry.LastUsed >= m_options.RuntimeIdleTimeout)
            {
                return true;
            }
            foreach (McpSecretDependency dependency in entry.Dependencies)
            {
                if (dependency.Invalidated.IsCompleted || dependency.Reference.ExpiresAt <= now)
                {
                    return true;
                }
            }
            foreach (Task signal in entry.InvalidationSignals)
            {
                if (signal.IsCompleted)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Releases an owned runtime when a borrowed session/model generation is invalidated.
        /// </summary>
        private async Task WatchInvalidationAsync(Entry entry, Task invalidated)
        {
            var stopping = Task.Delay(Timeout.InfiniteTimeSpan, entry.Lifetime.Token);
            await Task.WhenAny(invalidated, stopping).ConfigureAwait(false);
            if (!entry.Lifetime.IsCancellationRequested)
            {
                _ = BeginClose(entry);
            }
        }

        /// <summary>
        /// Cancels one runtime at credential expiry or invalidation and exits before cleanup drains this watcher.
        /// </summary>
        private async Task WatchDependencyAsync(Entry entry, McpSecretDependency dependency)
        {
            TimeSpan remaining = dependency.Reference.ExpiresAt - m_time.GetUtcNow();
            remaining = remaining < m_options.RuntimeLeaseTimeout ? remaining : m_options.RuntimeLeaseTimeout;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(entry.Lifetime.Token);
            var expiry = Task.Delay(
                remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, m_time, cancellation.Token);
            try
            {
                await Task.WhenAny(expiry, dependency.Invalidated).ConfigureAwait(false);
                if (!entry.Lifetime.IsCancellationRequested)
                {
                    _ = BeginClose(entry);
                }
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Produces secret-free metadata without exposing the runtime implementation or owner identity.
        /// </summary>
        private McpRuntimeInfo Snapshot(Entry entry)
        {
            return new McpRuntimeInfo(
                entry.Handle, entry.Name, entry.Capability,
                entry.Faulted ? McpRuntimeState.Faulted : entry.Closing ? McpRuntimeState.Closing :
                    entry.Runtime is not null ? McpRuntimeState.Ready : McpRuntimeState.Opening,
                entry.Created, entry.Created + m_options.RuntimeLeaseTimeout,
                entry.LastUsed + m_options.RuntimeIdleTimeout);
        }

        private readonly IMcpOperationPolicy m_policy;
        private readonly McpExecutionOptions m_options;
        private readonly TimeProvider m_time;
        private readonly ILogger m_logger;
        private readonly Lock m_state = new();
        private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
        private readonly List<Task> m_closing = [];
        private readonly CancellationTokenSource m_shutdown = new();
        private Task? m_sweeper;
        private Task? m_disposal;
        private bool m_disposed;

        /// <summary>
        /// Owns one activation and the operations that must drain before its resource can be disposed.
        /// </summary>
        private sealed class Entry(
            string handle, string name, McpCallerIdentity owner, McpCapability capability,
            DateTimeOffset created, McpExecutionOptions options)
        {
            public string Handle { get; } = handle;
            public string Name { get; } = name;
            public McpCallerIdentity Owner { get; } = owner;
            public McpCapability Capability { get; } = capability;
            public DateTimeOffset Created { get; } = created;
            public DateTimeOffset LastUsed { get; set; } = created;
            public IAsyncDisposable? Runtime { get; set; }
            public CancellationTokenSource Lifetime { get; } = new();

            public SemaphoreSlim Operations { get; } = new(
                options.MaxConcurrentOperationsPerRuntime, options.MaxConcurrentOperationsPerRuntime);

            public TaskCompletionSource<IAsyncDisposable?> Creation { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Drained { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task? CloseTask { get; set; }
            public ArrayOf<McpSecretDependency> Dependencies { get; set; } = [];
            public ArrayOf<Task> InvalidationSignals { get; set; } = [];
            public List<Task> DependencyTasks { get; } = [];
            public int Users { get; set; }
            public bool Closing { get; set; }
            public bool Faulted { get; set; }
        }
    }

    /// <summary>
    /// Public lifecycle information for one caller-owned runtime, containing no transport credentials.
    /// </summary>
    public sealed record McpRuntimeInfo(
        string Handle, string Name, McpCapability Capability, McpRuntimeState State,
        DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset IdleExpiresAt);

    /// <summary>
    /// Observable state of a caller-owned runtime, including cleanup failures that continue consuming capacity.
    /// </summary>
    public enum McpRuntimeState
    {
        /// <summary>
        /// Activation has reserved capacity but has not completed.
        /// </summary>
        Opening,

        /// <summary>
        /// The runtime can accept authorized operations.
        /// </summary>
        Ready,

        /// <summary>
        /// The runtime is draining operations and releasing resources.
        /// </summary>
        Closing,

        /// <summary>
        /// Cleanup failed and the host cannot promise that the resource was released.
        /// </summary>
        Faulted
    }

    /// <summary>
    /// A bounded operation over a borrowed runtime; disposing the use does not dispose the runtime.
    /// </summary>
    /// <typeparam name="T">The runtime implementation required by the calling module.</typeparam>
    public sealed class McpRuntimeUse<T> : IAsyncDisposable where T : class, IAsyncDisposable
    {
        /// <summary>
        /// Records one already-admitted operation and its release action.
        /// </summary>
        internal McpRuntimeUse(
            T runtime, Action release, CancellationTokenSource timeout, CancellationTokenSource linked)
        {
            Runtime = runtime;
            CancellationToken = linked.Token;
            m_timeout = timeout;
            m_linked = linked;
            m_release = release;
        }

        /// <summary>
        /// Gets the borrowed runtime without transferring its lifecycle ownership.
        /// </summary>
        public T Runtime { get; }

        /// <summary>
        /// Gets cancellation combining caller, operation deadline and runtime closure.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Action? release = Interlocked.Exchange(ref m_release, null);
            if (release is not null)
            {
                m_linked.Dispose();
                m_timeout.Dispose();
                release();
            }
            return ValueTask.CompletedTask;
        }

        private readonly CancellationTokenSource m_timeout;
        private readonly CancellationTokenSource m_linked;
        private Action? m_release;
    }

    /// <summary>
    /// Source-generated diagnostics for owned runtime cleanup failures.
    /// </summary>
    internal static partial class McpRuntimeRegistryLog
    {
        /// <summary>
        /// Reports a failed cleanup without suppressing the owning close operation's failure.
        /// </summary>
        [LoggerMessage(EventId = McpServerEventIds.McpRuntimeRegistry, Level = LogLevel.Warning,
            Message = "MCP runtime {Handle} cleanup failed.")]
        public static partial void RuntimeCleanupFailed(this ILogger logger, string handle, Exception exception);
    }
}
