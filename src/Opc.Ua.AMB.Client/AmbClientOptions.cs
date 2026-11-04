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

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// Opts an AMB client into bounded traversal and strict response validation.
    /// </summary>
    /// <remarks>
    /// Discovery, address-space reads, AssetId/link writes and documentation
    /// methods reject non-Good results, malformed responses and incorrectly
    /// typed values rather than returning absent values. Only a path's BadNoMatch
    /// denotes an absent optional path. Value reads request both timestamps.
    /// Limits apply before accumulating references, queues or result records;
    /// decoded value sizes remain governed by the session's transport quotas.
    /// Sessions are borrowed, never closed or disposed by the client. Event
    /// observation and acknowledgement retain their existing contracts.
    /// </remarks>
    public sealed record AmbClientOptions
    {
        /// <summary>
        /// Gets the maximum total references consumed by a traversal, including
        /// duplicates and references to remote nodes. Also bounds FindAlias
        /// aliases plus their targets before copying them into client records.
        /// The individual sections of an asset snapshot have separate budgets.
        /// </summary>
        public int MaximumBrowseReferences { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum pages per browsed node, including empty pages.
        /// Exhausting either limit fails with BadEncodingLimitsExceeded, never
        /// a silently truncated result. Hierarchies also respect AmbClient.MaxDepth.
        /// </summary>
        public int MaximumBrowsePages { get; init; } = 128;

        /// <summary>
        /// Validates the immutable limits before using them.
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
        }
    }
}
