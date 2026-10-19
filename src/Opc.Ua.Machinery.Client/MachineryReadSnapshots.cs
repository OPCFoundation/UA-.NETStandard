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
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Machinery.Result;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// A strictly read Machinery state or operation mode with per-field evidence.
    /// </summary>
    /// <param name="State">The compatible finite-state projection.</param>
    /// <param name="Values">Original state and transition fields, including optional absence.</param>
    public sealed record MachineryStateSnapshot(
        FiniteStateSnapshot State,
        ArrayOf<MachineryReadValue> Values)
    {
        /// <summary>
        /// Gets the optional, exactly UInt32 current-state number.
        /// </summary>
        public uint? CurrentStateNumber { get; init; }

        /// <summary>
        /// Gets the optional, exactly UInt32 last-transition number.
        /// </summary>
        public uint? LastTransitionNumber { get; init; }

        /// <summary>
        /// Gets the server's transition time, when published.
        /// </summary>
        public DateTime? TransitionTime { get; init; }
    }

    /// <summary>
    /// A bounded job-order list with its original source value. Empty, Good-null
    /// and absent lists remain distinguishable through Values.
    /// </summary>
    /// <param name="JobOrders">The decoded job orders and states.</param>
    /// <param name="Values">The list's original read or explicit absence evidence.</param>
    public sealed record MachineryJobOrdersSnapshot(
        ArrayOf<V2.ISA95JobOrderAndStateDataType> JobOrders,
        ArrayOf<MachineryReadValue> Values);

    /// <summary>
    /// A bounded job-response list with its original source value.
    /// </summary>
    /// <param name="JobResponses">The decoded job responses.</param>
    /// <param name="Values">The list's original read or explicit absence evidence.</param>
    public sealed record MachineryJobResponsesSnapshot(
        ArrayOf<V2.ISA95JobResponseDataType> JobResponses,
        ArrayOf<MachineryReadValue> Values);

    /// <summary>
    /// A published result variable with its original source, quality and time.
    /// This snapshot never downloads result payloads or dereferences result URIs.
    /// </summary>
    /// <param name="Result">The decoded result, or null for a present Good-null value.</param>
    /// <param name="Values">The original result variable value.</param>
    public sealed record MachineryResultSnapshot(
        ResultDataType? Result,
        ArrayOf<MachineryReadValue> Values);
}
