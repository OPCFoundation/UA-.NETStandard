// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// An order command or an internal execution-system action.
    /// </summary>
    public enum Isa95JobOperation
    {
        /// <summary>
        /// No operation was specified.
        /// </summary>
        Undefined = 0,

        /// <summary>
        /// Stores a new order without granting start permission.
        /// </summary>
        Store = 1,

        /// <summary>
        /// Stores a new order and grants start permission.
        /// </summary>
        StoreAndStart,

        /// <summary>
        /// Grants start permission to a stored order.
        /// </summary>
        Start,

        /// <summary>
        /// Replaces the payload of an order that has not started.
        /// </summary>
        Update,

        /// <summary>
        /// Stops an active order. V1 removes the order, while V2 retains it in Completed.
        /// </summary>
        Stop,

        /// <summary>
        /// Stops and removes an active order through the V1 interface.
        /// </summary>
        StopAndRemove,

        /// <summary>
        /// Removes a stored order that has not started.
        /// </summary>
        Cancel,

        /// <summary>
        /// Removes a terminal order.
        /// </summary>
        Clear,

        /// <summary>
        /// Suspends a running order.
        /// </summary>
        Pause,

        /// <summary>
        /// Resumes an interrupted order.
        /// </summary>
        Resume,

        /// <summary>
        /// Aborts an order that has not reached a terminal state.
        /// </summary>
        Abort,

        /// <summary>
        /// Revokes start permission without changing readiness.
        /// </summary>
        RevokeStart,

        /// <summary>
        /// Starts execution when permission and readiness allow it.
        /// </summary>
        BeginExecution,

        /// <summary>
        /// Holds a running order due to an internal condition.
        /// </summary>
        Hold,

        /// <summary>
        /// Completes a running or interrupted order.
        /// </summary>
        Complete,

        /// <summary>
        /// Closes a completed order.
        /// </summary>
        Close,

        /// <summary>
        /// Signals that the system can execute the order.
        /// </summary>
        MarkReady,

        /// <summary>
        /// Signals that the program or configuration for a ready order has been loaded.
        /// </summary>
        Load,

        /// <summary>
        /// Returns a loaded order to Ready.
        /// </summary>
        Unload,

        /// <summary>
        /// Returns a ready or loaded order to Waiting.
        /// </summary>
        MarkNotReady
    }
}
