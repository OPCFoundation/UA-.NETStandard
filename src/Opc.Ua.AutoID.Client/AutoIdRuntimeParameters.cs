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
