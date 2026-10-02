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
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Diagnostics;

namespace UaLens.Plugins.Continuity;

internal enum ContinuityRunPhase
{
    Ready,
    Starting,
    Running,
    Stepping,
    WaitingForRestore,
    ObservingAfterStep,
    RequiresSetup,
    Stopping,
    Stopped,
    Failed
}

/// <summary>
/// An opt-in run. Stop cancels and joins outstanding work before disposing any
/// backend resource, including a Start that has not returned yet.
/// </summary>
internal sealed class ContinuityRun : IAsyncDisposable
{
    public ContinuityRun(IContinuityBackend backend, ContinuityTimeline timeline)
    {
        m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
    }

    public ContinuityTimeline Timeline { get; }

    public ContinuityRunPhase Phase
    {
        get
        {
            lock (m_gate)
            {
                return m_phase;
            }
        }
    }

    public string Status
    {
        get
        {
            lock (m_gate)
            {
                return m_status;
            }
        }
    }

    public Task StartAsync(ContinuityConfiguration configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposeTask is not null, this);
            if (m_phase != ContinuityRunPhase.Ready)
            {
                throw new InvalidOperationException("Each Continuity run can be started only once.");
            }
            ct.ThrowIfCancellationRequested();
            ContinuitySetup setup = m_backend.CheckSetup(configuration);
            if (!setup.CanStart)
            {
                m_phase = ContinuityRunPhase.RequiresSetup;
                m_status = setup.Description;
                Timeline.Record(ContinuityEvidenceKind.Requirement, setup.Description);
                return Task.CompletedTask;
            }
            m_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            m_phase = ContinuityRunPhase.Starting;
            m_status = "Creating only the lab's resources…";
            m_operation = StartCoreAsync(configuration, m_cts.Token);
            return m_operation;
        }
    }

    public Task StepAsync(CancellationToken ct)
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposeTask is not null, this);
            if (m_phase is not (ContinuityRunPhase.Running or ContinuityRunPhase.WaitingForRestore))
            {
                throw new InvalidOperationException("A running lab scenario is required.");
            }
            m_phase = ContinuityRunPhase.Stepping;
            m_status = "Executing the selected step on owned resources…";
            m_operation = StepCoreAsync(ct);
            return m_operation;
        }
    }

    public Task StopAsync()
    {
        lock (m_gate)
        {
            if (m_stopTask is not null)
            {
                return m_stopTask;
            }
            m_phase = ContinuityRunPhase.Stopping;
            m_status = "Stopping; waiting for owned-resource cleanup…";
            m_stopTask = StopCoreAsync(m_operation, m_cts);
            return m_stopTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_disposeTask ??= DisposeCoreAsync();
            return new ValueTask(m_disposeTask);
        }
    }

    private async Task StartCoreAsync(ContinuityConfiguration configuration, CancellationToken ct)
    {
        try
        {
            await m_backend.StartAsync(configuration, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            SetState(ContinuityRunPhase.Running, "Observing actual V2 callbacks. No loss guarantee is inferred.");
            Timeline.Record(ContinuityEvidenceKind.Started, "Lab observation started after resource creation.");
        }
        catch (OperationCanceledException)
        {
            SetState(ContinuityRunPhase.Failed, "Start canceled; Stop releases partially created resources.");
            throw;
        }
        catch (Exception exception) when (exception is ServiceResultException or InvalidOperationException or
            NotSupportedException or TimeoutException or IOException or UnauthorizedAccessException or
            ArgumentException or FormatException or AggregateException)
        {
            SetState(ContinuityRunPhase.Failed, "Start failed: " + CorrelatedDiagnostics.Failure(exception));
            Timeline.Record(ContinuityEvidenceKind.Error, "Start failed: " + CorrelatedDiagnostics.Failure(exception));
            throw;
        }
    }

    private async Task StepCoreAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_cts!.Token);
        try
        {
            ContinuityStepResult result = await m_backend.StepAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            SetState(
                result.WaitingForRestore
                    ? ContinuityRunPhase.WaitingForRestore
                    : ContinuityRunPhase.ObservingAfterStep,
                result.Description);
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            InvalidOperationException or NotSupportedException or TimeoutException or IOException or
            UnauthorizedAccessException or ArgumentException or FormatException or AggregateException)
        {
            SetState(ContinuityRunPhase.Failed, "Step failed: " + CorrelatedDiagnostics.Failure(exception));
            Timeline.Record(ContinuityEvidenceKind.Error, "Step failed: " + CorrelatedDiagnostics.Failure(exception));
            throw;
        }
    }

    private async Task StopCoreAsync(Task operation, CancellationTokenSource? source)
    {
        Exception? cleanupFailure = null;
        try
        {
            if (source is not null)
            {
                try
                {
                    await source.CancelAsync().ConfigureAwait(false);
                }
                catch (AggregateException exception)
                {
                    Timeline.Record(ContinuityEvidenceKind.Error,
                        "Cancellation callback failed: " + CorrelatedDiagnostics.Failure(exception));
                }
            }
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
                InvalidOperationException or NotSupportedException or TimeoutException or IOException or
                UnauthorizedAccessException or ArgumentException or FormatException or AggregateException)
            {
                // The operation already recorded its failure. Cleanup must still run.
            }
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await m_backend.StopAsync(cleanup.Token).ConfigureAwait(false);
                SetState(ContinuityRunPhase.Stopped,
                    "Stopped. Lab client resources released; remote deletion is evidenced separately.");
                Timeline.Record(ContinuityEvidenceKind.Stopped, "Lab client cleanup completed.");
            }
            catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
                InvalidOperationException or NotSupportedException or TimeoutException or IOException or
                UnauthorizedAccessException or AggregateException)
            {
                SetState(ContinuityRunPhase.Failed,
                    "Cleanup could not be confirmed: " + CorrelatedDiagnostics.Failure(exception));
                Timeline.Record(ContinuityEvidenceKind.CleanupUncertain,
                    "Owned server resources may remain until expiry: " + CorrelatedDiagnostics.Failure(exception));
                cleanupFailure = exception;
            }
            finally
            {
                m_cts?.Dispose();
                m_cts = null;
            }
        }
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await m_backend.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void SetState(ContinuityRunPhase phase, string status)
    {
        lock (m_gate)
        {
            if (m_phase == ContinuityRunPhase.Stopping &&
                phase is not (ContinuityRunPhase.Stopped or ContinuityRunPhase.Failed))
            {
                return;
            }
            m_phase = phase;
            m_status = status;
        }
    }

    private readonly System.Threading.Lock m_gate = new();
    private readonly IContinuityBackend m_backend;
    private ContinuityRunPhase m_phase;
    private string m_status = "Ready. Start is explicit.";
    private CancellationTokenSource? m_cts;
    private Task m_operation = Task.CompletedTask;
    private Task? m_stopTask;
    private Task? m_disposeTask;
}
