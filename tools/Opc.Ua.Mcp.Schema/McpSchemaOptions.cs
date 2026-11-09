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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host limits for remote type discovery and schema generation, independent of caller page sizes.
    /// </summary>
    public sealed class McpSchemaOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of type definitions in one generation closure.
        /// </summary>
        public int MaxTypes { get; set; } = 512;

        /// <summary>
        /// Gets or sets the maximum field count of one structure or enumeration.
        /// </summary>
        public int MaxFieldsPerType { get; set; } = 1024;

        /// <summary>
        /// Gets or sets the maximum referenced-type dependency depth.
        /// </summary>
        public int MaxDepth { get; set; } = 32;

        /// <summary>
        /// Gets or sets the maximum serialized schema size, also bounded by the host transfer ceiling.
        /// </summary>
        public int MaxSchemaBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the complete operation timeout.
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Validates each explicit resource ceiling.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxTypes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxTypes, 512);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxFieldsPerType, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxFieldsPerType, 1024);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxDepth, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxDepth, 32);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxSchemaBytes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxSchemaBytes, 16 * 1024 * 1024);
            if (OperationTimeout <= TimeSpan.Zero || OperationTimeout > TimeSpan.FromSeconds(30))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(OperationTimeout), "The schema deadline must be positive and at most thirty seconds.");
            }
        }

        /// <summary>
        /// Captures immutable-for-the-operation host limits.
        /// </summary>
        internal McpSchemaOptions Snapshot()
        {
            Validate();
            return new McpSchemaOptions
            {
                MaxTypes = MaxTypes,
                MaxFieldsPerType = MaxFieldsPerType,
                MaxDepth = MaxDepth,
                MaxSchemaBytes = MaxSchemaBytes,
                OperationTimeout = OperationTimeout
            };
        }
    }
}
