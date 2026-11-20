// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="PublishedDataSetDataType"/> Add/Remove events
    /// </summary>
    public class PublishedDataSetEventArgs : EventArgs
    {
        /// <summary>
        /// ConfigurationId of <see cref="PublishedDataSetDataType"/> object
        /// </summary>
        public uint PublishedDataSetId { get; set; }

        /// <summary>
        /// Reference to <see cref="PublishedDataSetDataType"/> object
        /// </summary>
        public required PublishedDataSetDataType PublishedDataSetDataType { get; set; }
    }
}
