// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
