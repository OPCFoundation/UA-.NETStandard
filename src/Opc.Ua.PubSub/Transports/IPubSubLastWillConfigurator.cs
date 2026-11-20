/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Optional transport capability for configuring broker last-will messages
    /// before the transport opens.
    /// </summary>
    public interface IPubSubLastWillConfigurator
    {
        /// <summary>
        /// Configures the status payload published by the broker if the
        /// publisher disconnects ungracefully.
        /// </summary>
        /// <param name="topic">Status topic.</param>
        /// <param name="payload">Encoded status payload.</param>
        /// <param name="retain">Retain flag.</param>
        void ConfigureLastWill(string topic, ReadOnlyMemory<byte> payload, bool retain);
    }
}
