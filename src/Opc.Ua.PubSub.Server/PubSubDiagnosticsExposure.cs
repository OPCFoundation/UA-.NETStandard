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

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Controls how much of the standard <c>PubSubDiagnosticsType</c>
    /// node-set (Part 14 §9.1.11) is bound to the runtime
    /// <see cref="Diagnostics.IPubSubDiagnostics"/>
    /// instance.
    /// </summary>
    /// <remarks>
    /// Implements the exposure dial referenced by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11">
    /// Part 14 §9.1.11 PubSubDiagnosticsType</see>. The default
    /// (<see cref="Counters"/>) wires every cumulative counter from
    /// <see cref="Diagnostics.PubSubDiagnosticsCounterKind"/>
    /// onto the corresponding <c>Counters_*</c> Variable in the
    /// address space.
    /// </remarks>
    public enum PubSubDiagnosticsExposure
    {
        /// <summary>
        /// Do not bind any diagnostics counters. The
        /// <c>PublishSubscribe_Diagnostics</c> sub-tree stays at its
        /// default zero values loaded from the stack NodeSet.
        /// </summary>
        None,

        /// <summary>
        /// Bind the cumulative counter Variables in
        /// <c>PublishSubscribe_Diagnostics_Counters_*</c>.
        /// </summary>
        Counters,

        /// <summary>
        /// Bind the cumulative counters and the <c>TotalError</c>
        /// summary Variable, surfacing the most recent error captured
        /// by <see cref="Diagnostics.IPubSubDiagnostics"/>.
        /// </summary>
        Errors,

        /// <summary>
        /// Bind every PubSubDiagnostics Variable supported by the
        /// PubSub runtime, including <c>LiveValues_*</c> counters
        /// (configured and operational writer / reader totals).
        /// </summary>
        Full
    }
}
