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

namespace Opc.Ua.PubSub.Diagnostics
{
    /// <summary>
    /// Per-component diagnostics surface. One instance is owned by each
    /// PubSub component state machine (Application, Connection, Group,
    /// Writer, Reader) so counters and recent errors can be reported
    /// independently and aggregated by the address-space
    /// <c>PubSubDiagnosticsType</c> nodes in the server-side library.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11">
    /// Part 14 §9.1.11 PubSubDiagnosticsType</see>. Implementations must
    /// be thread-safe — every PubSub hot-path may call
    /// <see cref="Increment"/> without coordination. <see cref="Read"/>
    /// returns a consistent snapshot but may be slightly stale relative
    /// to in-flight increments.
    /// </remarks>
    public interface IPubSubDiagnostics
    {
        /// <summary>
        /// Configured verbosity tier of this diagnostics instance. The
        /// level is fixed at construction; callers should branch on it
        /// before constructing expensive error messages.
        /// </summary>
        PubSubDiagnosticsLevel Level { get; }

        /// <summary>
        /// Adds <paramref name="delta"/> to the counter identified by
        /// <paramref name="kind"/>. Negative deltas are not allowed; the
        /// counter is monotonically non-decreasing.
        /// </summary>
        /// <param name="kind">Counter identity.</param>
        /// <param name="delta">
        /// Non-negative increment; defaults to 1 to match the typical
        /// per-event call site.
        /// </param>
        void Increment(PubSubDiagnosticsCounterKind kind, long delta = 1);

        /// <summary>
        /// Reads the current value of the counter identified by
        /// <paramref name="kind"/>. Reads do not block writers.
        /// </summary>
        /// <param name="kind">Counter identity.</param>
        /// <returns>The current accumulated count.</returns>
        long Read(PubSubDiagnosticsCounterKind kind);

        /// <summary>
        /// Records the most recent error encountered by the owning
        /// component. The call is honoured only at
        /// <see cref="PubSubDiagnosticsLevel.Medium"/> or higher; at
        /// <see cref="PubSubDiagnosticsLevel.Low"/> it is a no-op so
        /// callers do not need to gate on <see cref="Level"/>.
        /// </summary>
        /// <param name="statusCode">
        /// Status code summarising the error condition.
        /// </param>
        /// <param name="message">
        /// Human-readable explanation. Must not contain sensitive
        /// data; redaction is the caller's responsibility.
        /// </param>
        void RecordError(StatusCode statusCode, string message);

        /// <summary>
        /// Resets all counters to zero and clears any retained error
        /// history. Provided to support the Part 14 PubSubDiagnosticsType
        /// <c>Reset</c> method when surfaced from the address space.
        /// </summary>
        void Reset();
    }
}
