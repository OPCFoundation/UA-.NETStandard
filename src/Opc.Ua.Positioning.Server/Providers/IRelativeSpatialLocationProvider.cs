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
