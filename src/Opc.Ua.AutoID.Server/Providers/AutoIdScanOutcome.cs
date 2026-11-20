// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// The outcome of a synchronous scan: the results and the operation status.
    /// </summary>
    public readonly struct AutoIdScanOutcome : IEquatable<AutoIdScanOutcome>
    {
        /// <summary>
        /// Creates an outcome.
        /// </summary>
        /// <param name="results">
        /// The scan results. They must be the result type of the reader's kind
        /// (<see cref="RfidScanResult"/> for an RFID reader, and so on) or a subtype of it.
        /// </param>
        /// <param name="status">The operation status; <c>SUCCESS_0</c> when the scan completed.</param>
        public AutoIdScanOutcome(ArrayOf<ScanResult> results, AutoIdOperationStatusEnumeration status)
        {
            Results = results;
            Status = status;
        }

        /// <summary>
        /// Gets the scan results. May be empty.
        /// </summary>
        public ArrayOf<ScanResult> Results { get; }

        /// <summary>
        /// Gets the operation status.
        /// </summary>
        public AutoIdOperationStatusEnumeration Status { get; }

        /// <inheritdoc/>
        public bool Equals(AutoIdScanOutcome other)
        {
            return Status == other.Status && Results.Equals(other.Results);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AutoIdScanOutcome other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Results, Status);
        }

        /// <summary>Compares two outcomes.</summary>
        public static bool operator ==(AutoIdScanOutcome left, AutoIdScanOutcome right)
        {
            return left.Equals(right);
        }

        /// <summary>Compares two outcomes.</summary>
        public static bool operator !=(AutoIdScanOutcome left, AutoIdScanOutcome right)
        {
            return !left.Equals(right);
        }
    }
}
