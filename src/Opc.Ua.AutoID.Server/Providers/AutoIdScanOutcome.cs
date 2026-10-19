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
