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
    /// Verbosity tier of <see cref="IPubSubDiagnostics"/>. Higher tiers
    /// retain more information at the cost of additional memory and a
    /// small per-operation overhead.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11.2">
    /// Part 14 §9.1.11.2 DiagnosticsLevel</see>. The three tiers map to the
    /// repository research supplement (§8): <see cref="Low"/> tracks
    /// monotonic counters only, <see cref="Medium"/> additionally records
    /// the most recent error <see cref="StatusCode"/> per component, and
    /// <see cref="High"/> keeps a bounded ring buffer of recent error
    /// events suitable for live troubleshooting.
    /// </remarks>
    public enum PubSubDiagnosticsLevel
    {
        /// <summary>
        /// Counter-only: <see cref="IPubSubDiagnostics.Increment"/> updates
        /// monotonic counters but <see cref="IPubSubDiagnostics.RecordError"/>
        /// is a no-op.
        /// </summary>
        Low,

        /// <summary>
        /// Counters plus last-error per component: the most recent
        /// <see cref="StatusCode"/> reported via
        /// <see cref="IPubSubDiagnostics.RecordError"/> is retained.
        /// </summary>
        Medium,

        /// <summary>
        /// Counters, last-error, and a bounded ring buffer of recent error
        /// events with timestamps. Suitable for live troubleshooting.
        /// </summary>
        High
    }
}
