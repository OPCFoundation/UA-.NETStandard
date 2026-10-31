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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// Immutable limits and defaults of a <see cref="LadsClient"/>.
    /// </summary>
    /// <remarks>
    /// Every browse is bounded: a traversal that exceeds
    /// <see cref="MaximumBrowseReferences"/> or <see cref="MaximumBrowsePages"/>
    /// fails with <c>BadEncodingLimitsExceeded</c> instead of returning a silently
    /// truncated list.
    /// </remarks>
    public sealed record LadsClientOptions
    {
        /// <summary>
        /// Gets the default options.
        /// </summary>
        public static LadsClientOptions Default { get; } = new();

        /// <summary>
        /// Gets the maximum references consumed by the browse of one node.
        /// </summary>
        public int MaximumBrowseReferences { get; init; } = 4096;

        /// <summary>
        /// Gets the maximum browse pages consumed by the browse of one node.
        /// </summary>
        public int MaximumBrowsePages { get; init; } = 128;

        /// <summary>
        /// Gets the maximum nesting depth of function sets that the function
        /// discovery follows.
        /// </summary>
        public int MaximumFunctionDepth { get; init; } = 8;

        /// <summary>
        /// Gets the maximum size in bytes of a result file that is downloaded.
        /// </summary>
        public long MaximumFileSize { get; init; } = 64L * 1024 * 1024;

        /// <summary>
        /// Gets the chunk size in bytes used to read result files, from 1 to 1 MiB.
        /// </summary>
        public int FileChunkSize { get; init; } = 64 * 1024;

        /// <summary>
        /// Gets the sampling interval requested for observed values.
        /// </summary>
        public TimeSpan SamplingInterval { get; init; } = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Gets the server queue size requested for observed values.
        /// </summary>
        public uint QueueSize { get; init; } = 10;

        /// <summary>
        /// Validates the options.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A limit is invalid.</exception>
        public void Validate()
        {
            if (MaximumBrowseReferences <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowseReferences));
            }
            if (MaximumBrowsePages <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowsePages));
            }
            if (MaximumFunctionDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumFunctionDepth));
            }
            if (MaximumFileSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumFileSize));
            }
            if (FileChunkSize is <= 0 or > 1_048_576)
            {
                throw new ArgumentOutOfRangeException(nameof(FileChunkSize));
            }
            if (SamplingInterval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SamplingInterval));
            }
            if (QueueSize == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(QueueSize));
            }
        }
    }
}
