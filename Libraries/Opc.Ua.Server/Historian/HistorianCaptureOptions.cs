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
        DropNewest = 1,

        /// <summary>
        /// Block the value-setting thread until queue space frees up.
        /// Guarantees no sample loss at the cost of back-pressuring the
        /// producer.
        /// </summary>
        Wait = 2,
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

        /// <summary>
        /// Number of concurrent consumer tasks that drain the channel and
        /// flush batches into the provider. A value of 1 (default) gives
        /// ordered, single-threaded flushing. Higher values improve
        /// throughput when the provider's insert calls have significant
        /// latency (e.g. database writes) at the cost of relaxed
        /// inter-batch ordering. Must be at least 1.
        /// </summary>
        public int ConsumerCount { get; init; } = 1;
    }
}
