/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Opc.Ua.Server;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Registration helpers for the experimental QUIC data-channel binding.
    /// </summary>
    public static class QuicStandardServerExtensions
    {
        /// <summary>
        /// Installs the QUIC data-channel transport adapter on a server.
        /// </summary>
        public static StandardServer UseQuicDataChannelTransport(this StandardServer server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            server.DataChannelTransport ??= new QuicServerDataChannelTransport();
            return server;
        }
    }
}
