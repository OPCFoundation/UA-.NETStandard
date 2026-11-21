// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Positioning.Server
{
    /// <summary>
    /// Supplies relative spatial frames for one or more stable source identifiers.
    /// </summary>
    public interface IRelativeSpatialLocationProvider
    {
        /// <summary>
        /// Reads the current frame for <paramref name="sourceId"/>.
        /// </summary>
        ValueTask<RelativeSpatialLocationSample> ReadAsync(
            string sourceId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Streams subsequent frames for <paramref name="sourceId"/>.
        /// </summary>
        IAsyncEnumerable<RelativeSpatialLocationSample> WatchAsync(
            string sourceId,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// A provider sample containing an RSL frame and OPC UA quality metadata.
    /// </summary>
    public sealed class RelativeSpatialLocationSample
    {
        /// <summary>
        /// Creates a relative spatial location sample.
        /// </summary>
        public RelativeSpatialLocationSample(
            string sourceId,
            ThreeDFrame frame,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new System.ArgumentException(
                    "A stable source identifier is required.",
                    nameof(sourceId));
            }

            SourceId = sourceId;
            Frame = frame ?? throw new System.ArgumentNullException(nameof(frame));
            StatusCode = statusCode;
            SourceTimestamp = sourceTimestamp;
        }

        /// <summary>
        /// Stable provider-local source identifier.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        /// Relative frame value.
        /// </summary>
        public ThreeDFrame Frame { get; }

        /// <summary>
        /// OPC UA status for the sample.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Source timestamp for the sample.
        /// </summary>
        public DateTimeUtc SourceTimestamp { get; }
    }
}
