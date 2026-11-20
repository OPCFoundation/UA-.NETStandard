/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="WriterGroupDataType"/> Add/Remove events
    /// </summary>
    public class WriterGroupEventArgs : EventArgs
    {
        /// <summary>
        /// ConfigurationId of parent <see cref="PubSubConnectionDataType"/> object
        /// </summary>
        public uint ConnectionId { get; set; }

        /// <summary>
        /// ConfigurationId of <see cref="PublishedDataSetDataType"/> object
        /// </summary>
        public uint WriterGroupId { get; set; }

        /// <summary>
        /// Reference to <see cref="WriterGroupDataType"/> object
        /// </summary>
        public required WriterGroupDataType WriterGroupDataType { get; set; }
    }
}
