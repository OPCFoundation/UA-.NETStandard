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

using System.Collections.Generic;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Validated, normalised request envelope passed by the framework
    /// to <see cref="IHistorianDataProvider.ReadRawAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework validates and parses the OPC UA <c>ReadRawModifiedDetails</c>
    /// before invoking the provider:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <see cref="StartTime"/>/<see cref="EndTime"/> are populated with the
    /// effective range; one-sided requests get either
    /// <see cref="DateTimeUtc.MinValue"/> or <see cref="DateTimeUtc.MaxValue"/>
    /// sentinels.
    /// </item>
    /// <item>
    /// <see cref="IsForward"/> reflects the iteration direction (Part 11
    /// §5.2.4: forward when <c>StartTime &lt; EndTime</c>).
    /// </item>
    /// <item>
    /// <see cref="ReturnBounds"/> requests one bound on each side of the
    /// time window when set.
    /// </item>
    /// </list>
    /// </remarks>
    public sealed record HistorianRawReadRequest
    {
        /// <summary>The historizing variable.</summary>
        public required NodeId NodeId { get; init; }

        /// <summary>Effective start time (the earlier of start/end).</summary>
        public required DateTimeUtc StartTime { get; init; }

        /// <summary>Effective end time (the later of start/end).</summary>
        public required DateTimeUtc EndTime { get; init; }

        /// <summary>
        /// Maximum number of values to return for this node. Zero = unbounded
        /// (return up to the time window's worth).
        /// </summary>
        public uint MaxValues { get; init; }

        /// <summary>True for forward-in-time reads, false for reverse.</summary>
        public bool IsForward { get; init; }

        /// <summary>Return bounding values just outside the requested window.</summary>
        public bool ReturnBounds { get; init; }
    }

    /// <summary>
    /// Validated request envelope passed to
    /// <see cref="IHistorianModifiedProvider.ReadModifiedAsync"/>.
    /// </summary>
    public sealed record HistorianModifiedReadRequest
    {
        /// <summary>The historizing variable.</summary>
        public required NodeId NodeId { get; init; }

        /// <summary>Effective start time.</summary>
        public required DateTimeUtc StartTime { get; init; }

        /// <summary>Effective end time.</summary>
        public required DateTimeUtc EndTime { get; init; }

        /// <summary>Maximum number of values to return. Zero = unbounded.</summary>
        public uint MaxValues { get; init; }

        /// <summary>True for forward-in-time reads, false for reverse.</summary>
        public bool IsForward { get; init; }
    }

    /// <summary>
    /// Validated request envelope passed to
    /// <see cref="IHistorianAtTimeProvider.ReadAtTimeAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Providers that implement this interface return one value per
    /// requested timestamp. When the underlying archive does not have an
    /// exact match the provider may either interpolate (per the
    /// historization configuration) or return a bounded raw value. If no
    /// provider override is registered the framework falls back to a
    /// streaming interpolation pipeline over the raw read API.
    /// </para>
    /// </remarks>
    public sealed record HistorianAtTimeReadRequest
    {
        /// <summary>The historizing variable.</summary>
        public required NodeId NodeId { get; init; }

        /// <summary>The requested timestamps, in the order the client supplied them.</summary>
        public required IReadOnlyList<DateTimeUtc> RequestedTimes { get; init; }

        /// <summary>
        /// When true, returns the closest bound rather than interpolating
        /// (Part 11 §5.2.6).
        /// </summary>
        public bool UseSimpleBounds { get; init; }
    }

    /// <summary>
    /// Validated request envelope passed to
    /// <see cref="IHistorianProcessedProvider.ReadProcessedAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the provider does not implement
    /// <see cref="IHistorianProcessedProvider"/> the framework computes
    /// aggregates via the central <see cref="AggregateManager"/>, streaming
    /// raw values through an <see cref="IAggregateCalculator"/>.
    /// </para>
    /// </remarks>
    public sealed record HistorianProcessedReadRequest
    {
        /// <summary>The historizing variable.</summary>
        public required NodeId NodeId { get; init; }

        /// <summary>The aggregate function identifier.</summary>
        public required NodeId AggregateId { get; init; }

        /// <summary>Start of the aggregation window.</summary>
        public required DateTimeUtc StartTime { get; init; }

        /// <summary>End of the aggregation window.</summary>
        public required DateTimeUtc EndTime { get; init; }

        /// <summary>Interval length, in milliseconds, between aggregate outputs.</summary>
        public required double ProcessingInterval { get; init; }

        /// <summary>Aggregate configuration overrides (Part 11 §5.2.6.4).</summary>
        public required AggregateConfiguration Configuration { get; init; }
    }

    /// <summary>
    /// Validated request envelope passed to
    /// <see cref="IHistorianAnnotationProvider.ReadAnnotationsAsync"/>.
    /// </summary>
    public sealed record HistorianAnnotationReadRequest
    {
        /// <summary>The historizing variable (NOT the Annotations property).</summary>
        public required NodeId NodeId { get; init; }

        /// <summary>Effective start time.</summary>
        public required DateTimeUtc StartTime { get; init; }

        /// <summary>Effective end time.</summary>
        public required DateTimeUtc EndTime { get; init; }

        /// <summary>Maximum number of annotations to return. Zero = unbounded.</summary>
        public uint MaxValues { get; init; }

        /// <summary>True for forward-in-time reads, false for reverse.</summary>
        public bool IsForward { get; init; }
    }
}
