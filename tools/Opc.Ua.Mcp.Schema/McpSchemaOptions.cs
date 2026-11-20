/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
