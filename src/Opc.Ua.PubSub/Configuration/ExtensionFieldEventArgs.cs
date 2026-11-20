// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for ExtensionFields Add/Remove events
    /// </summary>
    public class ExtensionFieldEventArgs : EventArgs
    {
        /// <summary>
        /// Get/Set parent PublishedDataSet configuration id
        /// </summary>
        public uint PublishedDataSetId { get; set; }

        /// <summary>
        /// Get/Set the configuration id for the ExtensionField
        /// </summary>
        public uint ExtensionFieldId { get; set; }

        /// <summary>
        /// Get/Set the ExtensionField
        /// </summary>
        public required KeyValuePair ExtensionField { get; set; }
    }
}
