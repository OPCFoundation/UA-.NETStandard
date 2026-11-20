// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="DataSetReaderDataType"/> Add/Remove events
    /// </summary>
    public class DataSetReaderEventArgs : EventArgs
    {
        /// <summary>
        /// ConfigurationId of parent <see cref="ReaderGroupDataType"/> object
        /// </summary>
        public uint ReaderGroupId { get; set; }

        /// <summary>
        /// ConfigurationId of <see cref="DataSetReaderDataType"/> object
        /// </summary>
        public uint DataSetReaderId { get; set; }

        /// <summary>
        /// Reference to <see cref="DataSetReaderDataType"/> object
        /// </summary>
        public required DataSetReaderDataType DataSetReaderDataType { get; set; }
    }
}
