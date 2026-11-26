// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// The result status of each scan parameter read or write.
    /// </summary>
    public sealed record AutoIdScanParameterStatus
    {
        /// <summary>
        /// Gets the <c>Duration</c> read or write status.
        /// </summary>
        public StatusCode Duration { get; init; }

        /// <summary>
        /// Gets the <c>Cycles</c> read or write status.
        /// </summary>
        public StatusCode Cycles { get; init; }

        /// <summary>
        /// Gets the <c>DataAvailable</c> read or write status.
        /// </summary>
        public StatusCode DataAvailable { get; init; }

        /// <summary>
        /// Gets the <c>CodeType</c> read or write status.
        /// </summary>
        public StatusCode CodeType { get; init; }

        /// <summary>
        /// Gets the <c>CodeTypes</c> read or write status.
        /// </summary>
        public StatusCode CodeTypes { get; init; }
    }

    /// <summary>
    /// The typed values and individual service statuses from a scan parameter read.
    /// </summary>
    /// <param name="Parameters">The decoded values; inspect Status before using each field.</param>
    /// <param name="Status">The individual parameter read statuses.</param>
    public sealed record AutoIdScanParameterReadResult(
        AutoIdScanParameters Parameters,
        AutoIdScanParameterStatus Status)
    {
        /// <summary>
        /// Gets the supported code names indexed by the CodeTypes selections.
        /// </summary>
        public ArrayOf<LocalizedText> CodeTypeNames { get; init; }

        /// <summary>
        /// Gets the status of the CodeTypes EnumStrings read.
        /// </summary>
        public StatusCode CodeTypeNamesStatus { get; init; }
    }

    /// <summary>
    /// The active state and individual service status of a reader's <c>ScanActive</c> variable.
    /// </summary>
    /// <param name="IsActive">The decoded active state, or false for an unreadable value.</param>
    /// <param name="Status">The individual variable status.</param>
    public sealed record AutoIdScanActiveReadResult(bool IsActive, StatusCode Status);
}
