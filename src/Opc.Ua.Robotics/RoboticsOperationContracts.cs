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

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Standard OPC 40010 operation states.
    /// </summary>
    public enum RoboticsOperationState
    {
        /// <summary>
        /// The operation is idle.
        /// </summary>
        Idle,

        /// <summary>
        /// The operation is ready.
        /// </summary>
        Ready,

        /// <summary>
        /// The operation is executing.
        /// </summary>
        Executing
    }

    /// <summary>
    /// Standard stop mode values passed to OPC 40010 Stop methods.
    /// </summary>
    public enum RoboticsStopMode : short
    {
        /// <summary>
        /// The default stop mode configured by the server.
        /// </summary>
        Default = 0,

        /// <summary>
        /// A controlled stop mode.
        /// </summary>
        Controlled = 1,

        /// <summary>
        /// A quick stop mode.
        /// </summary>
        Quick = 2,

        /// <summary>
        /// An immediate stop mode.
        /// </summary>
        Immediate = 3
    }

    /// <summary>
    /// Context passed to standard operation method handlers.
    /// </summary>
    public sealed record RoboticsOperationContext
    {
        /// <summary>
        /// The operation object NodeId.
        /// </summary>
        public NodeId OperationNodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The operation state machine NodeId.
        /// </summary>
        public NodeId StateMachineNodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The state observed before the method handler was invoked.
        /// </summary>
        public RoboticsOperationState CurrentState { get; init; }
    }

    /// <summary>
    /// Request data passed to standard Stop method handlers.
    /// </summary>
    public sealed record RoboticsStopRequest
    {
        /// <summary>
        /// The operation context.
        /// </summary>
        public RoboticsOperationContext Context { get; init; } = new();

        /// <summary>
        /// The requested stop mode.
        /// </summary>
        public RoboticsStopMode StopMode { get; init; }
    }

    /// <summary>
    /// Describes a completed operation state transition.
    /// </summary>
    public sealed record RoboticsOperationTransition
    {
        /// <summary>
        /// The state before the transition.
        /// </summary>
        public RoboticsOperationState FromState { get; init; }

        /// <summary>
        /// The state after the transition.
        /// </summary>
        public RoboticsOperationState ToState { get; init; }

        /// <summary>
        /// The transition NodeId.
        /// </summary>
        public NodeId TransitionId { get; init; } = NodeId.Null;

        /// <summary>
        /// The transition reason written to LastTransitionReason.
        /// </summary>
        public short Reason { get; init; }
    }

    /// <summary>
    /// Result returned by task-control program load and unload handlers.
    /// </summary>
    public sealed record RoboticsProgramResult
    {
        /// <summary>
        /// The OPC UA service result for the method call.
        /// </summary>
        public ServiceResult ServiceResult { get; init; } = ServiceResult.Good;

        /// <summary>
        /// The method-specific status output.
        /// </summary>
        public int Status { get; init; }
    }
}
