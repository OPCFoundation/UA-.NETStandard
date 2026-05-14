/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
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
    NodeId? ObjectId,
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
/// configured target rate against an OPC UA session, cooperatively
/// cancellable.  Concurrency is bounded by a <see cref="SemaphoreSlim"/>
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

    private CancellationTokenSource? m_cts;
    private Task? m_loopTask;

    /// <summary>
    /// Fired once per completed op (whether success or failure).
    /// May be raised on a non-UI thread; subscribers must marshal as
    /// needed.
    /// </summary>
    public event Action<BenchmarkSample>? OnSample;

    /// <summary>Fired exactly once when the runner stops (cancelled or completed).</summary>
    public event Action<string?>? OnFinished;

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
        m_generator = generator;
        m_targetRatePerSec = Math.Max(1.0, targetRatePerSec);
        m_unboundedBurst = unboundedBurst;
        m_duration = duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : duration;
    }

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
    /// Starts the run on a background task.  Idempotent — subsequent
    /// calls are ignored while a run is already in flight.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        m_cts = new CancellationTokenSource();
        CancellationToken ct = m_cts.Token;
        m_loopTask = Task.Run(() => RunAsync(ct), ct);
    }

    /// <summary>Cancels the run and awaits the loop to drain.</summary>
    public async Task StopAsync()
    {
        try
        {
            m_cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
        Task? loop = m_loopTask;
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
        m_cts?.Dispose();
        m_cts = null;
        m_loopTask = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? error = null;
        int concurrency = m_unboundedBurst
            ? MaxConcurrencyCap
            : RecommendConcurrency(m_targetRatePerSec);
        using var inflight = new SemaphoreSlim(concurrency, concurrency);

        long startTicks = Stopwatch.GetTimestamp();
        long endTicks = startTicks + (long)(m_duration.TotalSeconds * Stopwatch.Frequency);
        long opIndex = 0;

        // Inter-op delay in 100 ns ticks; unbounded burst sends as fast as
        // the inflight semaphore allows.
        double tickGap = m_unboundedBurst
            ? 0
            : Stopwatch.Frequency / m_targetRatePerSec;
        long nextOpTicks = startTicks;

        try
        {
            while (!ct.IsCancellationRequested
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
                                await Task.Delay(sleepMs, ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                        // Final spin / yield for the last few hundred µs.
                        while (Stopwatch.GetTimestamp() < nextOpTicks
                            && !ct.IsCancellationRequested)
                        {
                            Thread.Yield();
                        }
                    }
                    nextOpTicks += (long)tickGap;
                }

                await inflight.WaitAsync(ct).ConfigureAwait(false);
                long thisIndex = opIndex++;
                // CA2025: IssueOpAsync uses `inflight` (a `using var` in this
                // method).  The drain loop below waits up to 5 s for all
                // permits to return (i.e. all in-flight ops to release the
                // semaphore) before `inflight` falls out of scope and gets
                // disposed.  The pattern is safe.
#pragma warning disable CA2025
                _ = IssueOpAsync(thisIndex, inflight, ct);
#pragma warning restore CA2025
            }

            // Drain in-flight ops on graceful stop — but don't wait
            // forever if the channel is wedged.
            int drainTimeoutMs = 5_000;
            long drainEnd = Environment.TickCount64 + drainTimeoutMs;
            while (inflight.CurrentCount < concurrency
                && Environment.TickCount64 < drainEnd)
            {
                await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
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
            OnFinished?.Invoke(error);
        }
    }

    private async Task IssueOpAsync(long opIndex, SemaphoreSlim inflight, CancellationToken ct)
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
                NodeId objectId = m_target.ObjectId ?? NodeId.Null;
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
                OnSample?.Invoke(new BenchmarkSample(endTicks, latencyMs, ok));
            }
            catch
            {
                // swallow consumer errors so they don't bubble into the runner loop
            }
            try
            {
                inflight.Release();
            }
            catch (ObjectDisposedException)
            {
                // shutdown race — semaphore got disposed first
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
