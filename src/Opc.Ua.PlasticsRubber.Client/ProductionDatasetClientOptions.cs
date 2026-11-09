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

namespace Opc.Ua.PlasticsRubber.Client
{
    /// <summary>
    /// Bounds high-level dataset operations without assuming that a remote peer enforces the same limits.
    /// </summary>
    public sealed record ProductionDatasetClientOptions
    {
        /// <summary>
        /// Gets the maximum bytes copied by one upload or download.
        /// </summary>
        public int MaximumFileBytes { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum datasets accepted in one list operation.
        /// </summary>
        public int MaximumDatasetCount { get; init; } = 1024;

        /// <summary>
        /// Gets the bounded stream buffer and FileType chunk size.
        /// </summary>
        public int ChunkSize { get; init; } = 64 * 1024;

        /// <summary>
        /// Gets the deadline for an entire operation, including asynchronous server commit completion.
        /// </summary>
        public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Validates immutable settings at client construction.
        /// </summary>
        internal void Validate()
        {
            if (MaximumFileBytes <= 0 || MaximumDatasetCount <= 0 || ChunkSize <= 0 ||
                ChunkSize > MaximumFileBytes || OperationTimeout < TimeSpan.FromMilliseconds(1) ||
                OperationTimeout > TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(ProductionDatasetClientOptions));
            }
        }
    }
}
