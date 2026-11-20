// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="DataSetWriterDataType"/> Add/Remove events
    /// </summary>
    public class DataSetWriterEventArgs : EventArgs
    {
        /// <summary>
        /// ConfigurationId of parent <see cref="WriterGroupDataType"/> object
        /// </summary>
        public uint WriterGroupId { get; set; }

        /// <summary>
        /// ConfigurationId of <see cref="DataSetWriterDataType"/> object
        /// </summary>
        public uint DataSetWriterId { get; set; }

        /// <summary>
        /// Reference to <see cref="DataSetWriterDataType"/> object
        /// </summary>
        public required DataSetWriterDataType DataSetWriterDataType { get; set; }
    }
}
