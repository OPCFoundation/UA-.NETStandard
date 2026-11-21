// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="PubSubState"/> Change events
    /// </summary>
    public class PubSubStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Reference to the object whose <see cref="PubSubState"/> was changed
        /// </summary>
        public required object ConfigurationObject { get; set; }

        /// <summary>
        /// Configuration Id of the object whose <see cref="PubSubState"/> was changed
        /// </summary>
        public uint ConfigurationObjectId { get; set; }

        /// <summary>
        /// New <see cref="PubSubState"/>
        /// </summary>
        public PubSubState NewState { get; set; }

        /// <summary>
        /// Old <see cref="PubSubState"/>
        /// </summary>
        public PubSubState OldState { get; set; }
    }
}
