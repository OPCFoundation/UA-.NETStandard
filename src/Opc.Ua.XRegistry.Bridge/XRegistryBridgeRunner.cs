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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge
{
    /// <summary>
    /// Reusable bridge deployment modes.
    /// </summary>
    public enum XRegistryBridgeMode
    {
        /// <summary>
        /// HTTP routes over an authoritative OPC UA endpoint.
        /// </summary>
        HttpGateway,

        /// <summary>
        /// Native projection over an authoritative HTTP endpoint.
        /// </summary>
        OpcUaGateway,

        /// <summary>
        /// Durable reconciliation of independently writable registries.
        /// </summary>
        Synchronization
    }

    /// <summary>
    /// One named, caller-scoped upstream used for health inspection and optional invalidation hints.
    /// </summary>
    public sealed record XRegistryBridgeUpstream(
        string Name, IXRegistryEndpoint Endpoint, XRegistryCallContext Context);

    /// <summary>
    /// Runtime cadence and deadline configuration. No transport or credential is created by the runner.
    /// </summary>
    public sealed record XRegistryBridgeRunOptions
    {
        /// <summary>
        /// Gets the deployment mode.
        /// </summary>
        public XRegistryBridgeMode Mode { get; init; }

        /// <summary>
        /// Gets the full-repair cadence retained even when change hints are available.
        /// </summary>
        public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets the lower bound between hint-driven scans.
        /// </summary>
        public TimeSpan MinimumPassInterval { get; init; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets the deadline for each health inspection or projection refresh.
        /// </summary>
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets the bounded wait for subscription shutdown; late cleanup is retained and observed.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets whether the runner completes after one pass.
        /// </summary>
        public bool Once { get; init; }

        /// <summary>
        /// Gets whether synchronization is read-only and terminates after its preview.
        /// </summary>
        public bool DryRun { get; init; }
    }

    /// <summary>
    /// An atomic runtime status snapshot without credentials or upstream document contents.
    /// </summary>
    public sealed record XRegistryBridgeStatus
    {
        /// <summary>
        /// Gets whether the most recent complete pass found the bridge ready.
        /// </summary>
        public bool Ready { get; internal init; }

        /// <summary>
        /// Gets whether inspected upstream guarantees permit this mode's mutations.
        /// </summary>
        public bool CanWrite { get; internal init; }

        /// <summary>
        /// Gets whether the continuous runner has stopped.
        /// </summary>
        public bool Stopped { get; internal init; }

        /// <summary>
        /// Gets when the last pass completed.
        /// </summary>
        public DateTimeOffset? LastAttempt { get; internal init; }

        /// <summary>
        /// Gets the last verified successful pass time, retained when a subsequent pass fails.
        /// </summary>
        public DateTimeOffset? LastSuccess { get; internal init; }

        /// <summary>
        /// Gets the latest reconciliation report, or null for a gateway.
        /// </summary>
        public XRegistrySyncReport? Synchronization { get; internal init; }

        /// <summary>
        /// Gets a non-sensitive failure summary, or null when the last pass was successful.
        /// </summary>
        public string? Failure { get; internal init; }

        /// <summary>
        /// Time since the last successful complete pass, or null until one has succeeded.
        /// </summary>
        public TimeSpan? Lag { get; internal init; }

        /// <summary>
        /// Current native memory/disk/handle reservations when the projection supplies them.
        /// </summary>
        public XRegistryBridgeResourceUsage? ResourceUsage { get; internal init; }
    }

    /// <summary>
    /// Diagnostic reservation counts; they are not admission-control tokens or cross-process limits.
    /// </summary>
    public sealed record XRegistryBridgeResourceUsage(int OpenHandles, long BufferedBytes, long SpooledBytes);

    /// <summary>
    /// Optional resource accounting supplied by a native projection.
    /// </summary>
    public interface IXRegistryBridgeResourceUsage
    {
        /// <summary>
        /// Current usage, or null when the native manager is not available.
        /// </summary>
        XRegistryBridgeResourceUsage? ResourceUsage { get; }
    }

    /// <summary>
    /// Optional native projection control, implemented by the registered bridge node-manager factory.
    /// </summary>
    public interface IXRegistryBridgeProjection
    {
        /// <summary>
        /// Whether the projection or notification channel has degraded.
        /// </summary>
        bool IsDegraded { get; }

        /// <summary>
        /// Rebuilds a complete, generation-checked projection from its authoritative endpoint.
        /// </summary>
        ValueTask RefreshAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Injectable all-mode lifecycle, readiness and periodic repair with bounded coalesced change hints.
    /// The host owns transports, endpoint resolvers and the synchronization state store.
    /// </summary>
    public sealed class XRegistryBridgeRunner
    {
        /// <summary>
        /// Configures a runner without opening a listener, session or subscription.
        /// </summary>
        public XRegistryBridgeRunner(
            XRegistryBridgeRunOptions options, ArrayOf<XRegistryBridgeUpstream> upstreams, ITelemetryContext telemetry,
            XRegistrySynchronizer? synchronizer = null, IXRegistryBridgeProjection? projection = null,
            TimeProvider? timeProvider = null)
        {
            m_options = options.ThrowIfNull(nameof(options));
            if (upstreams.Count == 0 ||
                options.Mode is < XRegistryBridgeMode.HttpGateway or > XRegistryBridgeMode.Synchronization ||
                !Duration(options.PollInterval) ||
                !Duration(options.RequestTimeout) ||
                !Duration(options.ShutdownTimeout) ||
                options.MinimumPassInterval < TimeSpan.Zero ||
                options.MinimumPassInterval.TotalMilliseconds > uint.MaxValue - 1 ||
                ((options.Mode == XRegistryBridgeMode.Synchronization) != (synchronizer is not null)))
            {
                throw new ArgumentException(
                    "A mode, upstreams, bounded deadlines and matching synchronization job are required.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRegistryBridgeUpstream upstream in upstreams)
            {
                upstream.ThrowIfNull(nameof(upstreams));
                upstream.Endpoint.ThrowIfNull(nameof(upstreams));
                upstream.Context.ThrowIfNull(nameof(upstreams));
                if (string.IsNullOrWhiteSpace(upstream.Name) || !names.Add(upstream.Name))
                {
                    throw new ArgumentException("Upstreams require distinct nonempty names.", nameof(upstreams));
                }
            }
            m_upstreams = upstreams.Span.ToArray();
            m_synchronizer = synchronizer;
            m_projection = projection;
            m_time = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.ThrowIfNull(nameof(telemetry)).CreateLogger<XRegistryBridgeRunner>();
        }

        /// <summary>
        /// Gets the last published runtime status without contacting upstreams.
        /// </summary>
        public XRegistryBridgeStatus Status
        {
            get
            {
                XRegistryBridgeStatus status = Volatile.Read(ref m_status);
                return status with
                {
                    Lag = status.LastSuccess is { } success ? m_time.GetUtcNow() - success : null,
                    ResourceUsage = (m_projection as IXRegistryBridgeResourceUsage)?.ResourceUsage
                };
            }
        }

        /// <summary>
        /// Executes one non-overlapping health/repair pass.
        /// </summary>
        /// <exception cref="InvalidOperationException">Another pass is already running.</exception>
        public async ValueTask<XRegistryBridgeStatus> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref m_passActive, 1) != 0)
            {
                throw new InvalidOperationException("A bridge pass is already running.");
            }
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref m_passCompletion, completion.Task);
            var operations = new List<Task>();
            try
            {
                bool writable = true;
                foreach (XRegistryBridgeUpstream upstream in m_upstreams)
                {
                    XRegistryEndpointDescription description = await RunOperationAsync(
                        token => upstream.Endpoint.InspectAsync(upstream.Context, token), operations, cancellationToken)
                        .ConfigureAwait(false);
                    writable &= description.SupportsAtomicMutations &&
                        description.SupportsConditionalMutations &&
                        description.SupportsWriteTouch &&
                        (m_options.Mode != XRegistryBridgeMode.HttpGateway ||
                            (description.SupportsPreparedMutations &&
                                upstream.Endpoint is IXRegistryPreparedEndpoint));
                }
                if (m_projection is not null)
                {
                    _ = await RunOperationAsync(async token =>
                    {
                        await m_projection.RefreshAsync(token).ConfigureAwait(false);
                        return true;
                    }, operations, cancellationToken).ConfigureAwait(false);
                }
                XRegistrySyncReport? report = m_synchronizer is null ? null :
                    await m_synchronizer.RunOnceAsync(m_options.DryRun, cancellationToken).ConfigureAwait(false);
                bool ready = m_projection?.IsDegraded != true &&
                    (report is null || report.Status == XRegistrySyncStatus.Succeeded);
                var status = new XRegistryBridgeStatus
                {
                    Ready = ready,
                    CanWrite = writable && !m_options.DryRun,
                    LastAttempt = m_time.GetUtcNow(),
                    LastSuccess = ready ? m_time.GetUtcNow() : Status.LastSuccess,
                    Synchronization = report,
                    Failure = ready ? null : "Projection or reconciliation requires attention."
                };
                Volatile.Write(ref m_status, status);
                m_logger.BridgePassCompleted(m_options.Mode, status.Ready, status.CanWrite);
                return status;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                (XRegistryOperationDeadline.IsEndpointFailure(exception) || exception is InvalidOperationException))
            {
                m_logger.BridgePassFailed(exception);
                XRegistryBridgeStatus status = Status with
                {
                    Ready = false,
                    CanWrite = false,
                    LastAttempt = m_time.GetUtcNow(),
                    Failure = "An upstream health or repair operation failed."
                };
                Volatile.Write(ref m_status, status);
                return status;
            }
            finally
            {
                if (operations.All(operation => operation.IsCompleted))
                {
                    Interlocked.Exchange(ref m_passActive, 0);
                    completion.TrySetResult(true);
                }
                else
                {
                    _ = CompletePassAsync(Task.WhenAll(operations), completion, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Waits for actual pass and watch completion, including operations that outlive a caller deadline.
        /// Stop scheduled execution first, then await this method before disposing caller-owned transports or stores.
        /// Canceling this wait does not release the operations or their ownership.
        /// </summary>
        public async ValueTask WaitForPendingOperationsAsync(CancellationToken cancellationToken = default)
        {
            await Task.WhenAll(Volatile.Read(ref m_passCompletion), Volatile.Read(ref m_runCompletion))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs scheduled full repairs until cancellation or one-pass completion. Observers receive completed statuses.
        /// Change notifications only shorten the next full-inventory wait; they never authorize incremental deletion.
        /// </summary>
        /// <exception cref="InvalidOperationException">The runner is already active.</exception>
        public async Task<int> RunAsync(
            Func<XRegistryBridgeStatus, CancellationToken, ValueTask>? observer = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref m_running, 1) != 0)
            {
                throw new InvalidOperationException("The bridge runner is already active.");
            }
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref m_runCompletion, completion.Task);
            CancellationTokenSource? lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken runToken = lifetime.Token;
            Channel<bool> hints = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
            var watches = new List<Task>();
            if (!m_options.Once && !m_options.DryRun)
            {
                foreach (XRegistryBridgeUpstream upstream in m_upstreams)
                {
                    if (upstream.Endpoint is IXRegistryChangeFeed feed)
                    {
                        watches.Add(WatchAsync(feed, upstream.Context, hints, runToken));
                    }
                }
            }
            try
            {
                while (true)
                {
                    await Volatile.Read(ref m_passCompletion).WaitAsync(runToken).ConfigureAwait(false);
                    XRegistryBridgeStatus status = await RunOnceAsync(runToken).ConfigureAwait(false);
                    if (observer is not null)
                    {
                        await observer(status, runToken).ConfigureAwait(false);
                    }
                    if (m_options.Once ||
                        m_options.DryRun ||
                        status.Synchronization?.Status == XRegistrySyncStatus.Failed)
                    {
                        return status.Synchronization?.ExitCode ?? (status.Ready ? 0 : 1);
                    }
                    await WaitForNextAsync(hints, runToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
            finally
            {
                // Cancellation callbacks share the watch lifetime and are joined before disposal or ownership transfer.
                // TODO: Remove when CA2025 understands the shared cleanup Task.WhenAll.
#pragma warning disable CA2025
                watches.Add(CancelLifetimeAsync(lifetime));
#pragma warning restore CA2025
                watches.Add(Volatile.Read(ref m_passCompletion));
                Task stopping = Task.WhenAll(watches);
                var deadline =
                    new XRegistryOperationDeadline(m_time, m_options.ShutdownTimeout, CancellationToken.None);
                await using ConfiguredAsyncDisposable deadlineLifetime = deadline.ConfigureAwait(false);
                try
                {
                    await stopping.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested)
                {
                    m_logger.BridgeWatchCleanupDeferred();
                    CancellationTokenSource retained = lifetime;
                    lifetime = null;
                    // The observer owns the source until the watch finishes, including after this method returns.
                    // TODO: Remove when CA2025 recognizes transferred asynchronous cleanup ownership.
#pragma warning disable CA2025
                    _ = ObserveLateWatchAsync(stopping, retained, completion);
#pragma warning restore CA2025
                }
                finally
                {
                    Volatile.Write(ref m_status, Status with { Ready = false, Stopped = true });
                    bool completed = lifetime is not null;
                    lifetime?.Dispose();
                    if (completed)
                    {
                        Interlocked.Exchange(ref m_running, 0);
                        completion.TrySetResult(true);
                    }
                }
            }
        }

        private async ValueTask<T> RunOperationAsync<T>(
            Func<CancellationToken, ValueTask<T>> action, List<Task> operations, CancellationToken cancellationToken)
        {
            var deadline = new XRegistryOperationDeadline(m_time, m_options.RequestTimeout, cancellationToken);
            CancellationToken token = deadline.Token;
            Task<T> operation = CompleteOperationAsync(action, deadline, token);
            operations.Add(operation);
            return await operation.WaitAsync(token).ConfigureAwait(false);
        }

        private static async Task<T> CompleteOperationAsync<T>(
            Func<CancellationToken, ValueTask<T>> action, XRegistryOperationDeadline deadline, CancellationToken token)
        {
            await using ConfiguredAsyncDisposable lifetime = deadline.ConfigureAwait(false);
            return await action(token).ConfigureAwait(false);
        }

        private async Task CompletePassAsync(
            Task operations, TaskCompletionSource<bool> completion, CancellationToken cancellationToken)
        {
            try
            {
                await operations.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller-requested shutdown is completion, not a new health failure.
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException)
            {
                m_logger.BridgePassFailed(exception);
            }
            finally
            {
                Interlocked.Exchange(ref m_passActive, 0);
                completion.TrySetResult(true);
            }
        }

        private static Task CancelLifetimeAsync(CancellationTokenSource lifetime)
        {
#if NET8_0_OR_GREATER
            return lifetime.CancelAsync();
#else
            return Task.Run(lifetime.Cancel);
#endif
        }

        private async Task WatchAsync(
            IXRegistryChangeFeed feed, XRegistryCallContext context, Channel<bool> hints, CancellationToken ct)
        {
            await Task.Yield();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await foreach (XRegistryChangeHint hint in feed.WatchAsync(context, ct).ConfigureAwait(false))
                    {
                        _ = hint;
                        hints.Writer.TryWrite(true);
                        if (m_options.MinimumPassInterval > TimeSpan.Zero)
                        {
                            await XRegistryOperationDeadline.DelayAsync(m_time, MinimumDelay(), ct).ConfigureAwait(
                                false);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (NotSupportedException)
                {
                    m_logger.BridgePollingOnly();
                    return;
                }
                catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                    exception is InvalidOperationException)
                {
                    m_logger.BridgeWatchFailed(exception);
                }
                hints.Writer.TryWrite(true);
                try
                {
                    await XRegistryOperationDeadline.DelayAsync(m_time, m_options.PollInterval, ct).ConfigureAwait(
                        false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private async ValueTask WaitForNextAsync(Channel<bool> hints, CancellationToken ct)
        {
            bool signaled = false;
            var deadline = new XRegistryOperationDeadline(m_time, m_options.PollInterval, ct);
            await using (deadline.ConfigureAwait(false))
            {
                try
                {
                    signaled = await hints.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // The periodic full repair is due even without a change hint.
                }
            }
            while (hints.Reader.TryRead(out _))
            {
            }
            if (signaled && m_options.MinimumPassInterval > TimeSpan.Zero)
            {
                await XRegistryOperationDeadline.DelayAsync(m_time, MinimumDelay(), ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
        }

        private async Task ObserveLateWatchAsync(
            Task task, CancellationTokenSource lifetime, TaskCompletionSource<bool> completion)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException)
            {
                m_logger.BridgeWatchFailed(exception);
            }
            finally
            {
                lifetime.Dispose();
                Interlocked.Exchange(ref m_running, 0);
                completion.TrySetResult(true);
            }
        }

        private static bool Duration(TimeSpan value)
        {
            return value > TimeSpan.Zero && value.TotalMilliseconds <= uint.MaxValue - 1;
        }

        private TimeSpan MinimumDelay()
        {
            return m_options.MinimumPassInterval <= m_options.PollInterval
                ? m_options.MinimumPassInterval : m_options.PollInterval;
        }

        private readonly XRegistryBridgeRunOptions m_options;
        private readonly XRegistryBridgeUpstream[] m_upstreams;
        private readonly XRegistrySynchronizer? m_synchronizer;
        private readonly IXRegistryBridgeProjection? m_projection;
        private readonly TimeProvider m_time;
        private readonly ILogger m_logger;
        private XRegistryBridgeStatus m_status = new();
        private Task m_passCompletion = Task.CompletedTask;
        private Task m_runCompletion = Task.CompletedTask;
        private int m_passActive;
        private int m_running;
    }

    /// <summary>
    /// Registers the reusable lifecycle while leaving transport and persistent-store ownership with the host.
    /// </summary>
    public static class XRegistryBridgeRunnerServiceCollectionExtensions
    {
        /// <summary>
        /// Registers one runner over explicitly supplied upstreams and optional registered
        /// synchronization/projection services.
        /// </summary>
        public static IServiceCollection AddXRegistryBridgeRunner(
            this IServiceCollection services, XRegistryBridgeRunOptions options,
                ArrayOf<XRegistryBridgeUpstream> upstreams)
        {
            services.ThrowIfNull(nameof(services));
            options.ThrowIfNull(nameof(options));
            services.AddSingleton(options);
            services.AddSingleton(provider => new XRegistryBridgeRunner(options, upstreams,
                provider.GetRequiredService<ITelemetryContext>(), options.Mode == XRegistryBridgeMode.Synchronization
                    ? provider.GetService<XRegistrySynchronizer>() : null,
                provider.GetService<IXRegistryBridgeProjection>(), provider.GetService<TimeProvider>()));
            return services;
        }
    }

    internal static partial class XRegistryBridgeRunnerLog
    {
        [LoggerMessage(EventId = XRegistryBridgeEventIds.Runner, Level = LogLevel.Information,
            Message = "xRegistry bridge pass for {Mode}: ready={Ready}, writable={Writable}.")]
        public static partial void BridgePassCompleted(
            this ILogger logger, XRegistryBridgeMode mode, bool ready, bool writable);

        [LoggerMessage(EventId = XRegistryBridgeEventIds.Runner + 1, Level = LogLevel.Warning,
            Message = "xRegistry bridge health or repair failed.")]
        public static partial void BridgePassFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryBridgeEventIds.Runner + 2, Level = LogLevel.Warning,
            Message = "xRegistry change hints are unavailable; periodic full repair continues.")]
        public static partial void BridgeWatchFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryBridgeEventIds.Runner + 3, Level = LogLevel.Information,
            Message = "xRegistry upstream has no change feed; periodic full repair remains active.")]
        public static partial void BridgePollingOnly(this ILogger logger);

        [LoggerMessage(EventId = XRegistryBridgeEventIds.Runner + 4, Level = LogLevel.Warning,
            Message = "xRegistry subscription shutdown exceeded its deadline; late cleanup remains observed.")]
        public static partial void BridgeWatchCleanupDeferred(this ILogger logger);
    }
}
