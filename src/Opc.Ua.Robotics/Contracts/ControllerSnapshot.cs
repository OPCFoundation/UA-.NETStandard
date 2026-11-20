// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a ControllerType instance.
    /// </summary>
    public sealed record ControllerSnapshot
    {
        /// <summary>
        /// The controller identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// Task controls hosted by this controller.
        /// </summary>
        public ArrayOf<NodeId> TaskControlIds { get; init; } = [];

        /// <summary>
        /// Additional DI component instance NodeIds installed in the controller.
        /// </summary>
        public ArrayOf<NodeId> ComponentIds { get; init; } = [];

        /// <summary>
        /// The SystemOperation instance, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId SystemOperationId { get; init; } = NodeId.Null;

        /// <summary>
        /// The CurrentState variable of the SystemOperationStateMachine, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId CurrentStateId { get; init; } = NodeId.Null;

        /// <summary>
        /// The state the SystemOperationStateMachine was in when read.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> when the machine is absent, the read failed or the
        /// value names a state other than Idle, Ready or Executing.
        /// </remarks>
        public RoboticsOperationState? CurrentState { get; init; }
    }
}
