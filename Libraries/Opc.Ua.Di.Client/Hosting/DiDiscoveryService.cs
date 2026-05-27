/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
