// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Lds.Server.Hosting
{
    /// <summary>
    /// Factory used by the hosted LDS to create the optional LDS-ME multicast component.
    /// </summary>
    public interface ILdsMulticastDiscoveryFactory
    {
        /// <summary>
        /// Creates the multicast discovery component for the supplied LDS.
        /// </summary>
        IMulticastDiscovery Create(LdsServer server);
    }
}
