// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client.Hosting
{
    /// <summary>
    /// Default <see cref="IDiDiscoveryService"/> implementation: opens
    /// the application's lazy <see cref="ManagedSession"/> on first
    /// use and delegates to <see cref="DiDiscoveryClient"/>.
    /// </summary>
    internal sealed class DiDiscoveryService : IDiDiscoveryService
    {
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionAccessor;
        private readonly ITelemetryContext m_telemetry;

        public DiDiscoveryService(
            Func<CancellationToken, Task<ManagedSession>> sessionAccessor,
            ITelemetryContext telemetry)
        {
            m_sessionAccessor = sessionAccessor
                ?? throw new ArgumentNullException(nameof(sessionAccessor));
            m_telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
        }

        public async IAsyncEnumerable<DeviceEntry> EnumerateDevicesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionAccessor(cancellationToken)
                .ConfigureAwait(false);

            await foreach (DeviceEntry entry in DiDiscoveryClient
                .EnumerateDevicesAsync(session, m_telemetry, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return entry;
            }
        }
    }
}
