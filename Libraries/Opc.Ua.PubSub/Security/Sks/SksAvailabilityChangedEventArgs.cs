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

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Event payload raised by an <see cref="ISecurityKeyService"/>
    /// when its connectivity to the underlying SKS endpoint changes.
    /// Subscribers use it to drive WriterGroup / ReaderGroup state
    /// (PreOperational while the SKS is unreachable).
    /// </summary>
    /// <remarks>
    /// Implements the operational notification described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>: when the SKS is
    /// unavailable, components must enter <c>PreOperational</c>
    /// rather than publish unsecured messages.
    /// </remarks>
    public sealed class SksAvailabilityChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="SksAvailabilityChangedEventArgs"/>.
        /// </summary>
        /// <param name="isAvailable">
        /// <see langword="true"/> when the SKS is reachable and
        /// returning Good results.
        /// </param>
        /// <param name="status">
        /// StatusCode describing the most recent transition.
        /// </param>
        /// <param name="reason">
        /// Optional human-readable reason. Sensitive values must
        /// never be passed here.
        /// </param>
        public SksAvailabilityChangedEventArgs(
            bool isAvailable,
            StatusCode status,
            string? reason)
        {
            IsAvailable = isAvailable;
            Status = status;
            Reason = reason;
        }

        /// <summary>
        /// <see langword="true"/> when the SKS is reachable and
        /// returning <c>Good</c> results.
        /// </summary>
        public bool IsAvailable { get; }

        /// <summary>
        /// StatusCode describing the most recent transition.
        /// </summary>
        public StatusCode Status { get; }

        /// <summary>
        /// Optional human-readable reason for this transition.
        /// </summary>
        public string? Reason { get; }
    }
}
