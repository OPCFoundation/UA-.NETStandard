// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Automatic execution-system transitions for a V2 job order.
    /// </summary>
    public enum Isa95JobExecutionTransition
    {
        /// <summary>
        /// Starts execution of an order in AllowedToStart.
        /// </summary>
        BeginExecution = 1,

        /// <summary>
        /// Holds a running order because of an internal condition.
        /// </summary>
        Hold = 2,

        /// <summary>
        /// Completes a running or interrupted order.
        /// </summary>
        Complete = 3,

        /// <summary>
        /// Closes an ended order after its response has been handled.
        /// </summary>
        Close = 4,

        /// <summary>
        /// Marks an unready V2 job order as ready.
        /// </summary>
        MarkReady = 5,

        /// <summary>
        /// Marks a ready V2 job order as loaded.
        /// </summary>
        Load = 6,

        /// <summary>
        /// Returns a loaded V2 job order to Ready.
        /// </summary>
        Unload = 7,

        /// <summary>
        /// Returns a ready or loaded V2 job order to Waiting.
        /// </summary>
        MarkNotReady = 8
    }
}
