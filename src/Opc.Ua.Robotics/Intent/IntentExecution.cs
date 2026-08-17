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
    /// What an executor is given when it is asked to carry out one intent.
    /// </summary>
    public sealed class IntentExecution
    {
        /// <summary>
        /// Creates an execution context.
        /// </summary>
        public IntentExecution(string intentId, IntentDataType intent, IIntentProgress progress)
            : this(intentId, intent, progress, NodeId.Null, string.Empty)
        {
        }

        /// <summary>
        /// Creates an execution context for a controller.
        /// </summary>
        public IntentExecution(
            string intentId,
            IntentDataType intent,
            IIntentProgress progress,
            NodeId controllerId,
            string controllerName = "")
        {
            IntentId = intentId ?? throw new ArgumentNullException(nameof(intentId));
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            ControllerId = controllerId;
            ControllerName = controllerName ?? string.Empty;
        }

        /// <summary>
        /// The identifier the intent was admitted under.
        /// </summary>
        public string IntentId { get; }

        /// <summary>
        /// The intent as admitted, after the Server applied its defaults.
        /// </summary>
        public IntentDataType Intent { get; }

        /// <summary>
        /// Progress and pose reporting for this execution.
        /// </summary>
        public IIntentProgress Progress { get; }

        /// <summary>
        /// The NodeId of the IntentControllerType object that owns this execution, or NodeId.Null if unavailable.
        /// </summary>
        public NodeId ControllerId { get; }

        /// <summary>
        /// The browse-name text of the IntentControllerType object that owns this execution, or an empty string if unavailable.
        /// </summary>
        public string ControllerName { get; }

        /// <summary>
        /// The mission this intent belongs to, or an empty string when it was
        /// submitted on its own.
        /// </summary>
        public string MissionId { get; init; } = string.Empty;

        /// <summary>
        /// The stop mode from the accepted cancellation request.
        /// </summary>
        /// <remarks>
        /// The cancellation token passed to the executor is signalled after this value
        /// is set. A superseded executing intent uses <see cref="StopModeEnum.QuickStop"/>,
        /// because an aborting replacement is not client-chosen and should release the
        /// controller for the replacing intent promptly. This is an application stop
        /// urgency only and does not select or imply an IEC 60204-1 stop category.
        /// </remarks>
        public StopModeEnum StopMode => (StopModeEnum)Volatile.Read(ref m_stopMode);

        /// <summary>
        /// Records the stop mode at the moment cancellation is accepted.
        /// </summary>
        /// <param name="stopMode">
        /// The accepted application stop urgency.
        /// </param>
        public void AcceptCancellation(StopModeEnum stopMode)
        {
            Volatile.Write(ref m_stopMode, (int)stopMode);
        }

        private int m_stopMode = (int)StopModeEnum.OnPath;
    }
}
