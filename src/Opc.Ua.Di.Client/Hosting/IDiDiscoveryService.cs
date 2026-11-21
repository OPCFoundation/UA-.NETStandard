// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Di.Client.Hosting
{
    /// <summary>
    /// DI-hosting-friendly facade around
    /// <see cref="DiDiscoveryClient"/>. Resolved from the service
    /// provider after <c>AddOpcUaDi()</c> on the client builder, this
    /// service uses the application's lazy
    /// <c>Func&lt;CancellationToken, Task&lt;ManagedSession&gt;&gt;</c>
    /// to enumerate DI devices on the connected server.
    /// </summary>
    public interface IDiDiscoveryService
    {
        /// <summary>
        /// Asynchronously streams every <c>DeviceType</c> instance
        /// (including subtypes) reachable from the <c>Objects</c>
        /// folder on the connected server. Devices are yielded as they
        /// are discovered so callers can begin processing without
        /// waiting for the entire browse recursion to complete.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// An asynchronous stream of discovered devices, each
        /// carrying the device NodeId, display name, and detected
        /// type definition.
        /// </returns>
        IAsyncEnumerable<DeviceEntry> EnumerateDevicesAsync(
            CancellationToken cancellationToken = default);
    }
}
