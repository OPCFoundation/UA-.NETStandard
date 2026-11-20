/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="PubSubConnectionDataType"/> Add/Remove events
    /// </summary>
    public class ConnectionEventArgs : EventArgs
    {
        /// <summary>
        /// ConfigurationId of <see cref="PubSubConnectionDataType"/> object
        /// </summary>
        public uint ConnectionId { get; set; }

        /// <summary>
        /// Reference to <see cref="PubSubConnectionDataType"/> object
        /// </summary>
        public required PubSubConnectionDataType PubSubConnectionDataType { get; set; }
    }
}
