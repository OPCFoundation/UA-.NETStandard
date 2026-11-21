// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Direction of flow an <see cref="IPubSubTransport"/> instance
    /// services. A PubSubConnection may publish, subscribe, or do both;
    /// the transport reports the configured direction so the dispatcher
    /// can skip wiring for the unused side.
    /// </summary>
    /// <remarks>
    /// Implements the publisher / subscriber binding selector from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7">
    /// Part 14 §6.2.7 PubSubConnection</see>.
    /// </remarks>
    [Flags]
    public enum PubSubTransportDirection
    {
        /// <summary>
        /// Connection is disabled or otherwise carries no traffic.
        /// </summary>
        None = 0,

        /// <summary>
        /// Publisher direction — the transport sends frames.
        /// </summary>
        Send = 1,

        /// <summary>
        /// Subscriber direction — the transport receives frames.
        /// </summary>
        Receive = 2,

        /// <summary>
        /// Convenience for connections that publish and subscribe over
        /// the same socket.
        /// </summary>
        SendReceive = Send | Receive
    }
}
