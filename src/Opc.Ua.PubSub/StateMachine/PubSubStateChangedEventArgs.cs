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

namespace Opc.Ua.PubSub.StateMachine
{
    /// <summary>
    /// Event data for a <see cref="PubSubStateMachine.StateChanged"/> event.
    /// Carries the from / to states, the transition reason, the optional
    /// status code, and the originating component metadata so subscribers can
    /// route diagnostics and audit records without re-querying the source.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.10.1">
    /// Part 14 §9.1.10.1 PubSubStatusType</see> change reporting. The
    /// <see cref="StatusCode"/> mirrors the <c>State</c> Variable's
    /// <c>StatusCode</c> after the transition.
    /// </remarks>
    public sealed class PubSubStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="PubSubStateChangedEventArgs"/> class.
        /// </summary>
        public PubSubStateChangedEventArgs(
            string componentName,
            PubSubComponentKind componentKind,
            PubSubState previousState,
            PubSubState newState,
            PubSubStateTransitionReason reason,
            StatusCode statusCode)
        {
            ComponentName = componentName ?? throw new ArgumentNullException(nameof(componentName));
            ComponentKind = componentKind;
            PreviousState = previousState;
            NewState = newState;
            Reason = reason;
            StatusCode = statusCode;
        }

        /// <summary>
        /// Human-readable name of the originating component
        /// (e.g. <c>"PubSubConnection.Mqtt.Default"</c>). Used in diagnostics
        /// logs and audit events.
        /// </summary>
        public string ComponentName { get; }

        /// <summary>
        /// Classifies the component type that transitioned.
        /// </summary>
        public PubSubComponentKind ComponentKind { get; }

        /// <summary>
        /// The state the component was in before the transition.
        /// </summary>
        public PubSubState PreviousState { get; }

        /// <summary>
        /// The state the component is in after the transition.
        /// </summary>
        public PubSubState NewState { get; }

        /// <summary>
        /// Why the transition occurred. Influences which diagnostics counter
        /// is incremented (see <see cref="PubSubStateTransitionReason"/>).
        /// </summary>
        public PubSubStateTransitionReason Reason { get; }

        /// <summary>
        /// The OPC UA <c>StatusCode</c> associated with the resulting state.
        /// <c>Good</c> for <c>Operational</c>; <c>BadOutOfService</c> for
        /// <c>Disabled</c>; specific sub-codes (e.g.
        /// <c>BadConfigurationError</c>, <c>BadCommunicationError</c>,
        /// <c>BadSecurityChecksFailed</c>) for <c>Error</c>.
        /// </summary>
        public StatusCode StatusCode { get; }
    }
}
