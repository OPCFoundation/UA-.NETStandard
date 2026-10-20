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

namespace Opc.Ua.CranesHoists.Client
{
    /// <summary>
    /// Bounds discovery and topology reads without silently truncating a result.
    /// </summary>
    public sealed record CranesHoistsClientOptions
    {
        /// <summary>
        /// Gets the maximum unique objects visited in one inventory.
        /// </summary>
        public int MaximumNodes { get; init; } = 256;

        /// <summary>
        /// Gets the requested maximum references per browse response.
        /// </summary>
        public uint BrowsePageSize { get; init; } = 32;

        /// <summary>
        /// Gets the maximum pages allowed for one browse.
        /// </summary>
        public int MaximumBrowsePages { get; init; } = 64;

        /// <summary>
        /// Validates bounds before making a service call.
        /// </summary>
        public void Validate()
        {
            if (MaximumNodes is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumNodes));
            }
            if (BrowsePageSize is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(BrowsePageSize));
            }
            if (MaximumBrowsePages is < 1 or > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowsePages));
            }
        }
    }
}
