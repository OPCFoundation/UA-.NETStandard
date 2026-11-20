// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host-owned ceilings for explicit GDS operations; registration never creates a connection or secret store.
    /// </summary>
    public sealed class GdsMcpOptions
    {
        /// <summary>
        /// Gets or sets the ceiling for one public certificate, CSR or trust-list artifact.
        /// </summary>
        public int MaxArtifactBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the serialized result ceiling, independent of the transfer policy.
        /// </summary>
        public int MaxResponseBytes { get; set; } = 256 * 1024;

        /// <summary>
        /// Gets or sets the complete remote-operation deadline, independent from post-commit secret persistence.
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the local reference lifetime when the remote protocol supplies no expiry.
        /// This is not a claim about remote credential validity.
        /// </summary>
        public TimeSpan ReferenceLifetime { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Gets or sets the lifetime of a locally tracked token request.
        /// </summary>
        public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Gets or sets the maximum pending token requests and push owners retained by this host.
        /// </summary>
        public int MaxTrackedOperations { get; set; } = 128;

        /// <summary>
        /// Validates host ceilings without performing I/O.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxArtifactBytes, 1024);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxArtifactBytes, 64 * 1024 * 1024);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxResponseBytes, 4096);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxResponseBytes, 4 * 1024 * 1024);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxTrackedOperations, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxTrackedOperations, 4096);
            if (OperationTimeout <= TimeSpan.Zero || OperationTimeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentOutOfRangeException(nameof(OperationTimeout));
            }
            if (ReferenceLifetime <= TimeSpan.Zero ||
                ReferenceLifetime > TimeSpan.FromDays(365) ||
                RequestLifetime <= TimeSpan.Zero ||
                RequestLifetime > TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(nameof(ReferenceLifetime),
                    "Reference lifetime must be at most one year and request lifetime at most one hour.");
            }
        }
    }
}
