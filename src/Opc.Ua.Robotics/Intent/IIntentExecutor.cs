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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.RobotIntent
{
    /// <summary>
    /// Carries out intents on the robot.
    /// </summary>
    /// <remarks>
    /// The host owns admission, queueing, the state machine, cancellation and the
    /// result; an implementation of this interface owns only the doing. Translating an
    /// intent into whatever the controller actually executes - URScript, RAPID, KRL, a
    /// TP program - is the whole of its job.
    /// </remarks>
    public interface IIntentExecutor
    {
        /// <summary>
        /// Executes one intent.
        /// </summary>
        /// <remarks>
        /// The cancellation token is signalled when a cancel has been ACCEPTED, which
        /// is the point at which the operation enters Cancelling. An implementation
        /// brings motion to a controlled end and then returns; it need not return
        /// Cancelled, because the host records the cancellation itself.
        /// </remarks>
        ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution,
            CancellationToken cancellationToken);

        /// <summary>
        /// Decides whether a cancel may be accepted for an intent that is executing.
        /// </summary>
        /// <remarks>
        /// Some motions cannot be abandoned part-way without leaving the cell in a
        /// worse state than completing them - a tool change mid-exchange, a placement
        /// mid-release. Returning false refuses this one occasion; declaring
        /// CancelSupported false in the capability refuses the whole intent type in
        /// advance.
        /// <para>
        /// This has no default implementation because the library targets .NET
        /// Framework, which has none, and because whether a motion can be safely
        /// abandoned is a decision worth making deliberately. An executor that has no
        /// such motions returns true.
        /// </para>
        /// </remarks>
        bool CanCancel(IntentExecution execution);
    }
}
