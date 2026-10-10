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
    /// Read-model snapshot of a TaskControlType instance.
    /// </summary>
    public sealed record TaskControlSnapshot
    {
        /// <summary>
        /// The task-control identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The optional execution mode, including status and timestamps.
        /// </summary>
        public DataValue ExecutionMode { get; init; } = DataValue.Null;

        /// <summary>
        /// The ExecutionMode variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId ExecutionModeId { get; init; } = NodeId.Null;

        /// <summary>
        /// Whether a task program is loaded, including status and timestamps.
        /// </summary>
        public DataValue TaskProgramLoaded { get; init; } = DataValue.Null;

        /// <summary>
        /// The TaskProgramLoaded variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId TaskProgramLoadedId { get; init; } = NodeId.Null;

        /// <summary>
        /// The loaded task-program name, including status and timestamps.
        /// </summary>
        public DataValue TaskProgramName { get; init; } = DataValue.Null;

        /// <summary>
        /// The TaskProgramName variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId TaskProgramNameId { get; init; } = NodeId.Null;

        /// <summary>
        /// The TaskControlOperation instance, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId TaskControlOperationId { get; init; } = NodeId.Null;

        /// <summary>
        /// Task modules exposed by this task control.
        /// </summary>
        public ArrayOf<NodeId> TaskModuleIds { get; init; } = [];

        /// <summary>
        /// The CurrentState variable of the TaskControlStateMachine, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId CurrentStateId { get; init; } = NodeId.Null;

        /// <summary>
        /// The state the TaskControlStateMachine was in when read.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> when the machine is absent, the read failed or the
        /// value names a state other than Idle, Ready or Executing.
        /// </remarks>
        public RoboticsOperationState? CurrentState { get; init; }
    }
}
