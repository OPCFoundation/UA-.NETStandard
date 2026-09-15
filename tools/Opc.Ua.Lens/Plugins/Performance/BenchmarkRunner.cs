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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Performance;

/// <summary>
/// The kind of synthetic operation issued by <see cref="BenchmarkRunner"/>.
/// </summary>
internal enum BenchmarkMode
{
    /// <summary>
    /// Single-value <c>WriteAsync</c> per op (Part 4 §5.10.4).
    /// </summary>
    Write,

    /// <summary>
    /// Method <c>CallAsync</c> per op (Part 4 §5.11.2).
    /// </summary>
    Call
}

/// <summary>
/// Strategy for generating per-op write values or method input arguments.
/// </summary>
internal enum ValueGenerator
{
    /// <summary>
    /// Uniform random in the appropriate domain for the data type.
    /// </summary>
    Random,

    /// <summary>
    /// Monotonically increasing counter, wrapped to fit the data type.
    /// </summary>
    Sequential,

    /// <summary>
    /// Same literal value reused for every op.
    /// </summary>
    Fixed
}

/// <summary>
/// Frozen target descriptor — the NodeId to write to (Write mode) or
/// the (Object, Method, InputArgument signature) tuple to call (Call
/// mode).  Picked once via <c>PerformanceTargetDialog</c> and reused
/// for every op of the run.
/// </summary>
internal sealed record BenchmarkTarget(
    BenchmarkMode Mode,
    NodeId NodeId,
    NodeId ObjectId,
    BuiltInType BuiltInType,
    int ValueRank,
    Argument[]? InputArguments,
    string DisplayName);

/// <summary>
/// One result point emitted by <see cref="BenchmarkRunner"/>: the
/// wall-clock latency of a single op in milliseconds, plus the wall-clock
/// timestamp of completion (ticks since <c>Stopwatch</c> startup).
/// Errors are signalled via <see cref="Success"/>=false.
/// </summary>
internal readonly record struct BenchmarkSample(
    long CompletedAtTicks,
    double LatencyMs,
    bool Success);

