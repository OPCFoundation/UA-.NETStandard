// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="ReaderGroupDataType"/> Add/Remove events
    /// </summary>
    public class ReaderGroupEventArgs : EventArgs
    {
        /// <summary>
        /// ConfigurationId of parent <see cref="PubSubConnectionDataType"/> object
        /// </summary>
        public uint ConnectionId { get; set; }

        /// <summary>
        /// ConfigurationId of <see cref="ReaderGroupDataType"/> object
        /// </summary>
        public uint ReaderGroupId { get; set; }

        /// <summary>
        /// Reference to <see cref="ReaderGroupDataType"/> object
        /// </summary>
        public required ReaderGroupDataType ReaderGroupDataType { get; set; }
    }
}
