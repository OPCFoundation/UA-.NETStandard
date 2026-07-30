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

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The version-neutral canonical life-cycle state of a job order tracked by
    /// the in-memory engine. It mirrors the Job Control V2 receiver state machine
    /// (with the two interrupted sub-states) and is projected to the Job Control
    /// V1 <c>ISA95JobOrderStateEnum</c> by the conversion helpers.
    /// </summary>
    internal enum Isa95JobCanonicalState
    {
        /// <summary>
        /// The order is stored but is not yet allowed to start
        /// (V2 <c>NotAllowedToStart</c>, V1 <c>Waiting</c>).
        /// </summary>
        NotAllowedToStart = 1,

        /// <summary>
        /// The order is stored and allowed to start
        /// (V2 <c>AllowedToStart</c>, V1 <c>Ready</c>).
        /// </summary>
        AllowedToStart = 2,

        /// <summary>
        /// The order is executing (V2 <c>Running</c>, V1 <c>Running</c>).
        /// </summary>
        Running = 3,

        /// <summary>
        /// The order is interrupted and held by an internal condition
        /// (V2 <c>Interrupted/Held</c>, V1 <c>Held</c>).
        /// </summary>
        Held = 4,

        /// <summary>
        /// The order is interrupted and suspended by an external pause
        /// (V2 <c>Interrupted/Suspended</c>, V1 <c>Suspended</c>).
        /// </summary>
        Suspended = 5,

        /// <summary>
        /// The order has completed (V2 <c>Ended/Completed</c>,
        /// V1 <c>Completed</c>). Terminal.
        /// </summary>
        Completed = 6,

        /// <summary>
        /// The order was aborted (V2 <c>Aborted</c>, V1 <c>Aborted</c>). Terminal.
        /// </summary>
        Aborted = 7,

        /// <summary>
        /// The completed order has been closed (V2 <c>Ended/Closed</c>,
        /// V1 <c>Closed</c>). Terminal.
        /// </summary>
        Closed = 8,

        /// <summary>
        /// The order has been loaded into the execution system (V1 <c>Loaded</c>).
        /// This is a V1-only response state with no distinct V2 equivalent; it is
        /// preserved so that V1 responses round-trip and is projected to
        /// V2 <c>Running</c> when a V2 view is requested.
        /// </summary>
        Loaded = 9,

        /// <summary>
        /// The order is in an error condition (V1 <c>Error</c>). This is a V1-only
        /// response state with no distinct V2 equivalent; it is preserved so that
        /// V1 responses round-trip and is projected to V2 <c>Aborted</c> when a
        /// V2 view is requested.
        /// </summary>
        Error = 10
    }
}