/// <summary>
/// Background runner that pumps synthetic Write or Call ops at a
/// configured target rate against an OPC UA session, cooperatively cancellable.
/// At most 256 operation tasks are retained. Stop and natural completion both
/// drain issued operations before publishing the final snapshot boundary.
/// </summary>
internal sealed class BenchmarkRunner : IAsyncDisposable
{
    public BenchmarkRunner(
        ISession session,
        BenchmarkTarget target,
        ValueGenerator generator,
        double targetRatePerSec,
        bool unboundedBurst,
        TimeSpan duration)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_target = target ?? throw new ArgumentNullException(nameof(target));
        if (!Enum.IsDefined(target.Mode) || target.NodeId.IsNull ||
            (target.Mode == BenchmarkMode.Call && target.ObjectId.IsNull))
        {
            throw new ArgumentException("The benchmark target is incomplete or invalid.", nameof(target));
        }
        if (!Enum.IsDefined(generator))
        {
            throw new ArgumentOutOfRangeException(nameof(generator));
        }
        if (!double.IsFinite(targetRatePerSec) || (!unboundedBurst && targetRatePerSec < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(targetRatePerSec));
        }
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        m_generator = generator;
        m_targetRatePerSec = targetRatePerSec;
        m_unboundedBurst = unboundedBurst;
        m_duration = duration;
        Argument[] signature = target.InputArguments ?? [];
        var inputTypes = new BuiltInType[signature.Length];
        for (int i = 0; i < signature.Length; i++)
        {
            inputTypes[i] = ValueFactory.BuiltInForArgument(signature[i]);
        }
        m_inputTypes = new ArrayOf<BuiltInType>(inputTypes);
    }

    /// <summary>
    /// True from Start until all issued operations have settled.
    /// </summary>
    public bool IsRunning => m_loopTask is { IsCompleted: false };

    public TimeSpan Elapsed { get; private set; }
    public BenchmarkCompletion Completion { get; private set; }

    /// <summary>
    /// Raised once per completed operation, on the runner thread.
    /// </summary>
    public event Action<BenchmarkSample>? OnSample;

    /// <summary>
    /// Raised once after all samples, including on cancellation or failure.
    /// </summary>
    public event Action<string?>? OnFinished;

    /// <summary>
    /// Computes the recommended max-concurrency cap for a given target
    /// rate.  Capped at <see cref="MaxConcurrencyCap"/> to avoid queue
    /// blow-up when latency spikes.
    /// </summary>
    public static int RecommendConcurrency(double targetRatePerSec)
    {
        if (!double.IsFinite(targetRatePerSec) || targetRatePerSec < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetRatePerSec));
        }
        if (targetRatePerSec >= MaxConcurrencyCap * 500.0)
        {
            return MaxConcurrencyCap;
        }
        return Math.Max(4, (int)Math.Ceiling(targetRatePerSec / 500.0));
    }

    /// <summary>
    /// Starts the run on a background task.  Idempotent — subsequent
    /// calls are ignored while a run is already in flight.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }
        if (m_loopTask is not null)
        {
            throw new InvalidOperationException("Stop or dispose the previous runner before starting it again.");
        }
        Elapsed = TimeSpan.Zero;
        Completion = BenchmarkCompletion.Unknown;
        m_cts = new CancellationTokenSource();
        CancellationToken ct = m_cts.Token;
        m_loopTask = Task.Run(() => RunAsync(ct));
    }

    /// <summary>
    /// Cancels the run and awaits every issued operation. A channel that has not
    /// honored cancellation remains visibly stopping rather than leaking work
    /// into a subsequent run or publishing an incomplete distribution as complete.
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            m_cts?.Cancel();
            if (m_loopTask is { } loop)
            {
                await loop.ConfigureAwait(false);
            }
        }
        finally
        {
            m_cts?.Dispose();
            m_cts = null;
            m_loopTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? error = null;
        bool loopFinished = false;
        int concurrency = m_unboundedBurst
            ? MaxConcurrencyCap
            : RecommendConcurrency(m_targetRatePerSec);
        var pending = new List<Task>(concurrency);
        long startTicks = Stopwatch.GetTimestamp();
        long opIndex = 0;
        double tickGap = m_unboundedBurst
            ? 0
            : Stopwatch.Frequency / m_targetRatePerSec;
        double nextOpTicks = startTicks;

        try
        {
            while (!ct.IsCancellationRequested && Stopwatch.GetElapsedTime(startTicks) < m_duration)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (pending[i].IsCompleted)
                    {
                        Task completed = pending[i];
                        pending.RemoveAt(i);
                        await completed.ConfigureAwait(false);
                    }
                }
                if (pending.Count == concurrency)
                {
                    Task completed = await Task.WhenAny(pending).WaitAsync(ct).ConfigureAwait(false);
                    pending.Remove(completed);
                    await completed.ConfigureAwait(false);
                }
                if (!m_unboundedBurst)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now < nextOpTicks)
                    {
                        double waitMs = (nextOpTicks - now) * 1000.0 / Stopwatch.Frequency;
                        int sleepMs = waitMs > 5 ? (int)waitMs - 1 : 0;
                        if (sleepMs > 0)
                        {
                            await Task.Delay(sleepMs, ct).ConfigureAwait(false);
                        }
                        while (Stopwatch.GetTimestamp() < nextOpTicks
                            && !ct.IsCancellationRequested)
                        {
                            Thread.Yield();
                        }
                    }
                    nextOpTicks += tickGap;
                }
                ct.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(startTicks) >= m_duration)
                {
                    break;
                }
                pending.Add(IssueOpAsync(opIndex++, ct));
            }
            loopFinished = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            loopFinished = true;
        }
        catch (Exception ex) when (ex is ServiceResultException or IOException or TimeoutException)
        {
            error = ex.Message;
            loopFinished = true;
        }
        finally
        {
            bool drained = false;
            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
                drained = true;
            }
            finally
            {
                Elapsed = Stopwatch.GetElapsedTime(startTicks);
                if (!loopFinished || !drained)
                {
                    error ??= "Benchmark execution failed; an operation or sample consumer faulted.";
                }
                Completion = error is not null ? BenchmarkCompletion.Failed
                    : ct.IsCancellationRequested ? BenchmarkCompletion.Stopped
                    : BenchmarkCompletion.Completed;
                OnFinished?.Invoke(error);
            }
        }
    }

    private async Task IssueOpAsync(long opIndex, CancellationToken ct)
    {
        long startTicks = Stopwatch.GetTimestamp();
        bool ok = false;
        try
        {
            if (m_target.Mode == BenchmarkMode.Write)
            {
                Variant v = ValueFactory.BuildScalar(
                    m_target.BuiltInType, m_generator, opIndex);
                ArrayOf<WriteValue> wvs =
                [
                    new WriteValue
                    {
                        NodeId = m_target.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(v)
                    }
                ];
                WriteResponse resp = await m_session.WriteAsync(null, wvs, ct).ConfigureAwait(false);
                ok = StatusCode.IsGood(resp.ResponseHeader.ServiceResult) &&
                    resp.Results.Count == 1 && StatusCode.IsGood(resp.Results[0]);
            }
            else
            {
                NodeId objectId = m_target.ObjectId;
                var args = new Variant[m_inputTypes.Count];
                for (int i = 0; i < args.Length; i++)
                {
                    args[i] = ValueFactory.BuildScalar(m_inputTypes[i], m_generator, opIndex + i);
                }
                ArrayOf<CallMethodRequest> calls =
                [
                    new CallMethodRequest
                    {
                        ObjectId = objectId,
                        MethodId = m_target.NodeId,
                        InputArguments = new ArrayOf<Variant>(args)
                    }
                ];
                CallResponse resp = await m_session.CallAsync(null, calls, ct).ConfigureAwait(false);
                ok = StatusCode.IsGood(resp.ResponseHeader.ServiceResult) &&
                    resp.Results.Count == 1 && StatusCode.IsGood(resp.Results[0].StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ok = false;
        }
        catch (Exception ex) when (ex is ServiceResultException or IOException or TimeoutException)
        {
            ok = false;
        }
        finally
        {
            long endTicks = Stopwatch.GetTimestamp();
            double latencyMs = (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
            OnSample?.Invoke(new BenchmarkSample(endTicks, latencyMs, ok));
        }
    }

    public const int MaxConcurrencyCap = 256;

    private readonly ISession m_session;
    private readonly BenchmarkTarget m_target;
    private readonly ValueGenerator m_generator;
    private readonly ArrayOf<BuiltInType> m_inputTypes;
    private readonly double m_targetRatePerSec;
    private readonly bool m_unboundedBurst;
    private readonly TimeSpan m_duration;
    private CancellationTokenSource? m_cts;
    private Task? m_loopTask;
}
