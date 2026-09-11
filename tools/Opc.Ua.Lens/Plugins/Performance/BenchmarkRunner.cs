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
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Performance;

/// <summary>
/// The kind of synthetic operation issued by <see cref="BenchmarkRunner"/>.
/// </summary>
internal enum BenchmarkMode
{
    /// <summary>Single-value <c>WriteAsync</c> per op (Part 4 §5.10.4).</summary>
    Write,

    /// <summary>Method <c>CallAsync</c> per op (Part 4 §5.11.2).</summary>
    Call
}

/// <summary>
/// Strategy for generating per-op write values or method input arguments.
/// </summary>
internal enum ValueGenerator
{
    /// <summary>Uniform random in the appropriate domain for the data type.</summary>
    Random,

    /// <summary>Monotonically increasing counter, wrapped to fit the data type.</summary>
    Sequential,

    /// <summary>Same literal value reused for every op.</summary>
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
    Guid RunId,
    long CompletedAtTicks,
    double LatencyMs,
    bool Success);

/// <summary>
/// Background runner that pumps synthetic Write or Call ops at a
/// configured target rate against an OPC UA session, cooperatively
/// cancellable. Concurrency is bounded by the set of owned operation tasks
/// so we never queue more than ~256 inflight ops at a time even when
/// the target rate temporarily outruns the channel.  Each op's
/// wall-clock latency is reported via <see cref="OnSample"/>; the host
/// view-model aggregates them into the throughput series + histogram.
/// </summary>
internal sealed class BenchmarkRunner : IAsyncDisposable
{
    /// <summary>Hard cap on max in-flight ops.  See class header.</summary>
    public const int MaxConcurrencyCap = 256;

    private readonly ISession m_session;
    private readonly BenchmarkTarget m_target;
    private readonly ValueGenerator m_generator;
    private readonly double m_targetRatePerSec;
    private readonly bool m_unboundedBurst;
    private readonly TimeSpan m_duration;
    private readonly TimeSpan m_drainTimeout;
    private readonly TimeProvider m_timeProvider;

    private CancellationTokenSource? m_cts;
    private Task? m_loopTask;

    /// <summary>
    /// Fired once per completed op (whether success or failure).
    /// May be raised on a non-UI thread; subscribers must marshal as
    /// needed.
    /// </summary>
    public event Action<BenchmarkSample>? OnSample;

    /// <summary>Fired exactly once when the runner stops (cancelled or completed).</summary>
    public event Action<Guid, string?>? OnFinished;

    /// <summary>
    /// Reports a missed cleanup deadline while the runner still owns pending operations.
    /// </summary>
    public event Action<Guid>? OnDrainTimedOut;

    public BenchmarkRunner(
        ISession session,
        BenchmarkTarget target,
        ValueGenerator generator,
        double targetRatePerSec,
        bool unboundedBurst,
        TimeSpan duration,
        TimeSpan? drainTimeout = null,
        TimeProvider? timeProvider = null)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_target = target ?? throw new ArgumentNullException(nameof(target));
        m_generator = generator;
        m_targetRatePerSec = Math.Max(1.0, targetRatePerSec);
        m_unboundedBurst = unboundedBurst;
        m_duration = duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : duration;
        m_drainTimeout = drainTimeout ?? TimeSpan.FromSeconds(5);
        m_timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Guid RunId { get; } = Guid.NewGuid();

    public bool DrainTimedOut { get; private set; }

    /// <summary>True between Start and the time the loop fully drains after cancellation.</summary>
    public bool IsRunning => m_loopTask is { IsCompleted: false };

    /// <summary>
    /// Computes the recommended max-concurrency cap for a given target
    /// rate.  Capped at <see cref="MaxConcurrencyCap"/> to avoid queue
    /// blow-up when latency spikes.
    /// </summary>
    public static int RecommendConcurrency(double targetRatePerSec)
    {
        // 2x rate / 1000 ≈ enough in-flight ops to keep a 2 ms RTT pipe
        // saturated without piling on more.  Floor at 4 so even low-rate
        // benches can overlap a few requests; cap at MaxConcurrencyCap.
        int suggested = (int)Math.Ceiling(targetRatePerSec * 2.0 / 1000.0);
        if (suggested < 4)
        {
            suggested = 4;
        }

        if (suggested > MaxConcurrencyCap)
        {
            suggested = MaxConcurrencyCap;
        }

        return suggested;
    }

    /// <summary>
    /// Starts this single-use run on a background task. Subsequent calls are ignored.
    /// </summary>
    public void Start()
    {
        if (m_loopTask is not null)
        {
            return;
        }

        m_cts = new CancellationTokenSource();
        CancellationToken ct = m_cts.Token;
        m_loopTask = Task.Run(() => RunAsync(ct), CancellationToken.None);
    }

