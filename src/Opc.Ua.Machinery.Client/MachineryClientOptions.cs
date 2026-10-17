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

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// Opts a Machinery client into bounded discovery and strict, evidenced reads.
    /// </summary>
    /// <remarks>
    /// Only an optional path's BadNoMatch denotes absence. Non-Good service,
    /// operation and value statuses, malformed responses and incompatible types
    /// fail explicitly. Read limits apply to decoded values, not transport
    /// decoding; configure session quotas as well. Generated proxies, event
    /// observation, job commands and result transfers retain their own contracts.
    /// </remarks>
    public sealed record MachineryClientOptions
    {
        /// <summary>
        /// Gets the maximum raw references per discovery traversal, including
        /// duplicates. Process-value discovery shares this budget across its
        /// machine and Monitoring roots.
        /// </summary>
        public int MaximumBrowseReferences { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum Browse/BrowseNext pages per traversal, including
        /// empty pages. Limits fail rather than silently truncating discovery.
        /// </summary>
        public int MaximumBrowsePages { get; init; } = 128;

        /// <summary>
        /// Gets the maximum decoded items per array and aggregate structured
        /// snapshot. This does not replace session message and decoding quotas.
        /// </summary>
        public int MaximumReadArrayItems { get; init; } = 1024;

        /// <summary>
        /// Copies and validates limits before a client or factory uses them.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal MachineryClientOptions CopyAndValidate()
        {
            if (MaximumBrowseReferences <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowseReferences));
            }
            if (MaximumBrowsePages <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowsePages));
            }
            if (MaximumReadArrayItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumReadArrayItems));
            }
            return this with { };
        }
    }
}
