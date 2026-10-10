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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Owns one observation, its ordered consumer, refresh reconciliation and explicit
/// commands. UI rendering polls bounded snapshots, never posts per-event work.
/// </summary>
internal sealed class AlarmWorkspace : IAsyncDisposable
{
    public AlarmWorkspace(IAlarmBackend backend, ITelemetryContext telemetry, TimeProvider? timeProvider = null)
    {
        m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentNullException.ThrowIfNull(telemetry);
        m_logger = telemetry.CreateLogger<AlarmWorkspace>();
        m_time = timeProvider ?? TimeProvider.System;
    }

    public AlarmCommandResult LastResult
    {
        get
        {
            lock (m_stateLock)
            {
                return m_lastResult;
            }
        }
    }

    public AlarmStreamHealth? Health
    {
        get
        {
            lock (m_stateLock)
            {
                return m_run?.Observation?.Health;
            }
        }
    }

    public AlarmSnapshot Snapshot()
    {
        lock (m_stateLock)
        {
            if (m_run?.Observation is { } observation)
            {
                m_state.ObserveDroppedUpdates(observation.Health.DroppedUpdates, m_time.GetUtcNow());
            }
            return m_state.Snapshot(m_time.GetUtcNow());
        }
    }

    public async Task StartAsync(
        AlarmSource source,
        TimeSpan publishingInterval,
        long generation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_closed.Token);
        await m_lifecycle.WaitAsync(request.Token).ConfigureAwait(false);
        try
        {
            lock (m_stateLock)
            {
                if (m_run is { } current && current.Generation == generation &&
                    current.Source == source && current.PublishingInterval == publishingInterval &&
                    current.Observation is not null && current.Consumer?.IsCompleted != true && m_state.IsObserving)
                {
                    return;
                }
            }
            await ReleaseRunAsync().ConfigureAwait(false);
            var run = new ObservationRun(source, publishingInterval, generation);
            lock (m_stateLock)
            {
                if (m_source != source)
                {
                    m_state = new AlarmState();
                    m_source = source;
                }
                m_run = run;
                m_state.ResetStreamCounters();
            }
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(
                request.Token, run.Cancellation.Token);
            bool installed = false;
            try
            {
                run.Observation = await m_backend.OpenAsync(source, publishingInterval, opening.Token)
                    .ConfigureAwait(false);
                opening.Token.ThrowIfCancellationRequested();
                lock (m_stateLock)
                {
                    m_state.SetObserving(true, m_time.GetUtcNow(),
                        "Source attached/recreated. Previous branches are stale until observed or refreshed.");
                }
                run.Consumer = ConsumeAsync(run);
                await RefreshRunAsync(run, automatic: true, opening.Token).ConfigureAwait(false);
                installed = true;
                m_logger.AlarmObservationStarted();
            }
            finally
            {
                if (!installed)
                {
                    await ReleaseRunAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            m_lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await CancelCurrentAsync().ConfigureAwait(false);
        await m_lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReleaseRunAsync().ConfigureAwait(false);
        }
        finally
        {
            m_lifecycle.Release();
        }
    }

    public async Task ResetAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (m_stateLock)
        {
            m_state = new AlarmState();
            m_source = null;
            m_lastResult = new AlarmCommandResult(
                AlarmCommandOutcome.Unavailable, "No operator command has been sent.");
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObservationRun? run;
        lock (m_stateLock)
        {
            run = m_run;
        }
        if (run?.Observation is null)
        {
            SetResult(new AlarmCommandResult(AlarmCommandOutcome.Unavailable, "Start observation before refreshing."));
            return;
        }
        await RefreshRunAsync(run, automatic: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArrayOf<AlarmOperation>> InspectAsync(AlarmKey key, CancellationToken cancellationToken = default)
    {
        if (!await m_work.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("An alarm operation is already in progress.");
        }
        try
        {
            ObservationRun run;
            AlarmRow row;
            lock (m_stateLock)
            {
                (run, row) = CurrentSelection(key);
            }
            using var request = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, run.Cancellation.Token, m_closed.Token);
            request.CancelAfter(TimeSpan.FromSeconds(20));
            return await run.Observation!.InspectAsync(row.Condition, request.Token).ConfigureAwait(false);
        }
        finally
        {
            m_work.Release();
        }
    }

    public async Task<AlarmCommandResult> ExecuteAsync(
        AlarmKey key,
        AlarmOperationKind operation,
        string comment = "",
        double shelvingMilliseconds = 60000,
        int responseIndex = 0,
        ByteString expectedEventId = default,
        CancellationToken cancellationToken = default)
    {
        if (!await m_work.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return SetResult(new AlarmCommandResult(AlarmCommandOutcome.Unavailable,
                "An alarm operation is already in progress; no second command was queued."));
        }
        try
        {
            ObservationRun run;
            AlarmRow row;
            lock (m_stateLock)
            {
                if (!m_state.TryGetCondition(key, out AlarmRow? selected) || selected is null || selected.IsStale)
                {
                    return SetResult(new AlarmCommandResult(AlarmCommandOutcome.Stale,
                        "The selected branch is missing or unreconciled. Wait for a fresh event or refresh."));
                }
                if (!expectedEventId.IsNull && selected.Condition.EventId != expectedEventId)
                {
                    return SetResult(new AlarmCommandResult(AlarmCommandOutcome.Stale,
                        "A newer event arrived for this branch. Review the latest event before sending a command."));
                }
                (run, row) = CurrentSelection(key);
            }
            using var request = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, run.Cancellation.Token, m_closed.Token);
            request.CancelAfter(TimeSpan.FromSeconds(20));
            var command = new AlarmCommand(row.Condition, operation, comment, shelvingMilliseconds, responseIndex);
            AlarmCommandAdapter.Validate(command);
            await run.Observation!.ExecuteAsync(command, request.Token).ConfigureAwait(false);
            m_logger.AlarmCommandAccepted(operation);
            return SetResult(new AlarmCommandResult(AlarmCommandOutcome.Accepted,
                $"{operation} accepted for EventId {row.Condition.EventId.ToBase64()}. " +
                    "Waiting for a server event; displayed state was not changed optimistically."));
        }
        catch (ServiceResultException exception)
        {
            m_logger.AlarmCommandRejected(exception, operation);
            return SetResult(AlarmCommandResult.FromServiceResult(exception));
        }
        catch (OperationCanceledException)
        {
            return SetResult(new AlarmCommandResult(AlarmCommandOutcome.Canceled,
                $"{operation} canceled or timed out. The server outcome may be unknown; no command was replayed."));
        }
        catch (NotSupportedException exception)
        {
            return SetResult(new AlarmCommandResult(
                AlarmCommandOutcome.Unsupported, AlarmLimits.Text(exception.Message)));
        }
        catch (InvalidOperationException exception)
        {
            return SetResult(new AlarmCommandResult(
                AlarmCommandOutcome.Unavailable, AlarmLimits.Text(exception.Message)));
        }
        catch (ArgumentException exception)
        {
            return SetResult(new AlarmCommandResult(AlarmCommandOutcome.Failed, AlarmLimits.Text(exception.Message)));
        }
        finally
        {
            m_work.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (m_stateLock)
        {
            return new ValueTask(m_disposal ??= DisposeCoreAsync());
        }
    }

    private (ObservationRun Run, AlarmRow Row) CurrentSelection(AlarmKey key)
    {
        if (m_run is not { Observation: { Health.IsReady: true } } run ||
            !m_state.IsObserving)
        {
            throw new InvalidOperationException("The alarm source is not connected and observing.");
        }
        m_state.ObserveDroppedUpdates(run.Observation.Health.DroppedUpdates, m_time.GetUtcNow());
        if (!m_state.TryGetCondition(key, out AlarmRow? row) || row is null || row.IsStale)
        {
            throw new InvalidOperationException("Select a freshly observed condition branch.");
        }
        return (run, row);
    }

    private async Task<bool> RefreshRunAsync(
        ObservationRun run,
        bool automatic,
        CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, run.Cancellation.Token, m_closed.Token);
        request.CancelAfter(TimeSpan.FromSeconds(20));
        bool entered = false;
        try
        {
            if (automatic)
            {
                await m_work.WaitAsync(request.Token).ConfigureAwait(false);
                entered = true;
            }
            else
            {
                entered = await m_work.WaitAsync(0, request.Token).ConfigureAwait(false);
            }
            if (!entered)
            {
                SetResult(new AlarmCommandResult(AlarmCommandOutcome.Unavailable,
                    "An alarm operation is already in progress; refresh was not queued."));
                return false;
            }
            IAlarmObservation? observation = run.Observation;
            lock (m_stateLock)
            {
                if (!ReferenceEquals(m_run, run) || observation is null ||
                    (!automatic && !observation.Health.IsReady) ||
                    !m_state.RequestRefresh(observation.Health.PartitionIds, m_time.GetUtcNow()))
                {
                    return false;
                }
            }
            await observation.RefreshAsync(request.Token).ConfigureAwait(false);
            m_logger.AlarmRefreshRequested();
        }
        catch (ServiceResultException exception)
        {
            m_logger.AlarmRefreshFailed(exception);
            RefreshFailed(AlarmCommandResult.FromServiceResult(exception).Detail);
        }
        catch (InvalidOperationException exception)
        {
            RefreshFailed(AlarmLimits.Text(exception.Message));
        }
        catch (NotSupportedException exception)
        {
            RefreshFailed("Unsupported: " + AlarmLimits.Text(exception.Message));
        }
        catch (TimeoutException exception)
        {
            RefreshFailed(AlarmLimits.Text(exception.Message));
        }
        catch (OperationCanceledException)
        {
            RefreshFailed("ConditionRefresh was canceled or timed out; no complete snapshot was established.");
        }
        finally
        {
            if (entered)
            {
                m_work.Release();
            }
        }
        return true;
    }

    private async Task ConsumeAsync(ObservationRun run)
    {
        IAlarmObservation observation = run.Observation!;
        CancellationToken token = run.Cancellation.Token;
        bool refresh = false;
        try
        {
            while (await observation.Updates.WaitToReadAsync(token).ConfigureAwait(false))
            {
                int budget = 128;
                while (budget-- > 0 && observation.Updates.TryRead(out AlarmUpdate? update))
                {
                    token.ThrowIfCancellationRequested();
                    lock (m_stateLock)
                    {
                        if (!ReferenceEquals(m_run, run))
                        {
                            return;
                        }
                        DateTimeOffset now = m_time.GetUtcNow();
                        m_state.ObserveDroppedUpdates(observation.Health.DroppedUpdates, now);
                        if (update.Kind == AlarmUpdateKind.RefreshStart && !m_state.IsRefreshActive)
                        {
                            m_state.RequestRefresh(observation.Health.PartitionIds, now);
                        }
                        m_state.Apply(update, now);
                    }
                    refresh |= update.Kind is AlarmUpdateKind.Created or
                        AlarmUpdateKind.Recovered or AlarmUpdateKind.RefreshRequired;
                }
                if (refresh)
                {
                    refresh = !await RefreshRunAsync(run, automatic: true, token).ConfigureAwait(false);
                }
            }
            lock (m_stateLock)
            {
                if (ReferenceEquals(m_run, run) && !token.IsCancellationRequested)
                {
                    m_state.SetObserving(false, m_time.GetUtcNow(),
                        "The event stream ended. Previous conditions are stale; restart observation.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The awaited document lifecycle owns cancellation and subscription disposal.
        }
        catch (ChannelClosedException exception)
        {
            RefreshFailed("The event stream failed: " + AlarmLimits.Text(exception.Message));
        }
    }

    private AlarmCommandResult SetResult(AlarmCommandResult result)
    {
        lock (m_stateLock)
        {
            m_lastResult = result;
            m_state.AddHistory(m_time.GetUtcNow(), result.Detail);
        }
        return result;
    }

    private void RefreshFailed(string detail)
    {
        lock (m_stateLock)
        {
            m_state.RefreshFailed(m_time.GetUtcNow(), detail);
        }
    }

    private async Task CancelCurrentAsync()
    {
        Task cancellation;
        lock (m_stateLock)
        {
            cancellation = m_run?.Cancellation.CancelAsync() ?? Task.CompletedTask;
        }
        await cancellation.ConfigureAwait(false);
    }

    private async Task ReleaseRunAsync()
    {
        ObservationRun? run;
        lock (m_stateLock)
        {
            run = m_run;
            m_run = null;
            m_state.SetObserving(false, m_time.GetUtcNow(),
                "Observation stopped/offline. Captured branches are stale; no mutation will be replayed.");
        }
        if (run is null)
        {
            return;
        }
        try
        {
            try
            {
                await run.Cancellation.CancelAsync().ConfigureAwait(false);
                if (run.Consumer is not null)
                {
                    await run.Consumer.ConfigureAwait(false);
                }
            }
            finally
            {
                await m_work.WaitAsync().ConfigureAwait(false);
                try
                {
                    await run.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_work.Release();
                }
            }
        }
        catch (ServiceResultException exception)
        {
            RefreshFailed("Subscription cleanup failed: " + AlarmLimits.Text(exception.Message));
            m_logger.AlarmCleanupFailed(exception);
            throw;
        }
    }

    private async Task DisposeCoreAsync()
    {
        await m_closed.CancelAsync().ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            m_closed.Dispose();
            m_lifecycle.Dispose();
            m_work.Dispose();
        }
    }

    private readonly IAlarmBackend m_backend;
    private readonly ILogger m_logger;
    private readonly TimeProvider m_time;
    private readonly Lock m_stateLock = new();
    private readonly SemaphoreSlim m_lifecycle = new(1, 1);
    private readonly SemaphoreSlim m_work = new(1, 1);
    private readonly CancellationTokenSource m_closed = new();
    private AlarmState m_state = new();
    private ObservationRun? m_run;
    private AlarmSource? m_source;
    private Task? m_disposal;
    private AlarmCommandResult m_lastResult = new(
        AlarmCommandOutcome.Unavailable, "No operator command has been sent.");

    private sealed class ObservationRun(
        AlarmSource source,
        TimeSpan publishingInterval,
        long generation) : IAsyncDisposable
    {
        public AlarmSource Source { get; } = source;

        public TimeSpan PublishingInterval { get; } = publishingInterval;

        public long Generation { get; } = generation;

        public CancellationTokenSource Cancellation { get; } = new();

        public IAlarmObservation? Observation { get; set; }

        public Task? Consumer { get; set; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Observation is not null)
                {
                    await Observation.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Cancellation.Dispose();
            }
        }
    }
}
