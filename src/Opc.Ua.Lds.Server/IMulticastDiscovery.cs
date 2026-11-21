// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// LDS-ME multicast discovery component hosted by <see cref="LdsServer"/>.
    /// </summary>
    public interface IMulticastDiscovery : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether multicast discovery is running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Starts mDNS announcement and discovery.
        /// </summary>
        Task StartAsync(
            string applicationUri,
            IList<string> discoveryUrls,
            IList<string> capabilities,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops mDNS announcement and discovery.
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);
    }
}
