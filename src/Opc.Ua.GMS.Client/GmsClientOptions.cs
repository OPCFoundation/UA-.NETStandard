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

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// Bounds native client workflows; exceeding a bound rejects the read rather than silently truncating it.
    /// </summary>
    public sealed record GmsClientOptions
    {
        /// <summary>
        /// Gets the aggregate native element bound, from one through 4096.
        /// </summary>
        public int MaximumElements { get; init; } = 128;

        /// <summary>
        /// Gets the aggregate examined browse-reference bound, from one through 100000.
        /// </summary>
        public int MaximumReferences { get; init; } = 16384;

        /// <summary>
        /// Gets the raw sample payload bound, from one through one mebibyte.
        /// </summary>
        public int MaximumPayloadBytes { get; init; } = 1024 * 1024;

        /// <summary>
        /// Rejects invalid limits before starting any service operation.
        /// </summary>
        public void Validate()
        {
            const string message = "GMS reads require finite positive element, reference and payload limits.";
            if (MaximumElements is < 1 or > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumElements), message);
            }
            if (MaximumReferences is < 1 or > 100000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumReferences), message);
            }
            if (MaximumPayloadBytes is < 1 or > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumPayloadBytes), message);
            }
        }
    }
}