    /// <summary>Cancels the run and awaits the loop to drain.</summary>
    public async Task StopAsync()
    {
        Exception? cancellationFailure = null;
        try
        {
            m_cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
        catch (Exception failure)
        {
            cancellationFailure = failure;
        }
        try
        {
            if (m_loopTask is { } loop)
            {
                await loop.ConfigureAwait(false);
            }
        }
        finally
        {
            m_cts?.Dispose();
            m_cts = null;
        }
        if (cancellationFailure is not null)
        {
            throw new AggregateException("Cancellation failed after the benchmark operations were joined.",
                cancellationFailure);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? error = null;
        int concurrency = m_unboundedBurst
            ? MaxConcurrencyCap
            : RecommendConcurrency(m_targetRatePerSec);
        var operations = new List<Task>(concurrency);
        using var duration = new CancellationTokenSource(m_duration, m_timeProvider);
        using var scheduling = CancellationTokenSource.CreateLinkedTokenSource(ct, duration.Token);
        CancellationToken scheduleToken = scheduling.Token;

        long startTicks = Stopwatch.GetTimestamp();
        long endTicks = startTicks + (long)(m_duration.TotalSeconds * Stopwatch.Frequency);
        long opIndex = 0;

        // Unbounded burst issues another operation whenever one owned slot is free.
        double tickGap = m_unboundedBurst
            ? 0
            : Stopwatch.Frequency / m_targetRatePerSec;
        long nextOpTicks = startTicks;

        try
        {
            while (!scheduleToken.IsCancellationRequested
                && Stopwatch.GetTimestamp() < endTicks)
            {
                if (!m_unboundedBurst)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now < nextOpTicks)
                    {
                        // Sleep in small chunks so we remain cancellable.
                        double waitMs = (nextOpTicks - now) * 1000.0 / Stopwatch.Frequency;
                        int sleepMs = waitMs > 5 ? (int)waitMs - 1 : 0;
                        if (sleepMs > 0)
                        {
                            try
                            {
                                await Task.Delay(sleepMs, scheduleToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                        // Final spin / yield for the last few hundred µs.
                        while (Stopwatch.GetTimestamp() < nextOpTicks
                            && !scheduleToken.IsCancellationRequested)
                        {
                            Thread.Yield();
                        }
                    }
                    nextOpTicks += (long)tickGap;
                }

                await JoinCompletedAsync(operations).ConfigureAwait(false);
                if (operations.Count == concurrency)
                {
                    await Task.WhenAny(operations).WaitAsync(scheduleToken).ConfigureAwait(false);
                    await JoinCompletedAsync(operations).ConfigureAwait(false);
                }
                scheduleToken.ThrowIfCancellationRequested();
                operations.Add(IssueOpAsync(opIndex++, ct));
            }
        }
        catch (OperationCanceledException)
        {
            // cancellation is normal
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            Task drain = Task.WhenAll(operations);
            try
            {
                await drain.WaitAsync(m_drainTimeout, m_timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                DrainTimedOut = true;
                error = "Operation cleanup timed out; all issued operations were joined before completion.";
                try
                {
                    m_cts?.Cancel();
                }
                catch (Exception failure)
                {
                    error += $" Cancellation also failed: {failure.Message}";
                }
                try
                {
                    OnDrainTimedOut?.Invoke(RunId);
                }
                catch (Exception)
                {
                    // Consumer failures must not abandon the owned operations.
                }
                finally
                {
                    // The deadline changes the outcome, not ownership. A caller cannot
                    // start a successor or dispose resources until these operations finish.
                    await drain.ConfigureAwait(false);
                }
            }
            try
            {
                OnFinished?.Invoke(RunId, error);
            }
            catch (Exception)
            {
                // Completion observers do not own the runner's resource cleanup.
            }
        }
    }

    private static async Task JoinCompletedAsync(List<Task> operations)
    {
        for (int index = operations.Count - 1; index >= 0; index--)
        {
            Task operation = operations[index];
            if (operation.IsCompleted)
            {
                await operation.ConfigureAwait(false);
                operations.RemoveAt(index);
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
                ok = resp.Results.Count > 0 && StatusCode.IsGood(resp.Results[0]);
            }
            else
            {
                NodeId objectId = m_target.ObjectId;
                Argument[] sig = m_target.InputArguments ?? Array.Empty<Argument>();
                var args = new Variant[sig.Length];
                for (int i = 0; i < sig.Length; i++)
                {
                    BuiltInType bi = ValueFactory.BuiltInForArgument(sig[i]);
                    args[i] = ValueFactory.BuildScalar(bi, m_generator, opIndex + i);
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
                ok = resp.Results.Count > 0 && StatusCode.IsGood(resp.Results[0].StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            ok = false;
        }
        catch (Exception)
        {
            ok = false;
        }
        finally
        {
            long endTicks = Stopwatch.GetTimestamp();
            double latencyMs = (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
            try
            {
                OnSample?.Invoke(new BenchmarkSample(RunId, endTicks, latencyMs, ok));
            }
            catch
            {
                // swallow consumer errors so they don't bubble into the runner loop
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
