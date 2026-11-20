// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Machinery.Server.StateMachines
{
    /// <summary>
    /// The four states of the OPC 40001-1
    /// <c>MachineryItemState_StateMachineType</c>.
    /// </summary>
    /// <remarks>
    /// The state machine declares no cause methods — the whole Machinery
    /// NodeSet contains no <c>UAMethod</c> at all. State changes therefore
    /// always come from the server, never from a client, which is why the
    /// server-side surface is a controller rather than a set of callable
    /// transitions.
    /// </remarks>
    public enum MachineryItemStateValue
    {
        /// <summary>
        /// The machinery item cannot be reached or has not reported yet.
        /// </summary>
        NotAvailable = 0,

        /// <summary>
        /// The machinery item is intentionally out of service.
        /// </summary>
        OutOfService = 1,

        /// <summary>
        /// The machinery item is available but not executing.
        /// </summary>
        NotExecuting = 2,

        /// <summary>
        /// The machinery item is executing.
        /// </summary>
        Executing = 3
    }

    /// <summary>
    /// The four states of the OPC 40001-1
    /// <c>MachineryOperationModeStateMachineType</c>.
    /// </summary>
    public enum MachineryOperationModeValue
    {
        /// <summary>
        /// No operation mode is reported.
        /// </summary>
        None = 0,

        /// <summary>
        /// The machinery item is being maintained.
        /// </summary>
        Maintenance = 1,

        /// <summary>
        /// The machinery item is being set up.
        /// </summary>
        Setup = 2,

        /// <summary>
        /// The machinery item is processing.
        /// </summary>
        Processing = 3
    }
}
