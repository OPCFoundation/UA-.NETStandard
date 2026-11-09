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

namespace Opc.Ua.Positioning.Client
{
    /// <summary>
    /// Opts the high-level Positioning clients into bounded discovery and strict reads.
    /// </summary>
    /// <remarks>
    /// Service responses and values must be Good. Required paths must resolve to
    /// exactly one complete local target in an existing namespace. Limits fail
    /// with BadEncodingLimitsExceeded instead of returning truncated results.
    /// Limits apply before accumulating browse references or decoding structure
    /// arrays; incoming message sizes remain governed by the session transport
    /// quotas. Sessions and streaming subscriptions are borrowed, never disposed.
    /// Generated proxies returned by Open methods retain their own read contracts.
    /// </remarks>
    public sealed record PositioningClientReadOptions
    {
        /// <summary>
        /// Gets the maximum raw references per discovery enumeration, including
        /// duplicates and nonmatching references, before copying a browse page.
        /// Each enumeration currently browses one parent.
        /// </summary>
        public int MaximumBrowseReferences { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum pages per browse, including the first and empty pages.
        /// </summary>
        public int MaximumBrowsePages { get; init; } = 128;

        /// <summary>
        /// Gets the maximum frames read while resolving an RSL Base chain.
        /// The next frame is not resolved or read after this limit is reached.
        /// </summary>
        public int MaximumFrameChainLength { get; init; } = 128;

        /// <summary>
        /// Gets the maximum elements in a structure array, including a Zone's
        /// GroundControlPoints, before allocating the typed array or fitting it.
        /// This does not limit the fixed-width scalar frame and Base read request.
        /// </summary>
        public int MaximumReadArrayItems { get; init; } = 1024;

        /// <summary>
        /// Validates all immutable limits before the client performs any operation.
        /// </summary>
        internal void Validate()
        {
            if (MaximumBrowseReferences <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowseReferences));
            }
            if (MaximumBrowsePages <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowsePages));
            }
            if (MaximumFrameChainLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumFrameChainLength));
            }
            if (MaximumReadArrayItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumReadArrayItems));
            }
        }
    }
}
