// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Back-pressure policy for the auto-capture queue.
    /// </summary>
    public enum CaptureFullMode
    {
        /// <summary>
        /// Drop the oldest pending sample to make room for the new one
        /// (default). Keeps the queue length bounded and prefers freshness
        /// over completeness; appropriate for high-frequency telemetry
        /// where a brief overload should not block the value-setting
        /// thread.
        /// </summary>
        DropOldest = 0,

        /// <summary>
        /// Drop the new sample. Preserves the order of the existing
        /// queued samples; appropriate when older samples are more
        /// important than the latest reading (rare).
        /// </summary>
        DropNewest = 1
    }

    /// <summary>
    /// Knobs for the per-provider auto-capture pipeline. Defaults are
    /// tuned for typical industrial telemetry (a few hundred Hz across
    /// thousands of variables).
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>HistorianCaptureSink</c> is constructed per
    /// <c>HistorianBuilder</c> (i.e. per server-side historian binding);
    /// the same options apply to every variable opted in via
    /// <c>Historize(autoCapture: true)</c> through that builder.
    /// </para>
    /// </remarks>
    public sealed record HistorianCaptureOptions
    {
        /// <summary>
        /// Maximum number of samples buffered in the capture queue before
        /// <see cref="FullMode"/> kicks in. Default 4096.
        /// </summary>
        public int MaxQueuedSamples { get; init; } = 4096;

        /// <summary>
        /// Target sample count per flush. The consumer attempts to drain
        /// up to this many items per flush; smaller batches reduce
        /// latency, larger ones amortise per-call provider overhead.
        /// Default 64.
        /// </summary>
        public int BatchTarget { get; init; } = 64;

        /// <summary>
        /// Maximum time the consumer waits for additional samples before
        /// flushing a partial batch. Lower values reduce capture
        /// latency; higher values pack more samples per flush. Default
        /// 25 ms.
        /// </summary>
        public TimeSpan BatchWindow { get; init; } = TimeSpan.FromMilliseconds(25);

        /// <summary>
        /// Behavior when the queue is full. Default
        /// <see cref="CaptureFullMode.DropOldest"/>.
        /// </summary>
        public CaptureFullMode FullMode { get; init; } = CaptureFullMode.DropOldest;
    }
}
